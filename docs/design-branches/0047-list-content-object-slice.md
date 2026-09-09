# DB-047：List<T> 内容对象

> 状态：**Proposed / 待采纳，尚未实施**，2026-09-09。
> 问题：在既有统一对象路径上，能否完整保存、恢复和升级一个可变长度的 BCL 内容对象，而不依赖其内部字段布局？
> 最小成功见证：同一 `List<Point>` 实例连续增删改并 Commit，冷重开保留内容和共享身份；Point 升版后列表只升级一次、强制 Base，随后恢复普通 Delta。
> 前置事实：[DB-043](0043-vector-array-object-slice.md) 的可组合数组和 [DB-046](0046-unified-schema-catalog-slice.md) 的统一闭合目录已实现。

## 1. 为什么选择这一片

下一步比较过两条路线：继续持久化开放 Schema 模板，或增加首个 BCL 内容适配。
当前 SchemaStore 已直接恢复闭合 Schema；冷读不需要先展开磁盘上的开放模板。
[BindSchema/Match](../../src/DurableGraph/StateBindingContext.cs) 核对的是持久布局与保留的代码历史，
同时推导 DTO 的 exact 值操作数，承担 kind/arity、字段、base/inline、重复参数及泛型作用域一致性。
[Upgrade 推导](../../src/DurableGraph/StateBindingContext.Upgrade.cs) 和
[当前模型生成](../../src/DurableGraph.Generator/DurableSchemaGenerator.GenericProjection.cs) 确有构造后再匹配的往返，
但持久化模板本身不能删除这些证明义务，也不能替代手工 Schema/reader 入口。
尚未找到净简化的完整替换路径，因此本片不扩大这项重构。

List 则带来直接的产品能力：不改变引用身份、发布或 Schema 版本政策，只增加有序可变长度内容。
数组已有的静态元素操作、owned 状态、历史 reader、元素 Upgrade 均有可复用机制。
两路独立源码评估均推荐 List；分歧集中于 resize Delta，见 §4 的取舍。

## 2. 支持范围与不变量

- 支持 exact `System.Collections.Generic.List<T>`；List 子类、`IList<T>`/`IEnumerable<T>` 等接口字段及任意 object 槽不在本片范围。
  `List<Base>` 中的已登记 Derived 实例沿既有 durable 多态规则处理；不开放 List 泛型实参转换或数组协变。
- T 使用完整既有受支持槽闭包：13 种标量、string、durable class、递归 inline/generic struct、SZ/rank 2–4 数组，
  再加入 List 自身。覆盖 `List<List<int>>`、`List<Point[,]>`、`List<Node>[]`、`Box<List<Point>>` 与泛型 struct 组合。
  不以仅支持 int/string 作为完成标准；原先不支持的 enum/nullable/boxed value 等不会因此自动获得支持。
- List 是独立引用对象，字段/元素中的列表引用投影为 ObjectId。共享列表只捕获、归一化、分配一次；
  支持 `Node.Children: List<Node>` 与子 Node 指回同一列表形成循环。
- 仅持久化 Count、顺序和逻辑元素。Capacity、内部数组、修改计数等实现细节不是持久状态；只改 Capacity 不产生变化。
- 同实例 resize 保持 ObjectId。换成内容相同的新列表仍是新对象；移除最后可达路径继续由 membership 差集产生 Remove。
- Capture 期间领域图静止，沿用宿主同步合同。冻结结果拥有独立 `TState[]`；引用元素为 ID、值元素递归捕获，
  不持有可变领域元素。后续 List 或 inline 值变化不能改变候选内容。
- 本片不做 Dictionary/Set、comparer、通用集合插件、跨程序集模型发现、并发 Capture 或性能池化。

## 3. 类型、布局与目录

建议增加一个内建 List nominal 构造（下述新名称均为拟议名称）：

- `TypeExpr.List(element)` / `TypeExprKind.List`，一元递归构造，独立于 Named 与 Array。
  不用 `Named("System.List", ...)` 冒充用户定义，内建构造不需要用户 Schema/provider。
- `ListLayout(ElementSlot, CodecVersion=1)`：只含 complete exact 元素槽和内容 codec 版本。
  Count 属于对象状态，不属于布局。`List<Point>` 锁定 Point exact 值布局；`List<Node>` 只约束 Node nominal 引用。
- `ObjectLayout`、`ObjectStateKind`、`ObjectStateRecord` 增加 List 分支；状态拟为 `FrozenListState<TState>`，
  对外只暴露只读元素视图，不复用 ArrayShape 冒充 List。
- SchemaCatalog 增加 List 记录，共用已有连续 ID、预检、一次 append/flush 和恢复流程；
  Base 仍为 v4 的单 RepresentationId，Delta 仍继承终止 Base 的布局。inline 节点仍不能用作对象 Base。
  List 内含 exact inline 槽时登记依赖；引用 List 的 owner 不登记其元素 exact 布局为 owner 依赖。
  同一 List nominal 的不同历史元素布局可以取得不同表示 ID，与数组一致；用户 Schema 同 key 冲突规则不变。

