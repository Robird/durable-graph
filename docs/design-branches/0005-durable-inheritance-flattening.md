# DB-005：Durable 继承展平

> 状态：Deferred
>
> 创建日期：2026-08-27
>
> 当前快速原型选择：继续要求 durable type sealed、top-level 且直接继承 `DurableBase`；不展平领域继承。

## 问题

如果未来允许 durable domain class 继承其他 durable class，Snapshot 和 exact Schema 是否应展平完整继承链？这会同时改变 FieldId scope、private field access、base evolution、leaf versioning 和 handler visibility。

## 当前事实

- 当前 Generator 只接受 sealed class 直接继承 `DurableBase`，不遍历用户 base fields。
- 当前 Schema 是单一 flat `DurableFieldInfo` 序列，没有 declaring-type segment。
- Source Generator 能读取 base private field symbol，但生成在 derived partial class 中的代码不能访问 base private storage。
- 当前没有 durable inheritance consumer。

## 候选 A：每个 leaf Schema 完全展平

所有 base/derived durable slots 进入 leaf 的一个 flat Snapshot。

建议约束：

- FieldId 在整个 flattened `(SchemaId, Version)` 内唯一；base/derived 相同 FieldId 是编译错误。
- Snapshot member 继续使用 `Field{FieldId}`，CLR field 重名不构成碰撞。
- 每个声明 durable fields 的 source class 必须是 partial，并由 Generator 在其自身 scope 内生成 capture/hydrate adapter；derived binding 组合这些 adapters，而不是直接访问 base private fields。

尚未解决：base shape 改变是否强制所有 leaf Schema 同步 bump，以及 referenced-assembly base adapter 的可见性/兼容契约。

## 候选 B：FieldId 按 declaring type 分段

持久 identity 变成 `(declaring schema/type segment, FieldId)`，允许 base/derived 重复 FieldId。

优点：base 局部编号独立。

代价：当前 flat Schema、State fields 和所有比较/codec 都要改成复合 key；declaring CLR type 不能成为 durable identity，仍需额外稳定 segment ID。

## 候选 C：不支持继承，改用 durable composition

领域通过包含 durable value/reference component 复用状态，而不是继承存储布局。

优点：Schema ownership 和 private state 边界更清楚。

代价：领域模型可能需要额外转写；当前 durable references/value components 也尚未设计。

## 必须回答的问题

1. FieldId 的唯一性 scope 是 leaf Schema 还是 declaring segment？
2. base field 新增/删除/改类型时，哪些 leaf Version 必须 bump？
3. source base 与 referenced-assembly base 的 private fields 由谁 capture/hydrate？
4. upgrade handler 是否应看到 base private durable bits，还是只能通过生成的 projection？
5. 同一 base 被多个不同 SchemaId leaf 复用时，history 由谁拥有？
6. 构造/反序列化时 base 与 derived invariant 的恢复顺序是什么？

## 当前裁决

这些问题都没有现有消费者，且会扩张当前 Generator 的核心类型形状与 Schema identity。保持继承关闭；Snapshot struct 实验只处理当前类型直接声明的 fields。

## 重访触发条件

- 出现首个无法合理改写为 composition 的 durable inheritance 领域模型。
- durable value/reference composition 已有具体设计，可以做真实对比。
- 需要继承 referenced assembly 中已发布的 durable base。

## 相关材料

- `src/DurableGraph.Generator/DurableSchemaGenerator.cs`
- `docs/design-branches/0003-snapshot-value-shape-and-upgrade-signature.md`
