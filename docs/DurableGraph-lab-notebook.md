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

记录日期：2026-08-29

- **Observed**：仓库已跑通 boxed-value 的内存 Save/Load 与 read-time upgrade demo，并以隔离探针跑通单类型 Flat Graph Delta R1、generated graph operations R2、StoredGraphImage normalization R3a 与 two-pass CLR materialization R3b；production runtime/default Generator 仍无对象身份、reference graph、wire format 或持久化实现。
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
- Research roadmap：按 Graph Delta semantic probe → generated graph operations → normalized graph Load → logical delta chain → binary codec → persistent publication 的证据依赖安排后续切片。
- Flat Graph Delta R1：已用 fixture-only latest typed Snapshot baseline、flat ID table、`RequiresRewrite`、whole-object Upsert 与 success-only baseline replacement 完成可执行探针。
- Generated Graph Operations R2：internal、无 `[Generator]` 的 probe generator 已产生强类型 Capture/Snapshot equality/reference visitor，并与 R1 oracle 做差分验证；默认 package analyzer 路径不运行它。
- Stored Graph Normalization R3a：test-only mixed V1/V2 record table 经全表 exact preflight、typed decode/upgrade 与 source-reference gate 归一化成 current-Snapshot baseline。
- CLR Graph Materialization R3b：只对 normalized current root closure allocate-all/hydrate-all，恢复 sharing/cycles 后 root-only exposure；disconnected source rows 不分配。
- StateStore 基础设计：选择 one-Revision/one-RBF-frame、object-level version chains、ObjectVersionDict authority、LSB-tagged `RelativeFrameTicket` 与 current-head two-file reconstruction closure；产品实现尚未开始，TwoLegRotationProbe 已进入 provisional layout 模拟。
- 双腿轮转派生说明：记录 A/B/C evacuation、Revision shared prior-snapshot anchor、absolute-normalized ObjectVersionDict、one-frame bounds 与 `CanPrepareAndRotate` safety gate。
- Adaptive rotation branch DB-007：隔离尚未裁决的统一 Base/Delta/cold-migration/rotation 策略和内存模拟输入。
- Two-leg rotation probe：以独立 xUnit 项目建立 exact RBF v0.40 envelope、相邻 FileScope、runtime OVD authority、Frame/ObjectVersion 父链、deterministic workload 和三种 policy；StateMap 由 OVD replay 派生，并保留 contextual-self `ProvisionalRevisionV0` 组件尺寸/provenance。
- Candidate design branches：在 `docs/design-branches/` 隔离尚未裁决的架构分叉。
- 本实验簿：保存随实验演化的项目认识。

### 后续方向

- **Observed**：读取阶段的闭世界升级管线已经落地；Load 本身不回写，显式 Save 才保存升级后的当前版本对象。
- **Decided**：首轮升级机制只支持唯一相邻版本链 `V1 → V2 → V3`；出现真实跳版或分支消费者前不引入一般图。
- **Decided**：Generator 生成一个线性 read-time coordinator：入口只有一次 version switch，各 case decode 对应 Snapshot 后以 `goto SnapshotVnReady` 进入共享的顺序相邻边；ordinary struct 与 required partial `void UpgradeV1ToV2(in old, out next)` 契约不变。runtime historical binding/upgrade registry 继续暂缓。
- **Tentative**：快速原型的 local real build 自动 append checked-in Snapshot History；CI/design-time 只读，单 writer/单 TargetFramework/串行发布。显式 Accept target 记录为竞争分支。
- **Observed**：上述 local publish/CI verify 快速原型已由包内 `build/*.props/targets` 和 `DurableGraph.Build` 落地；其工作流已实现，但 snapshot 格式和发布模型仍是可替换的原型边界。
- **Decided**：继续关闭 durable 领域继承；FieldId 展平、base private field access 和 leaf version coupling 独立记录在 DB-005。
- **Observed**：EXP-011 已跑通 fixture-only 单类型 Graph Delta 语义探针；逻辑 baseline 是带 RootId 的 flat ID table，每项保存 current Snapshot 与 `RequiresRewrite`，未引入 bytes、持久 head 或正式 DurableId。
- **Observed**：EXP-012 已跑通 isolated generated graph operations；caller-provided `TIdentity : struct` 只是一条 test seam，未引入正式 DurableId、Reference TypeTag 或产品 Generator 支持。
- **Decided**：当前不把 self-reference 半接入 scalar-only Schema History/boxed Serializer；默认 `DurableSchemaGenerator` 继续 DG0007 fail closed，R3a/R3b 均留在 test-only logical graph。
- **Observed**：EXP-013 已跑通 test-only StoredGraphImage normalization；全表 Schema preflight 先于 Decode，V1/V2 均归一化为 current `ProbeSnapshot`，并保留完整 source record table 与 historical rewrite obligation。
- **Decided**：R3a 失败只承诺不修改输入、不返回 partial baseline；decode/upgrade hook 自身的外部 side effect 不可回滚。其 current baseline 随后成为 R3b materialization 输入。
- **Observed**：EXP-014 已跑通 root-only two-pass materialization；sharing/self-cycle/two-node cycle 恢复 `ReferenceEquals`，constructor/initializer 被绕过，transient 保持零值。
- **Decided**：materialized CLR root 是 disposable working graph，不是 baseline/StateMap authority；`RequiresRewrite` 只影响 Save。R4 将从带 disconnected rows/rewrite obligations 的 source StateMap 开始验证 apply 后 exact clean closure。
- **Decided**：historical payload 在读取边界 exact decode 并升级到 current Snapshot；reachable upgraded node 在下一次显式 Save whole-object rewrite，unreachable upgraded node 不被保活。
- **Decided**：normalized baseline 是派生比较投影，不复制 per-entry source Schema/object address；未来 persistent Save 通过 graph-level exact head + authoritative StateMap 与 projection 的同源 bundle 取得 provenance。
- **Decided**：当前研究优先级从低风险的 R4 logical StateMap/apply 暂时切换到 StateStore 双腿轮转；R4–R7 依赖顺序保留，未被否定。
- **Decided**：首版只保证 latest published Revision，采用进程独占 single writer；一次 Revision 暂为一个 RBF Frame，越过约 256 MiB payload/TailMeta 或 64 KiB TailMeta 边界时 fail closed，Extent 留待容量证据。
- **Decided**：持久地址使用 LSB-tagged `RelativeFrameTicket = (SizedPtr.Serialize() << 1) | same/previous`，进程内 authority 使用 `AbsoluteFrameAddress`；接受约 512 GiB 最大 frame-start 的容量代价。
- **Decided**：同 Revision 的 OVD binding 用字段级 `BindSelf=1`，不把 Self 加入通用 RelativeFrameTicket；RBF context 已提供 containing ticket，TailMeta 保存 OVD/record offset。literal self-ticket 因重复信息和多固定点 canonicality 被当前 Working Design 淘汰，multi-frame 时重访 DB-008。
- **Decided**：Delta 以 `DeltaParentFrameTicket` 直接指 exact ObjectVersion；Base 不保存 direct
  parent，lineage 统一使用 containing Revision OVD 的 shared prior-snapshot anchor。从 A/B 轮转到
  B/C 时，C OVD anchor 指最终 B PublishedRevision；不写 per-Object forwarding record。
