# DurableGraph StateStore 基础设计

> 状态：Working Design
>
> 更新日期：2026-08-29
>
> 边界：本文记录当前已选择的基础形状与仍在研究的策略问题，不代表已经实现或冻结的 wire format。

## 1. 当前范围与运行模型

首版采用以下约束：

- 只保证 latest published Revision 可恢复；历史 Revision、branch 与 time-travel 暂缓。
- StateStore 由进程独占的 single writer 推进；多 writer、跨 Save 长寿命 reader 暂缓。
- 成功返回的 Save 必须耐 OS crash；中途失败或崩溃后，reopen 只能选择完整 old 或完整 new，不能暴露 mixed/torn state。
- 一个逻辑 DurableGraphFrame 暂时就是一个 RBF BinaryFrame，一个 BinaryFrame 对应一个 Revision；超出单帧能力时 fail closed。未来若容量证据要求扩展，再引入 Extent，把多个 RBF Frame 组成一个逻辑 DurableGraphFrame。

为讨论文件轮转，统一使用：

```text
A = OldPrevious
B = Current
C = Next / NewCurrent / NewHead 所在文件
```

“当前文件”在相对地址语境中指承载该地址值的文件，不是进程里某个可变的全局字段。

## 2. BinaryFrame 与地址

底层使用 [Atelia.Rbf](https://github.com/Atelia-org/atelia/tree/main/src/Rbf) 提供的 append-only BinaryFrame 文件格式，并以 [SizedPtr](https://github.com/Atelia-org/atelia/raw/refs/heads/main/src/Data/SizedPtr.cs) 寻址一个 RBF Frame。

持久格式使用独立的 `RelativeFrameTicket`，进程内使用 `AbsoluteFrameAddress`：

```text
RelativeFrameTicket
    = 1-bit file selector + 63-bit encoded SizedPtr

AbsoluteFrameAddress
    = FileNumber + SizedPtr
```

file selector 放在 `RelativeFrameTicket` 的 LSB：

```text
wire = (SizedPtr.Serialize() << 1) | selector

selector = 0  -> 承载该值的同一文件
selector = 1  -> 承载该值的前一个文件
```

这种编码牺牲 `SizedPtr.Serialize()` 的最高 1 bit，使单文件最大可寻址 offset 约为 512 GiB；首版接受该容量代价，越界 fail closed。LSB tagging 保留小地址的 VarUInt 紧凑性，不会像置 MSB 那样让所有 previous-file ticket 膨胀为接近 10 bytes。

具体 canonical encoding、零值、范围检查和相对/绝对转换见 [`state-store-addressing-design.md`](state-store-addressing-design.md)。

## 3. ObjectVersion 寻址

每个 BinaryFrame 是一个多 ObjectVersion 容器。其 TailMeta 保存：

```text
ObjectId -> OffsetOfObjectVersion
```

一个 ObjectVersion 的逻辑地址为：

```text
(AbsoluteFrameAddress, ObjectId)
```

读取路径为：

1. 以承载 OVD version 的 frame context 解析 binding：`BindSelf` 使用 containing ticket，
   `BindRelative` 才按承载文件号解析 `RelativeFrameTicket`，两者都得到
   `AbsoluteFrameAddress`；
2. 读取并验证目标 RBF Frame；
3. 从 TailMeta 的 ObjectId 索引取得 ObjectVersion offset；
4. 从已经完成完整性验证的 Frame 内解析 ObjectVersion。

当前 RBF 的 L3 完整性边界是整个 Frame，不把 TailMeta-only L2 preview 误称为可信的 ObjectVersion 局部读取。首版假设完整读取并缓存 Revision Frame；真实 read amplification 留给后续测量。

## 4. Object-Level Version Chain

每个引用类型拥有独立 ObjectVersion chain。ObjectVersion 头部保存：

- `VersionKind`：`Base` 或 `Delta`；
- 当前 probe 中复用的 parent `RelativeFrameTicket`；direct 与 Revision-locator 两套 inspection 对 Base 的解释仍并存；
- `LogicalVersionOrdinal`：只随领域状态变化递增，不充当物理 chain depth；
- 后续 RebaseOrDeltify 策略所需、但尚未冻结的度量信息。

首次 ObjectVersion 的 parent 使用特殊值 0。后续 Delta 直接指 exact parent。DB-009 的领先候选把后续 Base parent 解释为 earlier Revision locator，再由该 Revision OVD 按 ObjectId 找 exact parent：

```text
Delta -> reconstruction 跟随 exact parent
Base  -> 已包含完整值；reconstruction 停止，lineage 才解析 Revision locator
```

locator discriminator 中，`VersionKind` 决定 parent ticket 的解释与它是否属于 reconstruction dependency；现有 direct oracle/旧 planner 仍把 Base parent 当 exact frame。未发布 wire 尚未裁决是否永久由 `VersionKind` 承担该 discriminant，也不为实验并存增加 tag。若 DB-010 选择 Revision 共同 prior-snapshot anchor，Base per-record ticket 将进一步删除。

runtime probes 进一步验证了不需要 maintenance kind 或 physical ordinal：

```text
child logical ordinal = parent + 1  -> domain change
child logical ordinal = parent      -> transparent physical maintenance
```

same-version zero-payload Delta 是 Relay，继续跟随 parent 但不改变 logical state；same-version
Base 是 RelocatedBase，自身包含完整状态并停止 reconstruction，但 lineage 仍穿过 parent。
物理先后和无环性由 append address、parent 与 cycle validation 表达。该结论目前只在 synthetic
size-state probe 中证明；未来真实 payload 的 RelocatedBase 仍必须来自 authoritative exact value，
不能用“尺寸相同”代替值相同。

## 5. 跨 A/B/C 的 Base Lineage Locator

从 A/B 轮转到 B/C 时，某个长期未修改对象的 latest ObjectVersion 可能仍在 A。把它以 Base
写入 C 后，C 中的 1-bit relative address 不能直接表达 A；但 C 可以表达 B 中一个 earlier
Revision。当前领先方向只对 Base 使用 Revision locator：

```text
C.Base(Object X)
    -> B.PublishedRevision locator
    -> LookupLive(B.OVD, ObjectId X)
    -> A.PreviousVersion(Object X)
```

Delta parent 仍直接指 exact ObjectVersion，因为它是 current reconstruction dependency。Base
已经包含完整状态，locator 只用于 lineage；current reconstruction 在 C Base 停止，不把 B/A
OVD reads 计入 two-file reconstruction closure。

runtime OVD discriminator 已以 C full OVD 作为唯一 current authority，执行性验证 AA/BA/BB：
B OVD 可分别把 prior exact head 解析到 A 或 B，relative binding 始终按其 source Revision scope
absolute-normalize。Remove 与 OVD Base absence 是 decisive absent；missing/malformed locator
fail closed。

旧 one-shot planner 仍使用 `direct C Base -> B relay helper -> A`，并把未发布 relay Revision
建模为 zero-payload helpers + empty OVD Delta。用户澄清的原始 relay 设想则是 helper + OVD
Self，再由 C Base 把该 relay Revision 当 locator。这两种形状不能混叫：locator 遇 empty relay
OVD 会继承 old B OVD 并跳过 helper。

对比实验中，relay + Self 与 relay-free locator 的 current state、logical ordinal 和 lineage root
相同。relay 额外产生 O(N) B records/OVD/TailMeta，但可能减少历史 OVD reads；当前没有
correctness consumer 要求显式物理 no-op hop。因此 relay 不再是硬约束，只作为可测的 lineage
read optimization 保留。planner 尚未迁移到 runtime OVD authority，旧 path 暂不删除。

文件 retention/GC 仍是独立问题：文件被物理删除后，其内部数据自然不可访问；StateStore
格式不承诺抵抗删文件，也不为此增加额外寻址或冗余机制。

候选证据与后续裁决门见 [`DB-009`](design-branches/0009-base-lineage-parent-locator.md)；把 Base
per-record locator 进一步合并到 Revision 共同 prior-snapshot anchor 的分叉见
[`DB-010`](design-branches/0010-base-lineage-anchor-scope.md)。

## 6. ObjectVersionDict

每次 Save ObjectGraph 都产生一个 Revision。Revision 的关键元数据 `ObjectVersionDict` 保存：

```text
ObjectId -> latest ObjectVersion address
```

ObjectVersionDict 拥有固定 ObjectId 和元数据身份，并复用 Object-Level Version Chain；它是当前对象成员集合与最新 record binding 的 authority。

内存中的 ObjectVersionDict value 必须是 `AbsoluteFrameAddress`。`RelativeFrameTicket` 只属于某个具体 ObjectVersionDict version 的 wire encoding：

- decode Map delta 时，按该 delta 所在文件解析新 value；
- 从祖先 Map version 继承的 value 保持 absolute，不按最新 Map frame 重新解释；
- Map Base/Rebase 写入新文件时，逐项相对于输出文件重新编码。

每个 BinaryFrame 的 TailMeta 固定位置保存当前 ObjectVersionDict version 的 frame 内
offset。承载它的 Revision ticket 已由 RBF append/read context 提供，不在 Frame 内重复
序列化。OVD 中指向本 Revision 新 records 的 value 使用字段级 `BindSelf`，其他 value
才使用 `RelativeFrameTicket`；两者读取后都立即 absolute-normalize。具体语法与淘汰
self-ticket fixed point 的依据见 [`state-store-addressing-design.md`](state-store-addressing-design.md)
和 [`DB-008`](design-branches/0008-revision-contextual-self-address.md)。

## 7. 一个 BinaryFrame 对应一个 Revision

首版把一次 Revision 的 ObjectVersions、ObjectVersionDict version 与索引放在同一个 RBF Frame 中。该选择有意利用：

- 多个小 ObjectVersion 共享 RBF fixed overhead、CRC、fence 与 padding；
- 一次完整 Frame 验证形成简单 candidate 单元；
- graph load 时同一 Revision 内的 ObjectVersion 可以共享 Frame cache。

已知硬边界：

- RBF 单 Frame 的 Payload + TailMeta 约小于 256 MiB；
- TailMeta 小于等于 65,535 bytes；
- 普通 Save 与轮转 evacuation 都必须在写入前 preflight 这些边界；若策略选择额外 relay/checkpoint，也同样受限。

越界时 Save fail closed，PublishedHead 不变。当前不自动拆 frame；Extent 是容量证据出现后的重访方案。

## 8. 双腿文件轮转

目标是让每个 published current Revision 的 reconstruction closure 至多涉及两个相邻 RBF 文件，以 1-bit file selector 换取紧凑地址和简单文件角色。

从 A/B 迈向 B/C 时：

1. 规划普通 Save 后的 live ObjectVersionDict；
2. 找出 reconstruction chain 的 Base 位于 A 的全部 live objects；
3. 若 C 暂时无法容纳全部 EvacuationSet，在 B 通过有限、published Base migrations 分批降低集合，并重算 authority；
4. 选择最终 B PublishedRevision 作为 Base lineage locator；只有测量证明有益时才附加可选 relay/checkpoint，并在写 C 前使其 durable；
5. 将剩余 EvacuationSet 以完整 Base 写入 C；
6. 本次其他新增、Base 或 Delta 也只写入 C；
7. 完全自包含于 B 的 unchanged objects 可以继续复用 B 中的版本；
8. ObjectVersionDict 在 C 写完整 Base，并相对于 C 重新编码全部地址；
9. 发布前验证 new head 的 reconstruction closure 只包含 B/C；
10. candidate durable 并发布 new head 后，A 才具备 retirement 资格。

轮转过程中物理上允许短暂同时存在 A/B/C。双文件约束描述的是 published current Revision 的读取闭包，不是每个瞬间目录里只能存在两个文件。

## 9. 待研究：统一的自适应 Rebase/Deltify/Rotation 策略

当前不引入 `MaxLogicalChainBytes`、`TargetFileBytes` 等经验超参数。希望研究能否从以下事实量推导统一策略：

- Base 与 Delta 的实际编码大小；
- lineage/reconstruction chain 的累计逻辑成本；
- current/previous 两条腿各自承担的有效与无效读取；
- 本次 Rebase 对下一次轮转 evacuation 的边际影响；
- CurrentFile 剩余的格式可寻址容量；
- optional relay/checkpoint、ObjectVersionDict 与 TailMeta index 的实际成本。

候选方向包括在普通 Save 中提前迁移少量 cold objects，把集中 evacuation 转化为渐进过程。该方向目前是待实验假说，不是已选算法；详见 [`DB-007`](design-branches/0007-adaptive-two-leg-rotation-policy.md)。

无论最终策略为何，下列条件不受 heuristic 支配：

- 任意已发布 candidate 都必须 exact、可重建且满足 two-file reconstruction closure；
- candidate frame 必须在目标文件的硬容量内；若选择额外 B maintenance，它也必须完整 preflight；
- 任意成功 Save 后必须满足 `CanPrepareAndRotate`：存在有限、合法且容量可承受的 Current published Base migration/checkpoint plan，随后能在 Next 中容纳剩余 EvacuationSet 的完整 Bases、ObjectVersionDict 与索引；dedicated relay 可选，但 preparatory Base migration 可能是 correctness 所需；
- 容量、算术或编码越界必须在发布前 fail closed；
- 失败不替换 PublishedHead，也不安装 clean in-memory baseline。
