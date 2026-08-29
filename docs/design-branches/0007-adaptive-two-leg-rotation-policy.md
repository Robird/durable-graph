# DB-007：自适应双腿轮转与 Rebase/Deltify 策略

> 状态：Open
>
> 创建日期：2026-08-28
>
> 更新日期：2026-08-29
>
> 当前方向：先建立纯内存模拟模型，比较统一策略；不先冻结固定链长、目标文件大小或搬迁字节阈值。

## 问题

在 current Revision 的 reconstruction closure 至多引用两个相邻文件的硬约束下，能否用同一组无权重事实量，同时决定：

- 单个 ObjectVersion 本次写 Base 还是 Delta；
- 普通 Save 是否顺带迁移 cold object；
- 选择哪些 objects 渐进迁移；
- 何时创建下一文件并完成 A/B → B/C 的迈腿；
- 如何在 read、write、lineage、storage 与单次暂停之间取得自适应平衡。

这是一项待验证假说。当前尚不能宣称 `TotalPersistBytes`、Previous-file ratio 或任何单一标量已经足以统一这些决策。

## 已选择的硬约束

以下来自主设计，不属于本分叉的策略选项：

1. 新 ObjectVersion 只写入 CurrentFile。
2. 每个 published current Revision 的 reconstruction closure 至多引用 Current/Previous 两个相邻文件。
3. 从 A/B 轮转到 B/C 时，Base 位于 A 的 live objects 必须在 C 产生完整 Base。
4. C 中 relocated Base 的 lineage locator 必须落在 B，并由 B 的权威 OVD 找到 prior exact ObjectVersion；dedicated relay 不是 correctness hard constraint。
5. ObjectVersionDict 内存使用 absolute address；输出时相对于目标 frame 编码。
6. `RelativeFrameTicket` 的约 512 GiB frame-start 范围、RBF 单 Frame 和 TailMeta 上限是格式安全门，不是调优参数。
7. 任意成功 Save 后必须满足 `CanPrepareAndRotate`：存在有限、容量合法的 B published Base migration/checkpoint plan，随后 C Revision 能容纳剩余 EvacuationSet 的完整 Bases、OVD 与 index；dedicated relay 可选。
8. 物理删除文件后的数据不可访问不属于格式需要抵抗的故障模型。

## 候选策略族

### 候选 A：对象局部一步成本

沿用 StateJournal 风格，以 Base/Delta 编码大小和已有 chain cost 做局部经济选择。

优点：简单、O(1) 决策形状清楚。

风险：局部最优未必为未来 relay/rotation 留出可完成路径；多 ObjectVersion 共享 Revision Frame 时，per-object bytes 也不等于整图物理读取。

### 候选 B：Previous/Current 重量平衡

估算 current Revision 的 unique reconstruction frames 中，有多少读取来自 Previous、多少来自 Current。当后腿较轻时迈步。

优点：直接贴近“两条腿交替承重”的直觉。

风险：Previous read ratio 不直接等于 evacuation Base bytes，也不能单独保证 C 能编码最终 evacuation Revision。

### 候选 C：渐进 cold migration

每次普通 Save 额外选择少量 cold objects，在 Current 中提前建立 Base 或 OVD checkpoint，使未来正式轮转不出现集中 full-copy 或历史 lookup 峰值。若实验 relay，它必须由权威 OVD 安装；未被 authority 引用的 helper 只是 orphan。

优点：有机会平滑写入与 pause。

风险：迁移本身会消耗 Current 空间；选择顺序和每轮数量若不收敛，可能制造更多 lineage/storage overhead。

### 候选 D：统一的边际收益/成本规划

对每个候选动作计算其对当前与未来状态的事实性变化：

```text
Base(O)
Delta(O)
CheckpointOrRelay(O)
Stay
Rotate
```

模拟器记录各动作造成的 read/write/lineage/frame-layout 变化，再尝试无固定阈值的排序、竞争或全局规划。

优点：最接近统一策略目标。

风险：可能只是把多个未验证权重藏进一个复杂 score；必须用简单策略作对照。

## 不带权重的原始观测量

### File / Frame

- A/B/C 的 FileNumber 与 TailOffset before/after；
- candidate Frame start；
- payload、TailMeta、padding、fixed overhead、fence 与 total bytes；
- 是否还能开始下一 Frame；
- TailMeta entry count/bytes；
- full-frame read bytes、useful ObjectVersion bytes 与 cache hit。

