# GenericBindingShapeProbe

2026-09-08 的独立机制见证：比较并验证 DurableGraph 泛型 DTO 候选中的
**领域类型参数 / 状态表示参数 / 静态操作类型参数**、运行期闭合及历史升级可书写性。
这是手写 generated-like C# 的编译运行实验，不是产品 Source Generator 已支持泛型的声明。

## 运行

```powershell
pwsh -NoProfile -File experiments/GenericBindingShapeProbe/Run-Probe.ps1
```

或直接执行：

```powershell
dotnet run --project experiments/GenericBindingShapeProbe/GenericBindingShapeProbe.csproj --configuration Release
```

依赖 .NET 10 与 Roslyn 5.3.0。项目不加入主 solution，不修改产品程序集、格式或接口。
`Program.cs` 用 Roslyn 编译 `Fixtures/*.cs.txt`，运行正例并检查四类编译负例；失败抛出异常且返回非零。

## 已观测的结果

2026-09-08，Windows、.NET SDK **10.0.201**、Release，以上命令 exit 0：

```text
ColdMakeGenericMethodCreateDelegateCachedFacade:True
SameUInt32RepresentationDistinctReferenceSemantics:True
PrimitiveUInt32ThirdBindingWithoutReferenceVisit:True
NestedStaticProjectionBodyAndReferenceVisit:True
FrozenUnmanagedDtoWithoutDomainReferences:True
GenericPrivateReadonlyClassFieldRefHydration:True
TrailingUnusedAccessorTypeParameterAlsoWorks:True
ReorderedAccessorParameters:MissingFieldException
GenericReadonlyStructDefaultRefHydration:True
OpenSameRepresentationAndClosedOwnerUpgrade:True
StaticHelperConstrainedCallWithoutCallvirt:True
HistoricalBodyAndUpgradeWithoutOldDomain:True
NamedFamilyDtoAliasesAndOwnerUpgrade:True
PhantomFirstEdgeHasMultipleStateClosures:True
ExplicitHistoricalMiddleThreeVersionUpgrade:True
HistoricalAssemblyWithoutOldDomainTypes:True
MissingBusinessConversionRejected:CS1503
MissingDomainConstraintRejected:CS0452
PhantomFirstEdgeCannotInferStateParameter:CS0411
CurrentStateCannotReplaceHistoricalMiddle:CS1503
GenericBindingShapeProbe:PASS
```

