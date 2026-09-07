# 实验与回归入口

产品当前能力与下一步见 [src/PROJECT-STATE.md](../src/PROJECT-STATE.md)。
按正在调查的问题选择入口；无需顺序阅读所有 Probe。下列项目均不在根 solution 中，
运行命令、证据范围与限制由各自 README 维护。

| 何时使用 | 入口 |
|---|---|
| 修改 Generator 输出、runtime 公共 API、history/build 或 package 接线，需要验证真实单包消费者 | [PackageConsumerProbe](PackageConsumerProbe/README.md)：活动产品回归 |
| 比较泛型 DTO/静态 helper、工厂闭合、readonly accessor 与历史 Upgrade 可书写性 | [GenericBindingShapeProbe](GenericBindingShapeProbe/README.md)：DB-038 的独立机制见证，非产品泛型实现 |
| 验证单 writer 进程中止、RBF 发布可见性与严格坏尾拒绝 | [PublicationCrashProbe](PublicationCrashProbe/README.md)：DB-036 底层故障见证，非断电保证 |

四个已完成或未采用路线的旧 Probe 已退出活动工作树，完整源码与证据保存在 Git 恢复点；
只在明确问题需要时查 [归档索引](ARCHIVE.md)。默认搜索与回归使用上表入口及 src/tests，
不为跟随产品演进而维护或恢复旧 Probe。
