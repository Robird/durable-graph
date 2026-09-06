# DurableGraph 产品开发工作集

> 校准：2026-09-07，最新产品验证见 [DB-031 §8](../docs/design-branches/0031-persisted-object-type-envelope-slice.md#8-实施合同与账本)。本文只维护当前能力、边界与续工入口。
> 文档不是实现授权；事实以当前源码、测试和工具输出为准。

## 从这里继续

先读本文，再按任务选择一份材料；不需要按 DB 编号通读历史。

- 理解产品目标与已选约束：[目标设计](../docs/DurableGraph-target-design-v0.md)。
- 选择下一片、查未完成决策与问题：[后续路线](../docs/DurableGraph-research-roadmap.md)。
- 查某项实现的来由或验收：[设计与施工记录索引](../docs/design-branches/README.md)。
- 查历史实验：[实验簿入口](../docs/DurableGraph-lab-notebook.md)；重跑独立实验：[Probe 导航](../experiments/README.md)。

## 当前焦点

DB-031 已完成持久 Schema 注册与 Base 类型引用，当前没有正在施工的分片，验证见分片账本。
MVP 使用专用 RBF Schema 日志和 Repository 内单调注册；Base 持 exact SchemaKey，Delta 沿同版链解释。
后续候选为 roots/自动读取分派、新 DTO 升级及加载基线导入，或受控工作会话；下一范围尚未选择。
WorkingTree/GraphSession 仍是统一持有 Parent、DTO 基线与实例身份的目标，现有保存接缝不等于 Commit。
未来联合 Commit/Ref 及内建类型自举的 SchemaStore 复用路线见
[路线图](../docs/DurableGraph-research-roadmap.md#41-schemastore-复用-statestore-与联合版本视图)。

## 当前能力与实际边界

| 层 | 已验证能力 | 尚未闭合的边界 |
|---|---|---|
| [DurableGraph](DurableGraph/DurableGraph.csproj) | immutable Schema、exact BaseSchema、内存 SchemaStore；Capture/string 身份、StringReadTable；统一 Prepare 从 candidate/Current 生成完整 owned 内容 | 无持久基线对应、工作会话 Commit 或一般领域图恢复 |
| [Generator](DurableGraph.Generator/DurableGraph.Generator.csproj) / [Build](DurableGraph.Build/DurableGraph.Build.csproj) | SchemaOnly 的祖先/history；生成各版 readonly DTO、current Capture/AddRoot 与稳定 preparation binding、Base Prepare/Write/Read、同版融合 PrepareDelta/Apply 及 string 引用校验；包内 history 发布/验证 | 新 DTO 路径无升级/Restore/加载类型分派；legacy boxed Snapshot/Upgrade 路径独立保留 |
| [StateStore](DurableGraph.StateStore/DurableGraph.StateStore.csproj) | 持久 Schema 批注册/严格重开；Base 类型引用与 exact typed 链读取；异构 prepared 图 → Schema/Parent 预检 → 注册/包头 → 固定 policy → 可追加 Revision；显式 BaseOnlyUpdate | 无自动 reader 分派、升级/加载基线管理、完整 Save 或发布 |
| [Storage](DurableGraph.StateStore.Storage/DurableGraph.StateStore.Storage.csproj) | local Base/Delta records、wire v3、exact Revision live map、Parent/prior 校验、object-first 原始重建链及实际 payload H；v3 Base 精确/Delta 上界计量；真实 Segment/RBF 冷重开 | 不解码 typed body；不拥有持久 roots、类型目录或发布 head；重复读取暂未缓存 |
| [Serialization](DurableGraph.StateStore.Serialization/DurableGraph.StateStore.Serialization.csproj) | 字节原语、string 内容 codec、拥有自有 bytes 的 PreparedBase/PreparedDelta、预制 string PrepareBase、显式 body 的 typed slot、SZ/rank-2 元素 ref 循环 | 无数组对象 envelope、一般 struct 生成器或通用泛型 codec |

容易混淆的限制：

- SG DTO/body 支持 13 种标量：bool、byte/sbyte、short/ushort、int/uint、long/ulong、char、Half、float、double；string 字段保存 UInt32 ID。
  SchemaOnly + GenerateBinaryBody 仍限同编译、顶层、非泛型、非 record 的 partial class 链；readonly durable 字段仍拒绝。
- AddRoot 登记根，Seal 捕获字段；Accept/Discard 只是内存候选协议。ID 单调分配、失败可烧号；退役实例映射清理不回收数字。
  空串 Capture/读取两端统一 Empty，非空 string 保留引用身份。
- CaptureSession.Prepare 自动使用 Current，完整预检 exact Schema/DTO/稳定 binding 后编码；全部 live Base 提前生成，
  existing durable 调用融合 Delta、existing string 为 unchanged。结果只标识内存 Previous/Candidate，不带磁盘地址。
  重复准备与失败不安装或放弃候选、不烧号；临时 guard 拒绝会话重入。capture-only 登记仍有效，缺 binding 仅 Prepare 拒绝。
  跨 Schema/DTO/binding 不匹配拒绝，不自动降级 BaseOnly；StateStore 的 CapturedRevisionPlanner 统一映射结果，调用方仍负责 exact Parent 对应。
- ReadVn 只产生 ID DTO；StringReadTable 和生成的引用校验分别负责 string 解码与槽位验证。
  DB-031 typed 集成从持久 Base/Schema 日志恢复 kind/exact Schema；查询 ID、roots 和编译期 reader 仍显式提供，尚非完整自描述图。
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
  不自动选择 CLR reader、Upgrade 或解析引用。Schema 注册帧是共享元数据，不摊入对象 B/D/H。
- CapturedRevisionPlanner 先核对 Previous/Parent、完整 prior ID 集合及所有 survivor 的 Base kind/exact Schema，
  包括 NoChange；再注册全部 current Schema、包装 Base 并调用原 planner。Schema 注册可持久生效，
  但该方法不追加 State/发布/Accept，也不证明 DTO 内容与 Parent 一致；合法迁移仍由后续加载层产生 BaseOnlyUpdate。
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
| 异构图统一准备内容 | [DB-030](../docs/design-branches/0030-captured-object-preparation-slice.md)、[runtime tests](../tests/DurableGraph.Tests/CapturedGraphPreparationTests.cs)、[生成 tests](../tests/DurableGraph.Tests/GeneratedCapturePreparationTests.cs) |
| 对象内容、地址与重开读取 | [Storage tests](../tests/DurableGraph.StateStore.Storage.Tests)、[DB-026](../docs/design-branches/0026-raw-base-object-content-slice.md)、[typed 文件见证](../tests/DurableGraph.Tests/RawBaseStorageGeneratorTests.cs) |
| 持久 Delta、prior 链与 H | [DB-028](../docs/design-branches/0028-persisted-object-delta-chain-slice.md)、[链测试](../tests/DurableGraph.StateStore.Storage.Tests/ObjectVersionChainStoreTests.cs)、[真实 SG 冷重开](../tests/DurableGraph.Tests/PersistedDeltaChainGeneratorTests.cs) |
| 已准备内容、Base/Delta 策略与 Revision | [DB-029](../docs/design-branches/0029-prepared-object-revision-planning-slice.md)、[规划器](DurableGraph.StateStore/ObjectRevisionPlanner.cs)、[策略实现](DurableGraph.StateStore/ReadAmplificationBaseBudgetPolicy.cs)、[策略 tests](../tests/DurableGraph.StateStore.Tests)、[DB-015](../docs/design-branches/0015-statestore-object-representation-policy.md) |
| 包、生成器消费和 history 发布 | [PackageConsumerProbe](../experiments/PackageConsumerProbe/README.md)；真实 PackageReference 验证不能由 ProjectReference 测试替代 |

代码变更后运行根 solution build 和相关 tests；包交付边界变化时按 PackageConsumer README 验证。
本文件不累积测试计数、命令日志或完成流水账；已有验证结果保留在相应施工记录。
