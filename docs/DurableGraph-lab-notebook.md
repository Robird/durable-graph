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

- **Observed**：仓库已跑通 boxed-value 的内存 Save/Load 与 read-time upgrade demo：包含元数据契约、Schema 值模型、内存 SchemaStore/StateStore、Snapshot History，以及生成 version-aware Serializer 与静态相邻升级链的 Source Generator；尚无对象身份、wire format 或持久化实现。
- **Observed**：`DurableGraph.slnx` 包含 runtime、Generator、Build tool、CLI 和 Tests 五个项目；Build tool 是随 NuGet 包部署的私有 snapshot-history publisher/verifier，不承载运行时持久化语义。
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
- Snapshot upgrade shape probe：固定 ordinary struct、`in/out`、partial implementation、definite assignment 与 overload 的 C# 语义边界。
- Package-delivered Snapshot History：单一 `Atelia.DurableGraph` 包向直接消费者交付 runtime、Generator、MSBuild 自动接入与私有 Build tool，并由真实 PackageReference probe 验证。
- Generated read-time upgrade：按 stored version 只分派一次，随后以强类型 Snapshot locals 和 required partial 相邻 handler 直达 current domain object；Load 不隐式写回。
- Candidate design branches：在 `docs/design-branches/` 隔离尚未裁决的架构分叉。
- 本实验簿：保存随实验演化的项目认识。

### 后续方向

- **Observed**：读取阶段的闭世界升级管线已经落地；Load 本身不回写，显式 Save 才保存升级后的当前版本对象。
- **Decided**：首轮升级机制只支持唯一相邻版本链 `V1 → V2 → V3`；出现真实跳版或分支消费者前不引入一般图。
- **Decided**：每个 stored version 生成一个静态直达 current 的入口协调器；入口只有一次 version switch，随后使用 ordinary struct Snapshot 与 required partial `void UpgradeV1ToV2(in old, out next)` direct calls。runtime historical binding/upgrade registry 继续暂缓。
- **Tentative**：快速原型的 local real build 自动 append checked-in Snapshot History；CI/design-time 只读，单 writer/单 TargetFramework/串行发布。显式 Accept target 记录为竞争分支。
- **Observed**：上述 local publish/CI verify 快速原型已由包内 `build/*.props/targets` 和 `DurableGraph.Build` 落地；其工作流已实现，但 snapshot 格式和发布模型仍是可替换的原型边界。
- **Decided**：继续关闭 durable 领域继承；FieldId 展平、base private field access 和 leaf version coupling 独立记录在 DB-005。
- **Open**：Schema runtime representation 与 canonical authority 的候选分叉记录在 `DB-001`，等待 exact codec/persistent format 实验裁决。
- **Open**：哪些类型和 API 最终属于核心程序集，等待真实代码形状出现后再判断。

### 当前自洽边界

截至 EXP-009，当前 demo 能证明的是受限、单线程、公有 API 路径上的结构自洽：

- 成功保存的每条 State record 都记录 exact `(SchemaId, Version)`，且对应 Schema 已先登记在同一个 `InMemoryStateStore.SchemaStore`。
- Schema conflict 和 serialization failure 都不会覆盖 slot 中原有 State。
- Load 从 State key 解析 authoritative exact stored Schema，并在进入 serializer 前拒绝 SchemaId 不同；generated serializer 在 payload decode 前验证其 generated historical exact Schema 的 version 和完整 shape。
- generated serializer 只按稳定 FieldId 处理当前四种 scalar durable field，并忽略 transient field。
- known historical version 的 payload 先 decode 到强类型 Snapshot，再依次升级；只有全部成功后才分配 current domain object。Load 不登记 current Schema 或覆盖旧 State。

当前不能声称：

- **对象始终语义自洽**：Save 不检查输入对象的领域 invariant；Load 绕过构造器，尚无 cross-field validation、RebuildTransient 或 repository-level invariant pass。
- **两个 Store 构成原子精确合集**：失败的 Save 可以在 SchemaStore 留下未被 State 引用的 Schema；当前只保证 State → Schema 引用闭合，不保证 Schema → State，也没有 unified commit。
- **任意输入都安全**：手写 serializer 可以返回任意 object；boxed values 只做浅拷贝，missing/type-mismatched fields 虽会在 handler 和领域对象分配前失败，但尚无最终统一错误模型；额外字段仍被忽略。
- **并发或故障下仍成立**：两个 Store 都不提供线程安全、事务、crash recovery 或 durability。

因此后续可以依赖“known historical State 能被 exact Schema 绑定并只在内存中升级到 current”，但不能把 handler 纯度、对象 invariant、跨 Store atomicity 或持久化安全当作已解决。

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

