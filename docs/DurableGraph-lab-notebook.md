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

- **Observed**：仓库已跑通 boxed-value 的内存 Save/Load demo：包含元数据契约、Schema 值模型、内存 SchemaStore/StateStore，以及生成 Schema 与 Serializer 的 Source Generator；尚无对象身份、升级器、wire format 或持久化实现。
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
- Boxed State demo：由 generated serializer 驱动 `InMemoryStateStore`，验证 Schema-first Save 与 validate-before-Deserialize。
- Source Generator history feedback probe：隔离证明 `AddSource` 不会自行反馈为后续 `AdditionalFiles`，但显式 post-compile publisher 可以形成下一轮可见的 Snapshot History。
- Candidate design branches：在 `docs/design-branches/` 隔离尚未裁决的架构分叉。
- 本实验簿：保存随实验演化的项目认识。

### 后续方向

- **Decided**：下一阶段先设计读取时自动尝试升级旧版本；Load 本身不回写，显式 Save 才保存升级后的当前版本对象。
- **Decided**：首轮升级机制只支持唯一相邻版本链 `V1 → V2 → V3`；出现真实跳版或分支消费者前不引入一般图。
- **Tentative**：在所有历史版本都属于当前 compilation 的闭世界模型中，优先生成 exact-version switch 与强类型相邻调用；暂缓 runtime historical binding/upgrade registry。
- **Open**：Snapshot History 的正式 authority、格式与发布工作流尚待设计；普通 build 自动改写源码树和显式 CLI/code-fix accept 都只是候选。
- **Open**：Schema runtime representation 与 canonical authority 的候选分叉记录在 `DB-001`，等待 exact codec/persistent format 实验裁决。
- **Open**：哪些类型和 API 最终属于核心程序集，等待真实代码形状出现后再判断。

### 当前自洽边界

截至 commit `cba0c41`，当前 demo 能证明的是受限、单线程、公有 API 路径上的结构自洽：

- 成功保存的每条 State record 都记录 exact `(SchemaId, Version)`，且对应 Schema 已先登记在同一个 `InMemoryStateStore.SchemaStore`。
- Schema conflict 和 serialization failure 都不会覆盖 slot 中原有 State。
- Load 在调用 deserializer 前验证 stored Schema 的 identity、version 和完整 shape。
- generated serializer 只按稳定 FieldId 处理当前四种 scalar durable field，并忽略 transient field。

当前不能声称：

- **对象始终语义自洽**：Save 不检查输入对象的领域 invariant；Load 绕过构造器，尚无 cross-field validation、RebuildTransient 或 repository-level invariant pass。
- **两个 Store 构成原子精确合集**：失败的 Save 可以在 SchemaStore 留下未被 State 引用的 Schema；当前只保证 State → Schema 引用闭合，不保证 Schema → State，也没有 unified commit。
- **任意输入都安全**：手写 serializer 可以返回任意 object；boxed values 只做浅拷贝，malformed/missing/type-mismatched fields 也没有最终错误模型。
- **并发或故障下仍成立**：两个 Store 都不提供线程安全、事务、crash recovery 或 durability。

因此下一阶段可以依赖“读取某条 State 前能够取得并校验其 exact historical Schema”，但不能把对象 invariant、跨 Store atomicity 或持久化安全当作已解决。

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

### EXP-005：Schema-gated boxed State round-trip

状态：Concluded

日期：2026-08-27

问题：能否用刻意潦草的进程内 State 表示，首次跑通“generated serializer → Save 自动登记 Schema → Load 先校验 Schema → generated deserializer”的闭环？

本轮明确不回答：

- wire format、跨进程/重启持久化、durability、事务和并发。
- DurableId、共享引用、循环图、collections、properties 与继承字段。
- 旧版本自动升级、upgrade registry、RebuildTransient 和对象图 invariant validation。
- malformed/untrusted boxed payload 的完整错误模型。

最小实验：