- **Rejected**：B 中 forwarding RelayRevision 只优化罕见 lineage/TailMeta reads，却扩大写入、容量、durable 顺序与恢复状态；竞争实现由 tag `research/relay-vs-relay-free-20260829` 保存，裁决见 DB-009。
- **Decided**：物理删除文件后的数据不可访问不属于地址格式需要抵抗的故障模型；Base locator 只承诺 retained files 之间的 lineage 可导航。
- **Observed**：S1 preparatory baseline 已把冻结 workload 的每个 Save 编译为带 runtime OVD 的单个 Frame；live StateMap 从 OVD replay 派生，Base lineage 读取 Revision shared anchor，Delta reconstruction/lineage 读取 exact parent，并以 checked symbolic Delta apply 逐 prefix 对照 logical replay。
- **Observed**：S1b 已对给定 Payload/TailMeta 长度复刻 exact RBF v0.40 envelope，并把 synthetic workload 接入逐 Save write 与 post-save reconstruction/co-read observations；accounting 明示排除 DG header/OVD/index/VarUInt，所以尚不能证明完整 Revision bytes、容量安全或策略 winner。
- **Observed**：S1c 已加入 ratio=3 的 `ObjectPayloadReadAmplification3` 与四场景 matrix；per-object reconstruction payload 随 ObjectVersion 保存并由 oracle 重算，但不计入 layout。结果只证明局部策略形成可复现 tradeoff，不代表 exact StateJournal port 或 winner。
- **Observed**：S1d `ProvisionalRevisionV0` 已按临时 grammar 计入 domain headers、OVD、TailMeta directory、relative VarUInt 与 exact RBF envelope；run-level provenance、layout、capacity gates、contextual Self 和旧 baseline 回归均有 executable evidence。它仍是 size-only estimator，不是 byte codec 或 rotation capacity proof。
- **Decided**：当前不引入 `MaxLogicalChainBytes`、`TargetFileBytes` 或固定 migration budget；先在纯内存模拟中采集无权重原始量，比较自适应统一策略。
- **Open**：统一策略能否仅依靠 two-file pressure、lineage/reconstruction overhead 与渐进 cold Base migration 自动收敛；`CanPrepareAndRotate` 必须把 B Base migration 可完成性与 C evacuation Revision 可编码性一起作为策略无关的 safety oracle。
- **Open**：Schema runtime representation 与 canonical authority 的候选分叉记录在 `DB-001`，等待 exact codec/persistent format 实验裁决。
- **Open**：哪些类型和 API 最终属于核心程序集，等待真实代码形状出现后再判断。

### 当前自洽边界

截至 EXP-014，公有 demo 仍只具有 EXP-010 的受限、单线程结构自洽边界；EXP-011/012/013/014 是隔离证据，不扩大 package/runtime 保证：

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

> 初始的每版本独立协调器代码形状已由 EXP-010 线性化；本实验建立的 runtime seam、强类型相邻链与无写回语义继续有效。

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
- **Superseded**：初次独立挑战曾在“每入口单帧直线协调器”和“多层 O(N) suffix helpers”之间选择前者；EXP-010 后来用 goto labels 找到单帧且 O(N) 的第三种形状。
- **Observed**：初始 O(N²) 只涉及所有入口累计生成的 call-sites/IL，一次 Load 的执行边数始终是 O(current-n)；EXP-010 消除了前者。
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

### EXP-010：Goto-label linear upgrade coordinator

状态：Concluded

日期：2026-08-27

问题：能否在不增加 helper frames、不改变任何 public/runtime/handler 语义的前提下，用 generated `goto` labels 共享相邻升级后缀，把所有版本入口累计生成规模从 O(N²) 降为 O(N)？

本轮明确不回答：

- JIT 是否复用不同 Snapshot locals 的 stack slots，或单个较大方法的首次 JIT 成本。
- 大型 Snapshot/长历史链的实测栈帧、机器码体积和吞吐。
- 一般版本图、跳边、循环或分支 path selection。

最小实验与实现：

- 独立 warnings-as-errors probe 预先声明全部 Snapshot locals；每个 switch case 完成 exact-version decode 后跳到 `SnapshotVnReady` label。
- switch 后只生成一份有序 labels；每个非 current label 调用并包装一条相邻 edge，然后自然落入下一 label；current label 只 materialize/hydrate/return 一次。
- 保持 shape-before-decode、decode-before-handler、handler-before-domain-allocation，以及 unknown/identity/shape/edge typed failure 语义不变。
- Generator tests 结构性锁定：一个 switch statement、没有 `DeserializeVn` methods、每个版本字段 decode 一次、每条 edge call 一次、`GetUninitializedObject` 一次。

观察：

- **Observed**：C# definite-assignment 接受 case→label 多入口与 labels 的顺序穿透；V1/V2/V3 和零字段历史 Snapshot 均成功动态编译执行。
- **Observed**：原有 V1 Save→V2 Load/no-writeback、V1→V2→V3 顺序、异常包装、shape/payload gates、CS8795/CS0177、DG0017 与 package E2E 语义保持不变。
- **Decided**：generated 源码和 IL 控制流结构现在随版本/边数量线性增长；一次 Load 仍执行 O(current-stored) 条边。
- **Open**：单方法包含全部 Snapshot locals；虽然其生命周期呈线性，`in/out` 会取地址，JIT 不保证 slot reuse。历史很长或 Snapshot 很大时需用实际 stack/JIT/IL 测量裁决。

