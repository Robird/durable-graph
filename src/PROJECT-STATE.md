# DurableGraph 产品开发工作集

> 最近校准：2026-09-05
>
> 状态：StateStore 基础切片、估算策略、祖先元数据、typed slot/数组元素及 SG Versioned DTO/Capture/body 已实现；图管线尚未实施。
>
> 范围：src 中的产品项目及对应 tests。本文是共享上下文与导航，不是实现授权或功能规格。

## 目标与当前焦点

逐层把已有探索成果组合为可用的 DurableGraph：对象 Schema/codec、StateStore 内容存取、
图恢复及最终持久提交。每次只闭合一个可验证机制，保持原型边界可调整。

用户已选择从基础类型/生成式 codec 开始，并补充整体对象图模型：所有受支持引用类型统一按引用
身份登记，包括 string；值成员嵌套，引用成员只写 ObjectId，对象记录头带 TypeCodec。
当前形状草图见 [DB-018](../docs/design-branches/0018-generated-graph-codec-shape.md)，
祖先 Schema/history 分片已按本轮实现授权落地，范围见 [DB-019](../docs/design-branches/0019-schema-ancestry-implementation-slice.md)。
typed 值槽位与 SZ/rank-2 元素循环已落地，范围见 [DB-020](../docs/design-branches/0020-typed-slot-array-binding-slice.md)。
用户已选择先捕获版本化状态，再比较/估算/编码。
[DB-022](../docs/design-branches/0022-versioned-state-dto-capture.md)已取代 DB-021 直接领域 body：
SchemaOnly + GenerateBinaryBody 生成 readonly V1..Vcurrent DTO、current Capture 及 typed DTO binary body。
[DB-023](../docs/design-branches/0023-scalar-schema-dto-slice.md)将 Schema/history/DTO 贯通到 13 种标量；
历史 DTO 依 exact 祖先闭包生成，不需要旧 CLR 祖先保留。
下一候选是接 Capture 的 string 引用上下文与最小对象列表；用户明确暂缓 BCL 集合内容支持。
完整图和旧运行时序列化器翻新仍未实施。
[DB-017](../docs/design-branches/0017-object-codec-design-points.md)保留早期要点/旧实现证据；
其 string 字段 inline 方案已被取代。codec-first 排序依据见 DB-016。

## 当前能力与分层

| 项目 | 已有能力及边界 |
|---|---|
| [DurableGraph](DurableGraph/DurableGraph.csproj) | immutable DurableSchema 含 exact BaseSchema 链；内存 SchemaStore 整链预检再登记；保留 boxed serializer/state demo，非持久图 Store |
| [Generator](DurableGraph.Generator/DurableGraph.Generator.csproj) / [Build](DurableGraph.Build/DurableGraph.Build.csproj) | legacy 保留 mutable Snapshot/相邻 Upgrade；SchemaOnly 支持祖先元数据/history；额外 GenerateBinaryBody 生成 readonly Vn、current Capture、各版 typed Write/ReadVn；publisher 仍积累同一 metadata |
| [StateStore](DurableGraph.StateStore/DurableGraph.StateStore.csproj) | 固定 ReadAmplificationBaseBudgetPolicy：全部 post-live 估算 → 稀疏只读 Base/Delta 写计划；无内容执行/完整 Save |
| [Storage](DurableGraph.StateStore.Storage/DurableGraph.StateStore.Storage.csproj) | immutable membership StateRevision、canonical provisional wire、真实 RBF/Segment append/read/reopen、exact-head shallow live map；无 ObjectVersion payload |
| [Serialization](DurableGraph.StateStore.Serialization/DurableGraph.StateStore.Serialization.csproj) | BCL-only 字节原语/string 内容 codec、显式 body 的 typed slot、SZ/rank-2 元素 ref 循环；primitive slot 查表仅为测试共享工具，尚无数组对象 envelope 或对象级 Base/Delta |