- `IDurableSerializer<T>` 暴露 exact Schema、FieldId → boxed value 的 Serialize，以及反向 Deserialize。
- `InMemoryStateStore` 用 ordinal string slot 保存 `(SchemaId, Version, boxed fields)`；slot 明确不是 DurableId。
- Save 在调用 serializer 和替换 State 前注册 Schema。
- Load 在调用 deserializer 前校验 stored exact Schema 的 identity、version 和 shape。
- Generator 在 `Schema` 旁生成静态 `Serializer`，支持当前四种 scalar TypeTag。
- generated Deserialize 使用 `RuntimeHelpers.GetUninitializedObject` 创建实例并直接填充 durable fields。

观察：

- **Decided**：不使用 obsolete 的 `FormatterServices.GetUninitializedObject`；.NET 10 路径使用官方替代 `RuntimeHelpers.GetUninitializedObject`。
- **Observed**：只有带参构造的动态测试类型可成功恢复；Load 不再次运行构造器或 field initializer。
- **Observed**：所有 durable fields 由生成代码赋值，transient field 保持零值；尚无 RebuildTransient。
- **Observed**：Schema conflict 在 Serialize 前失败；serialization failure 不覆盖旧 State；Load mismatch 在 Deserialize 前失败。
- **Decided**：当前 boxed fields 是浅拷贝的 `Dictionary<int, object?>`，仅用于同进程流程实验，不构成 serialization 或 persistence 证据。
- **Decided**：当前不支持继承，durable type 必须 sealed，防止派生实例经基类 serializer 静默截断。
- **Decided**：readonly durable field 被 DG0011 拒绝；generated deserializer 必须能直接写入全部 durable fields。
- **Observed**：缺失 FieldId 或 boxed type 错误由 dictionary/cast 自然失败，多余字段被忽略；这些不是最终 malformed-input contract。
- **Open**：当前 Deserialize 在读取全部字段前分配未初始化对象。内存 Store 正常路径不会产生 malformed boxed state，但持久化或不可信输入阶段必须先读入/验证全部 locals，再分配对象，避免带 finalizer 的半初始化对象产生可观察行为。
- **Observed**：独立审查发现并促成 sealed-type 和 enclosing type named `Schema` 两个 fail-closed 修复；复审后无 blocking 或 medium finding。

结论：第一个 Schema-gated State round-trip demo 成立；solution 构建为 0 warning / 0 error，71 个测试全部通过。

相关源码/测试：

- `src/DurableGraph/IDurableSerializer.cs`
- `src/DurableGraph/InMemoryStateStore.cs`
- `src/DurableGraph/StateSchemaMismatchException.cs`
- `src/DurableGraph.Generator/DurableSchemaGenerator.cs`
- `tests/DurableGraph.Tests/InMemoryStateStoreTests.cs`
- `tests/DurableGraph.Tests/DurableSchemaGeneratorTests.cs`

### EXP-006：Source Generator Snapshot History feedback

状态：Concluded

日期：2026-08-27

问题：Source Generator 能否把本轮生成的 Durable shape 自动累积为后续 compilation 的 `AdditionalFiles`，并在不保留旧领域类源码的情况下重建强类型历史 Snapshot？

本轮明确不回答：

- Snapshot History 的正式 canonical 格式和 durable authority。
- 是否允许普通 build 改写受版本控制的源码树。
- 并行 build、多 TargetFramework、原子发布和跨程序集 history ownership。
- version-aware runtime serializer、真实 upgrade handler contract 和 StateStore 集成。

最小实验：

- 在 `experiments/SourceGeneratorHistoryProbe/` 建立与正式 Generator/Runtime 隔离的 Roslyn Generator 和单一 consumer project。
- 同一 consumer project 由 `ProbeVersion` 分别只编译 V1 或 V2 领域源码；V2 源码没有 V1 领域类副本，却直接引用 generated `CharacterSnapshotV1` 与 `CharacterSnapshotV2`。
- Generator 从当前 marked fields 和显式 `*.dgsnapshot` AdditionalFiles 生成 sealed reference Snapshot，并额外产生 comment-only snapshot candidate。
- opt-in `AfterTargets="CoreCompile"` target 只在成功编译后把 candidate 复制到外部 history；下一次独立 project evaluation 才把它作为 AdditionalFile。
- PowerShell runner 覆盖 no-feedback 负对照、V1 publish、clean V2 build、强类型 handler 执行、clean rebuild 和重复发布幂等。

