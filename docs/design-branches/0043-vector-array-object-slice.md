# DB-043：SZ VectorArray 对象纵向分片

> 状态：Proposed — 2026-09-08。本文是下一施工分片计划，不是实施完成声明。
> 当前基线：[PROJECT-STATE](../../src/PROJECT-STATE.md)；MVP 数组边界见
> [目标设计](../DurableGraph-target-design-v0.md#mvp-功能边界)；早期元素循环证据见
> [DB-020](0020-typed-slot-array-binding-slice.md)。

## 1. 为什么下一片选择 VectorArray

当前自定义 class/struct、string、泛型 Schema/history、值 Upgrade、对象图 Capture/Restore、
GraphSession Commit 与持久发布已经闭合。数组是下一项能直接扩大领域模型表达力、同时检验“所有受支持
引用类型共享 ObjectId 和对象目录”的能力。

不先做孤立的 Array TypeExpr 或内建模型目录；它们会先冻结最危险的格式而没有产品消费者。
也不在一片内同时实现 Vector、rank 2–4、所有历史元素升级和 BCL 容器。下一片以一个真实 World 中的
`T[]` 完成 Capture → Commit → reopen → Load → 同实例 second Commit，所需基础设施随纵切一起落地。

最小成功标准：零下界 SZ `T[]` 是有独立 ObjectId 的图对象；候选拥有冻结元素状态；共享、引用环、
Base/Delta、旧 Revision 与冷重开均可观察；所有不支持的数组形状和元素升级在发布前 fail-close。

## 2. 产品范围

本片支持：

- `T[]`，且 actual CLR 类型必须与声明类型完全相同；不接受 CLR 数组协变替代。
- 元素 `T` 为当前已支持的 13 种标量、string 或自定义 durable class；后者可以是明确写出的闭合
  泛型类型，例如 `Node<int>[]`。
- 数组类型在声明处必须 syntactically closed。泛型 owner 可以含固定的 `int[]` 字段，但不支持
  `Holder<T>.Items : T[]`，也不允许普通 Parameter 槽在运行时闭合成 array。
- 数组只能作为 World 图中的引用对象，MVP 的根仍是一个非空 durable World。

本片明确不支持：

- 非 SZ rank-1 `T[*]`、任何非零下界数组，以及 rank 2–4 的执行 codec；
- inline struct 元素、jagged/nested array、数组元素 exact 布局升级、CLR 数组协变、
  interface/object 通配元素、boxed value；
- BCL 容器、Reflection.Emit、通用运行时扫描或新的程序集；
- 数组容量优化、chunking、压缩，以及跨数组的内容去重。

TypeExpr grammar 本片只增加 VectorArray；rank 2–4 不预留公开构造或持久 numeric tag，仍由后继分片在
目标允许的上界 3/4 中结合实际循环与格式证据选择。rank 2+ 必须由 SG 或模型闭合边界明确拒绝。

## 3. 类型与引用语义

### 3.1 Array TypeExpr

数组是一等结构化类型构造，不伪装成 `Named("$Array", ...)`。后者会把内建数组错误解释成拥有用户
definition history 和 SchemaStore 版本的声明族。

建议公开形状为 `TypeExpr.VectorArray(TypeExpr elementType)`；比较、哈希、替换和字符串表示都包含递归
element type。当前产品只接受 closed、非 array 的支持 element。wire 码位为：

```text
1 Builtin
2 Named
3 Parameter（只允许开放内存/template grammar；SchemaBatch/Base 的 closed TypeExpr 仍拒绝）
4 VectorArray
```

5–255 在本片仍未知并拒绝；不提前把它们宣称为 rank 码位。

### 3.2 统一对象引用槽

字段中的自定义 class 与 array 都保存同一种 ObjectId，并由 TargetType 表达目标约束。因此保留 wire 数值
15，同时把源码语义从 `TypeTag.DurableReference` 收敛为 `TypeTag.ObjectReference`：

```text
ObjectReference slot = ObjectId + closed TargetType
TargetType Named(...)  -> 自定义 reference-object family
TargetType VectorArray(...) -> 内建 array object
```

不新增 ArrayReference tag。额外 tag 会与 TargetType 重复表达对象类别，并迫使以后为 List/Dictionary
继续复制引用读写、null、Delta、visitor 与校验分支。string 暂时保留已有专用 tag 和对象 kind；本片不借数组
重构 string 路径。

相应源码术语建议同步为 `ObjectIdStateOps`、`VisitObject(ObjectId, TypeExpr)`；
`DurableFieldInfo.Reference(...)` 已是中性工厂名，可以保留。旧持久 history/schema 中数值 15 的含义兼容；
原型期不保留双枚举名 alias。

## 4. 内建数组布局与格式

数组本体走类似 string 的内建通道，不为每个闭合数组制造伪 DurableSchema，也不在 SchemaStore 登记数组定义。
Base 类型头新增 array object kind，并携带唯一的元素语义来源：

```text
ArrayLayout {
    CodecVersion
    ElementSlotDescriptor // scalar/string tag，或 ObjectReference + Named TargetType
}

Vector Base body {
    Length : canonical UInt32
    ElementBaseBody[Length]
}
```

VectorArray TypeExpr 由 `ElementSlotDescriptor` 唯一派生，不在 Base 中再保存一份可产生冲突的 ClosedArrayType。
引用 element 只保存 nominal Named ObjectReference，不传播目标对象版本。数组 Delta 沿 Base 继承 ArrayLayout。
inline 与 nested-array element 将来用新的 array codec version 扩展，不把未实现语义偷塞进 v1。
layout/binding 必须确认 Named element definition 是 ReferenceObject 且有受支持的 domain model；空数组没有
实际 element 可让后续 visitor 代为发现错误，因此这项检查不能延迟到逐元素验证。

同一 CLR 数组实例的元素 CLR 类型、rank 与各维长度不可变；CaptureSession 按引用相等延续 ObjectId。
fresh、尚未绑定的替换数组会取得新 ObjectId/Base；若替换为会话中已绑定的另一数组实例，则复用其已有 ID。
被替换数组仅在没有其他引用时退出新 Revision。因此同一 ObjectId 的 shape 改变是内部错误或损坏数据，
不是合法 resize Delta。

格式版本推荐：

- SchemaBatch v4：TypeExpr grammar 增加 VectorArray，兼读 v1–v3；无数组的旧数据语义不变。
- `.dgschema` history v4：TypePattern 支持 syntactically closed VectorArray，兼读旧版本；Parameter
  array pattern 仍明确拒绝。
- Base envelope v3：新增 object-kind tag 3 array，兼读 v1/v2；StateRevision wire 不变。
- 数组 Base/Delta codec 自身从版本 1 开始。后续若改变 array body grammar，由 ArrayLayout 的 codec
  version 或新的 Base 断链承载，不能让旧 Base 后的 Delta 改变解释；只要仍承诺读取旧 Revision，
  相应历史 array codec reader 就必须保留。

上述 numeric tag 是 Proposed 选择；接受本文后由 G0 以字段顺序和新旧格式 golden 固化，当前尚未落盘。

## 5. 冻结状态、binding 与对象图

数组不能塞进 `StateModelBinding<TDomain,TState>`：自定义 class 继续保留 `DurableBase + unmanaged DTO` 约束。
数组使用专用、不可变的 managed state wrapper：

```text
FrozenArrayState<TState>
    Length
    private TState[] Elements  // Capture 时逐元素投影后的 owned copy
```

`TState : unmanaged`；元素由既有 `IValueProjection<TDomain,TState>` Capture/Hydrate，body、Delta 与引用遍历
由 `IStateOps<TState>` 完成。运行时按元素 `StateValueBinding` 闭合
`VectorArrayBinding<TDomain,TState,TOps,TProjection>`，只在冷绑定路径使用 Type/MakeGenericType；元素循环中静态调用。
早期 Serialization `ValueSlotCodec/ArrayElementCodec` 可借鉴 ref 循环，但不升级成产品 ABI，也不把
`PrimitiveSlotCodecs` 移回产品。

自定义 class 与数组在对象目录层实现一个最小 internal common binding，统一 Normalize、VisitReferences、
Allocate、Hydrate 与 Capture；不为此改变公开 `StateModelBinding` 的强类型表面。ObjectReadTable 的内部目录
扩为 ObjectId→object，生成路径使用类型化的对象解析方法。

Capture 与 Load 顺序保持现有图不变量：

1. 每条数组引用边先校验 declared/actual exact array type，再查询是否已经 intern；数组第一次出现时
   按引用相等分配 ObjectId、登记并入队，随后遍历元素；
2. Capture 将每个元素投影到 frozen state，之后修改领域数组不能改变候选；
3. Load 完成全部 class/array 状态解码与当前验证；
4. 从 World 求 current 引用闭包，为所有 class 和 array 先分配 exact CLR 实例；
5. 再 Hydrate 字段和元素，因此共享数组及 `Node → Node[] → Node` 环可以恢复；
6. 成功发布后安装原候选和包括数组实例的 identity map，下一次 Commit 延续同一 ObjectId。

数组不是 SchemaStore 用户类型；array binding 必须验证 ArrayLayout 与 element binding。source provenance
使用 String / exact DurableSchema / exact ArrayLayout 的判别表示（或等价结构），不能用伪 DurableSchema、
只有 ObjectStateKind，或 current binding 代替 Parent 中数组的 exact layout。

读取 Length 后、分配 frozen/domain array 前，同时验证 Length 不超过 CLR vector limit，所有长度、字节数和
分配尺寸计算使用 checked 运算，并结合 remaining payload 与 element 最小 canonical 编码尺寸拒绝明显不可能的声明；
极小 payload 声明巨型数组必须在分配前拒绝。Frame 容量不等于 CLR allocation 上限；通用可配置加载内存预算
留给独立资源治理分片，本片不虚构一个 256MB 内存合同。

## 6. 融合 Delta

Vector Delta 使用升序稀疏流：

```text
(linearIndex + 1), ElementDeltaBody
(linearIndex + 1), ElementDeltaBody
...
0  // terminator
```

索引使用 canonical UInt32。Prepare 对每个元素恰调用一次 `TOps.PrepareDelta`；有变化时直接追加 index token
和已准备 element delta。因此同一结果回答 HasChanges、D 与 payload。密集变化产生的大 Delta 交给现有
Base/Delta policy 选择 Base，不另设数组策略。

Apply 克隆 prior frozen elements 后修改，不能污染已接受基线；index 必须严格递增且小于 Length，缺 terminator、
重复/逆序/越界、坏 element body 和尾随数据均拒绝。element codec 按其 exact slot 自定界消费。
shape 不进入 Delta；相同 ID 的 prior/current Length 或 ArrayLayout 不同必须在 Prepare 前拒绝。

未来加入 inline element 后，其 stored/current exact layout 变化不能接 Delta；显式数组元素 Upgrade 成功后，
数组对象须标记 RequiresRewrite，下一次保存强制 Base。

## 7. 施工依赖与分工

```text
G0 TypeExpr/TypePattern + wire/history goldens
  ↓
G1 ObjectReference 术语迁移 + internal object-model seam
  ↓
G2 Vector frozen state/binding + Base/Delta/reference traversal
  ↓
G3 SG field/history + Capture/Normalize/Allocate/Hydrate + StateStore envelope
  ↓
G4 GraphSession/package consumer end-to-end acceptance
```

- G0 冻结格式后再允许并行修改 Runtime 与 Generator；所有 old-format readers/goldens先保留。
- G1 只抽取 array 与现有 class 都实际使用的 internal seam，不建设通用 BCL codec registry。
- G2 先以直接 runtime binding 验证 scalar/string/object-reference Vector，再接 SG。
- G3 串起完整 Revision decode、Schema dependency registration、normalization、两阶段 materialization 与 planning。
- G4 使用真实生成代码和 NuGet package；不能用手写 binding 或 ProjectReference 代替交付证据。

若施工容量只能停在 G0–G2，不得将其称为产品数组支持，也不更新 PROJECT-STATE 的已验证能力；保留为
未完成分支继续 G3–G4。本片完成门槛是整个纵向闭环。

## 8. 验收矩阵

- 类型/格式：VectorArray TypeExpr canonical equality/order/string/wire；SchemaBatch/history/Base 新旧版本 golden；
  旧 v1–v3 tag 15 Named reference 继续读取；截断/未知 TypeExpr node、object kind、array codec version、
  非法 element descriptor、Named→InlineValue、深度与 node 上限拒绝，空数组也执行 layout/binding 校验。
- 生成：普通 closed array 字段及明确的闭合泛型 class element；`T[]` Parameter pattern、Parameter 闭合为 array、
  inline/jagged、`T[*]`、rank 2+、数组协变及不支持 element 明确诊断或在闭合边界拒绝，失败不留下 partial binding/cache/Schema registration。
- 冻结：Seal 后修改原数组或引用元素槽不改变 candidate/Base/Delta；空数组及内容相等的非空数组仍各自保留引用身份，不像 empty string 合并。
- 图：两个字段共享同一数组；重复元素共享目标；`World → Node[] → Node → World` 完整验证并在两阶段恢复后保持引用相等。
- Delta：无变化 terminator、首/中/末及多元素稀疏变化、浮点位语义、string/object IDs；
  Apply(Base, Delta) 等于 current Base，坏 index/order/terminator/body/tail 拒绝且不修改 prior。
- 持久：Create/Commit/close/reopen/Load；修改同一数组元素后 second Commit 保持 array ObjectId，旧 Revision 仍读旧内容；
  第二次无变化 Commit 不产生伪 array update；fresh/unbound replacement 产生新 ID/Base，被替换数组仅在无其他引用时 Remove；
  已绑定的另一数组实例仍复用其已有 ID；策略选择的 Base/Delta 均可冷读。
- 恶意输入：巨型 Length + 极小 body、截断 element、尾随 bytes 在分配/交付前拒绝；数组协变在 Capture
  与 Load 两端拒绝；坏 Delta 不修改 prior frozen state。失败不发布 Revision，仓库 head 不推进。
- 包：真实 PackageReference consumer 覆盖生成、history publish/verify、持久冷重开与连续 Commit。
- 根 build/full tests、持久格式 golden、文档链接及独立 correctness/format review 全部通过。

## 9. 后继分片

1. Syntactically open `T[]` 与 Parameter 闭合为 array，以及 jagged/nested Vector 组合。
2. Inline struct element 与数组内建 owner 的显式 element Upgrade 规则：解决共享数组不能借 owner-local rule
   的选择问题；逐元素升级后强制 Base。
3. Rank2/Rank3 及可选 Rank4 executable bindings：届时选择上界，零下界、各维 UInt32 长度、CLR
   row-major/末维最快，复用线性 Delta。非零下界与 `T[*]` 永久 fail-close。
4. 结合实际 payload 测量再考虑 dense bitmap/range Delta、large-array chunking 或分段对象；不影响首版正确性。
5. BCL 容器逐类型定义内容、顺序、comparer 与重建时机，复用已验证的 object-reference/type-constructor seam。
