# DurableGraph 实验簿

> 状态：Living Working Note  
> 用途：记录逐步实验形成的认识，帮助作者与 Coding Agent 跨会话恢复项目上下文。  
> 边界：本文不是需求规格、实现指令或当前行为的权威来源；当前源码、测试和可复现输出优先。

## 1. 记录约定

重要陈述使用以下状态：

- **Observed**：已由当前源码、测试或可复现实验直接观察。
- **Decided**：当前采用的工作决定；后续证据允许推翻。
- **Tentative**：有一定依据，但尚未得到足够验证。
- **Rejected**：已经试验或讨论后放弃，并保留放弃原因。
- **Open**：尚待回答的问题。

只记录会影响后续工作的事实、判断和悬案，不复制聊天过程或大段命令输出。

## 2. 当前基线

记录日期：2026-08-27

- **Observed**：仓库已加入元数据 Attribute 契约、wire-format-independent Schema 值模型、内存 SchemaStore 和首个 Schema Source Generator；尚无对象身份、StateStore、升级器或持久化实现。
- **Observed**：`DurableGraph.slnx` 包含 `DurableGraph`、`DurableGraph.Generator`、`DurableGraph.Cli` 和 `DurableGraph.Tests`。
- **Observed**：核心库将 Generator 作为 Roslyn analyzer 引用；CLI 引用核心库；测试项目引用核心库和 Generator。
- **Observed**：运行时项目和测试项目目标框架为 `net10.0`；Generator 为兼容 Roslyn 加载而目标框架为 `netstandard2.0`。
- **Observed**：`Directory.Build.props` 将程序集名、根命名空间和包名统一加上 `Atelia.` 前缀。
- **Observed**：`docs/DurableGraph-target-design-v0.md` 描述目标设计和 P0-P7 原型设想，但尚不代表实现事实。
- **Decided**：近期不进一步细分程序集，也不为了分类整齐而提前整理命名空间。
- **Decided**：采用自底向上的细粒度实验；想清楚一部分、实现和验证一部分，再逐步拼合全景。
- **Decided**：允许后续依据实验结果重构命名、命名空间、项目边界和暂定 API。

## 3. 当前工作地图

### 已有材料

- Target design：提供问题空间、候选不变量和远期方向。
- 空项目骨架：提供最小构建、测试、Generator 和 CLI 边界。
- Schema 值模型：提供 `TypeTag`、`DurableFieldInfo` 和 `DurableSchema`。
- 内存 SchemaStore：提供 exact version 注册、查询、幂等和冲突语义。
- Schema Generator：从受限的 durable class/field 声明生成静态 `DurableSchema`。
- Candidate design branches：在 `docs/design-branches/` 隔离尚未裁决的架构分叉。
- 本实验簿：保存随实验演化的项目认识。

### 后续方向

- **Open**：下一实验应先做 generated Schema 的注册/发现、版本升级路径注册，还是带 Schema key 的内存 StateStore/load orchestration。
- **Open**：Schema runtime representation 与 canonical authority 的候选分叉记录在 `DB-001`，等待 exact codec/persistent format 实验裁决。
- **Open**：哪些类型和 API 最终属于核心程序集，等待真实代码形状出现后再判断。

## 4. 实验记录

### EXP-001：Durable 类型与字段分类契约

状态：Concluded

日期：2026-08-27

问题：能否用最小公共 API 明确 durable object 的 opt-in、稳定 Schema 身份，以及每个字段是否参与持久化？

本轮明确不回答：

- Generator 如何检查每个实例字段恰好选择 `[DurableField]` 或 `[Transient]`。
- durable object 实例身份、继承图、序列化和落盘格式。

最小实验：

- 引入空的抽象基类 `DurableBase`。
- 引入仅作用于 class 的 `DurableTypeAttribute(string schemaId, int version)`。
- 引入仅作用于 field 的 `DurableFieldAttribute(int fieldId)`。
- 引入仅作用于 field 的 `TransientAttribute`。
- 用契约测试验证 Attribute 目标、参数和显式字段分类示例。

观察：

- **Observed**：`System.Attribute` 已定义 `TypeId` 属性；在 `DurableTypeAttribute` 上暴露同名属性会产生成员隐藏警告和反射歧义。
- **Decided**：Attribute 对外使用 `SchemaId` 属性，避开 BCL 的 `Attribute.TypeId`。
- **Decided**：`SchemaId` 必须为非空白字符串，Schema version 和 FieldId 必须为正整数；精确的 Schema 版本由 `(SchemaId, Version)` 二元组标识。
- **Decided**：Type 与 Field 使用两个 Attribute 类型，避免仅靠 Generator 解释条件必填参数。
- **Decided**：durable 字段与 transient 字段采用显式标记，不以 CLR 字段名称充当长期身份。
- **Observed**：当前版本尚未强制所有实例字段必须二选一；该能力需要后续 Generator diagnostic。

结论：最小元数据契约成立；solution 构建为 0 warning / 0 error，12 个契约测试全部通过。

相关源码/测试：

