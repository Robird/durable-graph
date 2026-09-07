# DurableGraph 产品开发工作集

> 校准：2026-09-07，本轮验收见 [DB-034 §8](../docs/design-branches/0034-durable-reference-graph-batch.md#8-实施合同与验收账本)。本文只维护当前能力、边界与续工入口。
> 文档不是实现授权；事实以当前源码、测试和工具输出为准。

## 从这里继续

先读本文，再按任务选择一份材料；不需要按 DB 编号通读历史。

- 理解产品目标与已选约束：[目标设计](../docs/DurableGraph-target-design-v0.md)。
- 查首选用语、概念示意和对应代码：[项目术语表](../docs/DurableGraph-glossary.md)；后续文档、代码命名与注释的一致化以此定位差异。
- 选择下一片、查未完成决策与问题：[后续路线](../docs/DurableGraph-research-roadmap.md)。
- 查某项实现的来由或验收：[设计与施工记录索引](../docs/design-branches/README.md)。
- 查历史实验：[实验簿入口](../docs/DurableGraph-lab-notebook.md)；重跑独立实验：[Probe 导航](../experiments/README.md)。

## 当前焦点

[DB-034 引用图与首次保存](../docs/design-branches/0034-durable-reference-graph-batch.md) 已完成 G0–G6 验收；
范围、冻结合同及执行证据集中在其 §8。普通 new World 可以经公开 PrepareNew 得到无 Parent 计划，
宿主 Append 后按显式地址/WorldId 恢复非泛型领域引用图，再 Prepare 对象级增量；共享、循环、
已登记派生实例与 private/readonly 引用由统一对象身份和两阶段恢复支持。
完整提交/发布仍未实现；Append 后重新 Load 建立新基线。
活跃文档、当前源码注释和测试见证已按[项目术语表](../docs/DurableGraph-glossary.md)
完成一轮语境化一致；内部 B/D/H 输入现分别命名为精确 Base payload、Delta payload 上界和
重建链 payload。[DB-035](../docs/design-branches/0035-public-contract-terminology-migration.md)
Wave 1 已删除 legacy 生成路径并把裸 `[DurableType]` 收敛为唯一 State model；Wave 2 已将构建期
Schema history 原子迁移为 `.dgschema`、`SchemaHistory` tool/manifest 和对应 MSBuild 合同；Wave 3
已将生成 ABI 收敛为 `__DurableState` 及显式 Base/Delta body 方法，以 `ObjectStateRecord` 统一单行
carrier，并用 StateStore 内部 `EncodedBaseObjectBody` 保证 Base 类型头只包装一次。Storage API 清理
仍留给后续 Wave；State wire v3 未改变。
自定义 struct、有限数组对象和泛型闭合待后续分别选片；本批不自动进入下一片。
持久 World 根、Commit/Ref 和其他类型扩展继续按[路线图](../docs/DurableGraph-research-roadmap.md)独立排期。
未来联合 Commit/Ref 及内建类型自举的 SchemaStore 复用路线见
[路线图](../docs/DurableGraph-research-roadmap.md#41-schemastore-复用-statestore-与联合版本视图)。

## 当前能力与实际边界

| 层 | 已验证能力 | 尚未闭合的边界 |
|---|---|---|
| [DurableGraph](DurableGraph/DurableGraph.csproj) | immutable Schema、exact BaseSchema、nominal 引用；显式模型目录、队列 Capture、string 身份；refs-only 遍历/目录验证、ObjectReadTable；统一 Prepare/typed 整链读取 | 无工作会话 Commit；复合值和一般类型组合待扩展 |
| [Generator](DurableGraph.Generator/DurableGraph.Generator.csproj) / [Build](DurableGraph.Build/DurableGraph.Build.csproj) | 裸 `[DurableType]` 生成各版 readonly DTO、标量/引用 ID 静态 body、Capture/引用遍历、历史 reader/model、相邻 DTO Upgrade、无构造器/readonly Hydrate；`.dgschema` Schema history 含 nominal family | struct、泛型、数组/BCL 尚无对象生成 |
| [StateStore](DurableGraph.StateStore/DurableGraph.StateStore.csproj) | 持久 Schema、Base 类型头；完整 stored/current 引用验证、可达图两阶段恢复；公开 PrepareNew、fixed-Parent Prepare、升级强制 Base/不可达 Remove | 无持久 roots、原地 Accept/Commit 或发布 |
| [Storage](DurableGraph.StateStore.Storage/DurableGraph.StateStore.Storage.csproj) | local Base/Delta records、wire v3、exact Revision live map、Parent/prior 校验、object-first 原始重建链及实际 payload H；v3 Base 精确/Delta 上界计量；真实 Segment/RBF 冷重开 | 不解码 typed body；不拥有持久 roots、类型目录或发布 head；重复读取暂未缓存 |
| [Serialization](DurableGraph.StateStore.Serialization/DurableGraph.StateStore.Serialization.csproj) | 字节原语、string 内容 codec、拥有 raw bytes 的 PreparedBaseBody/PreparedDeltaBody、预制 string Base body、显式 body 的 typed slot、SZ/rank-2 元素 ref 循环 | 无数组对象 envelope、一般 struct 生成器或通用泛型 codec |

容易混淆的限制：

- SG DTO/body 支持 13 种标量：bool、byte/sbyte、short/ushort、int/uint、long/ulong、char、Half、float、double；string 和受支持 durable class 字段保存 UInt32 ID。
  裸 `[DurableType]` 仍限同编译、顶层、非泛型、非 record 的 partial class 链；
  支持 readonly 持久字段和没有无参构造器的领域类。RuntimeHelpers 分配、SG Hydrate/声明层 UnsafeAccessor
  不执行实例构造器或字段初始化表达式；Transient 由用户交付后重建。
- AddRoot 登记根；BeginCapture(models) 冻结 exact CLR Type/model 目录，CaptureDurable 逐边校验 nominal 约束，
  先分配 ID/登记再排队；Seal 用增长队列捕获可达对象，子对象不加入根列表。未知实际派生类型明确拒绝。
  Accept/Discard 只是内存候选协议。ID 单调分配、失败可烧号；退役实例映射清理不回收数字。
  空串 Capture/读取两端统一 Empty，非空 string 保留引用身份。
  现有多根 Capture 是内部能力/机制见证；LoadedWorld 外层入口限定一个固定 World。
- CaptureSession.Prepare 自动使用 Current，完整预检 exact Schema/DTO/稳定 binding 后编码；全部 live Base 提前生成，
  existing durable 调用融合 Delta、existing string 为 unchanged。结果只标识内存 Previous/Candidate，不带磁盘地址。
  重复准备与失败不安装或放弃候选、不烧号；临时 guard 拒绝会话重入。capture-only 登记仍有效，缺 binding 仅 Prepare 拒绝。
  跨 Schema/DTO/binding 不匹配拒绝，不自动降级 BaseOnly；StateStore 的 CapturedRevisionPlanner 统一映射结果，调用方仍负责 exact Parent 对应。
- SG RegisterReaders 显式登记一个模型族的全部可用 Vn；StateReaderRegistry 同 binding 实例幂等，
  同 key 另一实例拒绝，读取开始复制固定索引。Schema 日志不包含可执行 reader，完全移除的模型族仍拒绝。
  Runtime typed 循环完成整链后才装箱，字段 body 保持静态绑定；无程序集扫描或一般 TypeCodec。
- RevisionDecoder.Read 读取指定 Revision 全部 live 行，逐对象匹配完整 Schema 后解码，最后由每版 SG
  VisitReferences 统一验证目标 Revision 的 string/durable 引用；nominal 约束按该目录 stored Schema 祖先判断。
  晚期失败不返回部分结果，不要求全批 body 零调用。
  DecodedRevision 保留 stored-exact DTO、查询地址及每 ID 唯一 string 实例，关闭 Store 后仍可使用；
  无 roots/领域实例/Upgrade，不是 CaptureSession.Current，不能直接作为已加载的可编辑基线。
- StateModelRegistry 显式登记稳定 SG Model，在操作开始快照 family、exact CLR Type 与 reader 三份索引；
  同 exact CLR Type 的其他模型原子拒绝。可选普通静态 UpgradeStateVnToVnPlus1
  按相邻版本转换完整 leaf DTO，不重复升级祖先。已声明边逐一强类型检查；缺边仅阻止需要该边的 current Load。
  LoadedWorld.Load 先完整 exact 解码、再升级全部 source 行，按 current Schema 重新校验全部引用；
  从所选 exact World 迭代求可达闭包，全部可达 durable 实例分配后才 Hydrate。分配必须 exact、非空、彼此不同。
  内部仅保留 current DTO 比较基线及 source Schema/完整 membership，升级仍 live 必须 Base。
  不可达 source 仍须解码/归一化/验证，但不要求其 current 类型可以 Allocate；历史 ancestry 不能用 current CLR 反推。
- LoadedWorld.PrepareNew 从普通新建图生成无 Parent 的完整冻结计划，返回 WorldId；可持久注册 Schema，
  不追加 State 或执行 State 屏障/发布，不安装基线。单根非空且要求 exact 已登记 CLR 类型。
- LoadedWorld.Prepare 固定 Parent/WorldId，返回 owned StateRevision，成功或失败均释放临时 Capture，
  不推进基线。宿主 Append 后重新 Load；Schema 注册可持久生效，不代表发布。
  Empty 反向映射选择最小 source ID，但基线槽保留旧 ID，首次 Capture 形成真实 Delta/Remove。
  分配从完整 source live max+1 起，只承诺会话内单调；uint 耗尽不阻止已有对象保存。
  恢复的可达 durable 实例身份导入同一捕获会话；child-only 修改不改变 owner ID 槽，
  断开最后根路径后整个循环岛由完整 source − candidate 得到 Remove，旧 Revision 不受影响。
- PrepareDeltaBody 每槽比较一次形成位图，再静态写变化值；结果含 HasChanges 和可复用 raw body，裸 Delta body 大小可直接取长度。
  策略 D 还须计入对象 envelope，不能直接以裸 body 大小代替。
  PrepareBaseBody 对每版 DTO 复用 WriteBaseBody；全部 live Base 提前准备，决策后复用 bytes，性能优化留待 MVP 后。
  B 为完整 Base payload 精确值，D 仅对未定文件距离按 5 字节上界计量（超额 0..4）；H 仍是原记录实编码。
  ApplyDeltaVn 只处理同 Vn；不证明 prior 身份，之后仍须对完整 DTO 验证引用。
- SchemaStore 借用独占的专用 IRbfFile；完整祖先闭包与同 key 冲突预检后，一批次一帧追加/flush，等价注册不写。
  tag15 DurableReference 只携带稳定 TargetSchemaId，不绑定目标版本或形成 exact 注册依赖；
  nominal 自环/互环无 Schema 初始化环。SchemaBatch 与 `.dgschema` history 各自维护冻结格式，字段 type tag 的旧 1–14 不变；
  nominal 约束改变属于 owner Schema 改变，目标自身升版则不传播 owner 版本。
  严格重放全部帧/CRC；坏尾、tombstone、未知格式拒绝且不自动截断。写入不确定后 faulted，须重开；
  可写非空重开先 flush 再交付，readonly 不确认新屏障。尚无 Schema 分段、联合版本目录或自动修复。
- StateStore 内部 BaseObjectBodyCodec 只为 raw Base body 加 v1 类型头，返回 `EncodedBaseObjectBody`；
  durable 使用逻辑 SchemaKey，string 走内建路径，Delta 仍为裸 body。typed planner 不能漏包或重复包装类型头。
  TypedObjectVersionReader 在 callbacks 前匹配持久完整 Schema，逐 body 全消费；string 拒绝 Delta。
  它保留单对象显式入口，与 RevisionDecoder 共用读取规则；不执行 Upgrade。
  Schema 注册帧是共享元数据，不摊入对象 B/D/H。
- CapturedRevisionPlanner 先核对 Previous/Parent、完整 prior ID 集合及所有 survivor 的 Base kind/exact Schema，
  包括 NoChange；再注册全部 current Schema、包装 Base 并调用原 planner。Schema 注册可持久生效，
  但该方法不追加 State/发布/Accept，也不证明 DTO 内容与 Parent 一致；合法迁移由受控 LoadedRevisionPlanner 路径产生 BaseOnlyUpdate。
- ObjectRevisionPlanner 只读 exact Parent，校验完整 post-live rows 的新旧分类/prior；对 NoChange/Delta Update
  读取链 H，BaseOnlyUpdate 不读取旧内容链。输出 map Base（无 Parent）或 map Delta（有 Parent）及 Removes。
  typed producer 负责内容/Schema/基线对应；planner 不调用 Append/Accept，结果可作为显式 Parent 的分支追加。
- Storage 的 ObjectHeadMap 与对象内容的 Base/Delta 独立组合。ReadObjectVersionChain 逐条核对
  prior 等于该记录 exact Parent Revision 选定的对象 head，要求 direct local record；Base 截断内容链，H 随之重置。
  Append 只预检直接 edge；完整 map 的 external heads 仍是浅声明，不认证全局实体历史。
  ReadObjectBase 仍只接受 Base head，不回退 parent 补内容；wire v3 拒绝 v1/v2。
  H 含 kind/prior/length/body，不含 ObjectId/membership/共享 Frame；不是总冷读 I/O。
  先直读 RBF，缓存优化留有 [TODO](DurableGraph.StateStore.Storage/StateRevisionStore.cs)。
- 数组循环可操作已有 rank-2 非零下界数组，但尚无 shape 编码/分配、其他 rank 或非 SZ rank-1 支持。
  此底层循环能力不等于目标支持范围；后续数组产品入口须按 MVP 边界拒绝非零下界、非 SZ rank-1
  及超过所选上界的 rank；当前仍保留已有底层元素循环。
  已知成员的 SG body 静态绑定字节原语；PrimitiveSlotCodecs 只在测试工具中。
- Generator 中未注册的 graph operations probe 和 tests 中 logical graph R1–R3b 是机制见证，不能算产品通用图能力。

产品依赖为 StateStore → Runtime + Storage，二者分别复用 Serialization；Storage 另用 RBF substrate。
DurableGraph runtime 也引用 Serialization，单一 runtime PackageReference 可取得传递依赖。

## 按任务定位证据

| 准备修改 | 先查源码/测试，再按需读合同 |
|---|---|
| 领域引用图/首次准备 | [DB-034](../docs/design-branches/0034-durable-reference-graph-batch.md)、[真实生成图冷读](../tests/DurableGraph.Tests/PersistedReferenceGraphTests.cs)、[双视图与失败测试](../tests/DurableGraph.StateStore.Tests/LoadedReferenceWorldTests.cs) |
| Schema、DTO、静态 body | [Generator tests](../tests/DurableGraph.Tests)、[DB-019](../docs/design-branches/0019-schema-ancestry-implementation-slice.md)、[DB-022](../docs/design-branches/0022-versioned-state-dto-capture.md)、[DB-023](../docs/design-branches/0023-scalar-schema-dto-slice.md) |
| 同版 DTO Delta 准备与应用 | [DB-027](../docs/design-branches/0027-generated-same-schema-delta-body-slice.md)、[body tests](../tests/DurableGraph.Tests/FusedDeltaBodyTests.cs)、[history/Capture tests](../tests/DurableGraph.Tests/FusedDeltaHistoryTests.cs) |
| Capture 与 string 读取 | [DB-024](../docs/design-branches/0024-reference-capture-and-reusable-object-ids.md)、[DB-025](../docs/design-branches/0025-string-object-decoding-slice.md) |
| 持久 Schema、Base 引用及 typed 冷读 | [DB-031](../docs/design-branches/0031-persisted-object-type-envelope-slice.md)、[SchemaStore](DurableGraph.StateStore/SchemaStore.cs)、[注册 tests](../tests/DurableGraph.StateStore.Tests/SchemaStoreTests.cs)、[typed reader](DurableGraph.StateStore/TypedObjectVersionReader.cs) |
| current 升级、readonly 恢复与续写 | [DB-033](../docs/design-branches/0033-upgrade-restore-resave-batch.md)、[SG tests](../tests/DurableGraph.Tests/GeneratedStateModelTests.cs)、[加载 tests](../tests/DurableGraph.StateStore.Tests/LoadedWorldTests.cs)、[冷重开集成](../tests/DurableGraph.Tests/LoadedWorldGeneratorTests.cs) |
| 完整历史 DTO 目录与 reader 登记 | [DB-032](../docs/design-branches/0032-exact-revision-decoding-slice.md)、[RevisionDecoder](DurableGraph.StateStore/RevisionDecoder.cs)、[生成冷读 tests](../tests/DurableGraph.Tests/DecodedRevisionGeneratorTests.cs) |
| 异构图统一准备内容 | [DB-030](../docs/design-branches/0030-captured-object-preparation-slice.md)、[runtime tests](../tests/DurableGraph.Tests/CapturedGraphPreparationTests.cs)、[生成 tests](../tests/DurableGraph.Tests/GeneratedCapturePreparationTests.cs) |
| 对象内容、地址与重开读取 | [Storage tests](../tests/DurableGraph.StateStore.Storage.Tests)、[DB-026](../docs/design-branches/0026-raw-base-object-content-slice.md)、[typed 文件见证](../tests/DurableGraph.Tests/RawBaseStorageGeneratorTests.cs) |
| 持久 Delta、prior 链与 H | [DB-028](../docs/design-branches/0028-persisted-object-delta-chain-slice.md)、[链测试](../tests/DurableGraph.StateStore.Storage.Tests/ObjectVersionChainStoreTests.cs)、[真实 SG 冷重开](../tests/DurableGraph.Tests/PersistedDeltaChainGeneratorTests.cs) |
| 已准备内容、Base/Delta 策略与 Revision | [DB-029](../docs/design-branches/0029-prepared-object-revision-planning-slice.md)、[规划器](DurableGraph.StateStore/ObjectRevisionPlanner.cs)、[策略实现](DurableGraph.StateStore/ReadAmplificationBaseBudgetPolicy.cs)、[策略 tests](../tests/DurableGraph.StateStore.Tests)、[DB-015](../docs/design-branches/0015-statestore-object-representation-policy.md) |
| 包、生成器消费和 history 发布 | [PackageConsumerProbe](../experiments/PackageConsumerProbe/README.md)；真实 PackageReference 验证不能由 ProjectReference 测试替代 |

代码变更后运行根 solution build 和相关 tests；包交付边界变化时按 PackageConsumer README 验证。
本文件不累积测试计数、命令日志或完成流水账；已有验证结果保留在相应施工记录。
