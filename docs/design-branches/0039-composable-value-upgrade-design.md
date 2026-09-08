# DB-039：owner 显式调用的可组合值 Upgrade

> 状态：Proposed — 2026-09-08。承接用户授权的进一步研究，不是产品实施授权。
> 基线：`24004ab`；产品仍为 DB-037 的非泛型能力。
> 本文细化并修订 [DB-038 §6](0038-generic-schema-state-and-binding-design.md#6-upgrade通用方法与显式闭合边)；泛型身份、历史及仓库一致性继续由 DB-038 说明。

## 1. 问题和不可改变的边界

DB-038 的闭合 owner Upgrade 可以正确表达 `Box<PointV1State> → Box<PointV2State>`，
但不同 owner 即使采用同一种 Point 业务转换，也要重复书写闭合 DTO 签名和外层组装函数。
普通 helper 已能复用 Point 的业务代码；本次要进一步消除这些重复的**闭合外层函数**。

| 约束 | 来源及本轮含义 |
|---|---|
| 业务转换由用户决定，owner 控制执行 | 用户当前要求；框架可以绑定工具，不能主动遍历 inline 值并执行 Upgrade |
| 单对象、无其他对象读取或新 ObjectId | 用户已裁剪的 MVP；转换参数仅为状态值和事先绑定的纯转换能力 |
| DTO readonly/unmanaged，引用为 ID | 当前 DB-037；delegate 存在于执行 binding 中，不是 DTO 字段 |
| exact 布局先确定，再绑定整条对象升级链 | DB-038 §6.4；值能力不填补 phantom 参数缺少的历史版本信息 |
| 目录在操作开始冻结，失败不交付部分 World | 当前模型目录及 DB-038 的扩展方向；不建立全局 latest 转换目录 |
| 仓库内同 key 完整 Schema 一致，无闭合历史账本 | 用户已选 DB-038 §3.3；本次不改变版本轴或保证范围 |

最小观察标准：同一 Point 规则被开放 Box/Pair 复用并可嵌套；同 DTO 类型的不同业务规则可以明确区分；
声明的依赖槽错绑、缺依赖和歧义在该对象整条链的业务 callback 之前失败。
本轮只研究代码形状、绑定合同和独立实验；不改产品 SG、wire、SchemaStore 或 Normalize。

## 2. 推荐的执行形状

默认使用一个强类型委托，作为通用 owner 函数的额外参数：

```csharp
public delegate TNext ValueUpgrade<TPrior, TNext>(in TPrior prior)
    where TPrior : unmanaged
    where TNext : unmanaged;

static void UpgradeBox<A, B>(
    in BoxStates.V1<A> old,
    out BoxStates.V2<B> next,
    ValueUpgrade<A, B> value)
    where A : unmanaged where B : unmanaged {
    next = new(value(in old.Value), 0);
}

static PointStates.V2 UpgradePoint(in PointStates.V1 old) =>
    new(old.X * 1000, old.Y * 1000, old.LabelId);

static PairStates.V2<B> UpgradePair<A, B>(
    in PairStates.V1<A> old, ValueUpgrade<A, B> element)
    where A : unmanaged where B : unmanaged =>
    new(element(in old.Left), element(in old.Right));
```

DTO/family 名字、字段及接口名为示意。`value` 已经绑定到本次确切的源/目标语义和业务规则；
owner 的普通 C# 调用决定何时执行。删字段的 Upgrade 可以完全不声明该值依赖；
已声明但在某条数据分支中未调用的依赖，仍须预先绑定成功，不按运行数据临时查找。

Pair 的转换也是用户显式提供的函数。框架可以按它声明的依赖组装 Point 转换，
但只有 owner 调用 Pair 委托，Pair 函数才调用 Point 委托。这没有增加 struct Normalize 阶段。
若两个 Point 字段分别需要缩放和保持数值，可以给 owner 两个委托参数并选不同规则；
重复类型参数仍须满足 DB-038 的同一状态表示约束，不允许借不同规则构造相互矛盾的布局。

| 形状 | 收益及代价 | 本轮建议 |
|---|---|---|
| 普通 helper＋闭合 owner | 最少框架概念；不能消除每种闭合外层签名 | 继续支持，适合少数特例 |
| 注入 `ValueUpgrade<A,B>` | 无逐值装箱/查表；可捕获已绑定子委托，用户只有状态类型参数 | **本次默认推荐**；每次调用有 delegate 间接调用，闭合时可能分配委托/闭包 |
| `IValueUpgrade<A,B>`＋`TTransition` static helper | 可用 helper 类型递归表达所选规则，有潜在内联机会 | 技术可行；更多类型参数和组装类型，保留优化候选 |
| typed 实例接口 | 能携带依赖或状态 | 目前单一操作用 delegate 已足够，不另增实例接口合同 |
| 自动按类型搜索迁移图 | 用户声明少 | 无法决定业务规则或缺失的历史布局，本次不引入 |

这不修订 Capture/Base/Delta 的静态 helper 主干。当前 Upgrade 由历史 Load 的 Normalize 按对象/版本调用，
并非每次保存的数据成员循环。没有性能测量证明此处增加 TTransition 参数值得。
static 方案也不必使用动态 emit 或可变全局状态：纯代码 leaf/组合 helper 可以经闭合类型表达选择，
只是当前 delegate 更容易携带 snapshot 中已选的子能力。

## 3. 显式选择：类型正确还不够

一个值能力在逻辑上绑定：

```text
用户选择的规则集/意图
    + source 完整槽语义 + target 完整槽语义
    + typed 输入/输出 + 所选 provider 与子能力
```

完整槽语义包括基元 kind、string/durable 引用类别及 nominal 约束，或 inline 的完整 exact Schema。
DTO CLR 类型只是交叉检查，不能代替这些信息。数值 uint、string ID、Node ID 都可以是 uint，
却不能因为两个泛型参数相等就共用 Identity 或业务转换。
引用槽的匹配依 nominal 约束；目标对象自己的 Schema 升版不等于该 ID 槽需要值转换，
它也不授权通过值能力调用目标对象的 Upgrade。

owner 定义时一次声明每个能力参数的**规则选择及 source/target 槽位置**。
规则名属于应用代码选择，不是新的持久 ID；不能扫描程序集后按“唯一看起来可用”替用户决定。
槽位置基于历史 Schema 声明段与 FieldId，继承中必须保留声明段，不能仅用可能重复的数字。
模板按本次 exact Schema 解析后，才得到两端完整槽语义。重复 T、相同 TS 都不抹掉字段的选择位置。

以下为待 SG 验证的用户写法草图，属性名不是已存在 API：

```csharp
[OwnerUpgrade("Box", 1, 2)]
static void Upgrade<A, B>(
    in BoxStates.V1<A> old, out BoxStates.V2<B> next,
    [UpgradeDependency(typeof(Coordinates), sourceField: 1, targetField: 1)]
    ValueUpgrade<A, B> value)
    where A : unmanaged where B : unmanaged {
    next = new(value(in old.Value), 0);
}

[ValueUpgradeProvider(typeof(Coordinates), "Point", 1, 2)]
static PointStates.V2 PointToMillimetres(in PointStates.V1 old) =>
    new(old.X * 1000, old.Y * 1000, old.LabelId);
```

简例只有一个声明段，因此省略段参数。正式实现须用生成的字段选择器或明确的声明段/FieldId 定位；
具体属性、selector 表达及规则集登记外观应在真实 SG 首闸门冻结，不能把示意语法视作已验收 API。
泛型 Pair 可在该规则集中登记一个开放值 provider，声明一份 element 依赖，由用户函数重复调用。
两个 T 槽的完整语义一致性已由 DB-038 的统一替换检查承担，不再增加“列全委托使用位置”的元数据。
需要不同业务规则时则声明不同能力参数。
声明依赖选择的是转换来源，不能静态证明任意用户 C# 传入了声明的 source 字段、最后把结果放到声明的 target 字段。
例如 Count 与 NodeId 同为 uint，把 Count 误传给 Node 转换，且其数值碰巧是合法 ID，最终引用校验也未必能发现。
框架校验声明与绑定的语义，用户业务函数的数据流仍需自己的测试；不引入方法体数据流分析器。

## 4. 绑定协议，复用同一代码目录

扩展 DB-038 已有 provider 目录及操作 snapshot，不另建全局可变注册器：

1. 应用显式登记 owner provider、值 provider 及它们声明的依赖。SG 发出登记材料与强类型调用 adapter；
   frozen snapshot 内再按实际的 nominal family/两端完整 Schema 闭合。
2. 先按 DB-038 确定对象的完整相邻版本路径。值能力只处理已给定的两个端点，
   不枚举版本、不根据已登记转换猜中间布局、不执行回调探路。
3. 对 owner 声明的每个依赖，先在所选规则集中按两端 nominal 身份/版本、kind 及显式模板适用形状定位候选，
   再严格核对完整 expected 槽语义和状态类型。多个候选直接拒绝，不增加 exact/open 模板优先级搜索。
   候选存在但完整 Schema 不匹配属于错误，不能把它当成“无候选”；不同规则集可拥有相同端点的不同业务实现。
4. 开放值 provider 可以递归请求其明确声明的子能力。这是代码依赖绑定，
   不为未请求的字段自动发明转换，也不对未知类型尝试闭合所有备选模板。
   递归绑定环和超出支持深度明确失败；不发布半成品 binding。
5. 所有相邻边及其依赖绑定成功后，生成/缓存该对象族的 typed 执行链，再调用用户代码。
   依赖对象中保存委托，DTO 中没有委托；每个值转换不再查注册表、不用 DynamicInvoke。
6. 已选择条目的 expected Schema 不匹配、子依赖缺失或执行抛错时直接失败。
   不尝试另一个业务规则，也不回退为 Identity。缓存以 snapshot 为边界并保持完整 Schema 一致性。

可显式提供 `KeepExact` 透传能力。为了让 Box<int> 在定义统一 bump 时复用通用 owner，
规则集也可由作者**明确启用**“根本无显式候选且完整槽等价时使用 KeepExact”的有限兜底。
显式匹配规则优先；它即使在同 Schema 下执行业务变换也不能被 Identity 吞掉。
多个显式匹配不靠优先级选胜者，匹配条目失败也不进入兜底。仅 A=B=uint 不是完整槽等价。
特别是同 Point@2 的显式规则 expected 布局有误时，不能因完整 Schema 不等而排除它，再悄悄透传实际值。
未启用兜底、槽不等价或缺子依赖时，均在执行前失败。

闭合 owner 专用边仍按 DB-038 优先于通用 owner，已选闭合边失败仍不回退。
本节没有新增值版本最短路、通用 overload 排序、全局默认升级、Initializer 或自动业务转换。
需要 Point v1→v3 的值转换时，用户可以写明确调用 v1→v2、v2→v3 的普通 helper；
所有参与的中间表示仍由用户显式指定，不由运行时搜索路径。
框架能预检的是声明的 owner 链和值能力依赖；普通 C# helper 内的局部 DTO/直接函数调用不是 binder 可审计的图，
不承诺检查任意函数体里的所有临时表示。它们仍属于用户业务代码责任。

## 5. 保留的限制与实际简化

- `Phantom<T>` 旧版没有 T 值，不能凭 `ValueUpgrade<A,B>` 获得不存在的 A 或决定中间 B。
  DB-038 §6.4 的已登记中间 Schema/显式闭合边等依据仍必需；不增加 default(T) 初始化协议来掩盖缺信息。
- same key 完整 Schema 冲突仍拒绝，包括框架推导或绑定链中的临时中间 Schema/DTO；不能凭相同 CLR 状态类型绕过。
- 历史值 provider/组合 helper 只引用纯状态 family DTO，不依赖已删除的领域类型。
  独立引用对象族的 current/migration CLR 能力保留规则仍由 DB-036/038 约束。
- 值规则可以构造新状态、保留或清除引用 ID，但没有其他对象内容/新 ID 上下文。
  整体 Upgrade 后仍按 current 全目录验证引用。匹配正确不能证明用户业务逻辑正确。
- 预绑定失败保证该对象链尚未调用业务转换；不许诺整个 Revision 的其他对象也零 callback。
  当前 Normalize 逐 source 行执行。执行期间用户异常也不能撤销任意外部副作用，纯转换仍是调用合同。
- frozen 目录固定选择和依赖，不会自动深冻结用户 delegate 捕获的可变外部对象；推荐普通纯静态业务函数。

当 K 种 owner 包装 M 种值且复用相同规则时，可以从 K×M 个闭合外层函数降为 K 个通用外层函数、
M 个叶子规则，加上各模板一次性的依赖声明。开放 Pair 组合后无需专门编写 Box<Pair<Point>> 的业务外壳。
若每个闭合 owner 都有独特业务语义，仍需要相应规则选择，不能凭组合框架消除这份业务信息。

## 6. 产品接缝与研究验收

现有 [SG StateModel](../../src/DurableGraph.Generator/DurableSchemaGenerator.StateModel.cs) 只接受
非泛型两参数 `in/out` Upgrade；其 DTO 符号在当前生成轮尚未存在，采用发出强类型调用后由编译器校验的办法。
新能力须沿用这条原则：先读取显式 family/version/槽选择的元数据与 history，
发出 expected typed adapter，再让最终编译器检查用户方法，不能依赖初轮不完整的 DTO Roslyn 符号推断全部绑定。

| 验证层级 | 要回答的问题 |
|---|---|
| 独立机制 | static/delegate 两种形状均能组合；Box/Pair/嵌套复用、两字段不同策略、unmanaged 状态、冷闭合后 typed 调用 |
| 小范围绑定反例 | 同 TS 异语义、同 key 异布局、无依赖/多匹配、预绑定失败零 callback、执行异常不回退 |
| 真 SG（后续） | 用户源码＋依赖声明自动生成登记/adapter；同规则集的叶子/开放 Pair；继承段及重复类型参数；错误签名在编译期拒绝 |
| 产品闭环（后续） | 完整 Schema/目录 snapshot、多跳缺中间布局、真实历史包删除旧领域类型、Load 强制 Base 与稳定续写 |

独立实验的入口和证据边界见 [GenericBindingShapeProbe](../../experiments/GenericBindingShapeProbe/README.md)。
手写 generated-like adapter 和实验槽 record 不证明真 SG、产品 Schema 匹配或整套 Normalize 已实现。
不在本研究中推进产品泛型实施或新增 wire 版本。

语言事实参考：[C# 泛型方法推导](https://learn.microsoft.com/en-us/dotnet/csharp/programming-guide/generics/generic-methods)
说明不能只从返回类型或约束推导类型参数；[静态接口成员](https://learn.microsoft.com/en-us/dotnet/csharp/advanced-topics/interface-implementation/static-virtual-interface-members)
说明受约束泛型参数的调用形状。具体选用 delegate 是本项目成本判断，不是语言禁止 static 方案。

## 7. 审阅与结论记录

本轮由最小架构、需求/技术反审及历史语义三位 subagent 独立研究并交叉质询；
历史语义沿用用户指定的 gpt-5.6-sol / max。主代理检查实际 Normalize/SG 接缝，独立实验由另一位 agent 完成。
最小架构审阅者初选 static helper，在比较当前调用路径与委托组合后撤回其必要性；
需求反审也明确撤回“静态方案必然需要 emit/全局可变目录”的过强推断。
推荐的核心是显式依赖及 exact 适用性，委托是当前较小的执行形状；并未以审阅人数代替证据。

最终定向复核没有剩余设计阻塞。审阅促成三项局部加固：定位显式候选后再校验完整 Schema，
防止坏条目被 KeepExact 吞掉；按对象明确预绑定范围；区分依赖声明校验与无法保证的用户函数体数据流。
Pair 的重复 T 一致性复用现有替换检查，不另增委托使用位置清单。

主代理重跑 `pwsh -NoProfile -File experiments/GenericBindingShapeProbe/Run-Probe.ps1`，
**35 项预期结果与 PASS，exit 0**，保留 DB-038 原 20 项并增加 15 项值组合/选择见证。
新增 fixture 为 342 行：一部分比较 static/delegate 的真实 C# 组合与冷闭合；
另一部分以实验槽 record 和有限 `uint→uint` 委托测试明确 rule ID 的预绑定。
它验证了缺失/冲突/循环/错完整布局在业务 callback 前拒绝、显式 KeepExact 及坏候选不得透传、执行异常不重选。
**没有验证按规则集和 nominal 模式自动发现候选、真 SG 依赖属性、完整 Schema 求解或产品 Normalize。**
这些仍属于 §6 的产品首闸门；不把研究结果当作已实现泛型支持。
根 `dotnet build DurableGraph.slnx --verbosity quiet` 通过，零警告、零错误。
本轮没有修改产品源代码或格式，相关执行验证使用上述独立 Probe，没有重复将旧产品测试计数作为本轮验收。
