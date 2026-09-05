# DurableGraph 产品开发工作集

> 最近校准：2026-09-05
>
> 状态：StateStore 基础切片、估算策略、SchemaOnly 祖先元数据及 typed slot/数组元素分片已实现；生成式图 codec 尚未实施。
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
下一候选是让真实 SG body 消费字节/槽位边界；用户明确暂缓 BCL 集合内容支持。
完整图和旧运行时序列化器翻新仍未实施。
[DB-017](../docs/design-branches/0017-object-codec-design-points.md)保留早期要点/旧实现证据；
其 string 字段 inline 方案已被取代。codec-first 排序依据见 DB-016。

## 当前能力与分层

| 项目 | 已有能力及边界 |
|---|---|
| [DurableGraph](DurableGraph/DurableGraph.csproj) | immutable DurableSchema 含 exact BaseSchema 链；内存 SchemaStore 整链预检再登记；保留 boxed serializer/state demo，非持久图 Store |
| [Generator](DurableGraph.Generator/DurableGraph.Generator.csproj) / [Build](DurableGraph.Build/DurableGraph.Build.csproj) | 默认路径保留 typed Snapshot/相邻 Upgrade；显式 SchemaOnly 支持同编译领域继承的 Schema/GetSchema 历史元数据，history/publisher 验证祖先闭包与版本传播 |
| [StateStore](DurableGraph.StateStore/DurableGraph.StateStore.csproj) | 固定 ReadAmplificationBaseBudgetPolicy：全部 post-live 估算 → 稀疏只读 Base/Delta 写计划；无内容执行/完整 Save |
| [Storage](DurableGraph.StateStore.Storage/DurableGraph.StateStore.Storage.csproj) | immutable membership StateRevision、canonical provisional wire、真实 RBF/Segment append/read/reopen、exact-head shallow live map；无 ObjectVersion payload |
| [Serialization](DurableGraph.StateStore.Serialization/DurableGraph.StateStore.Serialization.csproj) | BCL-only 字节原语/string 内容 codec、13 种 primitive typed slot、SZ/rank-2 元素 ref 循环；尚无数组对象 envelope 或对象级 Base/Delta |

当前产品依赖为 StateStore → Storage → Serialization，Storage 另用 RbfSegmentStore/Rbf 与地址基础类型。
DurableGraph runtime/package 尚未接入 Serialization；实际 generated-code 消费者出现时才建立必要引用与可见性。
默认 Schema/历史工具目前只支持 bool/int/long/string 四种 kind，不能把新增 slot 类型清单视为 Schema 支持。
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
- 真实 parent、保存视图、变更分类与 Removes 属于调用方；估算计划不能自行证明 live 集合完整。

## 近期依赖顺序

1. 已闭合 Base/Middle/Leaf 的 Schema/history 版本传播；保持 SchemaOnly 与旧 boxed serializer 的明确边界。
   SchemaOnly 整条领域链同编译、顶层、非泛型、非 record、partial，可 abstract；当前字段 kind 仍为四种。
2. 已闭合 internal ValueSlotCodec<T> 的 primitive 与调用方显式复合 body；绑定对象直接创建 SZ/rank-2 typed 模板，
   无需数组层反射或全局 Type cache。运行时闭合 Cell<T>/Cell<Cell<int>> 的工厂目前是手写测试见证。
   rank-2 元素循环支持已有数组的非零下界；shape 编码/分配、其他 rank/非 SZ rank-1 尚未实现。
3. 下一候选接真实 SG primitive body 与含 string 引用的小对象图，扩展必要 Schema/生成器能力；
   首个 string 消费者采用统一身份，不能继续实现旧字段 inline 路径。
4. 接入真实 ObjectVersion 内容存取；raw Base-only 是小范围候选。codecs 与 raw storage 没有硬性先后依赖，
   当前按用户已选择的 codec-first 推进。
5. 根据真实消费者收敛 Delta、原始版本链、B/D/H 的来源与提交更新，再接策略和 Save。

引用、数组的支持和存储接入具体穿插顺序尚未冻结；BCL 集合/comparer/索引问题已明确暂缓。
不在 primitive 首片预做通用图框架、容器工厂或未使用的扩展接口。

## 当前待定项

- Schema 层基础类型扩充、boxed 值、空字符串独立实例分配。slot 层已选择 13 种：
  bool、byte/sbyte、short/ushort、int/uint、long/ulong、char（UInt16 code unit）、Half、float、double；浮点位保持。
- SG 定义的 body 生成/注册与 exact historical binding；slot 组合不能代替 Schema 校验。
- 继承 payload/升级与跨程序集 helper 可见性；声明层 FieldId、祖先闭包/history 校验已由 DB-019 落地。
- 带引用上下文的 codec 签名、编码 revision 归属、计数策略和 generated-code 原语公开边界。
- 底层原地读入与外层未发布结果的失败隔离；一般 struct 支持不能从 byref 机制自动推导。
- 多态引用、数组 shape/wire/分配；容器统一 identity 原则保留，comparer/key 恢复暂缓讨论。
- ObjectVersion record/codec 绑定、H 恢复及 baseline 更新、ObjectHeadMap checkpoint。

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
- Schema/history 增强已完成；新增 Deserialize 目前仅为值槽位/已有数组元素原地读入。
  SchemaOnly 是当前可用的 metadata 入口，不生成 Serializer、typed payload Snapshot 或 Upgrade。
  history v1 的可选 base 行与运行时 BaseSchema 都是原型形状；GetHashCode 不是持久 SchemaHash。
- 下一推荐闭环是实际 SG body 消费已有 byte/slot 机制。原语/slot 仍为 internal；跨程序集开放需随消费者实施。
  DB-020 不提供成员发现、引用上下文、通用泛型注册或历史 decoder；不能声称已生成 struct serializer。
  SG body + runtime 按需闭合仍是推荐路线，不是整体翻新旧 IL 后端的决定。BCL 集合继续暂缓。
- 代码验证结果保存在实验笔记；generic ref 机制的
  [完整复跑附件](../docs/design-branches/0018-runtime-binding-witness.md)不依赖会话工具输出。
