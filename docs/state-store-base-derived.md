# StateStore 基础设计的派生结论

> 状态：Derived Notes
>
> 更新日期：2026-08-28
>
> 性质：本文不是独立设计 authority。基础决策以 [`state-store-base-design.md`](state-store-base-design.md) 为准，地址 wire 以 [`state-store-addressing-design.md`](state-store-addressing-design.md) 为准。

## 1. A/B/C 模型

```text
A = OldPrevious
B = Current
C = Next / NewCurrent
```

轮转前：

```text
ReconstructionFiles(PublishedHead) ⊆ {A, B}
```

轮转后：

```text
ReconstructionFiles(NewHead) ⊆ {B, C}
```

这条性质约束 published current Revision 的重建闭包，不要求轮转事务期间目录里只能有两个文件。构造 C 时保留 A/B 是旧 head 可恢复的必要条件，因此短暂的 A/B/C 三文件状态是正常状态。

## 2. 为什么“把 Base 在 A 的 live objects 写 Base 到 C”足够

一个 ObjectVersion reconstruction chain 从 latest version 向 Parent 回溯，只在遇到 Base 时停止。于是每个 live object 对更老文件的重建依赖由其 terminating Base 所在文件决定。

从 A/B 轮转到 B/C 时：

- Base 在 A：不迁移就仍需 A；在 C 写完整 Base 后，重建在 C 停止；
- Base 在 B：可以继续依赖 B；
- 本次在 C 新写 Base：只依赖 C；
- 本次在 C 写 Delta：其 parent chain 仍必须最终只终止于 B 或 C 的 Base。

因此，对所有“Base 在 A”的 live objects 强制在 C 写 Base，再验证整个 candidate closure，可以消除 new head 对 A 的 reconstruction dependency。

这个推导不依赖 Previous-byte ratio、链长阈值或特定 RebaseOrDeltify 收益公式；那些量只影响何时以及以何种节奏迁移。

## 3. RelayRevision 的必要性与形状

### 3.1 需要 relay 的集合

Evacuation 与 relay 是两个不同集合：

```text
EvacuationSet
    = reconstruction Base 位于 A 的 live objects

RelaySet
    = EvacuationSet 中 latest ObjectVersion 仍位于 A 的 objects
```

EvacuationSet 全部需要在 C 写 Base。只有 RelaySet 的直接 parent 无法从 C 以一位 selector 编码，因此必须经 B 中继。

若 latest version 已在 B，只是 Delta chain 的 Base 位于 A，则 C Base 可以直接指向 B latest；后续 lineage 自然由 B latest 继续指向 A。

### 3.2 一个共享 relay frame 仍是 O(N)

一个 RelayRevision 可以服务多个 objects，但不能是完全空的通用跳板。ParentVersion 只保存 FrameTicket，ObjectId 由遍历中的对象身份提供，所以 relay frame 必须为每个 RelaySet ObjectId 提供可定位 entry：

```text
TailMeta:
    ObjectId1 -> RelayEntry1 offset
    ObjectId2 -> RelayEntry2 offset

Payload:
    RelayEntry1(parent = A.HeadOfObject1, no field mutation)
    RelayEntry2(parent = A.HeadOfObject2, no field mutation)
```

forwarder 可以建模为 synthetic no-op Delta。它不重复领域完整状态，但至少包含 ObjectVersion header、parent ticket、ObjectId index，以及“形式合法 Revision”所要求的 ObjectVersionDict/Revision metadata。因此成本相对 full Base 较低，但 payload 与 TailMeta 都随 RelaySet 大小增长，不是 O(1) bytes。

紧邻轮转创建的 dedicated RelayRevision 是 maintenance artifact，不成为 PublishedHead，也不增加用户可观察的 Save/Revision 次数。HEAD 只从 old user Revision 一次性推进到 C 中的 new user Revision。

渐进模式有所不同：RelayEntry 可以随 B 中一次普通 user Save 写入其 published Revision；同次 ObjectVersionDict 必须把该 ObjectId 的 latest binding 更新为 B 中 RelayEntry。这样 relay 成为 published state 的一部分，但不会额外制造 relay-only PublishedHead。只 append entry、却没有任何 PublishedHead/OVD/C Base parent 引用的 relay 是 orphan，不能从 RelaySet 或 relay debt 中扣除。

### 3.3 写入与 durable 顺序

因为 C Base 的 lineage parent 会指向 B relay：

```text
append RelayRevision to B
    -> DurableFlush(B)
append candidate Revision to C
    -> DurableFlush(C)
validate new head
    -> publish HEAD old -> new
```

若 HEAD 仍是 old，B relay 是未发布尾部，可以在 reopen 时丢弃。若 HEAD 已是 new，B relay 是 new head 的 lineage dependency，不能把 B 无条件截断到 old head 的末尾。

reopen 的尾部归一化必须先读取唯一 PublishedHead，再分别保留：

- current file 中 published Revision 所需的物理尾部；
- previous file 中 new head 的 reconstruction 或已承诺 immediate-lineage 所引用的最大物理尾部。

### 3.4 Relay 与文件 retention 的职责边界

在 A/B/C 都被保留时，Relay 完整解决：

```text
C -> B 的下一跳可由 1-bit selector 编码
B -> A 的下一跳可由 1-bit selector 编码
C -> B -> A lineage 连续可导航
```

