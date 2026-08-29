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
- `ParentVersion` 的 `RelativeFrameTicket`；
- `LogicalVersionOrdinal`：只随领域状态变化递增，不充当物理 chain depth；
- 后续 RebaseOrDeltify 策略所需、但尚未冻结的度量信息。

首次 ObjectVersion 的 Parent 使用特殊值 0。后续 Base 与 Delta 都保留 lineage parent，因此 Base 不在结构上断开版本关系；但重建当前值时：

```text
Delta -> 跟随 ParentVersion
Base  -> 已包含完整值，不跟随 ParentVersion
```

也就是说，`ParentVersion` 同时携带 lineage，而 `VersionKind` 决定它是否属于 reconstruction dependency。

当前 direct-parent runtime probe 进一步验证了不需要 maintenance kind 或 physical ordinal：

```text
child logical ordinal = parent + 1  -> domain change
child logical ordinal = parent      -> transparent physical maintenance
```

same-version zero-payload Delta 是 Relay，继续跟随 parent 但不改变 logical state；same-version
Base 是 RelocatedBase，自身包含完整状态并停止 reconstruction，但 lineage 仍穿过 parent。
物理先后和无环性由 append address、parent 与 cycle validation 表达。该结论目前只在 synthetic
size-state probe 中证明；未来真实 payload 的 RelocatedBase 仍必须来自 authoritative exact value，
不能用“尺寸相同”代替值相同。

## 5. 跨 A/B/C 的 Lineage Relay

从 A/B 轮转到 B/C 时，某个长期未修改对象的最新 ObjectVersion 可能仍在 A。把它以 Base 写入 C 后，C 中的 1-bit relative address 不能直接表达 A。

为保留 Base 的直接 lineage parent，在 B 中先追加一个未发布的 `RelayRevision`：

```text
C.Base(Object X)
    -> B.Relay(Object X)
    -> A.PreviousVersion(Object X)
```

紧邻轮转创建的 dedicated `RelayRevision` 是物理上合法、但不会成为 PublishedHead 的 maintenance Revision。它为需要跨两代寻址的 ObjectId 保存轻量 forwarding ObjectVersion；forwarder 自身不重复领域状态，只把 lineage/reconstruction parent 指回 A。

当前 one-shot 尺寸原型把 dedicated relay 的 ObjectVersionDict 建模为以旧 B head 为 parent
的 empty Delta：relay helpers 进入 TailMeta directory，但不被这份未发布 OVD 安装；随后 C
Base 的 lineage parent 直接引用它们。C 的 full OVD Base 以该 relay Revision 为 parent，并
把 evacuated objects 安装为 C Self。这样 dedicated relay 不制造一份马上被覆盖的中间
latest-map。该选择目前是 provisional record grammar 的工作形状，不冻结 numeric opcode。

轮转时只有“最新 ObjectVersion 仍在 A”的对象需要 relay。若对象最新版本已在 B、只是其 reconstruction Base 位于 A，则 C 中的新 Base 可以直接把 B 中的最新版本作为 parent，不需要额外中继。

若自适应策略在普通 Save 中渐进创建 relay，则 relay entry 必须进入该次 B 中普通 published Revision，并由同次 ObjectVersionDict 把对应 ObjectId 的 latest binding 更新到该 relay。未被 published ObjectVersionDict 或随后 C Base parent 引用的 relay 只是 orphan，不得减少 RelaySet 或 relay debt。

Relay 解决的是 C 对直接父版本的 1-bit 可编码性，并在相关文件仍被保留时保持 lineage 连续可导航。文件 retention/GC 是独立问题：文件被物理删除后，其内部数据自然不可访问；StateStore 格式不承诺抵抗删文件，也不为此增加额外寻址或冗余机制。

更多推导、成本与边界见 [`state-store-base-derived.md`](state-store-base-derived.md)。

一个可能删除 dedicated relay 的竞争方案是：只把 Base 的 lineage parent 解释为 earlier
Revision locator，再由该 Revision 的 OVD 按 ObjectId 找到 prior ObjectVersion。该方案尚缺
可回放 OVD authority，不属于当前 Working Design；见 [`DB-009`](design-branches/0009-base-lineage-parent-locator.md)。

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
- relay、普通 Save 与轮转 evacuation 都必须在写入前 preflight 这些边界。

越界时 Save fail closed，PublishedHead 不变。当前不自动拆 frame；Extent 是容量证据出现后的重访方案。

## 8. 双腿文件轮转

目标是让每个 published current Revision 的 reconstruction closure 至多涉及两个相邻 RBF 文件，以 1-bit file selector 换取紧凑地址和简单文件角色。

从 A/B 迈向 B/C 时：

1. 规划普通 Save 后的 live ObjectVersionDict；
2. 找出 reconstruction chain 的 Base 位于 A 的全部 live objects；
3. 必要时在 B 追加 RelayRevision，并在写 C 之前使其 durable；
4. 将这些 objects 以完整 Base 写入 C；
5. 本次其他新增、Base 或 Delta 也只写入 C；
6. 完全自包含于 B 的 unchanged objects 可以继续复用 B 中的版本；
7. ObjectVersionDict 在 C 写完整 Base，并相对于 C 重新编码全部地址；
8. 发布前验证 new head 的 reconstruction closure 只包含 B/C；
9. candidate durable 并发布 new head 后，A 才具备 retirement 资格。

轮转过程中物理上允许短暂同时存在 A/B/C。双文件约束描述的是 published current Revision 的读取闭包，不是每个瞬间目录里只能存在两个文件。

## 9. 待研究：统一的自适应 Rebase/Deltify/Rotation 策略

当前不引入 `MaxLogicalChainBytes`、`TargetFileBytes` 等经验超参数。希望研究能否从以下事实量推导统一策略：

- Base 与 Delta 的实际编码大小；
- lineage/reconstruction chain 的累计逻辑成本；
- current/previous 两条腿各自承担的有效与无效读取；
- 本次 Rebase 对下一次轮转 evacuation 的边际影响；
- CurrentFile 剩余的格式可寻址容量；
- RelayRevision、ObjectVersionDict 与 TailMeta index 的实际成本。

候选方向包括在普通 Save 中提前迁移少量 cold objects，把集中 evacuation 转化为渐进过程。该方向目前是待实验假说，不是已选算法；详见 [`DB-007`](design-branches/0007-adaptive-two-leg-rotation-policy.md)。

无论最终策略为何，下列条件不受 heuristic 支配：

- 任意已发布 candidate 都必须 exact、可重建且满足 two-file reconstruction closure；
- RelayRevision 与 candidate frame 必须在各自目标文件的硬容量内；
- 任意成功 Save 后必须满足 `CanPrepareAndRotate`：存在一条有限的合法计划，可在 Current 中通过普通 published maintenance Base/relay 降低债务，并最终在 Next 中容纳剩余 EvacuationSet 的完整 Bases、ObjectVersionDict 与索引；
- 容量、算术或编码越界必须在发布前 fail closed；
- 失败不替换 PublishedHead，也不安装 clean in-memory baseline。
