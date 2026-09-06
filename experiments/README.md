# 实验与回归入口

产品当前能力与下一步见 [src/PROJECT-STATE.md](../src/PROJECT-STATE.md)。
按正在调查的问题选择入口；无需顺序阅读所有 Probe。下列项目均不在根 solution 中，
运行命令、证据范围与限制由各自 README 维护。

| 何时使用 | 入口 |
|---|---|
| 修改 Generator 输出、runtime 公共 API、history/build 或 package 接线，需要验证真实单包消费者 | [PackageConsumerProbe](PackageConsumerProbe/README.md)：活动产品回归 |
| 排查 AddSource、AdditionalFiles、编译成功后发布之间的机制或负对照 | [SourceGeneratorHistoryProbe](SourceGeneratorHistoryProbe/README.md)：已完成机制见证 |
| 复查强类型 struct 升级签名、确定赋值或相关 C# 编译诊断 | [SnapshotUpgradeShapeProbe](SnapshotUpgradeShapeProbe/README.md)：已完成语言见证 |
| 研究多 Segment 地址、对象版本链、Save planning 或合成 workload 的机制依据 | [MultiSegmentStateStoreProbe](MultiSegmentStateStoreProbe/PROJECT-STATE.md)：冻结可执行储备 |
| 重新评估双文件依赖、有界文件保留或 rotation/admission 的取舍 | [TwoLegRotationProbe](TwoLegRotationProbe/PROJECT-STATE.md)：冻结可执行储备，先读恢复条件 |

已完成实验保留源码、脚本与证据，不维护产品 roadmap；其中的历史 Next、Goal 和目标设计
只在其注明的实验范围内解释。需要恢复研究时，先依据当前代码与测试确定一个新的具体问题。