结论：goto 在手写领域代码中通常需要克制，但在确定性 generated state-machine-like code 中恰好表达了“多个静态入口共享同一升级后缀”，以更小生成规模保留了单帧、强类型和直线可读性。

相关材料：

- `src/DurableGraph.Generator/DurableSchemaGenerator.cs`
- `tests/DurableGraph.Tests/DurableSchemaGeneratorTests.cs`
- `docs/design-branches/0002-read-time-version-upgrade-pipeline.md`

### EXP-011：Flat Graph Delta semantic probe

状态：Concluded

日期：2026-08-27

问题：能否在不引入产品 DurableId、Generator graph adapter、wire format 或 Store 的前提下，用单类型强类型 fixture 验证 flat latest-Snapshot baseline、identity-aware traversal、whole-object delta 和成功后 clean baseline 的核心状态律？

本轮明确不回答：

- historical payload → current baseline 的真实 Load integration。
- generated `Capture / Equals / VisitReferences`。
- two-pass CLR graph hydrate、正式 DurableId、异构类型和 Schema identity gate。
- binary codec、StateMap/head、persistent delta chain、commit 和 concurrent mutation。
- allocation、stack、throughput 或内存优化。

最小实现：

- 在测试项目内定义 internal `ProbeId`、`ProbeNode`、`ProbeSnapshot`、`BaselineEntry`、`NormalizedBaselineGraph` 和 `GraphDelta`，不修改 runtime public surface。
- `PlanSave` 用显式 stack 和单一 ID→CLR instance table 遍历 current root closure；同 ID/different instance 在 visited skip 前 fail closed。
- 每个节点只读取一次 reference fields，同一 locals 同时形成 Snapshot ID slots 和 child work items。
- missing、`RequiresRewrite` 或 Snapshot unequal 产生完整 Upsert；source baseline IDs 与 current reachable IDs 的差集成为 Unreachable。
- `AcceptForAssertion` 拒绝 Upsert/Unreachable overlap、遗漏 rewrite obligation、missing root、dangling reference 和结果 table 的 disconnected entries；成功时返回全新的 clean baseline。

观察：

- **Observed**：21 个聚焦测试覆盖 no-op、transient、leaf locality、identity replacement、sharing、cycles、duplicate ID、root replacement、rewrite/unreachable 分流、default ID、defensive copy、capture failure/retry、malformed candidate 和状态律。
- **Observed**：`RequiresRewrite` 不必携带 source version 就能满足当前比较与首次显式重写；未来 record provenance 仍属于 authority StateMap，不是本轮结论。
- **Observed**：baseline 可以包含因 normalization 而暂时 disconnected 的 source entries，但 accepted clean baseline 必须恰好等于新 RootId closure；独立审查发现并促成了该 gate。
- **Observed**：当 ProbeId immutable 时，无需同时维护 ID map、reference visited set 和 reachable set；一个 ID map 足以保留全部 R1 语义。
- **Observed**：Map/Set 容器枚举顺序没有被提升为语义；确定性展示由测试显式按 ID 排序。
- **Observed**：solution build 0 warning / 0 error，完整测试从 100 增至 121 且全部通过；三路最终复审无 blocker/medium。

结论：DB-006 R1 semantic probe 成立。它证明的是纯内存逻辑状态转换，不是产品对象图持久化；下一主线入口是 R2 generated graph operations。

相关材料：

- `tests/DurableGraph.Tests/GraphDeltaProbe.cs`
- `tests/DurableGraph.Tests/GraphDeltaProbeTests.cs`
- `docs/design-branches/0006-flat-graph-delta-prototype.md`
- `docs/DurableGraph-research-roadmap.md`

### EXP-012：Generated Graph Operations probe

状态：Concluded

日期：2026-08-27

问题：Source Generator 能否为单个 direct-self-reference durable type 产生 R1 所需的强类型 current Snapshot capture、durable equality 和 reference visitation，同时暂不定义正式 identity、Reference TypeTag、wire format 或产品 graph API？

本轮明确不回答：

- 默认/package `DurableSchemaGenerator` 如何发布 reference Schema/history。
- 正式 `DurableId`、allocator、异构 dispatch、polymorphism 或跨程序集 reference。
- historical graph payload normalization、two-pass hydrate、StateMap、bytes、commit 或 recovery。
- provisional `TIdentity`、delegate 与 generated private shape 的性能或最终 API 适合性。

最小实现：

- 在 Generator 程序集内新增 internal、无 `[Generator]` 的 `DurableGraphOperationsProbeGenerator`；测试用 Roslyn driver 显式运行，默认 analyzer discovery 保持不变。
- 支持四种 scalar 与字段类型恰好等于当前 durable type 的 direct self-reference；Snapshot reference slot 使用 caller-provided `TIdentity?`，CapturedReferences 保存具体 CLR child。
- `CaptureCurrent` 先按 FieldId 把 durable fields 各读入一个 local，再用同一 reference local 同时写 Snapshot ID 和 captured child；`DurableEquals` 比较 scalar/ID；`VisitReferences` 按 field slot 访问且不预先去重 shared child。
- generated-driven test coordinator 继续复用 R1 的 identity-conflict/cycle/reachability 语义，但 expected baseline 和 literal delta 不由被测 capture 生成。

观察：

- **Observed**：shared two-node cycle 的 root/child Snapshot、重复 alias visitation 和 transient omission 与 literal values 及 EXP-011 oracle 一致。
- **Observed**：两个不同 CLR child 具有相同 ID、或只修改 child 内容时，parent Snapshot 保持相等；child ID/null slot 改变时 parent Snapshot 改变。
- **Observed**：no-op、transient-only、child scalar locality 与 same-valued child replacement 的 generated delta 同时匹配 literal Upserts/Unreachable 和 R1 oracle。
- **Observed**：array、base-typed、cross-type reference 以 DG0007 fail closed；reserved helper 以不可配置的 probe-only DG0018 fail closed；失败的单类型 run 不产生空 hint。
- **Observed**：生成文本不受字段声明顺序影响并使用 LF；reference visit 明确按 FieldId。syntax/symbol/semantic gates 证明 generated normal path 没有 `object`/`dynamic` data flow、dictionary、TypeTag 或 Serializer。
- **Observed**：测试首先抓到 validation failure 后残留空 hint，以及文本扫描不足以排除 weak `object` flow 两个问题；修复后两路独立复审均无 blocker/medium。

验证：

