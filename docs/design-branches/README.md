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
| [DB-005](0005-durable-inheritance-flattening.md) | Durable 继承展平 | Chosen | DB-019/021 落地声明段、祖先版本及当前标量 body；引用/历史升级另片推进 |
| [DB-006](0006-flat-graph-delta-prototype.md) | Flat Graph Delta 原型 | Chosen | R4 logical StateMap/apply、production reference adapter、persistent head 或 measured baseline cost |
| [DB-007](0007-adaptive-two-leg-rotation-policy.md) | 自适应双腿轮转与 Rebase/Deltify 策略 | Deferred | DB-014 无法满足有界 dependency/retention SLO，TwoLeg 被明确重启 |
| [DB-008](0008-revision-contextual-self-address.md) | Revision 内的 contextual self address | Chosen | multi-frame Revision、脱离 containing ticket 的裸 OVD 消费者或真实 codec 对照数据 |
| [DB-009](0009-base-lineage-parent-locator.md) | Base lineage direct parent 与 Revision locator | Chosen | 真实 lineage consumer 出现且 OVD/TailMeta lookup 成为稳定瓶颈 |
| [DB-010](0010-base-lineage-anchor-scope.md) | Base lineage anchor 的作用域 | Chosen | mixed-provenance Revision、import/rescue/stale Save 或 DurableId reuse/epoch |
| [DB-011](0011-two-phase-save-planning-and-capacity.md) | Save 策略规划与容量可行性分层 | Superseded | TwoLeg 重新成为产品候选且出现普通 workload capacity false-negative |
| [DB-012](0012-two-leg-strategy-benchmark-arena.md) | TwoLeg 多策略 Benchmark Arena 的最小边界 | Deferred | TwoLeg 研究明确恢复并出现真实多策略或 hand-built artifact consumer |
| [DB-013](0013-tiered-state-segments.md) | 按写入温度分片的双层 State segments | Deferred | TwoLeg 重启，或出现必须原子共存的独立 Hot/Cold placement consumer |
| [DB-014](0014-multi-segment-backward-file-distance.md) | 多历史 Segment 与 BackwardFileDistance 地址 | Chosen | MultiSegment probe 的跨四文件恢复、reopen/fail-close 与 recovery-closure evidence |
| [DB-015](0015-statestore-object-representation-policy.md) | StateStore 对象表示策略的估算 DTO 与保存计划 | Chosen | 整数倍数/百分比的纯 selector 已落地；估算生产、序列化接口及 Save 接入等真实消费者 |
| [DB-016](0016-next-product-object-content-slice.md) | 策略之后的下一块对象内容纵切 | Chosen | 用户选择 codec-first；整体引用图与生成器形状见 DB-018 |
| [DB-017](0017-object-codec-design-points.md) | 对象 codec 设计要点与逐项决策草稿 | Superseded | 整体模型转 DB-018；保留 scalar 要点和 Robird 证据，旧 string 字段 inline 方案已取代 |
| [DB-018](0018-generated-graph-codec-shape.md) | 统一引用身份、TypeCodec 与生成式 Serializer 形状 | Open | DB-019–021 落地祖先元数据、值槽位/数组元素及静态标量 body；生成式图 codec 尚未实施 |
| [DB-019](0019-schema-ancestry-implementation-slice.md) | 祖先 Schema/history 产品分片 | Chosen | SchemaOnly 元数据闭环；继承 serializer 与 binary codec 另片推进 |
| [DB-020](0020-typed-slot-array-binding-slice.md) | typed 值槽位与数组元素 binding 分片 | Chosen | primitive/ref 槽位及 SZ/rank-2 元素循环；DB-021 接 SG 标量 class body，完整数组对象 codec 另片推进 |
| [DB-021](0021-generated-primitive-body-slice.md) | 实际生成的 primitive class body | Superseded | 直接领域 Read/Write 接口由 DB-022 的 Versioned DTO/Capture 取代；保留原片证据 |
| [DB-022](0022-versioned-state-dto-capture.md) | Versioned DTO、Capture 与 DTO binary body | Chosen | readonly scalar Vn/历史积累/current Capture；引用 Capture、比较/估算、DTO 升级与 StateStore 管线另片推进 |
| [DB-023](0023-scalar-schema-dto-slice.md) | 标量 Schema、历史与 DTO 编码贯通 | Chosen | 13 种标量贯通；string 引用 Capture 与最小对象列表为下一候选 |
| [DB-024](0024-reference-capture-and-reusable-object-ids.md) | 引用 Capture、revision 内身份与可复用 ObjectId | Chosen（单调 ID 首片已实现） | SG root 适配器、封闭 ID DTO 图及内存 accept/discard；回收/恢复/struct 延期 |
