# DurableGraph 产品开发工作集

> 最近校准：2026-09-06
>
> 状态：基础存储/策略、祖先 Schema、typed slot/数组元素、SG DTO/body 及 string 引用 Capture/读取分片已实现并验收；完整图恢复与持久 Save 尚未实施。
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
string 引用上下文与最小对象列表已接入 Capture；设计与边界见
[DB-024](../docs/design-branches/0024-reference-capture-and-reusable-object-ids.md)。
用户明确 ObjectId 经过 StateRevision 解释，可回收复用；但首片已选择 session 内单调递增分配，回收延期。
首片已实现 string 引用 Capture、封闭对象列表与内存 accept/discard。
实施前交接见 [工作单](../docs/WORK-ORDER-REFERENCE-CAPTURE.md)和 [Goal 草稿](../docs/GOAL-REFERENCE-CAPTURE.md)，
用户已采纳 DB-024 §3.1 的 G0 方案；实现与验收证据见工作单。
SG 在 internal helper 生成 AddRoot，配对领域类型/Schema/DTO/Capture；Runtime 公开最小 session/context/候选接缝。
AddRoot 登记根，Seal 捕获字段；exact 类型检查只在根入口，保留基类 Capture 可接收派生实例。
DB-025 增加 string 内容读取表与 SG 各版引用校验；用户选择空串在 Capture/读取两端统一 string.Empty，
非空 string 保留引用身份。自定义 struct 的嵌套布局、exact 版本传播已记入 DB-024 TODO，独立排期；BCL 集合继续暂缓。
完整图和旧运行时序列化器翻新仍未实施。
[DB-017](../docs/design-branches/0017-object-codec-design-points.md)保留早期要点/旧实现证据；
其 string 字段 inline 方案已被取代。codec-first 排序依据见 DB-016。

## 当前能力与分层

| 项目 | 已有能力及边界 |
|---|---|
| [DurableGraph](DurableGraph/DurableGraph.csproj) | immutable DurableSchema 与 exact BaseSchema；内存 SchemaStore；CaptureSession/Context 维护 string 身份、单调 ID、封闭候选；StringReadTable 从独立 bodies 解码并解析 string，空串两端统一；非持久图 Store |
| [Generator](DurableGraph.Generator/DurableGraph.Generator.csproj) / [Build](DurableGraph.Build/DurableGraph.Build.csproj) | legacy Snapshot/Upgrade 保留；SchemaOnly 祖先/history；生成 readonly Vn、current Capture、concrete AddRoot、各版 Write/ReadVn/ValidateStringReferences；String schema 槽为 uint |
| [StateStore](DurableGraph.StateStore/DurableGraph.StateStore.csproj) | 固定 ReadAmplificationBaseBudgetPolicy：全部 post-live 估算 → 稀疏只读 Base/Delta 写计划；无内容执行/完整 Save |
| [Storage](DurableGraph.StateStore.Storage/DurableGraph.StateStore.Storage.csproj) | immutable membership StateRevision、canonical provisional wire、真实 RBF/Segment append/read/reopen、exact-head shallow live map；无 ObjectVersion payload |
| [Serialization](DurableGraph.StateStore.Serialization/DurableGraph.StateStore.Serialization.csproj) | BCL-only 字节原语/string 内容 codec、显式 body 的 typed slot、SZ/rank-2 元素 ref 循环；primitive slot 查表仅为测试共享工具，尚无数组对象 envelope 或对象级 Base/Delta |

当前产品依赖为 StateStore → Storage → Serialization，Storage 另用 RbfSegmentStore/Rbf 与地址基础类型。
DurableGraph runtime/package 已引用 Serialization；Reader/Writer 类型、构造、13 种标量和 reader 边界 API
对下游公开；non-null string ReadString/WriteString 也公开，nullable string/块操作与 typed slot/数组模板仍 internal。
单一 runtime PackageReference 能取得传递依赖。
Schema/历史工具支持 bool、byte/sbyte、short/ushort、int/uint、long/ulong、char、Half、float、double 及 string。
DTO body 支持 13 种标量及 string 引用的 UInt32 ID；String 内容独立编码/解码，引用槽由 SG 另行校验，尚无领域对象恢复。
位于 Generator 源码中的 graph operations generator 仍是未注册 probe，不能当成产品图能力。

## 已选方向

- 产品地址采用多历史 Segment 与 BackwardFileDistance；文件轮转是 soft threshold，不强制冷对象 Base。
- Storage 不理解 CLR 字段、Schema 升级或图 reachability；append 返回 candidate address，外层拥有发布 head。
- MVP 固定一个对象表示策略：整数 X 倍产生严格读动机，整数 Y% 控制可选 Base 软预算；
  规则与实现入口见 [DB-015](../docs/design-branches/0015-statestore-object-representation-policy.md)。
- 所有受支持引用对象统一身份：自定义对象、string、数组、BCL 容器均进引用表。
  string 成员写引用号，其对象 body 写内容；非空 string 不作值相等合并，空串显式统一 string.Empty。
  增量 ObjectId 与本次列表位置区分；空串使用普通会话 ID，不预留全局特殊号。
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
- concrete 类型的 internal AddRoot 自动配对 current Schema/DTO/Capture，Runtime 在根入口拒绝派生切片。
  基类 Capture 仍接收派生实例；只有 current exact 布局含 string 时才传共享 context，纯标量 Capture(value) 保留。
  DTO 使用 unmanaged 边界，候选条目私下装箱并按值返回；只读列表无 ICollection.SyncRoot 数组旁路。
  CapturedGraph 不持有领域对象或 session/context；只有当前/未决会话绑定持有管理引用。
