# DB-064：两份 Revision 合并读取与安全共享边界

> 状态：Proposed，2026-09-11；本轮为设计审阅，未实施。
> 与 [DB-062](0062-independent-graph-workspace-slice.md) 共用读取核心，可与 [DB-063](0063-event-history-journal-slice.md) 独立排期。
> 第一阶段缓存 DTO/string；普通领域对象的只读共享为可单独验收的第二阶段，不是 EventHistory 首次可用的前置条件。

## 1. 要修正的判据

用户提出：一次读取两份 Revision，尽可能共用相同版本的对象，并保持不同版本的领域快照独立。
目标合理，但现有 ObjectVersion 仅确定自身 DTO，不包含所有引用目标的版本：

```text
R1: A(head=a1).Next = ObjectId(2)     B(id=2, head=b1).Value = 10
R2: A(head=a1).Next = ObjectId(2)     B(id=2, head=b2).Value = 20
```

A 自己的字段没变，两个 Revision 复用 a1 完全正确；但已 Hydrate 的一个 A 只有一个 Next，
不能同时指向 B 的两个版本。即使 A 的 Next 字段是 readonly，问题仍存在。
引用版本是 Revision 的解释结果；不应为简化加载而给每个祖先重写 ObjectVersion，破坏现有增量语义。

这不是新假设：[GraphRepositoryTests](../../tests/DurableGraph.StateStore.Tests/GraphRepositoryTests.cs)
的 `ThreeCommitsRetainInstancesAndAdvanceExactParentThenReopenFromPublishedWorldId` 已写出只 child 变化、root 不写且有循环的见证；
[LoadedReferenceWorldTests](../../tests/DurableGraph.StateStore.Tests/LoadedReferenceWorldTests.cs)
检验未变 owner 的引用仍按当前 Revision 验证。

因此推荐的合同为：

- 同 exact ObjectVersion 可以共用 stored DTO 解码结果。
- 不同 ObjectVersion 不因内容相等而合并实例。
- 同 ObjectVersion 的普通 CLR 实例只有在完整引用闭包也可共享时才合并。
- 一侧可编辑时，不因初始值相同就共享会被修改的对象；默认只共享 DTO 和 string。
- Empty 继续沿全库既有 string.Empty 例外；不同 ID 的等值非空 string 不做值 intern。

## 2. 阶段 A：操作内共享 stored DTO 与 string

读取一次两份明确 `(RevisionAddress, RootId)` 的图，固定同一资源所有者与模型/reader snapshot。
复用 key 是 `(repository scope, ObjectId, head FrameAddress)`：
Frame 是多个对象的共同容器，只有 FrameAddress 不够；只有 ObjectId 也不够。
同一读取操作中 repository scope 可由所有者封闭，不必持久化一个新全局 ID。

为每份视图分别物化 ObjectHeadMap；按 exact head 查缓存。
首次命中前完成原始链读取、Base 表示/reader 匹配及 body 全消费，缓存拥有独立内存的 ObjectStateRecord。
不在缓存中保存需要文件 lease 继续存活的 span/buffer，不跨仓库或不同 reader snapshot 搬用绑定结果。

先只缓存 stored DTO，不缓存 NormalizedRevision、业务 Upgrade 的结果、领域实例表或 Capture 绑定。
每份视图仍独立执行：

1. stored 引用、名义类型与 Dictionary lookup 的完整验证；
2. 单对象 Upgrade 及现有实时 Schema dependency closure 复核；
3. current DTO 的引用/lookup 验证；
4. 按根求可达集合及两阶段 Allocate/Hydrate。

缓存命中不携带“已在另一个 Revision 验证通过”的通行证。
同 DTO 在一边可合法、另一边可悬空或目标类型不匹配，后者仍须拒绝。
升级可能删边，source live 验证与 current reachable 仍保持区别；不因为共享而绕过不可达历史行的原校验。

string DTO 自身即不可变 string；保留同 key 首次解码实例即可获得安全共享，
两份 graph 的可变 class/array/List/Dictionary 默认分开 Allocate。
可写 Resume 只导入 State 那份 baseline/ID 表，Event 从不变成它的可变别名。

缓存只活到本次读取结束，输出图持有其所需实例；无全局 weak cache、LRU、池或对象永久驻留。
这是“同版本解码一次”的直接收益，不承诺两份图所有相等内容都去重，也不承诺每个 RBF Frame 只读一次。

## 3. 阶段 B：显式只读双图的闭包共享

这一阶段有清晰可行的线性算法，不需要解决一般图同构；但必须另选公开的只读共享合同。
不自动用于会继续修改的 State，也不由 readonly 字段、record 或 IReadOnlyList 推断深不可变。

建议一次性 `ReadPair(..., sharing: ReadOnlyClosure)`（名称示意），调用方将两图全部视为只读。
框架不自动冻结普通 CLR 对象；修改已共享对象会在另一视图可见，不能再将其声明为隔离编辑图。
该调用结果不暴露接受为 GraphSession 的入口；未来深不可变类型白名单若有需求另定。

### 共享集合

分别按阶段 A 得到 validated current DTO 与可达集，先形成候选 C：