- R1+R2 聚焦 `~Probe`：31/31 passed，其中 EXP-012 新增 10 cases。
- Generator 单项目：0 warnings / 0 errors。
- `DurableGraph.slnx`：0 warnings / 0 errors；完整 `DurableGraph.Tests`：131/131 passed。
- `dotnet format --verify-no-changes`：passed；真实 PackageConsumerProbe：passed，确认 packaged analyzer 仍只自动发现产品 Generator。

结论：R2 的 code-generation seam 获得可执行证据，但仍是默认不可发现的隔离探针；它没有改变严格 1...4 Snapshot History 或产品 self-reference 的 DG0007 边界。下一主线入口是 R3a test-only StoredGraphImage → normalized baseline。

相关材料：

- `src/DurableGraph.Generator/DurableGraphOperationsProbeGenerator.cs`
- `tests/DurableGraph.Tests/GeneratedGraphOperationsProbeTests.cs`
- `tests/DurableGraph.Tests/GraphDeltaProbe.cs`
- `docs/design-branches/0006-flat-graph-delta-prototype.md`
- `docs/DurableGraph-research-roadmap.md`

### EXP-013：Stored Graph normalization probe

状态：Concluded

日期：2026-08-27

问题：能否把 mixed exact-version logical records 全有或全无地归一化为 latest/current typed Snapshot baseline，并把 historical rewrite obligation 与完整 source record table 正确交给 R1 Save？

本轮明确不回答：

- R3b CLR placeholder allocation、reference hydrate、sharing/cycle `ReferenceEquals` 或 transient rebuild。
- product DurableId、Reference TypeTag、Generator/Store integration 或 historical wire bytes。
- heterogeneous dispatch、upgrade-created node、StateMap/head、commit、recovery 或 concurrency。
- decode/upgrade handler 外部 side effect 的 rollback。

最小实现：

- `StoredGraphImage` 从 entry sequence defensive copy 出 record table；duplicate/default ID、missing/default root 和 null record 在构造阶段 fail closed，`Records.Keys` 是唯一 `SourceRecordIds`。
- test-only `ProbeStoredSchema` 对 SchemaId/version/sorted FieldId-kind 做内容相等；logical kind 只有 `Int32` 与 `Reference`，不修改产品 TypeTag 1...4。
- 封闭 V1/V2 record variants 分别返回 `ProbeSnapshotV1` 与 current `ProbeSnapshot`；V1 handler 是 `void(in old, out current)`。
- normalization 先按 ID 对全表 preflight identity/version/exact shape/payload variant，确认 required handler；随后逐 record typed decode/upgrade、验证对完整 SourceRecordIds 的 references，全部成功后才返回 `NormalizedBaselineGraph`。

观察：

- **Observed**：mixed/reversed V1/V2 records 得到相同 current baseline 与按 ID trace；value-changing 与 value-preserving historical upgrades 都 `RequiresRewrite=true`，current record 为 false。
- **Observed**：identity、shape、unknown version 与 payload variant mismatch 均在全表任何 Decode/Upgrade 前失败。
- **Observed**：current、upgrade-produced 和显式 nullable-default reference 都 fail closed；坏引用位于 disconnected source record 时仍被检查。
- **Observed**：upgrade 删除旧 edge 后 target entry 仍保留；R1 PlanSave 对 matching current root 强制 Upsert historical root，并把断开的 target 标为 Unreachable。
- **Observed**：late typed Decode failure 与 late Upgrade failure 都不返回 partial baseline，保留 input records/inner exception；同一 image 修复后重试重新 decode 并返回完整 baseline。
- **Observed**：schema/entry input arrays、typed value payload 与 normalized baseline 相互 detached；core map/set 枚举顺序仍不成为语义。
- **Observed**：独立 correctness/test-evidence review 找到并促成 decode-failure retry 与 null-record coverage；最终无 blocker/medium。

验证：

- R3a 聚焦测试：9/9 passed。
- `DurableGraph.slnx`：0 warnings / 0 errors；完整 `DurableGraph.Tests`：140/140 passed。
- `dotnet format --verify-no-changes`：passed。
- 真实 PackageConsumerProbe：passed，确认 test-only R3a 未改变 packaged runtime/Generator/history 行为。

结论：R3a normalization state law 成立，但只是一套 test fixture evidence。其 baseline 随后成为 R3b 输入；StoredGraphImage、probe Schema 与 sorted processing policy 都未提升为 public/durable contract。

相关材料：

- `tests/DurableGraph.Tests/StoredGraphNormalizationProbe.cs`
- `tests/DurableGraph.Tests/StoredGraphNormalizationProbeTests.cs`
- `tests/DurableGraph.Tests/GraphDeltaProbe.cs`
- `docs/design-branches/0006-flat-graph-delta-prototype.md`
- `docs/DurableGraph-research-roadmap.md`

### EXP-014：Two-pass CLR graph materialization probe

状态：Concluded

日期：2026-08-28

问题：能否从 normalized current-Snapshot baseline 只物化 current root closure，先 allocate-all 再 hydrate-all，恢复 sharing/cycles，并在 allocation/hydration failure 时不返回 partial root？

本轮明确不回答：

- product DurableId/allocator、Reference TypeTag、Generator materializer 或 heterogeneous dispatch。
- `RebuildTransient`、graph invariant validation 与它们失败时的最终 exposure policy。
- StateMap/cache/session ownership、bytes、commit、recovery 或并发。
- allocation、stack、throughput 或内存性能。

最小实现：

- 从 baseline RootId 用显式 stack 计算 current reachable ID set；不修改或裁剪 baseline，disconnected source rows 不进入 CLR closure。
- 第一轮以 `RuntimeHelpers.GetUninitializedObject` 为每个 reachable ID 建立唯一 placeholder；第二轮才从完整 map 解析 references 并一次性 hydrate scalar/identity/edges。
- `ProbeNode` 增加 fixture-only one-time hydration seam：只允许 constructor-bypassed、尚未绑定 ID 的 placeholder 调用；正常构造或已绑定节点 fail closed。
- 全部成功后只返回 root；phase hook 只接收 `(Phase, Id)`，不暴露 placeholder/map。可插拔 allocator 仅用于 fault injection，默认路径不变。

观察：

