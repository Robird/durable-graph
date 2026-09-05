# MultiSegmentStateStoreProbe 活跃工作集

> 状态：G0-G4 complete / Stage B membership head map and shared serialization leaf implemented
>
> 最近校准：2026-09-05

## 目标

用最小、可执行的机制验证多历史 Segment StateStore：持久引用可指向同目录内任意更早文件，文件达到
应用配置的 soft rollover threshold 后，在下一次 Save 前独立切换，冷 ObjectVersion 不因文件切换被强制
Base。

本探针不修改 `src/DurableGraph`，不依赖或替代 `TwoLegRotationProbe`；范围在 in-memory G0-G4 闭合后
结束。真实 filesystem/RBF 与产品整合属于独立阶段 B。

阶段 A 以 [`TARGET-DESIGN.md`](TARGET-DESIGN.md) 为全景规范；阶段 B 见
[`STATESTORE-SUBSYSTEM-DESIGN.md`](STATESTORE-SUBSYSTEM-DESIGN.md)。施工入口见
[`GOAL-G0-G4.md`](GOAL-G0-G4.md)，现仅作为 completed historical work order。本文件只保留当前工作集。

## 已选择不变量

- FileNumber 是 1-based `UInt32`，`0` 非法；published FileNumber 不复用，overflow fail closed；
- Store 目录与 canonical filename 直接决定物理路径，无集中映射与 StoreId 文件头；
- runtime authority 是 absolute `{FileNumber, FrameTicket}`；
- wire 保存 canonical `VarUInt32 BackwardFileDistance`，`0` 同文件，任意正值指更早文件；
- 不采用固定 `UInt16` horizon；
- future、underflow、zero target、non-canonical VarUInt 和 required zero FrameTicket fail closed；
- OVD `BindSelf` 与 optional None 保持字段局部语义，不混入通用 required reference；
- `RolloverThresholdBytes` 只在 writer acquisition 前与 existing tail 比较；crossing append 留在当前文件，
  下一次 Save 才轮转；threshold 不是文件大小上限；
- 每个 Save 只取得一个 writer lease、只 append 一个 Revision Frame，final Segment 确定后只 render 一次；
- v1 可保留所有 published 文件，不承诺总磁盘、依赖文件数或 cold-read fan-out 有界。

## 当前具备

- 1-based checked FileNumber 与十位十进制 `.rbf` canonical filename round-trip；
- strong `{OffsetBytes, LengthBytes}` FrameTicket、runtime `AbsoluteFrameAddress` 与 required
  `RelativeFrameTicket`；
- same/previous/>65,535/UInt32 最大距离的 absolute-relative round-trip；
- canonical VarUInt32/VarUInt64 writer-reader，以及 overlong、overflow、truncated 与 zero-ticket fail-close；
- graph traversal 的 same-file strictly-earlier validation 与 provisional single-Frame envelope hard bounds；
- in-memory append-only Segment/Store、origin-free logical plan、origin-dependent render/measure；
- tail-triggered `RolloverThresholdBytes`：existing tail 达到 threshold 时下一 Save 在 render 前轮转；crossing
  append 与 empty oversize 原地容纳；final origin 只 render 一次；hard bound 或 FileNumber overflow typed reject
  且不 append/publish；
- immutable Revision/OVD/ObjectVersion model：shared optional PriorRevision 位于 Revision；OVD Base/Delta 与
  BindSelf/External/Remove、ObjectVersion Base/Delta 均有 canonical shape；
- exact-head current materializer：F1-F4 中冷对象 head 留在 F1，热对象 Delta 链推进到 F4；OVD Base 的
  lineage-only prior 只校验地址、不进入 current closure；required missing、non-earlier、cycle、wrong ObjectId/
  parent state/ordinal 均 fail closed，且失败不暴露 partial state；
- exact-parent Save pipeline：workload normalization、显式 object/OVD selection、单一 origin-free RevisionPlan、
  whole-candidate render/admission、append 与 logical publication 分层；OVD Base 重编码历史 head 不 relocation；
- shared-prior lineage reader：Delta 走 exact parent，Base 经 prior OVD point lookup；current OVD replay 与 lineage
  共用 `ExactOvdMaterializer`，broken lineage 不反向破坏已验证 current load；
- append-before-publish failure 只留下不可见 candidate；cache-install failure 后 new head 仍是 authority，并可从
  exact head 重建；Stage-A `RevisionCommitSession` 只允许 empty Store 起步，不冒充 reopen；
- deterministic workload/generator/composer、pure ReadAmplification+BaseBudget policy 与 G3 adapter；
- admitted-only evaluator：W/P/F 来自实际 Segment tail，R 来自 OVD required frames 与 object paths 的 unique
  full-Frame union，L 同采样；Delta/Base references 与 `L=0` undefined 均显式；
- SameStateRebase witness 证明恰一次 rebase 保持 ordinal、增加总 W、降低 rebase 后累计 R；统一 corpus 的
  all-Delta/all-Base/adaptive 均产生 deterministic admitted raw report；
