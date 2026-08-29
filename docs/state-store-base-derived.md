# StateStore 基础设计的派生结论

> 状态：Derived Notes
>
> 更新日期：2026-08-29
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

## 3. Base Revision locator 与可选 Relay

### 3.1 Evacuation 不再推出 RelaySet

```text
EvacuationSet
    = reconstruction Base 位于 A 的 live objects
```

EvacuationSet 全部需要在 C 写 Base。direct-parent 模型曾进一步定义 `RelaySet`，因为 C 无法
直接编码 A address。runtime OVD probe 证明可把 C Base parent 解释为 B Revision locator，再
由 B OVD 按 ObjectId 找到 A/B 中的 prior exact ObjectVersion；所以 EvacuationSet 不再逻辑上
推出 dedicated relay。

### 3.2 三种 relay 形状的差异

当前旧 planner 的 relay 是 zero-payload helpers + empty OVD Delta，C Base 直接定位 helper。
用户澄清的 locator 形状则要求 relay OVD 对 helpers `BindSelf`：

```text
B.RelayRevision:
    RelayEntry1(parent = A.Head1, no field mutation)
    RelayEntry2(parent = A.Head2, no field mutation)
    OVD Delta(parent = old B head) { Object1=Self, Object2=Self }

C.Base(Object1) -> B.RelayRevision locator -> RelayEntry1
```

若 locator 形状仍使用 empty OVD，point lookup 会继承 old B OVD 并跳过 relay records。此时
helper 写入和 TailMeta 都是未消费成本。

即使使用 Self，一个共享 relay Frame 仍是 O(N)：每个对象至少需要 ObjectVersion header、
parent ticket、OVD entry 与 ObjectId index。它可能缩短 historical OVD lookup，但不是 O(1)
通用跳板。

### 3.3 relay-free 的读取与写入交换

canonical AA 在两种 locator 方案中的观察是：

```text
relay + Self:
    ObjectVersion hops = C, Relay, A
    OVD reads          = Relay

relay-free:
    ObjectVersion hops = C, A
    OVD reads          = B, A
```

current reconstruction 都只读 C，logical state、ordinal 与 root 相同。当前没有消费者要求物理
no-op hop，因此 relay-free 是更小的 correctness model；relay 只在实际 lineage read amplification
证明有收益时作为 optimization 重访。若只是想截短 OVD chain，也可实验更轻的 OVD External
checkpoint，而不一定写 domain no-op Delta。

若未来选择 relay，写入顺序仍必须是 B append/durable 后才能写 C dependency；失败窗口、orphan
与 reopen tail 规则随之重新进入设计。relay-free path 不制造这项额外 B publication dependency。

### 3.4 文件 retention 的职责边界

文件 retention/GC 决定历史数据实际保留多久。文件被物理删除后，其内部数据不可访问是删除
操作本身的结果，不是地址格式需要抵抗的故障模型。无论 A 是否保留，latest object 都从 C
Base 完整加载；lineage 诊断与 current reconstruction 必须继续分层。

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

无策略参数的核心容量 oracle 现在可以收窄为：

```text
CanEncodeEvacuationRevision(postSaveState, C)
    C 能容纳剩余 EvacuationSet 的 full Bases、full OVD 与 index

CanPrepareAndRotate(postSaveState, A, B, C)
    存在有限、容量合法的 B published Base migration plan，
    随后 B OVD 可作 lineage locator，且 C candidate 可合法发布
```

`CanCompleteRelay` 不再是 correctness 子问题。只有策略主动选择 dedicated relay 或 OVD-only
checkpoint optimization 时，相关额外容量才加入该候选计划的 preflight；但 B preparatory Base migration 仍可能
是把过大 EvacuationSet 分批转移、最终让 C 可编码的 correctness path。任何成功 Save 都必须
保留至少一条这样的有限完成路径。

旧 `ImmediateRotationPlanner` 是更窄的 executable witness：它在最多一个 B relay Frame 和一个
C evacuation Frame 内直接完成，并精确估算 empty-OVD relay 与 C full OVD。但它仍由 caller
StateMap 驱动，planned records 只是 size grammar，尚未 materialize/append。

后续 runtime probes 已证明 transparent Relay/RelocatedBase 的 logical ordinal 语义，并建立
单一 runtime OVD `LookupLive` 与 Base Revision-locator discriminator。C full OVD 是 current
authority；relay-free 与 relay+Self 的 logical state/root 相同，前者以 B/A OVD reads 换掉 B
maintenance write。所以下一步是迁移 planner authority 并 materialize relay-free plan，不是扩展
multi-frame relay completion。详情见 [`DB-009`](design-branches/0009-base-lineage-parent-locator.md)。

## 6. 一个 Frame 一个 Revision 的派生边界

当前 RBF 带来两道独立限制：

- Payload + TailMeta 的单 Frame 上限约 256 MiB；
- TailMeta 上限 65,535 bytes，可能先于 payload 被大量小 ObjectVersion 的 index 撞满。

所以普通 Revision 与 C 中 evacuation Revision 都需要完整 size preflight；若选择可选 RelayRevision，它也受相同约束。超限是合法的 fail-closed 结果，不得部分发布。

如果实际支持范围频繁撞墙，再引入 Extent：多个 RBF Frames 形成一个逻辑 DurableGraphFrame，并由最终 manifest/commit frame 形成 publication root。在此之前不把 multi-frame prepare/commit 状态加入首个策略模拟。

## 7. 单 Frame contextual self 消除 fixed point

实际 RBF append/read API 始终把 containing Frame 的 `SizedPtr` 交给上层，所以 OVD 无需
在同一 Frame 内重复保存 literal self-ticket。当前 Working Design 改为：

```text
OVD binding 1     -> containing Revision Frame
OVD binding >= 2  -> same/previous RelativeFrameTicket
TailMeta           -> OVD offset + ObjectId/record-offset directory
```

`1` 只在 OVD binding 字段中表示 `BindSelf`；通用 `RelativeFrameTicket` 仍把它视为
invalid。decode 后两种 binding 都立即归一化成 `AbsoluteFrameAddress`。

这样 Payload/TailMeta bytes 与最终 Frame length 不再互相递归，single-frame capacity
preflight 只需计算一次 RBF layout。literal-self 的多 fixed-point 反例、候选对照和
multi-frame 重访条件见 [`DB-008`](design-branches/0008-revision-contextual-self-address.md)。

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
