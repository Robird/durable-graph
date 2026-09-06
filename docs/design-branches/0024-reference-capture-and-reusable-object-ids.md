# DB-024：引用 Capture、revision 内身份与可复用 ObjectId

> 状态：首片方向已确认 / 实施接缝待评审 — 2026-09-06；产品代码基线 `0b9652c`。
>
> 用户已认可分析，并明确首片改用单调递增 ID、延期回收；本轮要求先形成文档，不启动实施。
> 承接 DB-018/022/023；本文区分用户已明确的语义与尚待讨论的实施建议。

## 1. 已明确与本轮建议

- 用户明确 ObjectId 的解释经过 StateRevision，允许回收后复用。不能继续把“全仓库永不复用”当作必要不变量。
- 已接受的统一引用身份与 DTO 捕获方向保持：string 也按 ReferenceEquals 区分；引用字段存 ID，
  对象内容进入独立条目；完成 Capture 后，比较/估算/编码消费同一个冻结候选。
- 本文建议先做内存中 string 引用 Capture 与对象列表，然后单独闭合加载引用保真，
  最后接真实 StateStore 发布。首片不用回收池；具体 API 与后续 string 独立分配仍需核验。

## 1.1 首片初始目标（本次用户确认后的收窄）

范围是“标量 + string 的领域 roots → ID 化 Versioned DTO → 封闭候选”，
以及单会话、单在途候选的 accept/discard 内存见证。string 作为独立内容条目，引用槽不 inline 内容。
不在首片实现字符串对象解码/领域 Restore、Durable 对象互引/循环、ID 回收池或完整 Save。
实施交接见 [工作单](../WORK-ORDER-REFERENCE-CAPTURE.md)；[Goal 草稿](../GOAL-REFERENCE-CAPTURE.md)尚未启动。

- 一个 CaptureSession 从非零 uint 域单调分配；0 表示 null。分配过的号在该 session 内不再发放。
  实施建议：失败/discard 允许消耗号码，高水位不回退；这免去首片的号段回滚与回收状态。
- parent 的有效实例绑定只用于保持相邻已接受图中持续存活对象的 ID；新绑定由候选持有。
  Capture 出错或 discard 不改变 parent；accept 安装原候选 DTO，不能重新读取已经变化的领域对象。
- 仍计算完整 live 集合及 `parentLive - candidateLive`，accept 后去掉退役实例映射，避免会话无限持有领域对象。
  清理映射不等于回收数字：对象重新入图会取得更大的新号。
- 首片从空会话开始，不承诺跨进程/跨 session 单调，不增加持久 high-water mark；
  不用 `max(live IDs)+1` 冒充全历史分配器。恢复计数器属于后续存储接入。
- 在 uint 域耗尽时明确失败，不回绕为 0 或旧号；用 near-limit 测试接缝验证，不分配数十亿对象。
- string Capture 可保留原不可变实例，null/空串/孤立代理项都不需要内容解码。
  多个独立空串的恢复问题因此不会阻塞本片，但不宣布它已经解决。

下文第 4 节及第 5 节的数字回收/预留解除策略均为延期素材，不是本片实现要求。

## 2. ObjectId 的作用域

持久查找的完整含义为 `(Store, exact StateRevision, ObjectId)`；候选阶段用所属 CapturedGraph 表示上下文。
同一持续存活对象在相邻保存中保留数字 ID，以支持不变对象/引用的复用；这不意味着裸 uint 是全历史实体身份。
同一 revision 内一个非零 ID 只有一个对象，一个 CLR 实例只有一个 ID；0 表示 null，无独立 null 条目。
不同 ID 的条目不能在该图的恢复过程中合并成一个 CLR 实例，即使内容相等。

例如 R10 的 7 号为旧字符串，R11 移除 7，R12 的 7 号为新字符串。
R10 和 R12 的查询分别经自己的 ObjectHeadMap，保留 R10 不妨碍 R12 复用数字 7。
分支或同时打开的旧 revision 使用各自的工作映射；跨 revision 的裸 ID 相等不能证明对象相同。
业务若需要脱离 revision 的长期实体键，使用业务字段或另行定义的句柄，不能暗用 ObjectId。