### Object

- ObjectId、latest head 与 terminating Base 的绝对地址；
- reconstruction closure 的 FrameTicket 集合；
- Base/Delta candidate encoded bytes；
- lineage locator、OVD lookup 与 optional relay/checkpoint encoded bytes；
- logical chain bytes、depth 与涉及的 unique frames；
- 距离上次领域修改和上次 Base 的 Save 数；
- 本次选择及事实性 reason tags。

### Revision / Rotation

- live、changed、new、unreachable、evacuation 与 optional maintenance object counts；
- A/B/C unique reconstruction frame sets 与 bytes；
- ordinary write、Base、Delta、optional relay/checkpoint、ObjectVersionDict 与 index bytes；
- Base-parent OVD lookup frames/bytes；
- deterministic evacuation plan 的 Frame 数与最终 TailOffset；
- `CanEncodeEvacuationRevision`、`CanPrepareAndRotate` 与 `CanRotateNow`；
- materialized logical graph hash/equality oracle。

所有比例、权重和评分均从这些原始量离线派生，避免模拟数据被首个候选算法污染。

## 不变量 oracle

策略比较必须共享同一组 correctness oracle：

```text
Materialize(candidate) == expected logical graph
ReconstructionFiles(candidate) ⊆ {Current, Previous}
CanPrepareAndRotate(successful post-state) == true
all frame starts/tickets/layouts are representable
failed plan leaves published state unchanged
```

`CanEncodeEvacuationRevision` 只覆盖当前 C 中 full Bases/OVD/index；`CanPrepareAndRotate` 还必须构造有限、容量合法的 B published Base migration/checkpoint plan，并验证最终 B OVD 可作 lineage locator。preparatory Base migration 可能是 correctness 所需；只有 dedicated relay 是可选 optimization。首版让 deterministic frame-layout estimator 实际构造 completion plan，以覆盖单帧 payload、TailMeta、padding、fence、frame-start 与 ObjectVersionDict 开销。

## 最小 workload 矩阵

- 大量 cold objects + 少数 hot objects；
- 单个 hot object 高频小 Delta；
- 对象持续增长、持续缩小与大小振荡；
- 突发大批修改后长时间稳定；
- 大量小 ObjectVersions 共享 Revision Frame；
- 少量大 ObjectVersions 造成整帧无效读取；
- latest head 在 B、但 terminating Base 在 A；
- latest head 仍在 A，由 B OVD locator 继承到 A；
- 接近 512 GiB frame-start、256 MiB frame 与 64 KiB TailMeta 边界；
- 连续多次 A/B → B/C 轮转。

每个 workload 都至少比较：AlwaysBase、AlwaysDelta-when-legal、StateJournal-style local cost、Previous-ratio、渐进 cold migration 与统一候选策略。

## 当前裁决

- `TotalPersistBytes` 可作为待测的 object-local 事实量，但当前不进入 wire，也不视为统一充分统计量。
- Previous/Current ratio 先作为观测量，不取得 correctness authority。
- 固定 `MaxLogicalChainBytes`、`TargetFileBytes` 与 migration-byte budget 暂缓。
- 约 512 GiB representability、one-frame bounds 与 `CanPrepareAndRotate` 是不可调的格式 gate，必须从第一个模拟器开始建模。
- 第一项实现工作应是纯内存、deterministic、可穷举小状态的策略模拟，而不是直接绑定真实 RBF I/O。

## S1b 阶段性证据：ObjectPayloadOnly RBF envelope

TwoLegRotationProbe 已把 FrameTicket 从序号推进为 `(OffsetBytes, LengthBytes)`，并按本地 RBF draft v0.40 复刻 HeaderFence、24-byte fixed frame overhead、4B padding、trailing Fence、TailOffset、原生 start 与 DurableGraph relative-start 边界。该层对给定 Payload/TailMeta 长度的 RBF envelope 是精确的。