- `src/DurableGraph/DurableBase.cs`
- `src/DurableGraph/DurableTypeAttribute.cs`
- `src/DurableGraph/DurableFieldAttribute.cs`
- `src/DurableGraph/TransientAttribute.cs`
- `tests/DurableGraph.Tests/DurableMetadataContractTests.cs`

### EXP-002：Wire-format-independent Schema 值模型

状态：Concluded

日期：2026-08-27

问题：能否先定义足够支撑版本化 Schema 冲突实验、但不绑定序列化格式的最小内存模型？

本轮明确不回答：

- Schema 如何编码、落盘和计算跨进程稳定的 `SchemaHash`。
- CLR field type 如何由 Generator 映射为 TypeTag。
- SchemaStore、StateStore、版本升级器和加载流程。
- nullability、collection、durable reference 等复合类型语义。

最小实验：

- `TypeTag` 只枚举 `Boolean`、`Int32`、`Int64` 和 `String`，并保留值为 0 的 `Invalid` sentinel。
- `DurableFieldInfo` 是由正整数 FieldId 与有效 TypeTag 构成的只读 record struct。
- `DurableSchema` 保存 SchemaId、正整数 Version 和不可变字段序列。
- 构造 Schema 时复制输入、按 FieldId 排序，并拒绝无效或重复 FieldId。
- Schema 结构相等比较使用 ordinal SchemaId、Version 和规范排序后的完整字段序列。

观察：

- **Rejected**：没有采用 `record struct DurableSchema(..., DurableFieldInfo[] Fields)`；可变数组别名、数组的引用相等和 struct 默认值会给 Schema 比较制造错误语义。
- **Decided**：`DurableSchema` 是不可变 sealed class，公开 `ImmutableArray<DurableFieldInfo>`。
- **Decided**：字段声明顺序不属于 Schema 语义；Fields 在构造时按 FieldId 升序规范化。
- **Decided**：TypeTag 的整数值属于 Schema 元数据身份，显式赋值并由测试锁定；当前枚举值不代表 serializer 已实现。
- **Observed**：相同 `(SchemaId, Version)` 可以构造出结构不相等的 Schema；后续 SchemaStore 应将该情况判为硬冲突。
- **Observed**：`GetHashCode()` 只满足进程内对象相等契约，不是稳定 `SchemaHash`，不得落盘或跨进程比较。

结论：最小 Schema 值模型成立；solution 构建为 0 warning / 0 error，32 个测试全部通过。

相关源码/测试：

- `src/DurableGraph/TypeTag.cs`
- `src/DurableGraph/DurableFieldInfo.cs`
- `src/DurableGraph/DurableSchema.cs`
- `tests/DurableGraph.Tests/DurableSchemaContractTests.cs`

### EXP-003：内存 SchemaStore 冲突语义

状态：Concluded

日期：2026-08-27

问题：能否在不涉及 wire format 和磁盘 authority 的前提下，验证 exact version Schema 注册与 fail-closed 冲突规则？

本轮明确不回答：

- 持久化 SchemaStore 的编码、事务、发布和恢复。
- 多线程访问与多写者并发。
- Schema upgrade path、StateStore 和 load orchestration。
- Store 接口的长期边界。

最小实验：

- `InMemorySchemaStore.Register(schema)` 首次登记 Schema。
- 相同 `(SchemaId, Version)` 且结构相等时幂等，并返回首次登记的规范实例。
- 相同 key 但结构不等时抛出 `SchemaConflictException`。
- `GetRequired(schemaId, version)` 只做 exact lookup，缺失时抛出 `SchemaNotFoundException`。

观察：

- **Decided**：当前只有一种 Store，不提前抽取 `ISchemaStore`。
- **Observed**：冲突注册不会替换或污染已经登记的 Schema。
- **Observed**：不同 Version 可以在同一 SchemaId 下共存。
- **Decided**：SchemaId 使用 ordinal、case-sensitive 身份语义。
- **Observed**：typed conflict 同时携带 registered 与 conflicting Schema，可供后续诊断使用。
- **Observed**：typed not-found 精确保留缺失的 SchemaId 与 Version，不提供 latest fallback。
- **Decided**：`InMemorySchemaStore` 明确不承诺持久化或线程安全，不能被描述为 authority Store。

结论：内存 SchemaStore 的最小 fail-closed 语义成立；solution 构建为 0 warning / 0 error，44 个测试全部通过。

相关源码/测试：

- `src/DurableGraph/InMemorySchemaStore.cs`
- `src/DurableGraph/SchemaConflictException.cs`
- `src/DurableGraph/SchemaNotFoundException.cs`
- `tests/DurableGraph.Tests/InMemorySchemaStoreTests.cs`

### EXP-004：首个 DurableSchema Source Generator

状态：Concluded

日期：2026-08-27

问题：能否从最小 durable class 声明生成可执行的静态 `DurableSchema`，并在编译期拒绝当前契约内的不明确字段？

本轮明确不回答：

