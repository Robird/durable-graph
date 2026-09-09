# DurableGraph 产品开发工作集

> 校准：2026-09-09，[DB-049](../docs/design-branches/0049-list-range-delta-and-matcher-trial-slice.md) 已完成 G0–G4 验收。本文只维护当前能力、边界与续工入口。
> 文档不是实现授权；事实以当前源码、测试和工具输出为准。

## 从这里继续

先读本文，再按任务选择一份材料；不需要按 DB 编号通读历史。

- 理解产品目标与已选约束：[目标设计](../docs/DurableGraph-target-design-v0.md)。
- 查首选用语、概念示意和对应代码：[项目术语表](../docs/DurableGraph-glossary.md)；后续文档、代码命名与注释的一致化以此定位差异。
- 选择下一片、查未完成决策与问题：[后续路线](../docs/DurableGraph-research-roadmap.md)。
- 查某项实现的来由或验收：[设计与施工记录索引](../docs/design-branches/README.md)。
- 查历史实验：[实验簿入口](../docs/DurableGraph-lab-notebook.md)；重跑独立实验：[Probe 导航](../experiments/README.md)。

四个早期 Probe 已归档到 Git，默认搜索优先 src/tests 与活动回归；只有具体历史机制问题才查
[归档恢复索引](../experiments/ARCHIVE.md)，不要把旧项目整体恢复为续工上下文。

## 当前焦点

[DB-051](../docs/design-branches/0051-bounded-list-delta-competition.md) 已形成经子代理审阅的施工计划，**下一轮实施，当前尚未改代码**：
完整 Local Delta 作基准，停滞时尝试独立有界 Myers，并在竞争编码达到基准长度时停止；计划新增默认 Adaptive，保留三种显式旧算法。
先流式输出 patch 去掉整段临时缓冲，再实现严格长度竞争；主要保证为候选 body 不大于原 Local，细分关卡/所有权/验证见该计划。

