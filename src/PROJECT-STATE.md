# DurableGraph 产品开发工作集

> 校准：2026-09-06，最新产品验证见 [DB-028 §6](../docs/design-branches/0028-persisted-object-delta-chain-slice.md#6-实施账本)。本文只维护当前能力、边界与续工入口。
> 文档不是实现授权；事实以当前源码、测试和工具输出为准。

## 从这里继续

先读本文，再按任务选择一份材料；不需要按 DB 编号通读历史。

- 理解产品目标与已选约束：[目标设计](../docs/DurableGraph-target-design-v0.md)。
- 选择下一片、查未完成决策与问题：[后续路线](../docs/DurableGraph-research-roadmap.md)。
- 查某项实现的来由或验收：[设计与施工记录索引](../docs/design-branches/README.md)。
- 查历史实验：[实验簿入口](../docs/DurableGraph-lab-notebook.md)；重跑独立实验：[Probe 导航](../experiments/README.md)。

## 当前焦点

DB-028 的持久 raw Delta、exact Parent/prior 校验、原始重建链与 H 已完成，当前没有正在施工的产品分片。
下一候选是对象列表比较/估算/策略执行，或先收敛持久类型头与目录；前者仍需 exact Parent 基线、
不可 Delta 的 Update 分类与候选 B/D 口径，后者补上 typed 见证当前显式提供的解释元数据。
详细待定项集中在后续路线，不据此启动完整 Save 或发布施工。
struct、一般引用 Capture、DTO 升级/Restore 可独立穿插，选择与待定点集中在后续路线。

## 当前能力与实际边界

| 层 | 已验证能力 | 尚未闭合的边界 |
|---|---|---|
| [DurableGraph](DurableGraph/DurableGraph.csproj) | immutable Schema、exact BaseSchema、内存 SchemaStore；CaptureSession 的封闭候选与 string 身份；StringReadTable | 非持久图 Store；无一般领域图恢复 |
| [Generator](DurableGraph.Generator/DurableGraph.Generator.csproj) / [Build](DurableGraph.Build/DurableGraph.Build.csproj) | SchemaOnly 的祖先/history；生成各版 readonly DTO、current Capture/AddRoot、Base Write/Read、同版融合 PrepareDelta/Apply 及 string 引用校验；包内 history 发布/验证 | 新 DTO 路径无升级/Restore/runtime 类型分派；legacy boxed Snapshot/Upgrade 路径独立保留 |
| [StateStore](DurableGraph.StateStore/DurableGraph.StateStore.csproj) | 固定 ReadAmplificationBaseBudgetPolicy：完整 post-live 估算输入 → 稀疏 Base/Delta 计划 | 无估算生产、内容执行或完整 Save |
| [Storage](DurableGraph.StateStore.Storage/DurableGraph.StateStore.Storage.csproj) | local Base/Delta records、wire v3、exact Revision live map、Parent/prior 校验、object-first 原始重建链及实际 payload H；真实 Segment/RBF 冷重开 | 不解码 typed body；不拥有持久 roots、类型目录或发布 head；重复读取暂未缓存 |
| [Serialization](DurableGraph.StateStore.Serialization/DurableGraph.StateStore.Serialization.csproj) | 字节原语、string 内容 codec、拥有自有 bytes 的 PreparedDelta、显式 body 的 typed slot、SZ/rank-2 元素 ref 循环 | 无数组对象 envelope、一般 struct 生成器或通用泛型 codec |

容易混淆的限制：

- SG DTO/body 支持 13 种标量：bool、byte/sbyte、short/ushort、int/uint、long/ulong、char、Half、float、double；string 字段保存 UInt32 ID。
  SchemaOnly + GenerateBinaryBody 仍限同编译、顶层、非泛型、非 record 的 partial class 链；readonly durable 字段仍拒绝。
- AddRoot 登记根，Seal 捕获字段；Accept/Discard 只是内存候选协议。ID 单调分配、失败可烧号；退役实例映射清理不回收数字。
  空串 Capture/读取两端统一 Empty，非空 string 保留引用身份。
- ReadVn 只产生 ID DTO；StringReadTable 和生成的引用校验分别负责 string 解码与槽位验证。
  typed 集成测试显式提供 kind/exact Schema/roots 元数据，尚非持久自描述图。
- PrepareDelta 每槽比较一次形成位图，再静态写变化值；结果含 HasChanges 和可复用 payload，裸 Delta body 大小可直接取长度。
  策略 D 还须计入对象 envelope，不能直接以裸 body 大小代替。
  ApplyDeltaVn 只处理同 Vn；不证明 prior 身份，之后仍须对完整 DTO 验证引用。尚无策略执行。
- Storage 的 ObjectHeadMap 与对象内容的 Base/Delta 独立组合。ReadObjectVersionChain 逐条核对
  prior 等于该记录 exact Parent 的当前 head，要求 direct local record；Base 截断内容链，H 随之重置。
  Append 只预检直接 edge；完整 map 的 external heads 仍是浅声明，不认证全局实体历史。
  ReadObjectBase 仍只接受 Base head，不回退 parent 补内容；wire v3 拒绝 v1/v2。
  H 含 kind/prior/length/body，不含 ObjectId/membership/共享 Frame；不是总冷读 I/O。
  先直读 RBF，缓存优化留有 [TODO](DurableGraph.StateStore.Storage/StateRevisionStore.cs)。
- 数组循环可操作已有 rank-2 非零下界数组，但尚无 shape 编码/分配、其他 rank 或非 SZ rank-1 支持。
  已知成员的 SG body 静态绑定字节原语；PrimitiveSlotCodecs 只在测试工具中。
- Generator 中未注册的 graph operations probe 和 tests 中 logical graph R1–R3b 是机制见证，不能算产品通用图能力。

产品依赖目前为 StateStore → Storage → Serialization；Storage 另用 RBF substrate。
DurableGraph runtime 也引用 Serialization，单一 runtime PackageReference 可取得传递依赖。

## 按任务定位证据

| 准备修改 | 先查源码/测试，再按需读合同 |
|---|---|
| Schema、DTO、静态 body | [Generator tests](../tests/DurableGraph.Tests)、[DB-019](../docs/design-branches/0019-schema-ancestry-implementation-slice.md)、[DB-022](../docs/design-branches/0022-versioned-state-dto-capture.md)、[DB-023](../docs/design-branches/0023-scalar-schema-dto-slice.md) |
| 同版 DTO Delta 准备与应用 | [DB-027](../docs/design-branches/0027-generated-same-schema-delta-body-slice.md)、[body tests](../tests/DurableGraph.Tests/FusedDeltaBodyTests.cs)、[history/Capture tests](../tests/DurableGraph.Tests/FusedDeltaHistoryTests.cs) |
| Capture 与 string 读取 | [DB-024](../docs/design-branches/0024-reference-capture-and-reusable-object-ids.md)、[DB-025](../docs/design-branches/0025-string-object-decoding-slice.md) |
| 对象内容、地址与重开读取 | [Storage tests](../tests/DurableGraph.StateStore.Storage.Tests)、[DB-026](../docs/design-branches/0026-raw-base-object-content-slice.md)、[typed 文件见证](../tests/DurableGraph.Tests/RawBaseStorageGeneratorTests.cs) |
| 持久 Delta、prior 链与 H | [DB-028](../docs/design-branches/0028-persisted-object-delta-chain-slice.md)、[链测试](../tests/DurableGraph.StateStore.Storage.Tests/ObjectVersionChainStoreTests.cs)、[真实 SG 冷重开](../tests/DurableGraph.Tests/PersistedDeltaChainGeneratorTests.cs) |
| Base/Delta 策略 | [策略实现](DurableGraph.StateStore/ReadAmplificationBaseBudgetPolicy.cs)、[策略 tests](../tests/DurableGraph.StateStore.Tests)、[DB-015](../docs/design-branches/0015-statestore-object-representation-policy.md) |
| 包、生成器消费和 history 发布 | [PackageConsumerProbe](../experiments/PackageConsumerProbe/README.md)；真实 PackageReference 验证不能由 ProjectReference 测试替代 |

代码变更后运行根 solution build 和相关 tests；包交付边界变化时按 PackageConsumer README 验证。
本文件不累积测试计数、命令日志或完成流水账；已有验证结果保留在相应施工记录。