- 独立 core/test `.slnx`，不进入产品 solution 或 TwoLeg 子树。

当前 Frame envelope、relative codec、OVD encoding estimate 和 synthetic plan 都只是内存探针 grammar；soft
threshold 的 overshoot 上界依赖 one-Revision/one-Frame discipline，严格文件大小上限不是当前保证；
没有正式 RBF/OVD wire 或持久 StateStore。

## 当前焦点

阶段 A 已闭合并停止实现。用户已按
[`STATESTORE-SUBSYSTEM-DESIGN.md`](STATESTORE-SUBSYSTEM-DESIGN.md) 启动阶段 B；产品侧现有空壳
`src/DurableGraph.StateStore`、已有首个纵切的 `src/DurableGraph.StateStore.Storage`，以及 BCL-only
`src/DurableGraph.StateStore.Serialization`；三者均有配套 xUnit 项目，Serialization.Tests 当前有 65 个 cases。
`DurableGraph.StateStore` 单向引用 `DurableGraph.StateStore.Storage`；Storage 再引用 BCL-only
`DurableGraph.StateStore.Serialization`，并直接 ProjectReference 当前 `RbfSegmentStore` checkout 及地址模型所需
`Data/SizedPtr`。上层 StateStore 尚不直接引用 Serialization，等第一个真实 ObjectVersion consumer 再建立该边。
Storage 当前已有 runtime
`FrameAddress { UInt32 FileNumber, SizedPtr FrameTicket }`、`FileScope`、immutable membership-only
`StateRevision`、纯 live-head map materializer、provisional canonical wire 与真实 `StateRevisionStore`
append/read/head reconstruction。
`StateRevisionWireReader/Writer` 与 `FrameAddressWireCodec` 已共用 Serialization 的 internal
`BinaryPayloadReader/Writer`；`StateRevisionStore` 通过 RBF `BeginAppend/EndAppend` 把 wire 直接编码到
`PayloadAndMeta`，不再构造并复制完整 `byte[]`。Storage 自有 VarUInt 实现已删除，既有 v1 golden bytes 保持
不变。wire codec 不再接收 containing Frame offset；same-file chronology 在持有完整 absolute addresses 的
live-head traversal 层验证，严格下降关系同时保证 parent chain 无环，无需 visited set。
`StateRevisionStore.ReadLiveObjectHeads(exactRevisionHead)` 是唯一 membership replay authority；它在 traversal
内部保留 `{FrameAddress, StateRevision}`，返回 immutable、ObjectId 升序的 shallow
`{ObjectId -> absolute FrameAddress}` map。local ID 指向 containing Revision Frame，Base external ID 保留记录的
旧 head，Base 早停；不读取或验证 ObjectVersion record。纯语义与真实文件测试已覆盖 exact head values、reopen、
多 Delta、Remove/reappearance、跨 Segment checkpoint，以及编码失败不提交 partial Frame；若 writer
acquisition 触发轮转，可留下并复用 header-only active Segment。实施边界见
[`WORK-ORDER-STATESTORE-LIVE-OBJECT-HEADS.md`](WORK-ORDER-STATESTORE-LIVE-OBJECT-HEADS.md)。尚无 ObjectVersion
payload record、自动 checkpoint policy、skip、published head/durability 或正式 wire compatibility。

基础能力缺失时，先检查冻结的 `TwoLegRotationProbe` 是否已有同领域机制。只复用代码片段、测试意图或
设计思想，不建立项目依赖，也不带回 A/B/C、A-debt、evacuation、paired candidate 或 terminal settlement。

## 近期 roadmap

无阶段 A 后续实现项。Probe 保留为 executable specification；阶段 B 的 exact
`{ObjectId -> FrameAddress}` enumeration 已闭合。用户已选择先设计估算 DTO → 稀疏写计划的固定
ReadAmplificationBaseBudgetPolicy，具体契约建议见
[DB-015](../../docs/design-branches/0015-statestore-object-representation-policy.md)，尚未实现。
近期先用人工估算验证纯 selector，再由真实 ObjectVersion/Save consumer 接入估算、内容编码和提交；
序列化算法不再是策略验证的前置条件。只在真实内容 consumer 出现时决定是否加入
`StateStore -> Serialization` 引用；不把 Probe 项目或 benchmark infrastructure 搬入产品程序集。

## 未闭合事项

阶段 A 无未闭合事项。产品策略方向已包含 NoChange 的 SameStateRebase 选择；其真实 Base 获取机制、
估算/H 的来源和提交更新、自动 OVD policy 与参数默认值仍待阶段 B 后续证据，不影响本 Probe 完成。

## 明确暂缓

- 自动文件删除、incremental segment cleaner、冷热分层与跨 Store merge；
- Extent/multi-frame Revision；
- Object payload、head durability、orphan reconciliation；
- EventJournal acquisition、product API、NuGet compatibility 或正式 wire migration；
- 任何总分、默认 file target 或 cold-read SLO。