当前产品依赖为 StateStore → Storage → Serialization，Storage 另用 RbfSegmentStore/Rbf 与地址基础类型。
DurableGraph runtime/package 已引用 Serialization；Reader/Writer 类型、构造、13 种标量和 reader 边界 API
对下游公开，string 内容/块操作与 typed slot/数组模板仍 internal。单一 runtime PackageReference 能取得传递依赖。
Schema/历史工具支持 bool、byte/sbyte、short/ushort、int/uint、long/ulong、char、Half、float、double 及 string。
DTO body 支持其中 13 种标量，string 仍须等待统一引用上下文；metadata 支持不代表图支持。
位于 Generator 源码中的 graph operations generator 仍是未注册 probe，不能当成产品图能力。

## 已选方向

- 产品地址采用多历史 Segment 与 BackwardFileDistance；文件轮转是 soft threshold，不强制冷对象 Base。
- Storage 不理解 CLR 字段、Schema 升级或图 reachability；append 返回 candidate address，外层拥有发布 head。
- MVP 固定一个对象表示策略：整数 X 倍产生严格读动机，整数 Y% 控制可选 Base 软预算；
  规则与实现入口见 [DB-015](../docs/design-branches/0015-statestore-object-representation-policy.md)。
- 所有受支持引用对象统一身份：自定义对象、string、数组、BCL 容器均进引用表。
  string 成员写引用号，其对象 body 写内容，不作值相等合并；增量 ObjectId 与本次列表位置区分。
- SG 强类型遍历 struct/class 字段、数组元素和 BCL 内容；值嵌套，引用只写 ID；
  class 按 base-first 组合 body；祖先 exact 依赖变化要求派生版本递增，DB-019 已选择声明层 FieldId 分段。
- 引用成员使用稳定 nominal 类型约束、对象头使用 exact 类型/Schema 的区别已获用户同意。
- 用户澄清 ref 的重点是 struct codec 共用字段/元素等真实槽位，读写可以分开；
  Robird 旧实现只作机制证据，不要求移植 DynamicMethod 或统一 mode visitor。
- 已知成员类型的 SG body 应直接调用字节原语或静态值 body，不要求经过 Type 查表或 slot 委托。
  PrimitiveSlotCodecs 已移入 Serialization 测试项目的 TestHelpers，保留现有组合测试；开放泛型绑定另行裁决。
- 保存管线以捕获后的 Versioned DTO 为输入；Capture 后领域变化不能影响候选状态。
  候选只在实际提交发布成功后成为基线，不能重新捕获领域对象代替已提交结果；完整基线管线尚未实现。
- DTO 的物理字段不决定 Schema。当前以 Segment0Field1 等名字展开 exact 声明链，Schema 仍分段；
  历史 Vn 来自 accepted history，每次再生成，不另存 DTO 源码历史。
- 真实 parent、保存视图、变更分类与 Removes 属于调用方；估算计划不能自行证明 live 集合完整。

## 近期依赖顺序

1. 已闭合 Base/Middle/Leaf 的 Schema/history 版本传播；保持 SchemaOnly 与旧 boxed serializer 的明确边界。
   SchemaOnly 整条领域链同编译、顶层、非泛型、非 record、partial，可 abstract；字段 kind 见上述支持清单。
2. 已闭合 internal ValueSlotCodec<T> 的 primitive 与调用方显式复合 body；绑定对象直接创建 SZ/rank-2 typed 模板，
   无需数组层反射或全局 Type cache。运行时闭合 Cell<T>/Cell<Cell<int>> 的工厂目前是手写测试见证。
   rank-2 元素循环支持已有数组的非零下界；shape 编码/分配、其他 rank/非 SZ rank-1 尚未实现。
3. 已闭合 SG Versioned DTO：SchemaOnly + GenerateBinaryBody 整链启用，所有 current/history closure 支持 13 种标量。
   current Capture 复制 private/base-first/FieldId 字段；readonly Vn 配对 GetSchema(n)，Write(in Vn)/ReadVn 操作 DTO。
   下一候选为 Capture 的首个 string 消费者接统一引用上下文及最小对象列表；
   struct 还需 inline exact Schema 表达。不能继续实现旧 string 字段 inline 路径。
4. 接入真实 ObjectVersion 内容存取；raw Base-only 是小范围候选。codecs 与 raw storage 没有硬性先后依赖，
   当前按用户已选择的 codec-first 推进。
5. 根据真实消费者收敛 Delta、原始版本链、B/D/H 的来源与提交更新，再接策略和 Save。

