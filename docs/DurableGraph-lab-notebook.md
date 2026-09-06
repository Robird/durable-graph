# DurableGraph 实验与验证证据入口

> 本页只索引证据和新增的简短实验结果。当前能力见[产品工作集](../src/PROJECT-STATE.md)，
> 后续事项见[路线图](DurableGraph-research-roadmap.md)；不从旧日志中的 Next 恢复排期。

## 按问题找证据

| 问题 | 证据入口与适用范围 |
|---|---|
| Schema/history、包交付与 legacy 读取升级 | [归档实验簿](archive/2026-09-06/DurableGraph-lab-notebook.md) EXP-001–010；包回归运行入口在 [PackageConsumer](../experiments/PackageConsumerProbe/README.md) |
| sharing/cycles、logical diff、升级断边、两阶段物化 | 归档实验簿 EXP-011–014；[DB-006](design-branches/0006-flat-graph-delta-prototype.md)。这些是 fixture/probe 证据，不是产品通用图 API |
| DTO Capture、string 身份与读取 | [DB-022](design-branches/0022-versioned-state-dto-capture.md)、[DB-024](design-branches/0024-reference-capture-and-reusable-object-ids.md)、[DB-025](design-branches/0025-string-object-decoding-slice.md)的验收部分 |
| raw Base、wire v2 与 exact Revision 文件重开 | [DB-026 §8](design-branches/0026-raw-base-object-content-slice.md#8-实施合同与验收账本) |
| 同版 DTO 融合 Delta 准备、历史/引用校验与包交付 | [DB-027 §6](design-branches/0027-generated-same-schema-delta-body-slice.md#6-本轮实施账本)；body codec，不含持久 prior 链 |
| 持久 raw Delta、exact prior 与原始重建成本 H | [DB-028 §6](design-branches/0028-persisted-object-delta-chain-slice.md#6-实施账本)；object-first 冷重开，typed 解释元数据仍由 fixture 提供 |
| 已准备内容到真实表示选择与可追加 Revision | [DB-029 §9](design-branches/0029-prepared-object-revision-planning-slice.md#9-实施合同与账本)；PrepareBase、完整 prepared rows、策略落盘/冷重开，typed 元数据与发布仍在边界之外 |
| 地址/策略选择史及可重跑技术储备 | [DB 索引](design-branches/README.md)、[Probe 导航](../experiments/README.md)；TwoLeg 与合成 payload 结果不构成产品恢复或性能保证 |

## 历史卷

截至 `54a33df` 的原实验簿已[完整归档](archive/2026-09-06/DurableGraph-lab-notebook.md)：
EXP-001–014 保存机制实验，后半部按日期记录后续存储与 codec 分片。
[归档目录](archive/README.md)同时保留当时目标稿和路线。
上述快照保留旧语境，仅修复史实或引用错误，不再同步产品进度。

## 新结果

### 2026-09-06：文档工作集治理

将当前事实、长期目标和未完成工作分别集中到三个核心文件；旧长稿归档，
设计记录与 Probe 按任务导航。产品代码与可执行实验保持原样。
独立审查保留了混合文档中的未完事项，并隔离了旧 Probe 的 no-ID-reuse 假设。
本次为文档变更，验证范围是语义对照、相对链接、归档保真与 Git diff，不重复产品测试。

后续若已有施工记录保存结果，本页只增加必要的证据入口；没有独立记录的小实验可在此写
“问题、观察、边界、来源”四项，结束后将影响当前工作的结论更新到其唯一维护位置。