- ObjectId 是指定 StateRevision 内的查找编号；相邻保存中持续存活的对象保留 ID，可回收后重新分配。
  不再要求全历史永不复用；跨 revision 裸 ID 相等不代表同一对象，新占用者应从 Base 开始。
  首片改用单调分配；失败/discard 可烧号，高水位不退回，parent 隔离。
  Accept 清理退役实例映射但不回收数字；恢复计数器/SlabBitmap/SlotPool/回收时机均延期。
- 自定义 struct 使用嵌套值布局；字段 exact struct 版本改变要求 owner 及其 inline/base 依赖者升版。
  引用边仍用 nominal 约束，不因引用目标升版而递归传播。类型描述/history 与 nested DTO 尚待实现。
- 真实 parent、保存视图、变更分类与 Removes 属于调用方；估算计划不能自行证明 live 集合完整。

## 近期依赖顺序

1. 已闭合 Base/Middle/Leaf 的 Schema/history 版本传播；保持 SchemaOnly 与旧 boxed serializer 的明确边界。
   SchemaOnly 整条领域链同编译、顶层、非泛型、非 record、partial，可 abstract；字段 kind 见上述支持清单。
2. 已闭合 internal ValueSlotCodec<T> 的 primitive 与调用方显式复合 body；绑定对象直接创建 SZ/rank-2 typed 模板，
   无需数组层反射或全局 Type cache。运行时闭合 Cell<T>/Cell<Cell<int>> 的工厂目前是手写测试见证。
   rank-2 元素循环支持已有数组的非零下界；shape 编码/分配、其他 rank/非 SZ rank-1 尚未实现。
3. 已闭合 SG 标量/string ID Versioned DTO 与内存候选；root 登记、Seal 捕获、Accept/Discard、失败烧号、退役映射清理均有见证。
   历史 String/旧 CLR 祖先消失后仍按 exact 布局生成 ID DTO；ReadVn 只读数字，不验证/解析引用目标。
   [DB-025](../docs/design-branches/0025-string-object-decoding-slice.md) 的 string-only 内容解码表与 SG 各版引用槽校验已实现，
   已通过验收；保留 ID DTO，typed 消费者负责完整目录和 exact Schema 预检，领域 Restore 另片。
   用户已裁决所有空串统一 Empty，非空维持身份；产品不采用非公开入口或公开 API 的独立空串分配技巧。
   不提前建立通用图加载/TypeCodec registry；精确进度见 DB-025 §7。
   struct 仍需 inline exact Schema 表达，独立排期。
4. 接入真实 ObjectVersion 内容存取；raw Base-only 是小范围候选。codecs 与 raw storage 没有硬性先后依赖，
   当前按用户已选择的 codec-first 推进。
5. 根据真实消费者收敛 Delta、原始版本链、B/D/H 的来源与提交更新，再接策略和 Save。

引用、数组的支持和存储接入具体穿插顺序尚未冻结；BCL 集合/comparer/索引问题已明确暂缓。
不在 primitive 首片预做通用图框架、容器工厂或未使用的扩展接口。

## 当前待定项

- boxed 值身份；空串已裁决两端统一 Empty，不再研究独立分配。13 种标量已贯通 Schema/DTO；
  char 按 UInt16 code unit（允许孤立代理项），Half/float/double 保持负零及 NaN payload 位。
  decimal、enum、nullable value、native int、Int128 和一般 struct 仍未支持。
- SG 已生成当前及历史标量/string ID DTO body；按 stored Schema 的运行时注册/分派、DTO 升级及领域恢复尚未实现。
- 跨程序集继承 helper 可见性、引用历史升级；现有 string Capture 与 body 仍限同编译领域链。
- 持久图编码的 revision 归属和计数策略；内存 CaptureSession/Context 与 primitive 公开边界已落地。
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

## 会话接续入口（2026-09-06）

- 恢复时先读根 AGENTS.md 和本文，再读 DB-018；DB-017 是 superseded 的探索记录，不恢复其旧 string 字段 inline 方案。
- 用户已授权自主按需提交；准确提交与工作区状态以 git log/status 为准。
- Schema/history 增强已完成；默认 SchemaOnly 仍是 metadata 入口。
  额外 GenerateBinaryBody 已改成 internal readonly Vn + current Capture + DTO Write/ReadVn，
  不保留直接领域 Read/Write，不生成 Serializer 或 Upgrade。legacy Snapshot 路径仍独立保留。
  history v1 的可选 base 行与运行时 BaseSchema 都是原型形状；GetHashCode 不是持久 SchemaHash。
- 当前接口以 DB-022 为准，DB-023 扩充 13 种标量；DB-021 是被取代的直接领域 body 历史。
  Schema TypeTag 1–4 不变，新增 5–14；旧工具拒绝新 tag，需要同步更新包。不是未来 TypeCodec 编号。
  已知成员仍直接静态调用 byte 原语。
  body 不含对象头或恢复分配；AddRoot 只检验所选 concrete binding，不是自动 runtime 类型分派。
  ReadVn 成功才返回 DTO，外层负责布局匹配和持久发布隔离；StringReadTable 与单独的 SG 引用校验已实现。
  通用泛型注册及 DTO 升级尚未实现；不能声称已生成一般 struct serializer。
  SG body + runtime 按需闭合仍是推荐路线，不是整体翻新旧 IL 后端的决定。BCL 集合继续暂缓。
- 代码验证结果保存在实验笔记；generic ref 机制的
  [完整复跑附件](../docs/design-branches/0018-runtime-binding-witness.md)不依赖会话工具输出。