观察：

- **Observed**：关闭 publisher 时，V1 `AddSource` candidate 即使仍物理存在，下一次 V2 build 也不会把它视为 AdditionalFile；V2 因缺少 `CharacterSnapshotV1` 按预期编译失败。
- **Observed**：开启 publisher 后，成功 V1 build 产生一个外部 V1 snapshot；clean V2 build 读取它并同时生成 `CharacterSnapshotV1`/`V2`，强类型 `V1 → V2` 方法成功执行。
- **Observed**：AdditionalFiles feedback 延迟一个 build；同一次 compilation 看不到 post-compile 新发布的文件。
- **Observed**：clean V2 rebuild 可只依赖显式 history 重现，重复发布保持两个 history 文件及其 SHA-256 不变。
- **Observed**：完整 probe 矩阵和现有 solution gate 都能做到 0 warning / 0 error；负对照中的预期编译错误由 runner 显式要求。
- **Rejected**：依靠 Source Generator 自身、compiler cache 或 `obj` 中 `.g.cs` 隐式累积历史。`AddSource` 是当前 compilation 的派生输出，不是下一轮输入 authority。
- **Tentative**：MSBuild hook、自制 build tool 或 CLI 可以承担显式 side effect；本实验只证明技术可行性，没有选定正式发布 owner。
- **Open**：正式 publisher 必须进一步定义同 key 异形冲突、并行与原子发布、CI/IDE/design-time 权限，以及 build 改写工作树是否可接受。

结论：强类型 Snapshot History 可以由“显式历史输入 → 纯 Generator 投影”稳定重建；全自动累积需要 Generator 外部的受控 build side effect，而且只能供下一次 build 使用。

相关材料：

- `experiments/SourceGeneratorHistoryProbe/README.md`
- `experiments/SourceGeneratorHistoryProbe/Run-Probe.ps1`
- `experiments/SourceGeneratorHistoryProbe/Probe.Generator/HistoryProbeGenerator.cs`
- `experiments/SourceGeneratorHistoryProbe/Probe/HistoryProbe.csproj`

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

### 2026-08-27：完成 Source Generator history feedback probe

- 用同一 consumer project 的 V1/V2 负对照证明 `AddSource` 物理输出不会自动进入下一轮 `AdditionalFiles`。
- 用 opt-in post-compile publisher 跑通 V1 history 发布、clean V2 强类型 Snapshot 恢复与幂等重复发布。
- 将 runtime registry 暂缓，下一步先围绕闭世界 generated version switch/typed adjacent calls 继续实验。
- 保留 history 格式、publisher owner 和普通 build 是否允许修改工作树为开放设计问题。

### 2026-08-27：Compaction 前冻结升级阶段入口

- 收窄 EXP-005 的保证：当前仅有 State → exact Schema 的结构闭合与 schema-gated load，不保证对象语义 invariant 或两个 Store 的原子合集。
- 下一阶段聚焦 read-time version upgrade；加载只产生当前版本内存对象，不隐式回写，后续显式 Save 才写入升级版本。
- 在 `DB-002` 记录 historical serializer binding、upgrade path 与 failure gates 的候选设计。

### 2026-08-27：完成 EXP-005 boxed State round-trip

- 新增 `InMemoryStateStore` 与 boxed serializer runtime seam，固定 Schema-first Save 和 validate-before-Deserialize。
- Generator 产生 `Serializer`，通过 `RuntimeHelpers.GetUninitializedObject` 绕过构造器并恢复四种 scalar field。
- 用 sealed-type、readonly field 和生成成员冲突 diagnostics 保持当前不支持范围 fail closed。
- 明确 boxed dictionary、零值 transient 和 allocation-before-validation 均是原型边界，不是持久化承诺。

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