**引用解析上下文是所加载的目标 revision，不是 owner payload 最初写入的 revision。**
旧 owner body 被新 revision 沿用时，其中的引用号也经新 revision 的 live map 解析；
这样子对象更新不要求 owner 重写。反过来读取旧 revision 必须完整使用旧上下文。
缓存 decoded graph 必须带 revision/view；原始 DTO/字节缓存可以按 exact record 地址和布局缓存，
但不能把已解析的 CLR 引用跨 view 当成同一份结果。Frame 可能含多个对象，record key 还须含对象定位信息。

## 3. Capture 的目标形状

每个工作视图拥有 parent 基线，以及按 ReferenceEqualityComparer 建立的 `object -> uint` 映射。
首次打开/加载时由同一 materialization 的 ID→实例表建立反向映射；不用对象自身字段承载 ID，
也不建立进程全局跨 Store、跨 revision 的字符串池。

一次候选 Capture：

1. 调用方在 Capture 期间提供稳定的领域视图；首片串行、一个在途候选。
2. 从显式 roots 登记对象。先确定其 ID 并登记，再将未处理对象入队；重复引用只返回相同 ID。
3. 处理对象时调用实际类型对应的生成 Capture，复制标量，并将引用成员交给同一上下文登记。
   发现与字段复制可以合并；不在 Write 阶段再次扫描领域对象。
4. string 是无出边的叶对象：独立条目保存 string 内容，可以保留原 string 实例作为不可变内容，
   不需要再复制其字符数组。相等内容的不同实例仍有不同条目，不作值 intern。
5. 队列排空后封闭候选：roots、ID→条目、完整 live 集合、候选新增绑定与待移除集合。
   可按 ID 排序供下游消费，但列表下标不成为 ObjectId，分配顺序不声明图同构 canonicalization。
6. 后续只读取候选。Write 直接编码 DTO 中的 ID；不需要 Type 查表或回头查询领域引用。

生成式形状草图（仅示意，不是现有 API）：

```csharp
// Name 对应 nominal String 字段；DTO 里是 uint 引用槽位。
internal static VCurrent Capture(Character value, CaptureContext context) =>
    new(value.Level, context.CaptureString(value.Name));

// DTO body 不必接收图上下文：对象查找已经在 Capture 完成。
internal static void Write(ref BinaryPayloadWriter writer, in VCurrent state) {
    writer.WriteInt32(state.Level);
    writer.WriteUInt32(state.NameId);
}
```

Vn 的物理 uint 字段不改变声明 Schema 中 String 的类型约束。
ReadVn 先还原 ID 值；对象存在性/声明类型相容性由外层图验证与 Restore 处理。
继承的 Capture 共享上下文；以后自定义 struct 的 Capture 递归接收相同上下文，输出嵌套 DTO。
成员级仍静态绑定；混合类型的对象列表可在对象边界暂存 boxed readonly DTO，不因此引入逐字段动态分派。
具体条目容器待首个消费者收敛，不先冻结通用 registry/interface。

例如两字段引用同一个 `s1`，第三字段引用内容相等但不同实例的 `s2`：

```text
roots: [1]
1 -> CharacterDTO { NameId=2, AliasId=2, CaptionId=3, OptionalId=0 }
2 -> StringState { Content="Ada" }   // 对应 s1
3 -> StringState { Content="Ada" }   // 对应 s2，不能与 2 合并
```

条目至少携带明确的内存 kind（String 或 Durable + exact Schema）和内容。
它对应未来对象头 TypeCodec 的职责，但首片不冻结数组/泛型 TypeCodec，也不假造完整磁盘图格式。

## 4. 回收与复用时机：延期素材，首片不实施

以下是用户决定延期回收之前的候选方案，待重新排期时复审。若实施回收，可把 parent 的所有 live IDs 暂时保留；新对象只从 parent 已空闲且未被本候选预留的 ID 中分配。
本轮发现的 Removes 在发布成功后才释放，下一个候选可以立即复用；不会等待全部历史 revision 被删除。
首个内存分片只显式模拟 accept/discard，不将其称为实际 durable commit。

优点是可一遍 Capture 并写入最终 ID，parent 基线无需提前修改。
代价是构建候选时需要容纳 parent live 与新增对象的并集；ID 域已满时，哪怕本轮删除很多对象也可能分配失败。
该边界必须明确报错；不能整数回绕或偷偷抢占尚未完成可达性判断的 ID。

