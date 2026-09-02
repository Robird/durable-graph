# MultiSegmentStateStoreProbe 活跃工作集

> 状态：Active Research Context
>
> 最近校准：2026-09-02

## 目标

用最小、可执行的机制验证多历史 Segment StateStore：持久引用可指向同目录内任意更早文件，文件达到
应用配置尺寸后独立切换，冷 ObjectVersion 不因文件切换被强制 Base。

本探针不修改 `src/DurableGraph`，不依赖或替代 `TwoLegRotationProbe`；产品整合只发生在地址、跨文件
重建、reopen 与 fail-close evidence 闭合之后。

后续实现以 [`TARGET-DESIGN.md`](TARGET-DESIGN.md) 为全景规范；本文件只保留当前工作集，不重复长期设计。

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
- runtime `AbsoluteFrameAddress` 与 required `BackwardFrameReference`；
- same/previous/>65,535/UInt32 最大距离的 absolute-relative round-trip；
- canonical VarUInt32/VarUInt64 writer-reader，以及 overlong、overflow、truncated 与 zero-ticket fail-close；
- 独立 core/test `.slnx`，不进入产品 solution 或 TwoLeg 子树。

`FrameTicketCode` 仍只是 non-zero opaque `SizedPtr` stand-in；当前没有正式 RBF wire、Frame store、OVD
或持久 StateStore。

## 当前焦点

建立最小 in-memory append-only Segment/Frame store 与 soft `TargetFileBytes` rollover，让文件切换只改变
append destination，不改变任何 ObjectVersion Base/Delta 决策。logical plan 在 placement 前冻结；若
切换到新文件，必须按新 origin 重新 relativize、编码和定尺，而不是复用旧 bytes/estimate 或重跑 policy。

基础能力缺失时，先检查冻结的 `TwoLegRotationProbe` 是否已有同领域机制。只复用代码片段、测试意图或
设计思想，不建立项目依赖，也不带回 A/B/C、A-debt、evacuation、paired candidate 或 terminal settlement。

## 近期 roadmap

1. 强类型 FrameTicket、same-file earlier validation、append-only store 与 soft rollover；
2. runtime OVD/ObjectVersion 跨 F1-F4 materialization，冷 Base 留在 F1、热 Delta 推进；
3. origin-free RevisionPlan、OVD Base historical-head reencode、shared-prior lineage 与 logical publication；
4. 移植 deterministic workload、精简 BaseBudget policy 与 W/P/F/R/L evaluator；
5. filesystem naming/reopen、exact PublishedHead、durability gates 与 missing dependency fail-close；
6. evidence 充分后，把最小地址与 reader contract 整合到产品 StateStore。

## 未闭合事项

- exact filename prefix/extension 是否沿用本探针的十位十进制 `.rbf`；
- 正式 `SizedPtr` canonical encoding 与 BackwardFileDistance 的 record framing；
- 当前 opaque FrameTicketCode 如何演化为可验证 same-file earlier 和可测 Frame length 的强类型 ticket；
- OVD Base/Deltify 的独立 read-amplification policy；
- `TargetFileBytes` 的 API、dedicated oversize file 与 hard bound；
- orphan file reconciliation、published head durability 与目录 metadata flush；
- GC、`CompactToNewStore`、backup packing 和 historical lineage retention。

## 明确暂缓

- 自动文件删除、incremental segment cleaner、冷热分层与跨 Store merge；
- Extent/multi-frame Revision；
- product public API、NuGet compatibility 或正式 wire migration；
- 任何总分、默认 file target 或 cold-read SLO。