拟议编码：TypeExpr 构造码 8 表示 List 后跟一个元素表达式；SCB1 增加 kind=4 的
`nodeId, kind, codecVersion, elementSlot` 记录，复用既有 slot 编码与完整验证，不增加第二份目录。
这是现行目录语法的新增构造；现有行的字节不变，未知构造继续拒绝，不增加旧格式读写后端。
实现 G0 须以独立 golden 固定新增码位，并检查现有未知标签测试的取值不再占用它们。
初始 golden：`TypeExpr.List(Int32)` 为 `08 01 02`；空目录首次登记 List<int> 的完整 SCB1 payload
为 `01 01 02 04 01 02`（格式、行数、ID、kind、codec、slot）。最短 List 行只有 4 字节，
必须将当前读取器按最短数组行 5 字节计算的 rowCount 下界检查同步调整，并以该单行测试防止误拒绝。

生成器与 Build 共同的 TypePattern、替换、canonical 文本、manifest/history 都必须理解 List：
拟使用 `l(element-pattern)`，新写 `.dgschema` v5；严格读取并原样保留已有 v1–v4 history，旧版本文档不得偷接收 List 语法。
新生成的 manifest 同步采用 v5；Generator reader、Build reader/writer 与 Shared 解析必须使用一致的版本门槛。
仅引入新 history 语法，不修改用户 Schema 的版本传播规则，也不批量重写旧历史文件或 hash。
`List<Point>` 字段不随 Point 升版传播 owner 版本；列表对象的 Base 记录其自身 exact 内容布局。

## 4. Base / Delta body

复用 `IStateOps<TState>`；没有独立 StateEquals 或尺寸估算器。
所有整数使用现有 canonical UInt32，Base/Delta 准备结果拥有 bytes，后续使用原策略及真实 payload 计量。

```text
Base:
    count
    count 个 element Base

Delta（有 prior，同一 ListLayout）:
    newCount
    零或多条 (index + 1, element Delta)，严格递增
    0                           // 共同前缀变化表结束
    max(0, newCount-oldCount) 个新增尾部 element Base
```

令 `common = min(oldCount, newCount)`：

1. 对 `[0, common)` 每槽调用一次融合 PrepareDelta；仅输出 HasChanges 的槽。
2. 新增尾部写 Base 值，包括 default 值与零字节 struct，不能用从 default 开始的 Delta 猜测新值。
3. 缩短仅改变 newCount，不输出已删除槽；中间插入/删除按位置变化自然编码，不推导编辑操作日志。
4. `HasChanges = count 不同 || 任一共同槽变化`，不能根据 bytes 是否为空判断相等。
5. Apply 新建 owned buffer，复制 prior 的共同前缀，应用变化，再读新增尾部；不修改 prior buffer。
   变化索引上界是 common，不能接受指向新增尾部或已删除部分的索引。

比较过“等长稀疏 Delta，resize 使用完整替换模式”：它需要额外 mode 和两套内容路径，且丢失共有前缀的差异复用。
上述单语法只在数组差分循环上增加 newCount、common 和尾部三处逻辑，故推荐采用。
暂不做 LCS、区间搬移、字典化、容量策略或小 Delta 特殊编码；是否最终写 Base 仍由既有策略决定。

读取要求 count 可表示且满足 CLR buffer 上限、checked 算术、适用时的 payload 最小长度预检；
索引重复/逆序/越界、缺尾值、截断、非 canonical 数值及尾随数据均拒绝。
零字节元素不能用 payload 大小推导总内存上限，沿用路线图中独立的加载预算待办，不宣称已防御所有内存耗尽。

## 5. Capture、恢复和 Upgrade

Runtime 预制 `ListObjectBinding<TDomain,TState,TProjection,TOps>` 与仅依赖 TState/TOps 的历史 reader，
由操作快照按闭合 CLR 类型组装并缓存；SG 负责识别字段/泛型模板并发出既有强类型引用槽操作。
SG 使用 Compilation 中准确的 BCL `List<T>` 符号身份识别，不能只匹配短类名或 namespace 文本。
已知元素调用静态泛型能力，逐元素不做 Type 哈希查找或反射。
首版可把 List 索引值读到局部变量，按 `in` Capture；Hydrate 按 `ref` 填好局部值后 Add。
不为了 ref 访问而序列化私有字段或提前引入 CollectionsMarshal 优化。

Allocate 创建空 `List<TDomain>`，可按 Count 预留容量；统一登记所有实例之后才按顺序 Add 恢复元素，
因此共享和循环不依赖元素的填充次序。内建 List 构造是适配器行为，不改变用户领域类无构造器分配合同。

List 自己是内容 Upgrade 的 owner，不由入边字段各自转换：

- 沿用数组的 exact source→current 元素规则绑定；拟新增 `UseListElementUpgrades(ruleSet)` 显式选择，
  与数组选择互不影响，不顺带把所有容器塞进通用策略注册 API。