文件 retention/GC 决定这些历史数据实际保留多久。文件被物理删除后，其内部数据不可访问是删除操作本身的结果，不是地址格式或 Relay 需要抵抗的故障模型。StateStore 不承诺抗删文件，也不为已删除内容维护额外副本。

无论 A 是否仍被保留，latest object 都从 C Base 完整加载，因为 reconstruction 不跟随 Base parent。current-state validator 只验证 reconstruction closure；lineage 诊断可以把目标文件已不在 retention set 报告为历史已退休，而不是 current-state corruption。

## 4. ObjectVersionDict 为什么必须 absolute-normalize

relative address 的 origin 是“写着该 value 的 Map version frame”。Map delta 从祖先继承 entry 后，各 entry 可能来自不同 origin。

因此 Load Map chain 时必须：

```text
relative + source frame number
    -> AbsoluteFrameAddress
    -> in-memory authority map
```

Map Base 写入 C 时再执行：

```text
AbsoluteFrameAddress + output file C
    -> new RelativeFrameTicket
```

例如 B 中的 same-file bit 0 在 C 中通常必须变为 previous-file bit 1。直接复制 raw relative bits 会把 target 从 B 静默漂移到 C。

## 5. 512 GiB 是格式边界，不是策略参数

LSB tagging 先把 `SizedPtr.Serialize()` 左移一位，所以原编码 bit 63 必须为 0。按当前 SizedPtr 的 Offset/Length 交错布局，可开始新 Frame 的最大 offset 是：

```text
512 GiB - 4 bytes
```

RBF 可以写出一个从合法 start 开始、end 超过该值的最后 Frame；但之后不能再开始新 Frame。因此模拟器不能把“512 GiB - TailOffset”简单当作普通可分配字节池，而应使用真实 frame-layout estimator 检查每次 frame start。

同理，B 中的 RelayRevision 必须在 B 仍能开始合法 Frame 时写入。任何成功的普通 Save 都不能把 Store 推入“B 放不下必要的 Base/relay maintenance，C 也装不下最终 evacuation Revision”的死锁状态。

需要区分三个无策略参数的 oracle：

```text
CanCompleteRelay(postSaveState, A, B)
    B 能为剩余 RelaySet 建立被 authority 引用的 relay coverage

CanEncodeEvacuationRevision(postPreparationState, C)
    C 能容纳剩余 EvacuationSet 的 full Bases、full OVD 与 index

CanPrepareAndRotate(postSaveState, A, B, C)
    存在有限的合法 B maintenance plans，随后满足前两项并发布 C
```

`CanCompleteRelay` 只证明 B 中 parent 落点可完成，不证明 C 的 evacuation Revision 装得下。`CanPrepareAndRotate` 才是成功 Save 后的无死锁安全门；它允许先在 B 的普通 published revisions 中渐进写 Base 或 relay，再由 C 中的完整 Base 消除剩余 A reconstruction dependency。三个判断都受 frame-start、单帧 payload、TailMeta、padding/fence 与 ObjectVersionDict 成本影响，首个模拟器宜直接构造 deterministic completion plan，不急于发明闭式公式。

## 6. 一个 Frame 一个 Revision 的派生边界

当前 RBF 带来两道独立限制：

- Payload + TailMeta 的单 Frame 上限约 256 MiB；
- TailMeta 上限 65,535 bytes，可能先于 payload 被大量小 ObjectVersion 的 index 撞满。

所以普通 Revision、RelayRevision 与 C 中 evacuation Revision 都需要完整 size preflight。超限是合法的 fail-closed 结果，不得部分发布。

如果实际支持范围频繁撞墙，再引入 Extent：多个 RBF Frames 形成一个逻辑 DurableGraphFrame，并由最终 manifest/commit frame 形成 publication root。在此之前不把 multi-frame prepare/commit 状态加入首个策略模拟。

## 7. 单 Frame self-ticket 的 fixed point

ObjectVersionDict 可能指向同一 Revision Frame 内刚写出的 ObjectVersions。frame `SizedPtr.Length` 包含 Map 中 self-ticket 的 VarUInt bytes，而 self-ticket 的 VarUInt width 又取决于最终 Length。

writer 可以在 append 前迭代 size-only layout：

```text
known start + assumed ticket width
    -> candidate frame length
    -> SizedPtr.Serialize()
    -> actual ticket width
    -> repeat until stable
```

VarUInt width 只有 1..10 且布局大小单调不减，因此这是有限 fixed-point preflight。不能收敛或超限时 fail closed；无需为此增加第二种 Self 地址格式。

## 8. 正确性与策略必须分层

不可由收益 heuristic 放松的 gate：

```text
ReconstructionFiles(NewHead) ⊆ {B, C}
CanPrepareAndRotate(successful post-save state) == true
all addresses/frame layouts are representable
failure leaves PublishedHead unchanged
```

仍待实验回答的 policy：

- Object 何时 Base、何时 Delta；
- 何时开始把 cold objects 渐进迁移；
- 每次迁移哪些 objects；
- 何时正式创建 C 并迈腿；
- lineage/read/write/storage 各成本能否形成稳定、自适应的统一选择。

模拟应先记录无权重的原始量，再离线比较策略；不能把某个早期加权 score 写成持久事实。