| 问题 | 见证与可支持的结论 |
| --- | --- |
| SG 所需的开放模板/运行期闭合形状在 C# 中可行吗？ | `FacadeFactory` 解析领域实参，递归闭合 `Pair<Pair<Point>>` 的状态和 helper，以 `MakeGenericMethod/CreateDelegate` 构建外观。当前单线程缓存按领域实参区分绑定；调用成员时无逐成员 Type 查找。 |
| `uint` 表示能否保留不同槽位语义？ | `Box<uint>` / `Box<string>` / `Box<Node>` 三者同为 `BoxStateV1<uint>`，绑定不同 projection 与 state ops。primitive uint 不登记对象、不访问引用表；string 状态交给 Node 引用遍历，以及数值 999 被误当作不存在的引用，均抛 `InvalidDataException`。这不意味着仅凭相同 CLR DTO/bytes 能判定选错了 binding；绑定身份必须由外层保留。 |
| 冻结状态是否需要领域引用？ | 嵌套 Pair/Point DTO 满足 `unmanaged`，`IsReferenceOrContainsReferences` 为 false；Capture 后领域改动不改变已捕获值。实际写入、读回及递归引用遍历全部通过。 |
| generic private/readonly 字段可以恢复吗？ | `ReferenceDomainAccess<T>` 返回 private readonly 字段的 ref；`ReferenceHydrate<TDomain,TState,TProjection>` 用静态 helper 写回无构造器分配的 class。`ValueDomainAccess<T>` 配合 default readonly struct/ref 恢复值，Transient 保持 0。class/unmanaged 约束均实际执行。 |
| accessor 与执行 helper 为何分开？ | 把领域参数维持在原 ordinal 的形状可用。额外尾随且未使用的 `TState` **也可用**，所以分离不是“参数数目必须完全相同”的要求；但调换成 `Access<TState,TDomain>` 后本例抛 `MissingFieldException`。领域参数优先且隔离表示参数可减少错位面。不能外推未试验的继承、嵌套类型或一般约束组合。 |
| static abstract 是否避免实例接口调用？ | Roslyn 生成的 `BoxProjection<,,>.Capture` IL 含 `constrained.` 和 `call`，没有 `callvirt`。此结论仅为该方法的 IL 形状；外层异构外观仍使用接口/装箱。 |
| 升级如何书写？ | 同状态表示可用一个开放 `Upgrade<TState>` 透传。Point v1→v2 的业务变化通过显式 closed owner edge 转换（X 乘 1000，引用 ID 保留）。只有不同 `TOld/TNew` 参数、却不提供转换的代码报 CS1503。 |
| 历史状态是否依赖旧领域 struct？ | 第二个独立编译不包含领域 Point/Box 定义，不引用第一个程序集；读取第一个程序集产生的 bytes，并执行历史 body/引用校验及 owner Upgrade。 |
| 用户能否自然命名泛型历史 DTO？ | `NamedHistory.cs.txt` 使用非泛型 `Generated.Family_Box` 宿主及嵌套 `V1<TState>/V2<TState>`；用户可 `using BoxStates = Generated.Family_Box`，书写 `in BoxStates.V1<PointStates.V1>` 及开放 generic Upgrade。仅证明 C# 表达与调用成立。 |
| 中间版本的状态参数能否自动推导？ | `Family_Phantom.V1` 不含 T，V2 才新增 T 字段。`AddField<TState>(in V1)` 未显式传 TState 时编译报 CS0411；即使赋值目标已声明为 `V2<Point.V1>`，也不能从返回类型推导。分别显式闭合 `uint` 与 `Point.V1` 均可编译运行，表明 C# 模板本身没有选出唯一中间表示。 |
| 能否拿 current 表示填补历史中间版本？ | 显式第一条 owner edge 选择 `V2<Point.V1>`，第二条把 Point.V1 转换为 Point.V2，形成 `Phantom.V1 → V2<Point.V1> → V3<Point.V2>` 完整执行链。把 `V2<Point.V2>` 传给第二条 edge 编译报 CS1503；不能省略历史中间表示或业务转换。 |

## 证据边界与后续闸门

- 这里使用 `BinaryWriter/Reader` 写少量定长字段，**不是 DurableGraph wire**；没有 Base/Delta 策略、
  SchemaStore、对象 envelope、Revision、GraphSession、真实源生成器或 NuGet 消费验收。
- helper/DTO 家族名、工厂实参映射与 Upgrade 都是手写；不证明 SG 可从真实 history 自动生成命名、
  找到 Upgrade，或完成泛型持久身份、exact 版本替换与引用约束匹配。
  三版本 phantom 见证只证明表达式无法推导、多个闭合可编译、显式历史链可运行；
  **未实现或验证完整 Schema 边规划器**，也不证明它能自动发现/消歧所有中间状态闭合。
- 工厂在单线程下缓存一个完整绑定，未验证并发初始化、失败重试、递归模型目录、发布半成品防护。
  `Pair` 是值组合见证，Node 仅作身份投影，不递归闭合引用目标 body。
- 第二个程序集证明编译依赖闭包中没有旧领域类型；两个程序集在同一进程运行，未模拟独立发布包。
  真正实现仍须跨版本真实 PackageReference 回归。
- IL 不是 JIT 机器码；没有性能 benchmark，也不保证内联、零间接调用或 NativeAOT。
- 尚未证明泛型继承、任意约束、任意参数表达式、数组/BCL 组合、自动业务升级。
  后续产品分片须先设计这些支持边界，再由真实 SG 与持久化测试验收。

本 Probe 保留为可复跑的机制证据。产品泛型分片完成后，再按其仍能捕获的独立风险决定是否保留回归。