### EXP-007：Snapshot struct 与 Upgrade 签名语义

状态：Concluded

日期：2026-08-27

问题：ordinary struct Snapshot 与 `in old/out next` 是否能为强类型相邻升级提供可执行的 C# 契约？`out`、`bool`、return overload 和 stack allocation 的真实边界是什么？

本轮明确不回答：

- struct 相比 class 的真实性能、GC、copy 或 stack pressure。
- production Generator 如何生成 Snapshot/partial declaration/direct pipeline。
- nullability、领域 invariant、malformed payload 和 handler error wrapping。
- durable inheritance flattening 和 jump-version path selection。

最小实验：

- 在 `experiments/SnapshotUpgradeShapeProbe/` 用同一 .NET 10 project 条件编译一组成功/失败源码。
- 成功路径执行 ordinary struct 的直接强类型 `in/out` V1 → V2，并用显式 out target 选择 V1 → V3 overload。
- 失败路径精确锁定 missing target field、false-without-out、missing partial implementation、return-only overload、`out var` ambiguity 和 managed stackalloc diagnostics。

观察：

- **Observed**：逐字段写完 V2 out Snapshot 可 0 warning / 0 error 编译并执行；漏写 Field2 产生 CS0177。
- **Observed**：显式 accessibility 的 partial upgrade declaration 缺 implementation 产生 CS8795。
- **Observed**：`newValue = default` 可以绕过逐字段 definite-assignment，且 string field 得到 null；out 只是结构赋值 tripwire，不是有效性证明。
- **Observed**：bool false path 仍须赋 out，否则同样产生 CS0177；当前没有 expected rejection consumer 支持引入 bool。
- **Observed**：两个只靠返回 V2/V3 区分的 `Upgrade(V1)` 产生 CS0111；out target type 可区分 overload，但 `out var` 产生 CS0121。
- **Observed**：含 string 的 managed Snapshot 作为 `stackalloc` element 产生 CS0208。
- **Decided**：当前原型使用 ordinary mutable struct + version-qualified required partial `void/in/out`；失败通过异常传播。
- **Deferred**：Try/bool 等到首个 expected data rejection；readonly/record struct、class return 和 `ref struct` 保留在 DB-003。
- **Rejected**：把 ordinary struct 描述成“保证栈分配、零复制或整个 State pipeline 无装箱”。

结论：用户提出的 `out` 具有真实的字段级编译期帮助；struct 值语义值得进入下一 vertical slice，但性能与完整性主张必须保持为待验证假设。

相关材料：

- `experiments/SnapshotUpgradeShapeProbe/README.md`
- `docs/design-branches/0003-snapshot-value-shape-and-upgrade-signature.md`
- `docs/design-branches/0004-snapshot-history-authoring-and-publishing.md`
- `docs/design-branches/0005-durable-inheritance-flattening.md`

### EXP-008：Package-delivered Snapshot History workflow

状态：Concluded

日期：2026-08-27

问题：能否把 EXP-006 的外部 feedback 路径固化为下游只需直接 `PackageReference` 即可获得的可复用产物，并让本地构建自动发布、CI 构建只读校验？

本轮明确不回答：

- runtime read-time upgrade orchestration、historical serializer 与自动 Save 回写。
- 最终 canonical Schema/wire format、跨程序集 ownership 和一般版本图。
- multi-targeting、并行 writer 与多个 history 文件的事务发布。
- `buildTransitive` 间接消费者语义、IDE code fix 或显式 Accept UX。

最小实现：

- Generator 严格读取单-block `*.dgsnapshot` AdditionalFiles，要求 current Vn 的 V1...V(n-1) 连续存在，并以 DG0012-DG0016 拒绝 malformed、冲突、缺口、current mismatch 和 generated member collision。
- Generator 产生固定 comment-only candidate manifest，以及当前 durable partial class 内的 private ordinary `__DurableSnapshotVn` structs；历史成员只用稳定 `Field{id}` 与 TypeTag 映射重建。
- 新增独立 `DurableGraph.Build` net10 tool，提供 `publish`/`verify`；它严格解析、全批预检、create-only 发布、同形幂等，并用 SchemaId/content 的 SHA-256 形成不含原始 SchemaId 的文件名。
- `Atelia.DurableGraph` nupkg 显式包含 runtime、`analyzers/dotnet/cs` Generator、`build` props/targets 与 `tools/net10.0` Build tool；直接消费者不写 `Import`、Analyzer reference、AdditionalFiles 或 post-compile script。
- local 默认为 Publish；`ContinuousIntegrationBuild=true` 默认为 Verify；design-time 跳过，`Off` 只作为显式诊断逃生口。