S1b 的 Revision accounting 有意保持不完整，固定标记 `ObjectPayloadOnly`：只计 synthetic ObjectVersion payload，TailMeta=0，排除 ObjectVersion header、ObjectVersionDict、TailMeta index 与 relative-ticket VarUInt。由此已经可以观测 Base/Delta payload write、unique reconstruction frames、required payload 与同帧 co-read；不能宣称完整 Revision bytes、完整 Revision capacity gate 或策略 winner。首个固定 generated workload 中，AlwaysBase 呈现“写更多、最终读更少”，AlwaysDelta 呈现“写更少、最终读更多”；这只证明度量管线能显现 tradeoff，不能据此选择统一策略。

这一后续依赖已由 S1d 的明确版本化、但不承诺兼容的 `ProvisionalRevisionV0`
满足；它以独立 accounting profile 重跑边界和候选策略，没有修改或覆盖旧 baseline 数字。

## S1c 阶段性证据：ObjectPayloadReadAmplification3

第三条基线保留 StateJournal `VersionChainStatus.ShouldRebase` 的 ratio=3 与局部判据形状：Base 不大于 Delta 时立即 Base；否则当本次选择 Delta 所节省的 payload 乘 3 已不大于父版本 reconstruction payload 累计时 Base。相等时选 Base。

该策略没有复制 StateJournal 对 one-object-per-frame RBF/metadata 的 38-byte 估值。DurableGraph 当前多个 ObjectVersion 共享 Frame，且 S1b 只允许 `ObjectPayloadOnly`，此时引入固定 per-object frame overhead 会同时双计共享成本并偷渡尚未冻结的 metadata 格式。因此类型名明确为 `ObjectPayloadReadAmplification3`，不是 exact StateJournal port。

每个 ObjectVersion 暂存 `ReconstructionObjectPayloadBytes`：Base 等于自身 payload，Delta 等于 parent cumulative 加自身 payload。它避免 runner 私有 dictionary 成为第二权威，并由 materialization oracle 独立重算后 fail closed；但当前不计入 layout，不代表已选择 wire 字段。

四个命名 workload 的 executable matrix 已证明 threshold/tie/reset、`Base <= Delta`、hot/cold shared-frame 与 fixed-seed mixed 的确定性。报告只将 write events 汇总；read 只报告 final post-save snapshot。首轮中第三策略的 modeled write/read 落在两个极端基线之间，但这只证明局部累计判据能形成不同决策，不代表已经选择 winner。shared-frame co-read、完整 codec bytes、relay/evacuation debt 与 `CanPrepareAndRotate` 仍不在该策略输入中。

## S1d 阶段性证据：ProvisionalRevisionV0

检查本地 Atelia RBF commit `fec021295828fcfe638434d69d04ff078c87c8ce` 后，确认现有
envelope 公式与容量边界正确；同时发现 RBF append/read context 已提供 containing
`SizedPtr`。此前设想的 literal self-ticket 不是底层要求，而且可能出现多个稳定宽度，
“迭代到稳定”不足以定义 canonical wire。当前 Working Design 改为 OVD 字段级
`BindSelf=1`，通用 `RelativeFrameTicket` 的 `0=None、1=invalid、>=2=required` 不变。
具体候选对照和 multi-frame 重访条件见 DB-008。

`ProvisionalRevisionV0` 为每次 run 选择独立 physical address space，按一个临时 grammar
精确计入 domain record headers、synthetic body、OVD record、TailMeta directory、canonical
VarUInt、RBF padding/fixed/fence；组件 provenance 与实际 append ticket/layout 必须一致。
它不写/读 bytes，不计入当前 simulator-only result-size/ordinal/cumulative 字段，也不冻结
RBF Tag、opcode 或最终 record 顺序。

同一 hot/cold workload 的 V0 modeled file/final-read frame bytes 为：AlwaysBase
`1900/1148`、AlwaysDelta `1440/1408`、local `1516/1148`；fixed-seed mixed 为
`536/184`、`464/448`、`468/300`。这些结果保留了 S1c 的方向，但 metadata 对三个策略
接近常量，尚未使 object-local payload policy 看见 shared-frame/rotation cost，也不构成
winner。write 汇总与 final read snapshot 继续分栏，不定义跨 Save `TotalReadBytes`。

V0 已能 fail closed 检查当前单文件 Save grammar 的 TailMeta、Payload+TailMeta、frame start
和 checked arithmetic。S1e 随后补上 C evacuation 的 mixed Self/Previous full OVD 与
dedicated Relay record，但仍没有一般 `CanPrepareAndRotate` completion plan，所以不能称为
完整 rotation capacity gate。