- 空列表同样预绑定工具与完整 exact requirement；缺规则、歧义或已登记 Schema 冲突在该列表首个业务回调前拒绝。
- 按顺序每元素转换一次，保留 ObjectId、Count 和位置。值工具不增删元素、不访问对象图或分配持久 ID。
  UpgradeContext 报告 List 的 source/target ObjectLayout；如暴露长度，使用 ListCount，不能伪造 ArrayShape。
  子工具保留相同 owner 事实，SourceObjectSchema/TargetObjectSchema 的 class-only 合同不放宽。
- exact 布局变化的存活列表在下一次保存强制 Base，之后正常 Delta；多个 owner 共享时仍只转换一次。
- 历史 reader 只需要保留的状态/操作能力，不要求旧领域 struct CLR 类型；仍不新增无 CLR 迁移壳的 current Normalize 能力。

## 6. 施工顺序与责任边界

本片是一个跨模块功能分片，按以下顺序逐步集成；不能把只通过 Runtime 测试当作交付。

| 阶段 | 工作包与依赖 | 最小出口 |
|---|---|---|
| G0 | 主线程冻结 List nominal/layout、body、history 语法及失败规则；保持 Proposed 直到用户采纳 | 上述合同无影响业务语义的未决分支，列出黄金编码 |
| G1 | Runtime 类型/内容 body 与 tests；SG/Shared/Build 的模式/history支持可由另一 agent 在接口约定后并行 | 递归类型与 Base/Delta/owned buffer 成立，SG 生成可编译 |
| G2 | Runtime List owner Upgrade；StateStore 快照闭合、引用验证、目录记录及 tests | 历史精确读取、空 List 升级预检、共享与长度变化保存闭环 |
| G3 | 主线程整合真实包跨版本场景；独立 agent 复核类型版本/候选所有权/差分边界 | 完整产品验收、文档收口及连贯提交 |

文件所有权按 Runtime、SG/Shared/Build、StateStore 划分；TypeExpr/Layout 等共同合同先落定。
主线程持有集成及最终验收，Windows build/test/package 串行。少量具体共同循环可在出现两个消费者后抽取；
不要求完整复制数组实现，也不把通用序列容器框架当作前置工程。

代码入口：
[数组当前投影](../../src/DurableGraph/ArrayObjectBinding.cs)、[历史 reader/body](../../src/DurableGraph/ArrayStateReader.cs)、
[数组升级](../../src/DurableGraph/StateBindingContext.ArrayUpgrade.cs)、[对象引用验证](../../src/DurableGraph/StateReferenceVisitor.cs)、
[绑定快照](../../src/DurableGraph.StateStore/StateModelSnapshot.Arrays.cs)、[SchemaCatalog](../../src/DurableGraph.StateStore/SchemaCatalogWireCodec.cs)、
[SG 类型模式](../../src/DurableGraph.Generator/DurableSchemaGenerator.TemplateHistory.cs)、[Shared 模式](../../src/Shared/SchemaHistoryTypePattern.cs)。
实施时审查全部 IsArray 分支：只有“内建引用构造”的判断应扩为 List，维数/shape/数组规则选择保持数组专用。

## 7. 验收矩阵

| 机制 | 必须可观察的结果 |
|---|---|
| 内容 Delta | append/truncate/clear/中间插删/交换/等长变化/default 尾值/零字节 struct；Apply(Prepare(a,b),a)=b，prior 不变；浮点按位、引用按 ID |
| 冻结 | Prepare 后修改领域 List、Capacity 或含引用的 inline struct；已准备 bytes/引用列表保持候选时状态 |
| 图身份 | 共享 List、Node↔List 循环、同实例 resize、同内容替换、child-only 修改、断开后 Remove；成功再次 Commit 无伪变化 |
| 组合类型 | List 的 List、数组与 List 双向嵌套、泛型 class/struct 参数、struct 含 List<自身>；不发生无限闭合；用户自定义同名 List<T> 不误识别为 BCL |
| exact 与 nominal | List<Point> 与引用 List<Point> 的 owner 版本独立；List<Node> 不锁 Node 版本；不同 nominal 即使状态类型相同也不合并 |
| 历史升级 | 真包 V1 seed→V2，保留 history 后删除旧元素领域 CLR 类型；exact decode、空列表缺规则失败、共享一次转换、Base 重写、普通 Delta、冷重开 |
| 目录与 body | 独立 golden、未知标签、错 inline ID/kind、重复布局、非法 count/index、截断/尾随数据；现有登记原子性与故障协议保持 |
| 发布与失败 | List 候选接上现有 GraphSession；确定失败不安装候选、发布后基线保持原实例；不另建提交协议 |

代码完成后根 `dotnet build DurableGraph.slnx`、相关 focused tests、完整 solution tests；
扩展 [PackageConsumerProbe](../../experiments/PackageConsumerProbe/README.md) 的 List 正常和跨版本包场景，
并验证既有 Array/Generic/ValueUpgrade/StateStore 包路径未退化。
只有 G3 的证据完成后才更新实现状态；本次规划没有运行或声称通过这些新增测试。