- **Observed**：R3a historical/current/disconnected baseline 经 R3b 恢复 literal scalar、shared alias、two-node cycle 与 leaf；随后 R1 PlanSave 仍由 baseline flag 强制 Upsert historical root，并把 disconnected source row 标为 Unreachable。
- **Observed**：self-cycle、two-node cycle 与 shared references 均以 `ReferenceEquals` 恢复；重复 materialization 产生互不共享的新 CLR graphs，不安装 cache。
- **Observed**：所有 Allocation phase 结束后才出现 Hydration phase；tests 只比较 phase 与 ID sets，不把 HashSet/Dictionary 枚举顺序提升为语义。
- **Observed**：constructor/field initializer 被绕过，transient 与 CaptureHook 保持零值；`RequiresRewrite` 不进入 CLR object；one-time seam 拒绝 rebind 且不修改既有 fields。
- **Observed**：late allocator-boundary failure 与 late hydration failure 均保持 result null、outer phase/id/inner exception 与 baseline 不变；同一 baseline retry 重新执行完整两阶段并返回完整 graph。
- **Observed**：dangling/default baseline 由 `NormalizedBaselineGraph` 在 materializer 入口前拒绝，phase hook 计数保持零；materializer 不重复建立第二套 malformed-input authority。
- **Observed**：独立 review 找到并促成真实 allocator fault seam 与移除 ID sorting；最终无 blocker/medium。

验证：

- R3b 聚焦测试：7/7 passed。
- `DurableGraph.slnx`：0 warnings / 0 errors；完整 `DurableGraph.Tests`：147/147 passed。
- `dotnet format --verify-no-changes`：passed；真实 PackageConsumerProbe：passed。

结论：R3b root-only two-pass materialization state law 成立，但仍是 test fixture evidence。materialized root 是 disposable working graph；下一主线进入 R4 logical StateMap/repeated delta apply，R5–R7 顺序不变。

路线修订：

- R4 初始 StateMap 允许含 read-time upgrade 后相对 current root disconnected 的 source rows 与 rewrite obligations；成功 apply 后必须得到 exact result-root closure 和 clean baseline。
- R4 增加 `Materialize(LoadNormalized(StateMapN)) == expected CLR root closure` 组合闸门。
- 正式 identity one-time binding 与未来 transient/invariant phase 成为产品化重访点；后者加入时 root exposure 必须延后到全部 validation 成功之后。

相关材料：

- `tests/DurableGraph.Tests/NormalizedGraphMaterializationProbe.cs`
- `tests/DurableGraph.Tests/NormalizedGraphMaterializationProbeTests.cs`
- `tests/DurableGraph.Tests/GraphDeltaProbe.cs`
- `docs/design-branches/0006-flat-graph-delta-prototype.md`
- `docs/DurableGraph-research-roadmap.md`

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

### 2026-08-29：选择 Revision shared prior-snapshot anchor

- DB-010 选择 one-Revision/one-accepted-prior law；Base 删除 per-record parent，Delta 属性明确为 `DeltaParentFrameTicket`。
- Base lineage 从 containing OVD parent 查询 prior ObjectId；mixed new/domain/relocated Base 与 exact-parent Delta、AA/BA、genesis/Absent/visible Remove/malformed anchor 均有 executable evidence。
- provisional Base record 删除 `NoneToken` 占位；更新后的 hot/cold modeled file/final-read 为 `1864/1132`、`1428/1396`、`1500/1132`，fixed mixed 为 `516/176`、`460/444`、`460/296`。
- OVD Base checkpoint 会丢弃旧 tombstone；跨 reopen 的 no-ID-reuse 仍需未来独立 ID epoch/retired-ID authority。mixed-snapshot import/rescue/stale Save 明确不在当前模型。
- Probe 211/211；下一风险切片回到一般 B Base migration 与 `CanPrepareAndRotate` completion oracle。

### 2026-08-29：闭合 relay-free runtime C append

- planned C 改以 immutable runtime `Frame` 为唯一语义 authority；provisional grammar 仅作尺寸投影，删除 plan 内重复的 `ProjectedStateMap`。
- `ImmediateRotationAppender` 在 source/candidate preflight 后，通过“首帧成功再注册文件”的 in-memory store seam 加入 C；不发布 StateStore head。
- append 后只由 `MaterializeLive(C)` 产生 B/C StateMap；AA/BA/BB 的 logical state、reconstruction 与 lineage 已闭合，当时 Probe 204/204。
- 完整历史 lineage 损坏仍由离线诊断暴露，但不再阻塞 current reconstruction 或 immediate rotation。
- 当时的下一步 DB-010 已由后一切片裁决；一般 B Base migration/`CanPrepareAndRotate`、bytes codec 与 publication/reopen/crash 继续分离。

### 2026-08-29：选择 relay-free 并删除 forwarding 主线

- 为三方案并存的可执行岔口 `b84620b` 创建 annotated tag `research/relay-vs-relay-free-20260829`，随后把 DB-009 裁决为 Revision locator。
- runtime 删除 direct-Base/locator 双 inspection：唯一 lineage 语义为 Delta exact parent、Base Revision locator。Delta 必须正 payload 且 ordinal 为 `parent + 1`；same-version maintenance 只保留 RelocatedBase。
- OVD 新增 `MaterializeLive(PublishedRevision)`；WorkloadSimulator 每次 Save 写 runtime OVD，StateMap 只由 OVD replay 派生，并以 point lookup 交叉校验。V0 Frame adapter 直接投影同一 OVD，不再从 SaveStep 重造计费副本。
- relay-free `ImmediateRotationPlanner` 删除 caller StateMap、RelaySet、RelayRevision、B capacity debt 与 Relay grammar role；source 只来自 B PublishedRevision OVD，所有 evacuation Bases 以 B 为 locator，planner 只产生 C full Base/OVD。
- canonical AA/BA/BB、Published Remove、OVD insertion order、C capacity、B exhausted tail、source scope 与零 mutation 均有 executable tests；Probe 198/198，独立 correctness review 无 blocker/high/medium。
- preparatory B Base migration 仍是一般 `CanPrepareAndRotate` 的 correctness path；当时遗留的 runtime C append 与仍 Open 的 DB-010 均已由后续切片闭合/裁决。

### 2026-08-29：历史 discriminator：建立 OVD authority 并比较 forwarding/relay-free（已归档）

