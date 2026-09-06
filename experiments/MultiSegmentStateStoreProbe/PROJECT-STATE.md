# MultiSegmentStateStoreProbe 冻结快照

> 状态：G0-G4 complete — 阶段 A 已完成，保留为可执行机制储备。
>
> 最近校准：2026-09-05

本页是已完成 Probe 的恢复入口。产品进展见 [src/PROJECT-STATE.md](../../src/PROJECT-STATE.md)，
其他实验按问题从 [实验导航](../README.md) 进入；不从本目录历史 Goal 接续产品开发。

## 目标与范围

本 Probe 验证同一 Store 目录内多历史 Segment 的地址、轮转、对象版本链与保存机制。
所有 Stage-A Frame envelope、合成 payload 和 publication 都是 in-memory 实验模型，
其验证结论不能替代产品 filesystem/reopen、持久 head 或 crash recovery 的独立证据。

产品开发进展已迁到 [src/PROJECT-STATE.md](../../src/PROJECT-STATE.md)。
本文件只维护 Probe 自身状态；产品 codec、Schema、StateStore 的路线不再在此重复更新。

## 保留的关键结论

- 1-based FileNumber 与 canonical filename；runtime 使用 absolute address，
  wire 用 BackwardFileDistance；required references 必须指向更早地址。
- tail-triggered soft rollover：crossing append 留在原文件，下次 Save 借 writer 时轮转；
  final origin 确定后编码一次。冷对象不因文件切换而被强制 Base。
- ObjectHeadMap Base/Delta 与对象 Base/Delta 正交；exact-head current reconstruction、
  shared-prior lineage、append-before-publish 和发布后缓存失效已有 Probe 见证。
- 单次 Save 一个 Revision Frame；所有历史文件保留，不保证总空间、依赖文件数或 cold-read fan-out 有界。
- policy 与 evaluator 使用合成 payload/raw outcomes；不把结果解释为产品默认参数或性能保证。

## 当前焦点与后续

阶段 A 无活动实现项或未闭合目标。保留源码、测试与 canonical corpus 作为可执行参照；
只有明确的新机制问题才重开本 Probe。产品提取设计思想或代码片段，不引用 Probe 程序集，
不混入 TwoLeg 的 A/B/C、evacuation 或旧文件局部性约束。

## 证据导航

- [README](README.md)：当前模型、raw report 与运行入口。
- [TARGET-DESIGN](TARGET-DESIGN.md)：阶段 A 目标与不变量。
- [GOAL-G0-G4](GOAL-G0-G4.md)：已完成的历史施工边界。
- [DB-014](../../docs/design-branches/0014-multi-segment-backward-file-distance.md)：产品地址方向的选择。
- [阶段 B 设计材料](STATESTORE-SUBSYSTEM-DESIGN.md)：保留总体分层讨论，当前进展见产品工作集。
- [实验笔记](../../docs/DurableGraph-lab-notebook.md)：历史结果与决策依据。