[DB-049](../docs/design-branches/0049-list-range-delta-and-matcher-trial-slice.md) 已实现静态 StateEquals、统一 List codec 2，
Position/LocalResync/BoundedMyers 共用 decoder，配置随模型 snapshot 冻结；根构建/tests、相关真实包和独立审查通过。
[ListDeltaReplayProbe](../experiments/ListDeltaReplayProbe/README.md) 的普通落盘、[白盒](../experiments/ListDeltaReplayProbe/WHITEBOX.md)及
[DB-050 回退研究](../experiments/ListDeltaReplayProbe/FALLBACK.md)保留证据：算法互补但搜索成功不保证 bytes 更小。
**当前产品默认仍为 LocalResync**；保存成本与实际字节为主要评价，冷读优化最低优先级。后续项在[路线图](../docs/DurableGraph-research-roadmap.md#32-list-差分算法选型与设计)。
[DB-047](../docs/design-branches/0047-list-content-object-slice.md) 的完整元素闭包、冻结/恢复、List owner Upgrade 与 history v5 继续沿用。

[DB-046 统一闭合目录](../docs/design-branches/0046-unified-schema-catalog-slice.md) 的单批次登记、exact 整数依赖，
以及 [DB-045](../docs/design-branches/0045-persisted-representation-id-slice.md) 的 Base v4 单 ID 边界继续沿用。
开放模板持久化仍按[路线图](../docs/DurableGraph-research-roadmap.md#31-版本化表示类型头的统一寻址)的净简化条件重访。

[DB-043](../docs/design-branches/0043-vector-array-object-slice.md) 已完成统一引用对象路径、可组合数组与数组元素 Upgrade；
[DB-042](../docs/design-branches/0042-upgrade-schema-requirement-set.md) 已完成 plan 级 exact Schema 依赖证书；
[DB-041](../docs/design-branches/0041-object-id-state-representation.md) 的非泛型 ObjectId、DB-039 可组合值 Upgrade
及此前的同实例 GraphSession、泛型/inline Schema/history 能力继续沿用。[DB-040](../docs/design-branches/0040-typed-object-id-representation-research.md)
的泛型目标品牌暂缓。

## 当前能力与实际边界

| 层 | 已验证能力 | 尚未闭合的边界 |
|---|---|---|
| [DurableGraph](DurableGraph/DurableGraph.csproj) | immutable Schema/exact DAG；统一 ObjectBinding、ObjectLayout、Capture/refs/恢复目录；SZ/rank 2–4 数组与 List owned 状态；静态 StateEquals、数组稀疏/列表区间 Delta、三种 List writer；独立 historical reader | 其他 BCL、数组协变；持久发布由 StateStore 拥有 |
| [Generator](DurableGraph.Generator/DurableGraph.Generator.csproj) / [Build](DurableGraph.Build/DurableGraph.Build.csproj) | class/struct 开放模板、readonly DTO/静态 body、Capture/Hydrate、泛型继承与递归数组/List 组合；history v5；三参 Upgrade/旧二参适配、值规则/局部依赖 adapter | 其他 BCL；跨程序集生成规则 |
| [StateStore](DurableGraph.StateStore/DurableGraph.StateStore.csproj) | 统一闭合 Schema/数组/List 目录与整数依赖、单批次登记、Base v4 ID 头；完整 stored/current 引用验证、可达图两阶段恢复；公开 PrepareNew/fixed-Parent Prepare；GraphRepository 单 head/持久 WorldId 与 GraphSession 同实例 Commit；升级 Base/Remove | 无 branch/Reset/根替换或联合 Store 视图 |
| [Storage](DurableGraph.StateStore.Storage/DurableGraph.StateStore.Storage.csproj) | AppendDurably 原 lease 屏障；local Base/Delta records、wire v3、exact Revision live map、Parent/prior 校验、object-first 原始重建链及实际 payload H；Base 精确/Delta 上界计量；真实 Segment/RBF 冷重开 | 不解码 typed body；不拥有持久 roots、类型目录或发布 head；重复读取暂未缓存 |
| [Serialization](DurableGraph.StateStore.Serialization/DurableGraph.StateStore.Serialization.csproj) | 字节原语、string 内容 codec、拥有 raw bytes 的 PreparedBaseBody/PreparedDeltaBody、显式 body 的 typed slot、早期元素 ref 循环 | 完整数组/List 对象操作位于 Runtime；其他 BCL 内容 codec 尚无 |

容易混淆的限制：

- SG DTO/body 支持递归 inline struct 与 13 种标量：bool、byte/sbyte、short/ushort、int/uint、long/ulong、char、Half、float、double；string、受支持 durable class、数组和 List 引用槽保存非泛型 ObjectId；字节层仍编码 UInt32。
  裸 `[DurableType]` 限同编译、顶层、非 record 的 partial class 链或显式 partial struct（包括 readonly），支持泛型；
  支持 readonly 持久字段和没有无参构造器的领域类。RuntimeHelpers 分配、SG Hydrate/声明层 UnsafeAccessor
  不执行实例构造器或字段初始化表达式；Transient 由用户交付后重建。
- SchemaKind 区分 ReferenceObject/InlineValue，同 family 不得跨 kind；inline 字段持有完整 exact Schema，
  版本变化沿 inline/base 传播，nominal 不传播。struct 独立生成 Schema/history，但不登记对象或独立 Upgrade。
  readonly DTO 递归嵌套，引用投影为 ID；子 PrepareDelta 的 HasChanges/bytes 决定父位，置位但子无变化拒绝。
  共享值 DTO/body 按 exact key 生成，不依赖当前领域 struct CLR 宿主；owner Upgrade 通过强类型构造器显式转换，
  删除 struct 后仍可保留完整 owner 升级链。struct Hydrate 从 default 临时值经 ref accessor 填充，
  完成后赋回字段/元素槽，不运行构造器或初始化器，Transient 默认。仍无 record/ref struct/CLR nested type 支持。
- `TypeExpr` 区分 builtin、named 定义及有序实参、声明内 parameter、SZ/rank 2–4 数组和内建 List 构造；持久 key 为闭合 TypeExpr + 定义版本。
  SchemaId 只表示定义 ID，不能用来区分闭合族。base/inline 显式升版仍沿定义传播；Box<int> 也随 Box 定义升版。
  目标仓库内同 key 完整布局严格一致；两个独立空库仍可能首次登记同 key 异形，不提供闭合历史账本或跨库 key 互换保证。
  TypeExpr depth≤64、展开 nodes≤4096、arity≤32，exact 布局 DAG depth≤256。
- 含泛型当前定义/历史、显式 DurableUpgrade 或值规则/依赖属性的编译使用 `Generated.Family_<UTF8HexId>.Vn<TState...>`，
  `Generated.DurableDefinitions.Register` 显式登记生成定义；亦可单独登记 Family.Definition。
  历史 DTO/body 不携带领域泛型参数，phantom 参数不产生状态参数。已知叶子直接调用，未知槽使用 IStateOps/IValueProjection 静态约束调用。
  旧纯非泛型编译保留 Schema/GetSchema/__DurableState；迁入 Family 路径后，内部 DTO 类型引用需改用 Family alias。
- 目录冻结定义/provider，成功闭合在 snapshot 内缓存；current 按 CLR Type，historical 按完整 stored Schema 绑定。
  snapshot 只冻结代码能力，仍观察同一 Repository 内单调积累的 SchemaStore 权威定义。
  同 T 多处 exact 不一致、错误 arity/kind、未知定义及不支持的 CLR 闭合拒绝；nominal 边不递归展开对象 body。
  UpgradePlan 持有展平去重的 exact Schema requirement set；缓存命中在 callback 前统一核对后续已注册 Schema，
  冲突报告 owner endpoint 到具体 base/inline 字段的稳定路径。普通 binding 缓存仍复用同一闭包算法；共享 DAG 不按树重复展开。
- 新 `DurableUpgrade` 方法使用非泛型 static host 中可访问的三参方法；运行时优先闭合 owner 特例，否则选择通用边。
  整条相邻链在该对象首次业务调用前绑定；中间 exact 布局来自显式 DTO 表示、已注册 Schema 或唯一历史推导，缺失则拒绝。
  不使用 latest 补缺，不自动升级 struct，失败不尝试另一业务规则。每对象/相邻边独立 UpgradeContext 含 ObjectId 及完整 Source/TargetObjectLayout；Schema 访问器仅适用于 durable owner。
  已有非泛型二参方法通过三参 adapter 调用，声明工具依赖则必须改为三参。Context 不含对象图读取或 ID 分配。
- `UpgradeDependency` 按 provider 局部 key 与两端声明 ID/FieldId 选择 exact 槽；`ValueUpgradeRuleSet` 显式选择业务规则，
  与定义一起进入同一冻结 snapshot。`GetValueUpgrade<A,B>(key)` 只返回该作用域的预绑定工具，未知 key/错 CLR 类型在调用时拒绝。
  全链声明依赖缺失/歧义/错完整槽在该对象首次 callback 前拒绝；显式候选失败不回退，KeepExact 必须启用且零候选、完整槽相等。
  子工具继承当前 owner 信息，按调用复用相同子计划以保留 DAG 共享，snapshot 不缓存带 ObjectId 的 Context/委托。
  SG `DurableValueUpgrade` 通过定义 ID/两端版本声明同一 inline family 的规则，可组合开放 Pair；仅支持同编译规则 marker。
  Runtime metadata 可明确指定 builtin/引用/闭合 nominal 模式及完整 expected 槽。保留 history 的值规则无需旧领域类型，
  也可在无当前 Durable 声明时生成登记材料；这不授予已删除引用对象族的 current Normalize 能力。
- AddRoot 登记根；BeginCapture(models) 冻结 exact CLR Type/model 目录，CaptureObject 逐边校验声明约束，
  先分配 ID/登记再排队；Seal 用增长队列捕获可达对象，子对象不加入根列表。未知实际派生类型明确拒绝。
  Accept/Discard 只是内存候选协议。ID 单调分配、失败可烧号；退役实例映射清理不回收数字。
  空串 Capture/读取两端统一 Empty，非空 string 保留引用身份。
  现有多根 Capture 是内部能力/机制见证；LoadedWorld 外层入口限定一个固定 World。
- CaptureSession.Prepare 自动使用 Current，完整预检 exact Schema/DTO/稳定 binding 后编码；全部 live Base 提前生成，
  existing class/array/List 调用融合 Delta、existing string 为 unchanged。结果只标识内存 Previous/Candidate，不带磁盘地址。
  重复准备与失败不安装或放弃候选、不烧号；临时 guard 拒绝会话重入。capture-only 登记仍有效，缺 binding 仅 Prepare 拒绝。
  跨 exact layout/DTO/binding 或数组 shape 不匹配拒绝，不自动降级 BaseOnly；StateStore 的 CapturedRevisionPlanner 统一映射结果，调用方仍负责 exact Parent 对应。
- SG RegisterReaders 显式登记一个模型族的全部可用 Vn；StateReaderRegistry 同 binding 实例幂等，
  同 key 另一实例拒绝，读取开始复制固定索引。Schema 日志不包含可执行 reader，完全移除的模型族仍拒绝。
  Runtime typed 循环完成整链后才装箱，字段 body 保持静态绑定；无程序集扫描或一般 TypeCodec。
- RevisionDecoder.Read 读取指定 Revision 全部 live 行，逐对象匹配完整 ObjectLayout 后解码，由 exact reader
  VisitReferences 验证 string/class/array/List 引用；class nominal 约束按 stored Schema 祖先判断，数组与 List 要求 exact nominal 类型。
  晚期失败不返回部分结果，不要求全批 body 零调用。
  DecodedRevision 保留 stored-exact DTO、查询地址及每 ID 唯一 string 实例，关闭 Store 后仍可使用；
  无 roots/领域实例/Upgrade，不是 CaptureSession.Current，不能直接作为已加载的可编辑基线。
- StateModelRegistry 显式登记稳定 SG Model，在操作开始快照 family、exact CLR Type 与 reader 三份索引；
  同 exact CLR Type 的其他模型原子拒绝。传统非泛型入口的可选普通静态 UpgradeStateVnToVnPlus1
  按相邻版本转换完整 leaf DTO，不重复升级祖先。已声明边逐一强类型检查；缺边仅阻止需要该边的 current Load。
  LoadedWorld.Load 先完整 exact 解码、再升级全部 source 行，按 current Schema 重新校验全部引用；
  从所选 exact World 迭代求可达闭包，全部可达 class/array/List 实例分配并登记 string 后才 Hydrate。分配必须 exact、非空、彼此不同，Empty 例外。
  内部仅保留 current 冻结状态比较基线及 source ObjectLayout/完整 membership，升级仍 live 必须 Base。
  不可达 source 仍须解码/归一化/验证，但不要求其 current 类型可以 Allocate；历史 ancestry 不能用 current CLR 反推。
- GraphRepository 独占 publication.rbf、schemas.rbf 和 state/，单 head、单活动 GraphSession；Create 只允许无已发布 head。
  Commit 完成冻结、Schema/表示登记、State AppendDurably、publication Append/flush 后安装原候选；保留 World/child 实例与原分配 cursor。
  下一基线 membership 等于成功候选，升级重写义务清除；移除对象以后重接获新 ID/Base。
  GraphCommitException 区分 NotPublished / Unknown / Published，确定未发布也须检查资源是否 faulted；
  Unknown/发布后安装失败禁止透明重试，dispose/reopen。故障/Dispose 不撤销用户领域修改。
  发布日志 v1 绑定前驱 Revision、新 Revision 和固定 WorldId；严格验证全部日志与被引用内容链/Schema。
  可写重开先确认 Schema，再验证/flush State 文件，最后确认 publication；强制关闭 Segment 自动尾恢复。
  已验证正常关闭、进程中止及确定性故障注入；不保证 OS crash/power loss、目录元数据或完整后缀被外部删除的检测。
- 迁移壳可保留旧族 reader/Upgrade，退出 current World 可达闭包后不分配；仍完整验证 source 行。
  传统生成路径不为完全删除的族生成 reader；Family 路径可按 retained history 生成 state-only exact reader，
  但 editable Load 仍要求每个 source 引用对象族有 current/migration CLR 模型和 Normalize。只承诺读已 Remove 该族的新 Revision 才能删除其恢复能力，
  仍支持旧 Revision 则须保留相应 reader/Normalize。见 DB-036 H1 真实包回归。
- LoadedWorld.PrepareNew 从普通新建图生成无 Parent 的完整冻结计划，返回 WorldId；可持久登记 Schema/表示，
  不追加 State 或执行 State 屏障/发布，不安装基线。单根非空且要求 exact 已登记 CLR 类型。
- LoadedWorld.Prepare 固定 Parent/WorldId，返回 owned StateRevision，成功或失败均释放临时 Capture，
  不推进基线。此低层路径由宿主 Append 后重新 Load；同实例连续保存使用 GraphSession。Schema/表示登记不代表发布。
  Empty 反向映射选择最小 source ID，但基线槽保留旧 ID，首次 Capture 形成真实 Delta/Remove。
  分配从完整 source live max+1 起，只承诺会话内单调；uint 耗尽不阻止已有对象保存。
  恢复的可达 durable 实例身份导入同一捕获会话；child-only 修改不改变 owner ID 槽，
  断开最后根路径后整个循环岛由完整 source − candidate 得到 Remove，旧 Revision 不受影响。
- PrepareDeltaBody 每槽比较一次形成位图，再静态写变化值；结果含 HasChanges 和可复用 raw body，裸 Delta body 大小可直接取长度。
  List 匹配另用静态 StateEquals 试探，仅实际变化的配对调用子 PrepareDelta；浮点按位、ObjectId 按值、inline 逐持久字段比较，忽略 padding。
  策略 D 还须计入对象 envelope，不能直接以裸 body 大小代替。
  PrepareBaseBody 对每版 DTO 复用 WriteBaseBody；全部 live Base 提前准备，决策后复用 bytes，性能优化留待 MVP 后。
  B 为完整 Base payload 精确值，D 仅对未定文件距离按 5 字节上界计量（超额 0..4）；H 仍是原记录实编码。
  ApplyDeltaBodyVn 只处理同 Vn；不证明 prior 身份，之后仍须对完整 DTO 验证引用。
- SchemaStore 借用独占的专用 IRbfFile，`schemas.rbf` 只接受 SCB1 v1 的统一闭合目录批次，拒绝旧 SGB1/RPB1。
  class Schema 记录本身就是对象表示；inline Schema、数组与 List 共用从 2 起连续单调的 UInt32 编号，0 无效、1 固定 string。
  inline 编号只作为元数据依赖，不能用于对象 Base。编号不回收、不跨仓库解释；shape/对象实例不属于表示身份。
  SchemaKey 为派生查询/冲突索引，不单独编码；Count 只统计用户 Schema，含 inline。完整闭包检查不能因已有 ID 而跳过。
  全部输入、完整 base/inline 闭包、名义 kind/arity、ID/单帧容量预检后，一次追加/flush，最后安装全部索引并交付 ID；幂等登记不追加。
  exact base/inline 使用先前节点 ID，按依赖顺序恢复；字段 tag15 引用仍携带 closed Named/Array/List TargetType，
  不绑定目标版本或形成 exact 注册依赖，nominal 自环/互环允许。Schema DAG depth≤256，数组外壳不额外占深度。
  List 使用目录 kind=4、nominal 构造码=8；数组/List 的 exact inline 元素外壳不增加 Schema DAG 深度。
  `.dgschema` 与 manifest 新写 v5，严格读取原 v1–v4 语法并保留已接受 history 的文件与 hash；旧版本拒绝 List 模式。
  字段 tag 1–16 不变，history-only 参数 tag=17；
  nominal 约束改变属于 owner Schema 改变，目标自身升版则不传播 owner 版本。
  严格重放全部帧/CRC；坏尾、tombstone、未知格式拒绝且不自动截断。写入不确定后 faulted，须重开；
  可写非空重开先 flush 再交付，readonly 不确认新屏障。尚无 Schema 分段、联合版本目录或自动修复。
- StateStore 内部 BaseObjectBodyCodec 为 raw Base body 加 v4 类型头：格式版本 + canonical RepresentationId，返回 `EncodedBaseObjectBody`。
  统一经 SchemaStore 取得完整 ObjectLayout；string 固定 ID 可无目录解析。仅支持 v4，每个解码结果都有 RepresentationId，旧格式明确拒绝。
  State 类型头不再编码 SchemaKey/TypeExpr/数组元素描述，描述语法集中在目录一侧；当前 DTO/SG/body/history/Upgrade 合同不变。
  SchemaStore.ResolveReader 使用本次操作的冻结目录，完整匹配布局；不全局缓存另一个 snapshot 的 CLR reader。
  Delta 沿终止 Base 继承 exact 表示，仍为裸 body。TypedObjectVersionReader 在 callbacks 前匹配持久完整布局，逐 body 全消费；string 拒绝 Delta。
  它保留单对象显式入口，与 RevisionDecoder 共用读取规则；不执行 Upgrade。
  Schema/表示登记帧是共享元数据，不摊入对象 B/D/H；Base 的 ID 实际字节宽度计入 B。
- CapturedRevisionPlanner 先核对 Previous/Parent、完整 prior ID 集合及所有 survivor 的完整 Base 布局，
  包括 NoChange；再批量登记全部 current 表示、包装 Base 并调用原 planner。Schema/表示登记可持久生效，
  但该方法不追加 State/发布/Accept，也不证明 DTO 内容与 Parent 一致；合法迁移由受控 LoadedRevisionPlanner 路径产生 BaseOnlyUpdate。
- ObjectRevisionPlanner 只读 exact Parent，校验完整 post-live rows 的新旧分类/prior；对 NoChange/Delta Update
  读取链 H，BaseOnlyUpdate 不读取旧内容链。输出 map Base（无 Parent）或 map Delta（有 Parent）及 Removes。
  typed producer 负责内容/Schema/基线对应；planner 不调用 Append/Accept，结果可作为显式 Parent 的分支追加。
- Storage 的 ObjectHeadMap 与对象内容的 Base/Delta 独立组合。ReadObjectVersionChain 逐条核对
  prior 等于该记录 exact Parent Revision 选定的对象 head，要求 direct local record；Base 截断内容链，H 随之重置。
  Append 只预检直接 edge；完整 map 的 external heads 仍是浅声明，不认证全局实体历史。
  ReadObjectBaseBody 仍只接受 Base head，不回退 parent 补内容；wire v3 拒绝 v1/v2。
  H 含 kind/prior/length/body，不含 ObjectId/membership/共享 Frame；不是总冷读 I/O。
  先直读 RBF，缓存优化留有 [TODO](DurableGraph.StateStore.Storage/StateRevisionStore.cs)。
- 数组对象支持 SZ/rank 2–4 与完整已有槽闭包：标量、string/class ID、inline/generic struct、递归数组。
  FrozenArrayState 拥有 shape 与元素 buffer；当前投影用静态 ref 循环，historical reader 不要求旧领域 struct CLR 类型。
  同布局稀疏 Delta 使用 row-major 索引与子 PrepareDelta；同实例保持 ID，替换/不可达继续遵循会话身份和 Remove 规则。
  数组独立 owner 显式选择元素 Upgrade 规则集，直接 source→current 端点预绑定，不自动搜索相邻规则路径；
  空数组也验证升级能力，共享数组只归一化一次，保持 ID/shape 并强制 Base。详细合同与证据见 DB-043。
  非零下界、非 SZ rank-1、rank > 4、数组协变与 unsupported 元素拒绝；object 内部入口不授予 object/interface 通配槽。
  已知成员的 SG body 静态绑定字节原语；PrimitiveSlotCodecs 只在测试工具中。
- exact BCL List<T> 复用完整槽闭包，可与数组、泛型 class/struct 递归组合；接口字段、List 子类及其他 BCL 容器仍拒绝。
  ListLayout 只含 exact 元素槽和 codec 版本，Count 属于 owned FrozenListState；Capacity 不持久化。
  同实例 resize 保持 ObjectId/表示 ID；Base=count+元素，codec 2 Delta=newCount+Copy/New/CopyAndPatch 区间，旧 codec 1 拒绝。
  Prepare/Apply 保持 prior/candidate 独立；source 可重复/倒序，只读 prior，区间内使用严格局部稀疏子 patch。
  UseListDeltaAlgorithm 选择 Position/LocalResync/BoundedMyers，默认 LocalResync；配置冻结到会话模型 snapshot，不进入布局/格式/表示 ID。
  matcher 不编码，搜索有界、回退按位置配对；整个 NoChange 判定独立于搜索预算。性能选择与保留边界见路线图 §3.2。
  UseListElementUpgrades 独立于数组规则选择，空 List 同样预绑定；每个共享 List 归一化一次，保留 ID/count 并强制 Base。
  UpgradeContext.ListCount 随子工具继承，不复用 ArrayShape；历史 reader 不依赖旧领域 struct CLR 类型。
- Generator 中未注册的 graph operations probe 和 tests 中 logical graph R1–R3b 是机制见证，不能算产品通用图能力。

产品依赖为 StateStore → Runtime + Storage，二者分别复用 Serialization；Storage 另用 RBF substrate。
DurableGraph runtime 也引用 Serialization，单一 runtime PackageReference 可取得传递依赖。

## 按任务定位证据

| 准备修改 | 先查源码/测试，再按需读合同 |
|---|---|
| List 区间 Delta、匹配与配置 | [DB-049](../docs/design-branches/0049-list-range-delta-and-matcher-trial-slice.md)、[matcher](DurableGraph/ListDeltaMatcher.cs)、[reader/body](DurableGraph/ListStateReader.cs)、[重放实验](../experiments/ListDeltaReplayProbe/README.md) |
| List 内容、冻结与历史元素 Upgrade | [DB-047](../docs/design-branches/0047-list-content-object-slice.md)、[当前投影](DurableGraph/ListObjectBinding.cs)、[List 升级](DurableGraph/StateBindingContext.ListUpgrade.cs)、[真实包](../experiments/PackageConsumerProbe/ListConsumer/README.md) |
| 统一闭合目录、持久表示 ID 与 Base 头 | [DB-046](../docs/design-branches/0046-unified-schema-catalog-slice.md)、[目录 codec](DurableGraph.StateStore/SchemaCatalogWireCodec.cs)、[SchemaStore](DurableGraph.StateStore/SchemaStore.cs)、[目录重放测试](../tests/DurableGraph.StateStore.Tests/SchemaCatalogReplayTests.cs)、[表示集成测试](../tests/DurableGraph.StateStore.Tests/RepresentationIntegrationTests.cs) |
| 数组对象、共同引用入口与元素 Upgrade | [DB-043](../docs/design-branches/0043-vector-array-object-slice.md)、[对象布局](DurableGraph/ObjectLayout.cs)、[数组绑定](DurableGraph/ArrayObjectBinding.cs)、[历史 reader](DurableGraph/ArrayStateReader.cs)、[数组 owner Upgrade](DurableGraph/StateBindingContext.ArrayUpgrade.cs) |
| 引用槽与对象身份包装 | [DB-041](../docs/design-branches/0041-object-id-state-representation.md)、[ObjectId](DurableGraph/ObjectId.cs)、[静态引用操作](DurableGraph/BuiltinStateValues.cs) |
| 可组合值 Upgrade / 规则集 / Context 子作用域 | [DB-039](../docs/design-branches/0039-composable-value-upgrade-design.md)、[值绑定](DurableGraph/StateBindingContext.ValueUpgrade.cs)、[SG 属性](DurableGraph.Generator/DurableSchemaGenerator.ValueUpgrades.cs)、[历史包](../experiments/PackageConsumerProbe/ValueUpgradeConsumer/README.md) |
| 泛型/历史绑定/UpgradeContext | [DB-038](../docs/design-branches/0038-generic-schema-state-and-binding-design.md)、[绑定上下文](DurableGraph/StateBindingContext.cs)、[生成模板](DurableGraph.Generator/DurableSchemaGenerator.GenericState.cs)、[三代历史包](../experiments/PackageConsumerProbe/GenericConsumer/README.md) |
| inline struct/嵌套 DTO/Schema DAG | [DB-037](../docs/design-branches/0037-inline-struct-state-slice.md)、[生成值 helper](DurableGraph.Generator/DurableSchemaGenerator.InlineState.cs)、[真实生成图](../tests/DurableGraph.Tests/InlineStructGraphTests.cs)、[历史包](../experiments/PackageConsumerProbe/InlineStructConsumer) |
| 工作会话/发布/历史能力 | [DB-036](../docs/design-branches/0036-working-session-and-history-capabilities.md)、[Repository](DurableGraph.StateStore/GraphRepository.cs)、[集成测试](../tests/DurableGraph.StateStore.Tests/GraphRepositoryTests.cs) |
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