同一次候选内回收并复用也是正确的可选方案，并非原则上禁止。需要先确定哪些 parent 对象仍可达，
再把已死对象的号分配给新对象（例如引用发现/标记一遍，再 Capture），或使用临时身份再重定位 DTO。
否则边遍历边抢旧 ID 可能抢走尚未访问到的存活对象；同号新旧对象的分类也不能只看 parent.ContainsKey(id)。
在出现接近容量上限的消费者前，建议避免首片承担这层中间状态。

无论何时复用，新的占用者都是 Insert，必须写自己的 Base，不能延续该号上一占用者的 Delta 链或冷读统计。
即使 Schema/内容恰好相同，也不能只凭 ID 和字节相等认作上一对象的 NoChange。
未来 lineage 若按 ID 向前寻找，必须在本次新建边界停下；ID 数字相等不足以跨空档连接历史。

当前 Storage 的 Local/Removed 集合要求互斥，不能编码同一 revision 的“Remove 7 + Insert 7”双动作；
若将来选择同候选复用，最终 membership 可以只表现为新 Base 对 7 的覆盖，但上层必须保留新占用者的事实。
推荐的隔发布复用无需改变当前 Storage 合同；这不是产品已验证完整复用/lineage 的声明。

## 5. 三种回收与失败边界

| 层面 | 判断/动作 | 与 ID 复用的关系 |
|---|---|---|
| 领域持久图可达性 | 从 durable roots 标记，`parentLive - candidateLive` 得到 Removes | 决定新 revision 不再包含哪些对象；不用 CLR GC 结果决定 |
| 内存 ID/对象槽位回收 | 成功安装候选后释放旧映射/槽位，后续分配复用 | 可以在旧 revision 仍保留时完成 |
| 磁盘历史/物理 GC | 检查保留 revisions 及其重建依赖后才能删除 records/segments | 与数字 ID 空闲分开；不在当前分片实施 |

只有 root 所属持久图的可达性参与标记；side table 为管理而持有的强引用不算领域 root。
发布成功后移除不可达对象的反向映射，释放持有的领域实例；旧基线若被显式保留，拥有自己的视图/映射。
应用仍持有已退役 CLR 实例，之后重新挂入根图时，它按当前会话中的未登记对象处理，不能取回已分配给别人的旧号。
如果业务希望离图期间仍保留身份，需要显式 pin/root 语义，属于另一个产品合同。

候选新增映射/预留使用候选所有权，未成功发布前不删除 parent 绑定。
明确失败或放弃：丢弃候选、解除本候选预留，parent 不变。
成功：安装实际提交的冻结候选与对应映射，然后释放旧槽位；不能重新 Capture 领域对象替代提交内容。
发布结果不确定：保留/封锁相关预留，reopen/reconcile 后再裁决，不能当作失败立即复用。

首片不向应用暴露可跨安装继续使用的裸池 index/ref；需要的读取总通过指定 view。
如未来要提供脱离 revision 的会话 handle，再选择 scope token/generation 与失效合同；
不必因此把 generation 预先塞进持久 uint ObjectId。

## 6. StateJournal 素材评估

已读取相邻仓库 `atelia/src/StateJournal/Pools/` 的 SlabBitmap、部分 Impl、SlotPool、SlotHandle，
并检查 GcPool 的组合形状；本轮未构建/运行该仓库，也未复制或修改其代码。

- SlabBitmap 使用 4096-bit slab、word/slab 摘要、位计数及集合/枚举操作，可作为 live/free/marked 集合的机制素材。
- SlotPool 以 free bitmap 定位较小空闲槽；value slab 按需分配，全空时释放；支持按既有 handle/value 重建。
  可借鉴低号优先、占用与内容分离、释放引用和重建的机制。
- 它不是整个引用身份屏障：仍需要 ReferenceEqualityComparer 的反向对象映射、领域引用遍历及候选隔离。
- 当前 SlotHandle 是 24-bit index + 8-bit generation，SlotPool 容量上限约 16M，generation 自然回绕；
  这不是全部非零 uint ObjectId 域，也不是无限期 stale-handle 保证，不宜原样定为持久格式。
  SlabBitmap 自身使用 int capacity/index，若用于 uint 域，还需单独校验可寻址范围。
- GcPool 的文档合同是 BeginMark 与 Sweep 间不 Store；与本设计“Capture 时发现新对象并分配”不直接吻合。
  可借鉴其位图求差/回收机制，不能未经改造直接调用其生命周期。
