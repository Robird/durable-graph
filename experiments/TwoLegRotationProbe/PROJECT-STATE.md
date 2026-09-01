# TwoLegRotationProbe 技术储备快照

> 状态：Paused Technical Reserve
>
> 冻结日期：2026-09-02
>
> 产品路线 successor：[`DB-014`](../../docs/design-branches/0014-multi-segment-backward-file-distance.md)
> 与 [`MultiSegmentStateStoreProbe`](../MultiSegmentStateStoreProbe/PROJECT-STATE.md)

本子项目已阶段性收尾，不再维护活跃 roadmap、下一编码切片或策略竞赛计划。源码、测试、独立 solution、
evaluator、workloads 与研究文档完整保留，作为可执行技术储备。当前产品候选采用多历史 Segment 与
`BackwardFileDistance`，不继承 TwoLeg 的相邻两文件 topology、轮转策略或债务机制。

若未来恢复本项目，先以当前源码和测试为事实，以本文件为入口；不要从历史 notebook 的旧 `Next` 项继续。

## 冻结时的研究边界

TwoLeg 探针研究的模型是：latest PublishedRevision 的 current-reconstruction closure 只涉及
`A=Previous` 与 `B=Current`，通过 Stay-B、preparatory Base migration 和 Rotate-C 把作用域推进为 B/C。

它是纯内存、size-only、single-writer 实验，不是产品 StateStore：

- 不建模 durable publication、crash/reopen、concurrency、Store identity、文件 GC 或正式 wire；
- workload 中对象彼此独立，不建模引用、reachability 或 GC；
- one Revision / one provisional RBF Frame，超出单帧能力 fail closed；
- `RelativeFrameTicket`、RBF v0.40 grammar 与 512 GiB start gate 都是本探针局部语义；
- Adaptive `(3,5%)` / `(4,4%)` 是已知不完整的实验 baseline，不是产品默认策略。

## 保留的可执行语义脊柱

- runtime OVD 是 live ObjectId → ObjectVersion head 的唯一 authority；StateMap 只是 materialized projection；
- 内存地址 absolute-normalize，持久相对值只能在 containing origin 下解释并在新 origin 重新编码；
- Base 终止 current reconstruction，Delta 指 exact prior ObjectVersion；current reconstruction 与
  historical lineage 分层；
- normalized `Insert / Update / Remove / NoChange` facts 来自同一 exact parent snapshot；
- candidate 必须完整构造、定尺、通过 hard gate 后才 apply；rejection 不变异 Store/cursor；
- preference 与 admission 分层，selected rejection 不 fallback；`RejectedUnproven` 不表示一般无解；
- scratch fork/replay、continuous A/B→B/C→C/D、same-state relocation 与 shared prior-snapshot anchor 均有
  executable witnesses；
- evaluator 只为 admitted run 产生 `Wworkload`、workload-only P、closed-horizon F、累计 R/L 与
  Delta/Base references；terminal write accounting 只作内部 closure/守恒诊断；
- frozen workload、aligned-channel composition、typed outcomes 与 canonical report 保持确定性。

完整实现地图和证据索引见 [`README.md`](README.md)，评价协议见
[`EVALUATOR-V1.md`](EVALUATOR-V1.md)。

## 已知未闭合边界

- budget-compatible 低放大 A-debt 在复合 trace 的 64 个自然 Saves 中保持不动；稳定输入下 pure selector
  会持续选择 Stay；
- 大型不可分割冷对象不能同时满足 eventual single-layer rotation、单次 Base 峰值低于对象自身尺寸和
  不改变对象边界；
- `ReadMotive / ReadinessProgress / ShouldRotate` 只形成候选设计，未实现；
- current target 把 Ready 与 Should 混合，可能产生长期 Stay 或短 leg churn；
- completion certificate 只证明一条有限保守路径，不是完备 solver；
- provisional layout、Frame sharing 与 payload facts 不足以冻结产品 wire 或产品策略。

细节保存在 [`READ-AMPLIFICATION-BASE-BUDGET-POLICY-V0.md`](READ-AMPLIFICATION-BASE-BUDGET-POLICY-V0.md)、
[`ADAPTIVE-ROTATION-CONTROL-CANDIDATE.md`](ADAPTIVE-ROTATION-CONTROL-CANDIDATE.md) 与
[`PRIOR-ART.md`](PRIOR-ART.md)。