观察：

- **Observed**：真实 local-feed restore 证明 package layout 会自动加载 Generator 与 build assets，项目文件只含一个普通 `PackageReference`。
- **Observed**：空 history 直接构建 V2 以 DG0014 失败且不发布；V1 成功构建发布 V1，随后 clean V2 build 从 V1 history 重建 V1/V2 private structs，并执行强类型 `in/out` 转换。
- **Observed**：V2 成功构建追加 V2；重复 local publish 内容哈希不变。CI Verify 不写 history；移走 current V2 后 CI 失败且不补写。
- **Observed**：候选使用固定 `DurableGraphSnapshotCandidates.g.cs`；没有 durable type 的成功 compilation 仍产生空 manifest，避免旧 manifest 被误当作当前候选，同时不会创建空 history 目录。
- **Decided**：AdditionalFiles 与 publisher 都只读取 history 根目录的 `*.dgsnapshot`；首版不承诺递归目录布局。
- **Decided**：包只放 `build/`，不放 `buildTransitive/`；当前只为直接引用 DurableGraph 的 C# durable project 自动接入。
- **Decided**：candidate manifest 与 checked-in history 使用不同 header；Generator 不接受把 candidate 重命名后冒充已发布 history。
- **Decided**：SchemaId 必须能由 strict UTF-8 无损编码；Generator 以 DG0002 拒绝孤立 UTF-16 surrogate，防止不同 ordinal SchemaId 折叠为同一 Base64 history identity。
- **Decided**：Generator 负责结构语义，Build tool 是 checked-in history 原始字节 canonicality 的最终 gate；例如 CRLF 可能先被 AdditionalText 文本层规范化，但 publisher/verify 仍会拒绝非 LF canonical bytes。
- **Observed**：当前 TypeTag build-time parser 只接受已锁定的 1...4；新增 TypeTag 时必须同步 Generator、publisher 与格式测试。
- **Open**：普通 build 写工作树在团队/IDE 场景中的摩擦、显式 Accept workflow 和 batch atomicity 仍保留在 DB-004。

结论：EXP-006 的技术验证已经成为一个可真实消费的单包 vertical slice；它证明 build-time Snapshot History 的自动累积与只读 CI gate 可行，但尚未实现读取旧 State 或调用 upgrade handler。

相关材料：

- `src/DurableGraph/build/Atelia.DurableGraph.props`
- `src/DurableGraph/build/Atelia.DurableGraph.targets`
- `src/DurableGraph.Build/`
- `tests/DurableGraph.Tests/SnapshotHistoryToolTests.cs`
- `experiments/PackageConsumerProbe/`
- `docs/design-branches/0004-snapshot-history-authoring-and-publishing.md`

### EXP-009：Generated static read-time upgrade coordinators

状态：Concluded

日期：2026-08-27

问题：能否让当前 compilation 仅凭 checked-in Snapshot History 和当前领域源码，生成从每个 known stored version 到 current 的强类型升级路径，并使 Load 升级成功后仍不隐式改写 State/Schema Store？

本轮明确不回答：

- wire format、persistent Store、对象图身份、增量保存或 Schema/State unified commit。
- 一般版本图、跳版本 handler、runtime registry、plugin/跨程序集贡献或 path selection。
- handler 纯度、领域 invariant、RebuildTransient 和统一 malformed boxed-payload 错误模型。
- O(N²) generated call-site/IL 在大量版本下的真实性能与体积影响。

最小实现：

- `IDurableSerializer<T>.Schema` 明确为 current/write Schema；`Deserialize` 接收 authoritative `storedSchema` 与 boxed fields。
- `InMemoryStateStore.Load` 只解析 stored exact Schema、拒绝不同 SchemaId，并把同 identity 的历史版本交给 version-aware serializer；它不登记 current Schema，也不写回 State。
- Generator 先建立 history-validated IR，再生成 serializer；同 compilation 两个 current CLR types 复用 SchemaId 时以 DG0017 双方 fail closed。
- 每个 durable type 生成 private ordinary `__DurableSnapshotVn` 与 required partial `UpgradeVnToVnPlus1(in,out)`；缺实现由 CS8795、漏赋目标字段由 CS0177 拒绝。
- nested serializer 生成每版 exact `DurableSchema`、唯一一次 stored-version switch，以及每个 stored version 的独立直达-current静态协调器。
- 每个协调器固定执行 exact shape validation → 起始 Snapshot decode → 相邻强类型 direct calls → 最后 `GetUninitializedObject` 并 hydrate current fields。
- unknown version、identity mismatch、shape conflict 和具体 edge failure 使用 typed exceptions；edge failure 保留原异常为 InnerException。

