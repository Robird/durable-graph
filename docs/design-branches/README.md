# DurableGraph 候选设计分叉

本目录保存已经值得记住、但证据尚不足以裁决的架构分叉。它们是后续实验的输入，不是实现指令、当前事实或已接受设计。

## 状态

- **Open**：正在收集证据，可以安排近期实验。
- **Deferred**：问题真实，但当前缺少消费者、格式或测量；等待明确触发条件。
- **Chosen**：已有证据选择某一方案；应链接对应 ADR、实验或实现。
- **Rejected**：已有证据否定，并保留具体失败或代价。
- **Superseded**：问题被新的分叉或更精确的模型取代。

## 维护规则

- 一个文件只承载一个核心分叉，并使用稳定编号 `DB-NNN`。
- 写明当前事实、候选方案、不可约约束、尚缺证据和重访触发条件。
- 不以“也许以后”作为保留复杂度的充分理由。
- 同一语义只能有一个 authority；其他表示必须说明如何从 authority 验证派生。
- 裁决后更新状态和索引；不要静默改写成仿佛从未存在过分歧。

## 索引

| ID | 标题 | 状态 | 重访触发条件 |
|---|---|---|---|
| [DB-001](0001-schema-authority-and-runtime-representation.md) | Schema authority 与运行时表示 | Deferred | 首个 canonical format、canonical-blob-bound generated codec 或持久化 SchemaStore 实验 |
| [DB-002](0002-read-time-version-upgrade-pipeline.md) | 读取阶段的版本升级管线 | Chosen | 首个一般图、runtime plugin/registry、persistent payload 或 generated coordinator 伸缩性问题 |
| [DB-003](0003-snapshot-value-shape-and-upgrade-signature.md) | Snapshot 值形状与 Upgrade 签名 | Open | struct/in-out probe 后的真实 pipeline、性能或 expected rejection 消费者 |
| [DB-004](0004-snapshot-history-authoring-and-publishing.md) | Snapshot History 创作与发布 | Open | 正式 Generator publisher、团队/CI 摩擦或 persistent Schema authority 实验 |
| [DB-005](0005-durable-inheritance-flattening.md) | Durable 继承展平 | Deferred | 首个真实 durable inheritance 模型或 composition 对照 |
| [DB-006](0006-flat-graph-delta-prototype.md) | Flat Graph Delta 原型 | Chosen | R4 logical StateMap/apply、production reference adapter、persistent head 或 measured baseline cost |
| [DB-007](0007-adaptive-two-leg-rotation-policy.md) | 自适应双腿轮转与 Rebase/Deltify 策略 | Open | 内存策略模拟、真实 RBF read log、Base-migration completion 反例或 one-frame 容量证据 |
| [DB-008](0008-revision-contextual-self-address.md) | Revision 内的 contextual self address | Chosen | multi-frame Revision、脱离 containing ticket 的裸 OVD 消费者或真实 codec 对照数据 |
| [DB-009](0009-base-lineage-parent-locator.md) | Base lineage direct parent 与 Revision locator | Chosen | 真实 lineage consumer 出现且 OVD/TailMeta lookup 成为稳定瓶颈 |
| [DB-010](0010-base-lineage-anchor-scope.md) | Base lineage anchor 的作用域 | Open | locator planner materialization、multi-snapshot Revision consumer 或 DurableId reuse 裁决 |