## 转向 DB-014 时可复用与不可继承

可复用的是模型和 failure discipline，而不是 TwoLeg assembly 依赖：absolute runtime address、
absolute-normalize/relative-reencode、append-only Frame、OVD authority、Base/Delta reconstruction、
normalized Save facts、whole-candidate gate、no-fallback apply、deterministic workload、symbolic replay，
以及 W/P/R/L 和 shared-Frame cold-read 的测量思想。

下列 TwoLeg 专属假设不得进入 MultiSegment 正常 Save：

- `FileScope={Previous,Current}` 与 1-bit same/previous `RelativeFrameTicket`；
- A/B/C、Stay-B/Rotate-C、A-debt、EvacuationSet 与 same-state NoChange migration；
- rollover 强制改变 Base/Deltify 或要求 OVD full Base；
- `CanPrepareAndRotate`、terminal settlement 与 two-file reconstruction hard gate；
- “轮转完成即可退休 A”以及只用 Current tail 表达全部文件压力；
- DB-013 的 parallel Hot/Cold heads。DB-014 的 multi-segment 是一个逻辑 append sequence 跨多个历史文件，
  不是冷热双 OVD 分片。

## 恢复条件

只有出现以下产品证据之一，才重启 TwoLeg 或从中抽取增量 cleaner：

- latest recovery 必须依赖常数个文件，DB-014 的任意历史 dependency fan-out 不可接受；
- 产品要求在线有界总磁盘/旧文件退休，而 `CompactToNewStore` 的停顿或峰值无法满足 SLO；
- backup、rescue 或运营要求证明 TwoLeg 的局部依赖显著优于 multi-segment；
- 一个真实 consumer 同时给出可裁决的 write peak、file retention 与 cold-read guardrails。

恢复时先新建独立 candidate，不原地把冻结的 Adaptive v2 描述成完整策略；继续报告 typed outcomes 与
raw metrics，不引入标量 winner。

## 冻结验证与恢复点

冻结基线命令：

```powershell
dotnet test experiments\TwoLegRotationProbe\TwoLegRotationProbe.slnx --no-restore --verbosity minimal
dotnet build experiments\TwoLegRotationProbe\TwoLegRotationProbe.slnx --no-restore --verbosity minimal
dotnet format experiments\TwoLegRotationProbe\TwoLegRotationProbe.slnx --no-restore --verify-no-changes --verbosity minimal
dotnet build DurableGraph.slnx --no-restore --verbosity minimal
```

2026-09-02 收尾时：TwoLeg tests `362/362`；TwoLeg solution 与 root solution 均为 `0 warning / 0 error`。

annotated tag：`research/two-leg-rotation-probe-tech-reserve-20260902`。该 tag 是完整可执行恢复点；是否已
推送到 remote 必须单独确认，不能从本地存在推导。

## 证据导航

- 当前实现模型：[`README.md`](README.md)
- evaluator contract：[`EVALUATOR-V1.md`](EVALUATOR-V1.md)
- Adaptive v2 contract：[`READ-AMPLIFICATION-BASE-BUDGET-POLICY-V0.md`](READ-AMPLIFICATION-BASE-BUDGET-POLICY-V0.md)
- 未实现 rotation-control candidate：[`ADAPTIVE-ROTATION-CONTROL-CANDIDATE.md`](ADAPTIVE-ROTATION-CONTROL-CANDIDATE.md)
- 前人成果：[`PRIOR-ART.md`](PRIOR-ART.md)
- TwoLeg 策略分支：[`DB-007`](../../docs/design-branches/0007-adaptive-two-leg-rotation-policy.md)
- planning/admission 分层：[`DB-011`](../../docs/design-branches/0011-two-phase-save-planning-and-capacity.md)
- Arena 边界：[`DB-012`](../../docs/design-branches/0012-two-leg-strategy-benchmark-arena.md)
- 历史冷热分片候选：[`DB-013`](../../docs/design-branches/0013-tiered-state-segments.md)
- 当前产品地址路线：[`DB-014`](../../docs/design-branches/0014-multi-segment-backward-file-distance.md)
- 阶段历史：[`../../docs/DurableGraph-lab-notebook.md`](../../docs/DurableGraph-lab-notebook.md)
