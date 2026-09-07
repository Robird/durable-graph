# DurableGraph 产品开发工作集

> 校准：2026-09-07，本轮验收见 [DB-033 §10](../docs/design-branches/0033-upgrade-restore-resave-batch.md#10-实施合同与验收账本)。本文只维护当前能力、边界与续工入口。
> 文档不是实现授权；事实以当前源码、测试和工具输出为准。

## 从这里继续

先读本文，再按任务选择一份材料；不需要按 DB 编号通读历史。

- 理解产品目标与已选约束：[目标设计](../docs/DurableGraph-target-design-v0.md)。
- 选择下一片、查未完成决策与问题：[后续路线](../docs/DurableGraph-research-roadmap.md)。
- 查某项实现的来由或验收：[设计与施工记录索引](../docs/design-branches/README.md)。
- 查历史实验：[实验簿入口](../docs/DurableGraph-lab-notebook.md)；重跑独立实验：[Probe 导航](../experiments/README.md)。

## 当前焦点

[DB-033](../docs/design-branches/0033-upgrade-restore-resave-batch.md) 已在 `cab7b5c` 完成；
根 build、完整回归、两个包消费者与独立审查的证据集中在其账本，本轮文档规划未重跑产品验证。
当前能从显式 Revision/WorldId 恢复标量/string World，并冻结后继计划；还不是一般领域引用图。
公共首次保存入口尚无：当前包/测试的旧 Revision 初始化仍由低层显式组装。

下一轮推荐 [DB-034 引用图与首次保存计划](../docs/design-branches/0034-durable-reference-graph-batch.md)（Proposed）：
nominal 引用 Schema、队列 Capture、两阶段可达图 Restore、最小 PrepareNew 与对象级增量续写。
本轮仅修订状态和计划，没有启动实施。Schema/history、图闭包与首次入口的范围/验收只维护在该计划；
持久 World 根、Commit/Ref 和其他类型扩展继续按[路线图](../docs/DurableGraph-research-roadmap.md)独立排期。
未来联合 Commit/Ref 及内建类型自举的 SchemaStore 复用路线见
[路线图](../docs/DurableGraph-research-roadmap.md#41-schemastore-复用-statestore-与联合版本视图)。

## 当前能力与实际边界

| 层 | 已验证能力 | 尚未闭合的边界 |
|---|---|---|
| [DurableGraph](DurableGraph/DurableGraph.csproj) | immutable Schema、exact BaseSchema、内存 SchemaStore；Capture/string 身份、StringReadTable；统一 Prepare；typed 整链读取、稳定模型 binding 与身份导入 | 无工作会话 Commit 或一般领域引用图恢复 |
| [Generator](DurableGraph.Generator/DurableGraph.Generator.csproj) / [Build](DurableGraph.Build/DurableGraph.Build.csproj) | SchemaOnly 祖先/history；各版 readonly DTO、current Capture/AddRoot、PrepareBase/融合 Delta、静态 body/string 校验、历史 reader/model 登记、相邻 DTO Upgrade、Allocate/readonly Hydrate；包内 history 发布/验证 | 一般引用/复合类型尚无生成；legacy boxed Snapshot/Upgrade 路径独立保留 |
| [StateStore](DurableGraph.StateStore/DurableGraph.StateStore.csproj) | 持久 Schema 批注册/严格重开；Base 类型引用；局部 reader 目录与完整 Revision 自动冷读、引用验证；受控 LoadedWorld/完整归一化与 fixed-Parent Prepare，升级强制 Base；原 prepared 图接缝保留 | 无公共首次 Prepare、持久 roots、原地 Accept/Commit 或发布 |
| [Storage](DurableGraph.StateStore.Storage/DurableGraph.StateStore.Storage.csproj) | local Base/Delta records、wire v3、exact Revision live map、Parent/prior 校验、object-first 原始重建链及实际 payload H；v3 Base 精确/Delta 上界计量；真实 Segment/RBF 冷重开 | 不解码 typed body；不拥有持久 roots、类型目录或发布 head；重复读取暂未缓存 |
| [Serialization](DurableGraph.StateStore.Serialization/DurableGraph.StateStore.Serialization.csproj) | 字节原语、string 内容 codec、拥有自有 bytes 的 PreparedBase/PreparedDelta、预制 string PrepareBase、显式 body 的 typed slot、SZ/rank-2 元素 ref 循环 | 无数组对象 envelope、一般 struct 生成器或通用泛型 codec |

容易混淆的限制：

- SG DTO/body 支持 13 种标量：bool、byte/sbyte、short/ushort、int/uint、long/ulong、char、Half、float、double；string 字段保存 UInt32 ID。
  SchemaOnly + GenerateBinaryBody 仍限同编译、顶层、非泛型、非 record 的 partial class 链；
  支持 readonly 持久字段和没有无参构造器的领域类。RuntimeHelpers 分配、SG Hydrate/声明层 UnsafeAccessor
  不执行实例构造器或字段初始化表达式；Transient 由用户交付后重建。legacy readonly 拒绝诊断保留。
- AddRoot 登记根，Seal 捕获字段；Accept/Discard 只是内存候选协议。ID 单调分配、失败可烧号；退役实例映射清理不回收数字。
  空串 Capture/读取两端统一 Empty，非空 string 保留引用身份。
  现有多根 Capture 是内部能力/机制见证；LoadedWorld 外层入口限定一个固定 World。
- CaptureSession.Prepare 自动使用 Current，完整预检 exact Schema/DTO/稳定 binding 后编码；全部 live Base 提前生成，
  existing durable 调用融合 Delta、existing string 为 unchanged。结果只标识内存 Previous/Candidate，不带磁盘地址。
  重复准备与失败不安装或放弃候选、不烧号；临时 guard 拒绝会话重入。capture-only 登记仍有效，缺 binding 仅 Prepare 拒绝。
  跨 Schema/DTO/binding 不匹配拒绝，不自动降级 BaseOnly；StateStore 的 CapturedRevisionPlanner 统一映射结果，调用方仍负责 exact Parent 对应。
- SG RegisterReaders 显式登记一个模型族的全部可用 Vn；StateReaderRegistry 同 binding 实例幂等，
  同 key 另一实例拒绝，读取开始复制固定索引。Schema 日志不包含可执行 reader，完全移除的模型族仍拒绝。
  Runtime typed 循环完成整链后才装箱，字段 body 保持静态绑定；无程序集扫描或一般 TypeCodec。
- RevisionDecoder.Read 读取指定 Revision 全部 live 行，逐对象匹配完整 Schema 后解码，最后统一
  验证目标 Revision 的 string 引用。晚期失败不返回部分结果，不要求全批 body 零调用。
  DecodedRevision 保留 stored-exact DTO、查询地址及每 ID 唯一 string 实例，关闭 Store 后仍可使用；
  无 roots/领域实例/Upgrade，不是 CaptureSession.Current，不能直接作为已加载的可编辑基线。
- StateModelRegistry 显式登记稳定 SG Model 并在加载前快照；可选普通静态 UpgradeStateVnToVnPlus1
  按相邻版本转换完整 leaf DTO，不重复升级祖先。已声明边逐一强类型检查；缺边仅阻止需要该边的 current Load。
  LoadedWorld.Load 先完整 exact 解码、再升级全部 source 行并校验引用，最后分配/填充所选 exact World。
  内部仅保留 current DTO 比较基线及 source Schema/完整 membership，升级仍 live 必须 Base。
  当前只物化所选 World；不可达 source 仍须归一化，但不要求其 current 类型可以 Allocate。
- LoadedWorld.Prepare 固定 Parent/WorldId，返回 owned StateRevision，成功或失败均释放临时 Capture，
  不推进基线。宿主 Append 后重新 Load；Schema 注册可持久生效，不代表发布。
  Empty 反向映射选择最小 source ID，但基线槽保留旧 ID，首次 Capture 形成真实 Delta/Remove。
  分配从完整 source live max+1 起，只承诺会话内单调；uint 耗尽不阻止已有对象保存。
- PrepareDelta 每槽比较一次形成位图，再静态写变化值；结果含 HasChanges 和可复用 payload，裸 Delta body 大小可直接取长度。
  策略 D 还须计入对象 envelope，不能直接以裸 body 大小代替。
  PrepareBase 对每版 DTO 复用 Write；全部 live Base 提前准备，决策后复用 bytes，性能优化留待 MVP 后。
  B 为完整 Base payload 精确值，D 仅对未定文件距离按 5 字节上界计量（超额 0..4）；H 仍是原记录实编码。
  ApplyDeltaVn 只处理同 Vn；不证明 prior 身份，之后仍须对完整 DTO 验证引用。
- SchemaStore 借用独占的专用 IRbfFile；完整祖先闭包与同 key 冲突预检后，一批次一帧追加/flush，等价注册不写。
  严格重放全部帧/CRC；坏尾、tombstone、未知格式拒绝且不自动截断。写入不确定后 faulted，须重开；
  可写非空重开先 flush 再交付，readonly 不确认新屏障。尚无 Schema 分段、联合版本目录或自动修复。
- BaseObjectPayloadCodec 只为 Base 加 v1 类型头，durable 使用逻辑 SchemaKey，string 走内建路径；Delta 仍为裸 body。
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
  prior 等于该记录 exact Parent 的当前 head，要求 direct local record；Base 截断内容链，H 随之重置。
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