观察：

- **Observed**：动态 V1 compilation 生成并 Save 的 State 可由独立 V2 compilation 读取；第一次与重复 Load 都调用 V1→V2 handler，且 SchemaStore 没有 V2。
- **Observed**：显式 Save 已升级的 V2 object 后，slot 才推进到 V2；后续 Load 走 current coordinator，不再调用历史 handler。
- **Observed**：V1→V2→V3 handler 严格按顺序执行并传递强类型字段值；generated code 只含一次 `storedSchema.Version switch`。
- **Observed**：historical shape mismatch、缺字段和错误 boxed type 在 handler 与 current domain object 分配前失败；handler 异常被包装为带 SchemaId/from/to 的 `DurableUpgradeException`。
- **Observed**：真实 local nupkg consumer 用手写 V1 serializer 保存 boxed State，再通过 package-delivered `Character.Serializer` 连续 Load、显式 Save 和 current Load，证明 runtime/Generator/build assets 的发布组合也执行同一升级语义。
- **Decided**：独立挑战后仍选择每入口单帧直线协调器。共享 O(N) suffix helpers 是唯一有竞争力替代，但会产生多层 frame 并把单条路径拆散；只有版本数或 generated IL 体积出现实测问题时重访。
- **Decided**：O(N²) 指所有入口累计生成的 call-sites/IL，一次从 Vn Load 到 current 仍只执行 O(current-n) 条相邻边。
- **Deferred**：额外 boxed fields 继续忽略；`newValue = default` 仍可绕过 out 的逐字段 tripwire；用户 handler 仍可自行产生外部 side effect。

结论：闭世界唯一相邻链的 read-time upgrade vertical slice 成立；动态性被限制在一次版本入口分派，历史 payload 进入 handler 前已转成强类型 Snapshot，Store authority 只有显式 Save 才改变。

相关材料：

- `src/DurableGraph/IDurableSerializer.cs`
- `src/DurableGraph/InMemoryStateStore.cs`
- `src/DurableGraph/DurableUpgradeException.cs`
- `src/DurableGraph/UnsupportedSchemaVersionException.cs`
- `src/DurableGraph.Generator/DurableSchemaGenerator.cs`
- `tests/DurableGraph.Tests/InMemoryStateStoreTests.cs`
- `tests/DurableGraph.Tests/DurableSchemaGeneratorTests.cs`
- `experiments/PackageConsumerProbe/Run-Probe.ps1`
- `docs/design-branches/0002-read-time-version-upgrade-pipeline.md`

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

### 2026-08-27：跑通 generated static read-time upgrade

- 独立方案挑战确认：共享 O(N) 后缀链只在版本数/IL 体积成为实测问题时更优；当前采用每个 stored version 一个单帧直达-current协调器。
- Generator 现在产生一次 version switch、exact historical Schema、required adjacent partial handlers 和强类型 Snapshot chain；DG0017 阻止 SchemaId 映射到多个 current CLR types。
- V1 Save→V2 Load、重复无写回、显式 Save 后推进、V1→V2→V3 顺序以及关键失败路径均由 executable tests 覆盖。
- 下一步尚未自动确定；wire format、领域 invariant/rebuild、malformed payload error、一般图与统一 commit 仍保持分离。

### 2026-08-27：将 Snapshot History feedback 固化为单一 NuGet 包

- `Atelia.DurableGraph` 包现在同时交付 runtime、Generator、自动导入的 build assets 和私有 publish/verify tool；下游无需复制脚本或手写 hook。
- production Generator 能从 checked-in `.dgsnapshot` 重建历史 private structs，并对连续版本、冲突和 current shape fail closed。
- package-only consumer probe 跑通 V2 负例、V1/V2 累积、强类型 Snapshot 使用、CI read-only failure 与重复发布幂等。
- 下一 vertical slice 回到 read-time upgrade：生成 historical deserialize/version switch、required adjacent handler，再让 Load 只在内存中升级到 current；本轮没有提前实现它。

### 2026-08-27：固定 Snapshot struct/in-out 原型方向

- 通过正反编译 probe 保留 ordinary struct 与 `in/out`，并删除当前无消费者的 bool failure channel。
- 快速原型选择成功 local build 自动发布 history；显式 Accept/CLI 与普通 build purity 保留为 DB-004 分叉。
- 将 Snapshot representation/signature、history authoring/publisher 和 durable inheritance flattening 分别记录为 DB-003/004/005。
- 下一 vertical slice 聚焦 generated version-aware serializer、required adjacent handler 与 read-time upgrade；不开放继承或跳版。

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