- 用户澄清原始中继设想是 `B zero-payload Delta + OVD Self`，C Base 把 B Revision 当 locator；这与当前 planner 的 `direct parent + empty OVD` 不是同一形状。
- runtime `Frame` 新增显式 nullable OVD；`null` 表示未建模 authority，不冒充 empty Base。`LookupLive` 不接收 caller StateMap，区分 Found/Removed/AbsentAtBase，并验证 decisive binding、source-scope relative decode、same ObjectId 与 earlier address。
- canonical C full OVD 成为唯一 current authority：AA/BA Self、BB External(B)，所有 current heads 先经 C OVD lookup。24 个聚焦 cases 覆盖 OVD model/lookup、AA/BA/BB、Remove/Base absence、malformed locator、确定性与只读 inspection；完整 probe 194/194。
- Base Revision-locator discriminator 证明：relay + OVD Self 的 AA exact lineage 为 `C/Relay/A`、OVD read 为 `Relay`；relay-free 为 `C/A`、OVD read 为 `B/A`；relay + empty OVD 会跳过 helper。两者 current state、logical ordinal 和 root 相同，且 current reconstruction 只读 C。
- 当前没有产品律要求每次物理跨文件都暴露 no-op ObjectVersion hop，故 relay-free 成为领先方向；relay 只保留为可能降低 historical OVD read amplification 的候选 optimization。planner 仍依赖 caller StateMap，尚不能删除旧 relay path。
- 新增 DB-010：在 single-prior-snapshot Revision 法律下，Base per-record locator 还可能与 `Revision.OVD.ParentRevisionTicket` 合并；merge/import/rescue 与 DurableId reuse 是明确重访条件。

### 2026-08-29：历史 forwarding 模型：logical version 与 transparent maintenance lineage（已归档）

- 将 probe 的 `VersionOrdinal` 明确重命名为 `LogicalVersionOrdinal`；物理先后继续由 append address、direct parent 与 cycle gate 表达，不增加 physical ordinal、maintenance flag 或 runtime kind。
- executable semantics 选择 `same ordinal = maintenance`、`parent + 1 = domain change`：在 synthetic size-state 模型中，zero-payload Delta 可作 Relay，Base 可作 RelocatedBase；Base current reconstruction 停止，独立 lineage inspection 继续穿过 Base。
- 12 个聚焦 cases 覆盖 C-only reconstruction / C-B-A lineage、Relay head、后续 Delta/Base 只推进一次、payload/result/cumulative/growth corruption、skip/regression 与确定性；logical equality 只观测 `(BasePayloadBytes, LogicalVersionOrdinal)`，完整 probe 170/170。
- 独立化简审查提出 DB-009：Base lineage parent 或可改指 earlier Revision，再经其 OVD point lookup prior ObjectVersion，从而删除 relay。AA/BA/BB 未发现反例，但当前无 replayable OVD authority，caller StateMap 不能冒充历史 OVD；Remove 后同 DurableId 重新接入也尚待裁决。
- 下一 discriminator 优先建立单一 authority 的 OVD Base/Delta point lookup，再决定 materialize relay plan 还是删除 relay；不先扩展 multi-frame completion planner 或策略 heuristic。

### 2026-08-29：历史 direct-forwarding ImmediateRotationPlan（已归档）

- 将 `ProvisionalRevisionV0` 的唯一尺寸算法改接显式 grammar IR；普通 `Frame + SaveStep` 入口降为 adapter，旧 golden 不变。IR 现在能独立表达 domain Relay、带 parent 的 OVD Base/Delta，以及 Self/External/Remove。
- 新增 source-chain 只读 inspection 与 `FileScope.Relativize`；pure planner 从 caller 提供的 StateMap 推导 `EvacuationSet=Base@A`、`RelaySet=EvacuationSet∩Head@A`，不接收第二份集合 authority。
- dedicated B relay 使用 zero-synthetic-payload helpers + empty OVD Delta，C 为全部 evacuation objects 写 Base，并用 mixed Self/Previous full OVD 产生唯一 ProjectedStateMap。planner 不 CreateFile、不 Append、不 publish。
- AA/BA/BB fixture 得到 `Evacuation={AA,BA}`、`Relay={AA}`；provisional relay ticket 为 `32/40`，C ticket 为 `4/88`。hard-coded golden 分别冻结 relay `0+4+5+4` 与 C `35+8+12+6` 的 body/header/OVD/TailMeta 分量。
- tests 覆盖 no-relay、empty graph、canonical order、B relay TailMeta overflow、C combined capacity overflow、失败零 mutation 和同 store retry；Probe 158/158、root tests 147/147、solution build 0 warning/error，两路独立复核无 blocker/medium。
- 该结果只证明“至多一个 B relay Frame + 一个 C evacuation Frame”的 immediate constructive witness；失败不排除多个 relay Frames 或 published B maintenance，不能当作一般 `CanPrepareAndRotate == false`。planned maintenance records 仍不 materialize，因为当前 `VersionOrdinal` 尚未拆分领域版本与物理 lineage 次序。

### 2026-08-29：用 contextual self 建立 ProvisionalRevisionV0 尺寸基线

- 复核本地 Atelia RBF commit `fec021295828fcfe638434d69d04ff078c87c8ce`：现有 envelope 常量与边界正确；完整 L3 read 仍读取整个 Frame，TailMeta preview 只有 L2；append/read context 均提供 containing ticket。
- literal self-ticket 反例在 `start=4, non-self=98, count=1` 下同时得到 width/frame `2/124` 与 `3/128` 两个 fixed points，故“迭代至稳定”不能独自定义 canonical wire。采用 OVD 字段级 `BindSelf=1`，通用 relative grammar 不变。
- 新增 `ProvisionalRevisionV0` run scope：domain record、OVD record、TailMeta directory、address-token subset 与 RBF layout 分项计量；preflight 后 Append 必须返回完全相同 ticket/layout，reconstruction 按逐地址 provenance 统计 unique full frames。
- 默认和显式 `ObjectPayloadOnly` goldens 完全一致；当时仍含 Base `NoneToken` 的旧 V0 grammar 得到 hot/cold `1900/1148`、`1440/1408`、`1516/1148` 与 fixed mixed `536/184`、`464/448`、`468/300`；DB-010 后的当前数字见较新的船长日志。
- executable tests 覆盖 Base128/SizedPtr projection、same/previous、contextual Self/external alias 拒绝、component conservation、remove-only、canonical order、TailMeta 65535/65536、combined capacity、512 GiB start 和 policy matrix；真实 bytes writer/parser、mixed-binding full OVD、relay/two-file planner 与 publication 仍未实现。
- 验证：Probe 133/133、root tests 147/147、root solution build 0 warning/0 error、两套 format verify 与 diff check 通过；两路独立复核最终无 blocker/medium。

