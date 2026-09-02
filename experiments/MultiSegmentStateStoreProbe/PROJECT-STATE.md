# MultiSegmentStateStoreProbe 活跃工作集

> 状态：G0-G4 complete / awaiting promotion decision
>
> 最近校准：2026-09-02

## 目标

用最小、可执行的机制验证多历史 Segment StateStore：持久引用可指向同目录内任意更早文件，文件达到
应用配置尺寸后独立切换，冷 ObjectVersion 不因文件切换被强制 Base。

本探针不修改 `src/DurableGraph`，不依赖或替代 `TwoLegRotationProbe`；范围在 in-memory G0-G4 闭合后
结束。真实 filesystem/RBF 与产品整合属于独立阶段 B。

阶段 A 以 [`TARGET-DESIGN.md`](TARGET-DESIGN.md) 为全景规范；阶段 B 见
[`STATESTORE-SUBSYSTEM-DESIGN.md`](STATESTORE-SUBSYSTEM-DESIGN.md)。施工入口见
[`GOAL-G0-G4.md`](GOAL-G0-G4.md)。本文件只保留当前工作集。

## 已选择不变量

- FileNumber 是 1-based `UInt32`，`0` 非法；published FileNumber 不复用，overflow fail closed；
- Store 目录与 canonical filename 直接决定物理路径，无集中映射与 StoreId 文件头；
- runtime authority 是 absolute `{FileNumber, FrameTicket}`；
- wire 保存 canonical `VarUInt32 BackwardFileDistance`，`0` 同文件，任意正值指更早文件；
- 不采用固定 `UInt16` horizon；
- future、underflow、zero target、non-canonical VarUInt 和 required zero FrameTicket fail closed；
- OVD `BindSelf` 与 optional None 保持字段局部语义，不混入通用 required reference；
- v1 可保留所有 published 文件，不承诺总磁盘、依赖文件数或 cold-read fan-out 有界。

## 当前具备

- 1-based checked FileNumber 与十位十进制 `.rbf` canonical filename round-trip；
- strong `{OffsetBytes, LengthBytes}` FrameTicket、runtime `AbsoluteFrameAddress` 与 required
  `RelativeFrameTicket`；
- same/previous/>65,535/UInt32 最大距离的 absolute-relative round-trip；
- canonical VarUInt32/VarUInt64 writer-reader，以及 overlong、overflow、truncated 与 zero-ticket fail-close；
- same-file strictly-earlier validation 与 provisional single-Frame envelope hard bounds；
- in-memory append-only Segment/Store、origin-free logical plan、origin-dependent render/measure；
- soft `TargetFileBytes` rollover：nonempty crossing 只重编码同一 plan，empty oversize 原地容纳，下一 Save
  自然切换；hard bound 或 FileNumber overflow typed reject 且不 append/publish；
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

当前 Frame envelope、relative codec、OVD encoding estimate 和 synthetic plan 都只是内存探针 grammar；
没有正式 RBF/OVD wire 或持久 StateStore。

## 当前焦点

阶段 A 已闭合并停止实现；等待是否按
[`STATESTORE-SUBSYSTEM-DESIGN.md`](STATESTORE-SUBSYSTEM-DESIGN.md) 启动独立阶段 B 的用户决定。

基础能力缺失时，先检查冻结的 `TwoLegRotationProbe` 是否已有同领域机制。只复用代码片段、测试意图或
设计思想，不建立项目依赖，也不带回 A/B/C、A-debt、evacuation、paired candidate 或 terminal settlement。

## 近期 roadmap

无阶段 A 后续实现项。Probe 保留为 executable specification，不在本阶段接入真实 filesystem/RBF 或产品代码。

## 未闭合事项

阶段 A 无未闭合事项。自动 OVD policy、产品阈值与 SameStateRebase 是否进入产品默认策略都保持阶段 B/未来
证据问题，不影响本 Probe 完成。

## 明确暂缓

- 自动文件删除、incremental segment cleaner、冷热分层与跨 Store merge；
- Extent/multi-frame Revision；
- filesystem/reopen、actual RBF/SizedPtr、head durability、orphan reconciliation；
- EventJournal/RbfSegmentStore acquisition、product API、NuGet compatibility 或正式 wire migration；
- 任何总分、默认 file target 或 cold-read SLO。