## S1e 阶段性证据：ImmediateRotationPlan

V0 estimator 的唯一尺寸算法现已改接显式 grammar IR；原 `Frame + SaveStep` 入口仅作为
普通 Save adapter，旧 golden 不变。显式 IR 能分别表示 domain Base/Delta/Relay、带 parent
的 OVD Base/Delta，以及 Self/External/Remove binding，因而不再把“OVD Base”等同于
“首 Revision 且所有 binding 都是 Self”。

纯 `ImmediateRotationPlanner` 从 caller 提供的 absolute StateMap 和已验证 reconstruction
chain 推导：

```text
EvacuationSet = terminating Base 位于 A
RelaySet      = EvacuationSet 中 latest head 仍位于 A
```

若 RelaySet 非空，planner 在 B 当前 tail 估算一个 dedicated relay Revision：每个对象有一个
zero-synthetic-payload helper record，OVD 是以旧 B head 为 parent 的 empty Delta，TailMeta
仍索引全部 helpers。该 OVD 不安装 relay；helper 由随后 C Base 的 lineage parent 直接引用。C 在初始
offset 估算所有 EvacuationSet 的 full Bases 和覆盖全部 live ObjectId 的 OVD Base：evacuated
对象使用 contextual Self，留在 B 的对象使用 Previous external binding。Projected StateMap
只从这份 full OVD 解码派生。

三对象 `AA(head/base@A) / BA(head@B, base@A) / BB(head/base@B)` fixture 得到
`Evacuation={AA,BA}`、`Relay={AA}`；临时 grammar 下 relay ticket 为 `32/40`，C ticket 为
`4/88`。测试覆盖无 relay、空图、输入顺序确定性、B relay TailMeta overflow、C combined
capacity overflow、失败零 mutation 与同 store 重试。

该结果只构成“至多一个 B relay Frame + 一个 C evacuation Frame”的 immediate-rotation
constructive witness。成功可证明这个具体 post-state 存在一条 preparation path；失败既不能
排除多个 relay Frames，也不能排除先做若干 published B maintenance Bases，因此不能推出
`CanPrepareAndRotate == false`。

后续 runtime semantic probe 已把属性明确改为 `LogicalVersionOrdinal`：在 synthetic
size-state 模型中，same-version zero-payload Delta/Base 分别表达 Relay/RelocatedBase，
不增加 maintenance kind 或 physical ordinal；logical equality 只观测
`(BasePayloadBytes, LogicalVersionOrdinal)`，current reconstruction 与 historical lineage
inspection 保持分层。该 probe 仍未
append 或 materialize planner records。

S1g 已建立不接收 caller StateMap 的 runtime OVD `LookupLive`，并用 C full OVD authority 完成
DB-009 discriminator。用户澄清的 relay + OVD Self 与 relay-free locator 得到相同 current
state、logical ordinal 与 lineage root；前者 AA exact hops/read 为 `C/Relay/A` 与 `Relay`，后者
为 `C/A` 与 `B/A`。relay record 若配 empty OVD，则 locator 会跳过 helper。

因此 dedicated RelayRevision 不再是 correctness hard constraint，而是可能用 B write 换历史
OVD read 的候选 optimization。当前 `ImmediateRotationPlanner` 仍是旧 direct-parent + empty OVD
形状，source authority 仍由 caller StateMap 提供；在它迁移并 materialize 前不删除旧代码，
也不扩展 multi-frame relay completion planner。进一步把 Base per-record locator 合并到 Revision
共同 prior-snapshot anchor 的分叉见 DB-010。

## 重访触发条件

- 模拟表明无参数策略不能保证 `CanPrepareAndRotate` 或产生明显振荡；
- 某类 workload 中 read/write/pause 出现稳定劣势，需要引入显式运营目标；
- 真实 RBF read log 提供 frame cache/locality 数据；
- lazy/partial Load 产生持久 chain-summary 消费者；
- one-frame Revision 碰到真实容量失败，开始设计 Extent。

## 相关材料

- `docs/state-store-base-design.md`
- `docs/state-store-base-derived.md`
- `docs/state-store-addressing-design.md`
- `E:/repos/Atelia-org/atelia/src/StateJournal/Internal/VersionChainStatus.cs`