- properties、auto-property backing field、继承字段、嵌套/泛型/record durable type。
- nullability、collection、durable reference 和用户自定义 value type。
- serializer/deserializer、SchemaHash、Store 自动注册和跨类型 Schema key 冲突。
- 历史版本升级路径与 State load orchestration。

最小实验：

- 实现 `DurableSchemaGenerator : IIncrementalGenerator`。
- 支持顶层、非泛型、非 record、`partial` 且直接继承 `DurableBase` 的 class。
- 要求直接声明的实例 field 恰好具有 `[DurableField]` 或 `[Transient]`。
- 把 `bool`、`int`、`long`、`string` 映射为现有 TypeTag。
- 为每个合法类型生成公共静态 `Schema` 属性。
- 用 DG0001-DG0009 覆盖类型形状、Schema metadata、字段分类、FieldId、field type、成员冲突和静态字段误标。

观察：

- **Decided**：Generator 不引用 runtime project，避免 `DurableGraph -> Generator -> DurableGraph` 循环；它按 metadata name 识别契约，并在生成代码中使用全限定 runtime 类型名。
- **Decided**：一次 compilation 生成单一 `DurableSchemas.g.cs`；类型按完全限定名排序，字段按 FieldId 排序，换行统一为 LF。
- **Observed**：Generator-driver 测试能编译输入源码、运行 Generator、emit 动态程序集，并从生成的静态属性读回正确 `DurableSchema`。
- **Observed**：仅比较简单类型名和 namespace 会误认同 namespace 下的嵌套 lookalike；metadata matcher 已收紧为拒绝 containing type，并有 DurableBase/Attribute 回归测试。
- **Observed**：单个非法 durable type 只抑制自身生成，不妨碍同 compilation 中其他合法类型。
- **Decided**：当前直接继承限制使继承字段语义保持关闭；尚未设计前不得静默遍历用户基类。
- **Observed**：auto-property 等合成存储目前被忽略，符合本轮 field-only 非目标，但仍是后续必须显式裁决的边界。
- **Observed**：独立只读审查在修复 metadata lookalike 问题后批准实现，无 blocking 或 medium finding。

结论：首个 Schema Generator walking skeleton 成立；solution 构建为 0 warning / 0 error，58 个测试全部通过。

相关源码/测试：

- `src/DurableGraph.Generator/DurableSchemaGenerator.cs`
- `src/DurableGraph.Generator/AnalyzerReleases.Unshipped.md`
- `tests/DurableGraph.Tests/DurableSchemaGeneratorTests.cs`

## 5. 实验记录模板

后续实验按需增加条目，不要求为了形式填写无意义内容。

```text
### EXP-NNN：名称

状态：Planned / Running / Concluded / Superseded
日期：YYYY-MM-DD

问题：
本轮明确不回答：
最小实验：
成功/失败判据：
观察：
结论：
遗留问题：
相关源码/测试：
```

## 6. 船长日志

### 2026-08-27：建立候选设计分叉库

- 建立 `docs/design-branches/`，把尚未获得足够证据的竞争方案与实验结论、正式决定分开。
- `DB-001` 记录 typed `DurableSchema` 与 canonical schema blob 的 authority/runtime representation 分叉。

### 2026-08-27：完成 EXP-004 Schema Generator walking skeleton

- 生成每个合法 durable type 的静态 `Schema`，并固定首批四种 scalar TypeTag 映射。
- 落地 DG0001-DG0009 fail-closed diagnostics 和 Roslyn 动态编译/emit 测试。
- 经独立审查修复嵌套 lookalike metadata 误认，补齐类型/字段顺序确定性证据。
- 保持 properties、继承、Store registration、SchemaHash 和 serialization 在本轮范围之外。

### 2026-08-27：完成 EXP-003 内存 SchemaStore

- 固定首次注册、等价幂等、同键异形冲突和 exact lookup 语义。
- 使用 typed exception 保留冲突双方或缺失 key，且不提供 latest fallback。
- 暂缓 Store interface、线程安全与持久化实现，等待后续真实消费者塑形。

### 2026-08-27：完成 EXP-002 Schema 值模型

- 定义首批稳定 TypeTag、字段描述和不可变 Schema 描述。
- 固定 FieldId 排序、重复字段拒绝和结构相等语义。
- 隔离 wire format，并明确普通 `GetHashCode()` 不是 durable SchemaHash。

### 2026-08-27：完成 EXP-001 元数据契约

- 落地 `DurableBase`、Type/Field 两类持久化 Attribute 和显式 `TransientAttribute`。
- 因 BCL 已占用 `Attribute.TypeId`，将 Attribute 的 CLR 属性名定为 `SchemaId`。
- 保留“所有实例字段必须显式分类”为下一阶段 Generator 诊断，而非声称当前已强制。

### 2026-08-27：建立协作与记录基线

- 完成 .NET 10、xUnit、Source Generator、CLI 和 `.slnx` 空骨架。
- 建立根目录 `AGENTS.md`，将稳定的协作方式与易变的实验认识分开。
- 决定先维护单一实验簿；只有实际内容增长到难以导航时才拆分目录或 ADR。