### 2026-08-29：加入 object-local payload read-amplification 基线

- 第三策略保留 StateJournal `ShouldRebase` 的 ratio=3/threshold 形状，但删除与多对象共享 Frame 冲突的 38-byte per-object overhead；名称明确为 `ObjectPayloadReadAmplification3`。
- ObjectVersion 记录 `ReconstructionObjectPayloadBytes`：Base 重置，Delta checked 累加；Builder 漏填 fail closed，materialization 从 Base 重算并拒绝 tamper。该字段仍排除在 ObjectPayloadOnly layout 外。
- 四场景 matrix 覆盖 threshold tie/reset、增长/缩小/等尺寸 direct Base、hot-one/cold-eight shared Frame 和 fixed-seed Field/List mixed；write totals 与 final read snapshot 分栏，不定义 TotalReadBytes。
- fixed mixed 中三策略 modeled file/final read 分别为 Base 436/148、Delta 364/348、local 372/232；hot/cold 中为 1700/1048、1268/1236、1340/1048。结果不是 winner，完整 codec/shared-frame/rotation-aware cost 仍待后续。
- 验证：Probe 100/100、root tests 147/147、root solution build 0 warning/0 error、两套 solution format check 与 diff check 通过；独立复核最终无 blocker/medium。

### 2026-08-28：建立 ObjectPayloadOnly RBF envelope 与 raw metrics

- `FrameTicket` 从 frame 序号改为 RBF byte-range `(OffsetBytes, LengthBytes)`；文件以 4-byte HeaderFence 起步，Append 精确计入 24-byte frame fixed overhead、padding 与独立 trailing Fence。
- 原生 RBF 约 1 TiB frame-start 与 DurableGraph 约 512 GiB relative-start gate 分层建模；最后合法 start 的 frame end 可以越界，下一次 Append 才失败。
- 每个 Save 记录 Base/Delta object payload write；每个 post-save state 记录 required versions、unique frames、required/in-frame/co-read payload 与 object-payload-only full-frame read bytes。读指标保持状态快照，不默认跨 Save 求和。
- 固定 generated mixed workload 首次让两基线显出可复现 tradeoff：AlwaysBase modeled file 436 bytes/final read 148 bytes，AlwaysDelta modeled file 364 bytes/final read 348 bytes；这些不是完整 wire bytes，也不构成 winner。
- OVD/index/header、TailMeta、relative VarUInt、self-ticket fixed point、two-file/relay/rotation 与 `CanPrepareAndRotate` 继续暂缓到对应具体 codec/planner 切片。
- 验证：Probe 90/90、root tests 147/147、root solution build 0 warning/0 error、两套 solution format check 与 diff check 通过；独立复核最终无 blocker/medium。

### 2026-08-28：跑通单文件 Base/Delta physical baseline

- 当时的单文件 baseline 将同一冻结 `WorkloadTrace` 分别编译为 fresh `AlwaysBase` 与 `AlwaysDeltaWhenLegal` runs；每个 Save 都产生一个 Frame，remove-only Save 产生空 Frame。当时 Base/Delta 都保存 object parent；DB-010 后 Base direct parent 已删除。
- 当时 live StateMap 使用 `AbsoluteFrameAddress`，ObjectVersion parent 使用 `RelativeFrameTicket`；当前只保留 Delta exact parent，Base lineage 已改由 Revision shared anchor 承担。
- Delta payload size 只作写成本；symbolic apply 先重建并校验 expected parent size、ordinal 与无压缩增长下界，再产生 result size。每个 Save prefix exact-match logical cursor 后才推进 private run。
- 本切片只是 S1 前置基线；frame bytes/layout、capacity/representability、two-file closure、relay、rotation 与自适应策略仍未实现。
- 验证：Probe 74/74、root tests 147/147、root solution build 0 warning/0 error、两套 solution format check 与 diff check 通过；独立复核最终无 blocker/medium。

### 2026-08-28：建立 deterministic workload generation/replay substrate

- 新增 immutable `WorkloadTrace`：以 canonical SaveStep 承载 Create/Update/Remove；logical replay 维护 live set、不可复用 ObjectId、Base size 与自动 VersionOrdinal。
- Update 的 previous size 只来自 replay state；在无压缩 literal-image 假设下拒绝 `DeltaPayloadBytes < max(0, currentBase - previousBase)`，同尺寸更新仍推进版本。
- 固定 SplitMix64 root 按 lifecycle/create/update 与 step/object/lane 非消费式分流；完整 trace 在策略运行前只生成一次，所有候选策略未来共享同一个内存实例。
- `GeneratedScenario` envelope 同时冻结结构相等的 ScenarioDefinition 与策略输入 WorkloadTrace；无需 trace 文件即可解释同名同 seed 下的参数差异，mixed-scenario golden 则约束 GeneratorVersion。
- 薄泛型 ObjectBehavior 以 candidate-first/measure/freeze/commit 保证失败不半提交；Field 维护固定 component sizes，List 维护 item sizes 并只生成合法 Insert/Remove/Replace。
- ScenarioGenerator 当前采用固定计数 lifecycle、Field/List 权重和显式 synthetic size accounting；生成后强制经过 independent logical replay 并核对 final live Base sizes。
- 当前未实现 trace 文件格式、真实 codec、Frame layout、BaseOrDeltify、rotation policy 或综合评分；随机生成补充手写边界 trace，不替代 exact-fit/relay/evacuation 反例。

### 2026-08-28：启动 Two-leg rotation S1 probe

- 建立隔离的 `experiments/TwoLegRotationProbe` .NET 10/xUnit 项目，不改产品 runtime 或根 solution 项目集合。
- 首轮只固定 one-based 单调 FileNumber、RbfFile append-only/random-read 容器和 `PreviousFileNumber = CurrentFileNumber - 1` 的相邻语义。
- `FrameBuilder/ObjectVersionBuilder` 是落盘前可变态；`Build()` 防御性复制为只读 ObjectId→ObjectVersion map，并冻结 Delta-only `DeltaParentFrameTicket`。
- `RelativeFrameTicket` 已建模为 `(IsPreviousFile, FrameTicket)`；`FileScope.ReadFrame` 以承载 ParentId 的 origin file 选择 Current/Previous。FileScope 固定 origin，迈腿时创建新文件与新 scope，旧 frame 仍使用旧 scope 解读。
- `FrameTicket` 暂为文件内零基 List key；ParentId/FileScope pair 不冒充 `SizedPtr` 或已经冻结的 durable ticket encoding。
- 下一步可从该骨架逐层加入 Base/Delta 内容、跨文件地址、Revision layout estimator 和可替换策略，不提前把候选 heuristic 写入容器层。