- 其 MoveSlot/compaction 会移动 index；DurableGraph 的活对象 ID 不应随内部槽位压实静默改变。
  本片不需要迁入 compaction、回滚日志和通用池框架，也不增加对 StateJournal 整个项目的依赖。

## 7. 实施候选与验收问题

建议分成有依赖的两个小片，而非在 string 首片同时实现所有 Save/Load：

1. **引用 Capture 与候选生命周期**：已支持标量的领域 roots + string 字段、继承 Capture 共享上下文；
   产出闭合的混合对象列表与 ID DTO。内存 parent/accept/discard 见证稳定 ID、移除、单调分配和失败隔离。
   首片尚无 Durable 相互引用；分配后入队的骨架将来可扩展到自环/互环，不宣称已支持。
2. **字符串对象编码和引用恢复**：引用槽写非零 uint/0，字符串记录写内容；
   为各条目建立加载表再解析 owner DTO，验证存在性/类型/共享。随后才接 Durable 对象壳与循环恢复。
   先明确恢复到 resolved witness 还是完整领域 Restore；不偷偷把 DTO Upgrade/构造规则并入本片。

必需见证：同实例多引用、相等内容不同实例、null/空串/代理项、Capture 后领域引用变更不影响候选；
跨 Capture 存活对象保持 ID；旧实例重新挂入、discard 重试取得新号且不串号；uint 耗尽明确失败。
R10→移除→R12 复用且旧 R10 可读属于回收分片；missing ID/错误引用类型的载入拒绝属于恢复分片。
完整磁盘保存不在这些内存见证的结论范围内。

string 恢复有一个已有缺口：StringPayloadCodec 的空内容路径直接返回 string.Empty，属于内容 codec，
不能直接证明多个独立空串对象的引用保真。实施恢复前应做实际分配见证；若输入存在不同空串实例，
应选能独立分配的机制或明确拒绝该输入，不能无声合并。Capture 自身可精确保留这些引用关系。

首片不提供 pin：accept 后离开 durable roots 的实例绑定结束，数字不回收。恢复完整领域对象另片推进。
隔发布/同候选复用与独立空串分配仍留待后续；当前需评审的公开生成代码接缝见工作单 G0。

## 8. 自定义 struct TODO：可独立排期，共享 Capture 接缝

用户明确：复合值类型嵌套布局，struct 的持久版本变化算作 owner Schema 变化，要求 owner 递增版本。
建议先记入近期 TODO，string Capture 不依赖 struct 先完成；但两片也不是完全零交互：

- 字段描述要从 FieldId+TypeTag 扩为可表达 `InlineValue(exact StructSchema)` 的形状；history 保存 exact 依赖，
  runtime equality/登记与两端历史校验都检查闭包。不能只给 struct 追加一个不带参数的 tag。
- `PositionV2 -> CharacterV4 -> 派生/外层容器值布局` 按 inline/base exact 边传播；
  nominal 引用边不传播目标版本，避免经对象互引构造无限 exact Schema 闭包。
- SG 为 struct 生成 readonly Versioned DTO；owner DTO 嵌套该版本 DTO，按字段顺序嵌套编码，
  不进引用对象表、不分配 ObjectId。历史 owner 不依赖当前 struct CLR 定义/名字仍然存在。
- 含引用 struct 的领域值直接浅拷贝不够：递归 Capture 标量/嵌套 DTO，引用转 ID，使用同一 CaptureContext。
  先做纯标量 struct 时可无上下文；引入引用后才添加所需参数，不预建空框架。
- ref accessor 仍用于真实 struct 字段和数组元素槽位；Write/Read 面向 DTO，未来 Restore 才写回领域 ref。
  明确 readonly struct、构造/填充边界，重审 legacy DG0011 限制，不能照搬 class 直接赋值规则。
- 首片建议非泛型 partial struct + 标量/已支持嵌套值，泛型、boxed struct 身份和数组容器另排。
  验收包含两层嵌套、FieldId 排序、历史 struct 消失、版本未传播拒绝及引用字段后续组合。

排期理由：先确定引用槽的“领域引用 -> ID DTO”边界，后续 struct 就能复用它；
值 Schema 扩充是独立 metadata 工作，不需要为了开始 string 而提前完成。