引用、数组的支持和存储接入具体穿插顺序尚未冻结；BCL 集合/comparer/索引问题已明确暂缓。
不在 primitive 首片预做通用图框架、容器工厂或未使用的扩展接口。

## 当前待定项

- boxed 值身份、空字符串独立实例分配。13 种标量已贯通 Schema/DTO；
  char 按 UInt16 code unit（允许孤立代理项），Half/float/double 保持负零及 NaN payload 位。
  decimal、enum、nullable value、native int、Int128 和一般 struct 仍未支持。
- SG 已生成当前及历史标量 DTO typed body；按 stored Schema 的运行时注册/分派、DTO 升级及领域恢复尚未实现。
- 带引用的继承 payload/历史升级与跨程序集继承 helper 可见性；当前标量继承 body 仅支持同编译领域链。
- 带引用上下文的 codec 签名、编码 revision 归属和计数策略；当前消费者所需的 primitive 公开边界已落地。
- DTO ReadVn 失败不返回半成品，只可能推进 Reader；底层 slot/数组仍是原地读入。
  一般 struct/含引用 DTO 的所有权与不可变内容不能从 scalar readonly DTO 自动推导。
- 多态引用、数组 shape/wire/分配；容器统一 identity 原则保留，comparer/key 恢复暂缓讨论。
- ObjectVersion record/codec 绑定、H 恢复及 baseline 更新、ObjectHeadMap checkpoint；DTO 容器和比较/估算另片推进。

## 明确暂缓与证据入口

本轮不做完整 Save、SchemaHash/持久 SchemaStore、持久 head 发布、crash recovery、物理 GC、
跨 Store 原子提交或正式兼容承诺。不要把 Probe 的合成 payload/恢复能力写成产品已实现。

当前事实以源码与对应 tests 为准；历史验证、提交和设计演进见
[实验笔记](../docs/DurableGraph-lab-notebook.md)，避免在这里复制测试清单和命令日志。
原阶段 B 的总体分层讨论保留在
[STATESTORE-SUBSYSTEM-DESIGN](../experiments/MultiSegmentStateStoreProbe/STATESTORE-SUBSYSTEM-DESIGN.md)；
那是设计材料，产品当前进展在本文维护。
已完成的 [MultiSegment Probe](../experiments/MultiSegmentStateStoreProbe/PROJECT-STATE.md)
和 [TwoLeg 技术储备](../experiments/TwoLegRotationProbe/PROJECT-STATE.md)分别维护自身上下文；
只复用可验证机制，不建立产品到 Probe 的依赖。

## 会话接续入口（2026-09-05）

- 恢复时先读根 AGENTS.md 和本文，再读 DB-018；DB-017 是 superseded 的探索记录，不恢复其旧 string 字段 inline 方案。
- 用户已授权自主按需提交；上一批工作集迁移、DB-016–019 与祖先实现已提交为 `29a4704`。
  DB-020 是其后的实现分片；准确提交与工作区状态以 git log/status 为准。
- Schema/history 增强已完成；默认 SchemaOnly 仍是 metadata 入口。
  额外 GenerateBinaryBody 已改成 internal readonly Vn + current Capture + DTO Write/ReadVn，
  不保留直接领域 Read/Write，不生成 Serializer 或 Upgrade。legacy Snapshot 路径仍独立保留。
  history v1 的可选 base 行与运行时 BaseSchema 都是原型形状；GetHashCode 不是持久 SchemaHash。
- 当前接口以 DB-022 为准，DB-023 扩充 13 种标量；DB-021 是被取代的直接领域 body 历史。
  Schema TypeTag 1–4 不变，新增 5–14；旧工具拒绝新 tag，需要同步更新包。不是未来 TypeCodec 编号。
  已知成员仍直接静态调用 byte 原语。
  body 不含对象头、分配或 exact runtime 类型分派；ReadVn 成功才返回 DTO，外层负责布局匹配和发布隔离。
  引用上下文、通用泛型注册及 DTO 升级尚未实现；不能声称已生成一般 struct serializer。
  SG body + runtime 按需闭合仍是推荐路线，不是整体翻新旧 IL 后端的决定。BCL 集合继续暂缓。
- 代码验证结果保存在实验笔记；generic ref 机制的
  [完整复跑附件](../docs/design-branches/0018-runtime-binding-witness.md)不依赖会话工具输出。