- 两边都可达，ObjectId 与真实 head 相同；
- 同一冻结代码目录，current model/完整 layout 匹配；
- 两边完整 current DTO 状态相同，包括容器 comparer metadata；初版可复用各自 preparation 生成 current Base body，
  逐字节相同作为充分条件，只比较这一对临时结果而不写盘。不同字节可以保守不共享，不做 hash-only 判断。
  这会多一次编码/缓冲成本；待实际测量再改为已有完整 StateEquals 的内部适配，不为首片新增公开接口；
- 首版保守要求未执行布局升级（两边 RequiresRewrite 都为 false）。升级后即便偶然相等也分别分配，
  避免为共享增加第二套跨版本状态等价判定或业务回调执行次数约定。

`RequiresRewrite` 仅由 source/current layout 差异决定，不证明 Normalize 没改值。
现有手工 StateModelBinding 可提供自己的 Normalize；不得仅凭该标志跳过上面的完整 current 状态核对。
string 单独按阶段 A 的同 key/同实例条件处理；不能取得完整状态比较能力的 binding 保守不共享。

对每个候选访问 current DTO 引用边；null 不产生边。任一目标不在 C，就将该候选标为不能共享。
构建反向引用表，把“不能共享”向所有引用者传播，直到队列为空。
剩余是这些保守候选中的最大引用闭合集：稳定循环可整体保留；一个节点分裂会传播到所有依赖它的祖先/环。
图剔除本身的时间与临时索引空间为 O(V+E)，V/E 取两份已加载图中的节点/引用数；
前述 current 状态比较另计编码字节量与临时 body 成本，不将总加载工作虚报为纯图算法成本。

然后先分配所有共享与各视图独立实例，再分别建立 ObjectId→实例表；共享节点只 Hydrate 一次。
其全部非空目标均已证明共享，任取一份对应表填充即可；独立 owner 可引用共享 child。
失败不交付任何部分图。数组元素、List/Dictionary 内容和嵌套 struct/Nullable 中的引用一律通过现有 VisitReferences 纳入。

暂不对一对多视图做一般分区细化，不进行内容 hash 合并，不为提高命中率追加任意自定义“immutable”回调。
对于 source/current 相同但布局刚升级的对象，允许漏掉共享机会，不能牺牲正确性。

## 4. 可观测验收

| 场景 | 必须结果 |
|---|---|
| 两份图复用相同 `(ID,head)` | 阶段 A exact body 解码一次；每视图的引用校验依然执行 |
| 同一个 Frame 的两个对象 | 两份缓存行，不得因 Frame 相同互相覆盖 |
| 同 ID、不同 head，包括同值的重写 Base | 不合并；不能用 layout/内容相等代替版本身份 |
| 相同非空 string 的同版本 | 两图 ReferenceEquals；另一个 ID 的等值非空串保持不同，Empty 沿现有规则 |
| owner head 未变、child head 改变 | owner/child 在两图中各自指向正确版本，不能共享 owner |
| 稳定环与含变化节点的环 | 阶段 B 稳定环共享；变化沿反向边传播后正确拆开，无无限递归 |
| DTO 命中但第二视图引用悬空/错 nominal | 第二视图失败，不暴露第一视图作为成功 pair |
| Upgrade 改边、current 删除旧 child | 各视图保持完整 source 验证；首版升级节点不进入 CLR 共享集合 |
| Resume 后修改 State child/List | 已加载 Event 旧值不变；默认模式不得共用可变 child/List |
| paired Resume 后 State 未改立即保存 | 不因 string 缓存引入新 ID、引用槽变化或额外 string Base；DTO.StringContent、领域实例与绑定表使用同一个字符串 |
| 同布局手工 Normalize 产生不同 current 值 | RequiresRewrite=false 也不能共享 CLR 实例；完整 current 状态比较排除此候选并向引用者传播 |
| 字典 key/value、数组元素、Nullable/struct 引用 | 访问所有实际引用；不能只沿 durable class 的直接字段判断闭包 |

阶段 A/B 各自记录 decoded row 数、Allocate 次数与 retained instance 数；wall time、分配与峰值只在实际样例中报告。
独立 Event 读取的验收仍由 DB-063 保持；不为利用缓存先加载无关 State。
真实包用从 DB-062 快照复用的真实 heads 验证 E/S，另用相邻 StateRevision 验证 changed-child 反例。

## 5. 推荐排期与尚待选择

1. 先完成 DB-062/063 的独立外观，满足消费者正确性和可用性；按其候选保存方案，可共享磁盘中真实未变的 ObjectVersion。
2. 阶段 A 可紧接并入 Resume 的内部 paired read，低风险地减少 DTO/string 重复读取和内存。
3. 阶段 B 算法可施工，但只读共享 API 是否应立刻交付，取决于用户是否需要同时浏览大量不可变子图；
   不能把其性能价值与可写会话隔离混成一个默认模式。

本片不要求更换引用 wire、生成每个对象的传递版本、历史新 ID 分配器或通用冻结框架。
从 `(ObjectId,head)` 到“完整引用环境”的区别属于现有语义本身；不能通过改名 ObjectVersion 消除。