### 2026-08-28：将研究优先级切换到 StateStore 双腿轮转

- 根据 StateJournal/RBF 本地实现复核，选择 A=OldPrevious、B=Current、C=Next 的 current-head two-file reconstruction 模型；R4 logical StateMap/apply 保留但暂缓。
- 地址层选择独立 `RelativeFrameTicket` / `AbsoluteFrameAddress`，以 LSB selector + 左移后的 `SizedPtr.Serialize()` 保留 VarUInt 紧凑性，并接受约 512 GiB frame-start 上限。
- 当时的候选轮转模型把 terminating Base 位于 A 的 live objects 以 Base 写 C，并设想用 B 中 lightweight per-ObjectId RelayRevision 保留 direct lineage；该方案后来由 DB-009 拒绝并归档于 tag `research/relay-vs-relay-free-20260829`。
- one Revision/one RBF Frame 保持为有界首版，Extent、固定性能阈值与持久 `TotalPersistBytes` 均等待模拟或容量证据。
- 建立 `state-store-base-design.md`、`state-store-base-derived.md`、`state-store-addressing-design.md` 与 DB-007；下一步先商定纯内存策略模拟模型，不宣称持久 Store 已实现。

### 2026-08-28：完成 Two-pass CLR graph materialization R3b probe

- 只 materialize normalized current root closure，allocate-all 后 hydrate-all，恢复 sharing/self-cycle/two-node cycle；disconnected source rows 留给 Save。
- constructor-bypassed placeholder 只允许一次 identity/hydration bind；materialized root 不成为 StateMap/baseline authority或 cache。
- allocator/hydration late failure 均 no-root 且可重试；复审促成真实 allocator fault injection 并删除非必要 ID sorting。
- 聚焦 7/7、完整 147/147、solution 0 warning / 0 error、PackageConsumerProbe passed；最终无 blocker/medium。下一主线仍为 R4，并增强 logical apply → normalized load → CLR materialize 组合闸门。

### 2026-08-27：完成 Stored Graph normalization R3a probe

- 用 immutable record table + full-table exact preflight + typed V1/V2 decode/upgrade 产出 current-Snapshot baseline，未引入产品 Reference TypeTag 或 wire format。
- 保留完整 SourceRecordIds；upgrade 删除 edge 后由下一次 R1 Save 将 target 分类为 Unreachable，而不是 normalization 提前裁掉。
- decode/upgrade late failure 均无 partial result，并能在同一 image 上修复重试；handler 自身外部副作用仍明确不回滚。
- 聚焦 9/9、完整 140/140、solution 0 warning / 0 error、PackageConsumerProbe passed；两路独立复审最终无 blocker/medium。下一主线为 R3b two-pass hydrate。

### 2026-08-27：完成 Generated Graph Operations R2 probe

- 选择 internal、无 `[Generator]` 的隔离 probe，而不是现在冻结 Reference TypeTag 或让产品 Generator 产生无法 hydrate 的半成品 boxed Serializer。
- 以 provisional `TIdentity : struct` resolver 生成 typed Snapshot/CapturedReferences/Capture/equality/visitor，并用 literal expectations + R1 oracle 双重验证 sharing、cycle、locality 与 replacement。
- 聚焦 31/31、完整 131/131、solution 0 warning / 0 error、PackageConsumerProbe passed；独立复审促成 semantic no-`object` gate 和 probe-only DG0018 元数据收口，最终无 blocker/medium。
- 默认产品 Generator 仍 DG0007 拒绝 self-reference；下一主线为 R3a logical StoredGraphImage normalization。

### 2026-08-27：完成 Flat Graph Delta R1 semantic probe

- 以 internal single-type fixture 跑通 latest Snapshot baseline、`RequiresRewrite`、identity-aware stack traversal、whole-object Upsert、Unreachable 与 root transition。
- 独立复审找到 accepted baseline 可残留 disconnected clean entry 的漏洞；新增 exact result-root closure gate 与 malformed candidate tests 后复核通过。
- 合并 visited/reachability 状态为单一 ID→instance table，并移除 core sorted containers；确定性只在展示边界显式排序。
- 聚焦 21/21、完整 121/121、solution 0 warning / 0 error；未修改 runtime 或 Generator public/product behavior。

### 2026-08-27：选择 latest-Snapshot flat baseline 作为 Graph Delta 下一探针

- 后续主线转向先验证 identity、reachability、sharing/cycle 与 whole-object delta，再为已证明的逻辑 IR 增加 binary codec。
- baseline 保存来源 StateMap 的 flat live ID set；每项 historical payload 在 Load 边界升级成 current typed Snapshot，并以 `RequiresRewrite` 记录下一次显式 Save 的推进义务。
- `RequiresRewrite` 属于 baseline entry envelope；失败时不清除，只有成功 publication 后通过安装新的 clean baseline 一次性消解。
- normalized projection 不成为第二 authority；未来 old record reuse 和 stale-baseline gate 由 exact head + authoritative StateMap 在整图层绑定。
- 将路线与实验 gate 写入 `DurableGraph-research-roadmap.md`，将最小算法、不变量和反例写入 DB-006。

### 2026-08-27：用 goto labels 线性化 generated upgrade chain

- 技术 probe 证明 C# definite-assignment 支持 switch case decode 后 goto typed Snapshot-ready labels，并能顺序穿透后续相邻边。
- 删除每版本复制升级后缀的 `DeserializeVn` methods；现在每个版本 decode、每条 edge 和 materialize 都只生成一次，代码规模从 O(N²) 降为 O(N)。
- 保留单方法帧、唯一动态 switch、required partial handlers、typed failures 和 Load 无写回语义。
- 将所有 Snapshot locals 共处单方法造成的 JIT/stack tradeoff 保留为未来实测问题，而不是宣称已优化运行时性能。

### 2026-08-27：跑通 generated static read-time upgrade

- 初始方案采用每个 stored version 一个单帧直达-current协调器；该代码形状随后由 EXP-010 的 goto-label 共享后缀线性化。
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
