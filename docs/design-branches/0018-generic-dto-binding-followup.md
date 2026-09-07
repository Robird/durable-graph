# DB-018 技术附件：泛型模板、运行时绑定与 DTO 表示参数

2026-09-07。用户已认可技术方向并要求留存；本文件不是下一施工分片或完整泛型支持已实现的声明。
接续 [DB-018 运行时绑定见证](0018-runtime-binding-witness.md)与
[DB-020](0020-typed-slot-array-binding-slice.md)，补充采用冻结 Versioned DTO 后必须处理的表示映射。
不回写旧实验的历史含义；排期与剩余工作见[路线图](../DurableGraph-research-roadmap.md)。

## 1. 已验证的技术基础

用户提出 `SerializerRegisterer<T>`：为每个闭合 T 缓存 codec，开放泛型 body 的 T 字段通过它转发。
这个思路与旧见证中的 `GSlots<T>.Read` 一致。SG 可以生成一次开放泛型模板，无需枚举全部闭合组合；
首次使用时以 MakeGenericType / MakeGenericMethod / CreateDelegate 闭合预编译代码并缓存，
后续保持 typed 调用，不要求 DynamicMethod 发出新 IL。

每个闭合泛型类型拥有独立静态成员，这是缓存成立的依据；不要求每种 T 都有独立机器码，
CLR 可以共享部分泛型执行代码。参见[静态成员规则](https://learn.microsoft.com/en-us/dotnet/csharp/programming-guide/classes-and-structs/static-classes-and-static-class-members)
与[运行时泛型机制](https://learn.microsoft.com/en-us/dotnet/csharp/programming-guide/generics/generics-in-the-run-time)。

证据分两层：

- DB-018 的完整可复跑见证：静态 `GSlots<T>` + 运行时闭合，读入 `GCell<int>[]` 得到 `[17,29]`。
- [RuntimeGenericSlotBindingTests](../../tests/DurableGraph.StateStore.Serialization.Tests/Serialization/RuntimeGenericSlotBindingTests.cs)：
  使用真实 BinaryPayloadReader/Writer，组合 `Cell<Cell<int>>` 与 `Cell<double>`，同一 ref body
  操作局部变量、字段、SZ 和 rank-2 元素。这里由工厂显式传入 member codec，不是全局 static-T 注册表。
  部分读取失败的测试仅证明已访问槽位的行为，不承诺整个图恢复的事务性。

2026-09-07 讨论时主代理执行：

```powershell
dotnet test tests/DurableGraph.StateStore.Serialization.Tests/DurableGraph.StateStore.Serialization.Tests.csproj --no-build --filter FullyQualifiedName~RuntimeGenericSlotBindingTests --verbosity quiet
```

结果 3/3，通过，无跳过。以上 body 是手写的生成代码等价物；当前 SG 仍拒绝自定义泛型领域类型，
未生成泛型 Schema/DTO/history。旧测试含非零下界矩阵，其机制覆盖不改变新选的 MVP 数组排除范围。

## 2. DTO 路线增加的表示映射

对领域 `Box<T> { T Value; }`，捕获后的字段类型不能总是继续写成 T：

| 领域 T | 冻结后的字段表示 |
|---|---|
| int | int |
| string 或受支持领域 class | UInt32 ObjectId |
| 含引用的自定义 struct | 该 struct 的对应版本 DTO，引用槽变为 ID |

静态注册槽解决“调用哪个函数”，并不能让 C# 字段声明自动变成“由 T 决定的另一种类型”。
推荐生成器区分领域参数与表示参数，示意为：

```text
BoxStateV1<TValueState>
BoxCodec<TDomainValue, TValueState>
```

首次工厂绑定确定两种参数的对应关系，然后闭合模板；额外表示参数由生成代码和工厂管理，
不要求领域作者修改 `Box<T>` 的定义。闭合后 Capture、比较、Base/Delta 与 Hydrate 保持 typed 调用，
装箱只保留在必要的异构对象目录边界。具体名称、方法签名、约束和 Schema 表示尚未冻结。

更复杂定义中的 T、T[]、Pair<T,U> 等字段，可能需要多个表示参数，不能假定一个 TValueState
足以让 C# 推导全部组合；按实际字段需要生成，不提前建设通用关联类型框架。

只给泛型槽使用装箱的不可变状态，是可比较的简化方案，但会增加字段级转换/分配并削弱静态约束；
当前更倾向于显式表示参数。类似 SnapshotSlot<T> 的包装若内部仍擦除类型，并未消除这个取舍。
若以后采用装箱表示，仍不能把可变领域实例直接塞进 DTO 来冒充冻结状态。

## 3. 缓存、版本和引用边界

- 已知具体类型的普通字段继续直接调用字节原语或静态 body；泛型槽可以使用缓存的 typed delegate/
  codec，不为所有字段引入 Type 查表。不预先承诺 JIT 必然内联或消除间接调用。
- 当前 Capture 的纯代码能力可以按闭合 CLR Type 缓存；历史读取必须保留 exact stored type/Schema
  绑定，不能以今天的 SerializerRegisterer<T> 覆盖历史布局。缓存不持有 Repository、Session 或对象实例。
  登记、初始化失败及递归初始化的具体协议在真正的消费者分片收敛。
- `Box<Node>` 和 `Box<string>` 都可能使用 `BoxStateV1<uint>`，但其领域类型约束不同。
  binding 必须保留泛型定义、领域实参 TypeExpr 和 exact Schema；不能根据 DTO CLR 类型反推持久身份。
  泛型定义/history 与闭合实参的规范表示还需要 Schema/TypeCodec 设计，不以 CLR 名称字符串替代。
- 引用槽只捕获/保存 ObjectId，不在 codec 绑定期间递归展开引用目标 body；实际对象进入遍历队列时
  再按实际类型处理，避免 `Node<T> → Node<Node<T>>` 等无穷静态展开。
- 泛型参数的值布局仍遵守 exact inline Schema 依赖；引用约束仍是 nominal，不能因引用目标升版
  就自动传播整个引用图的 owner 版本。历史泛型 DTO、Upgrade 和依赖传播尚未由现有见证验证。

## 4. 何时重访与最小验证

自定义泛型保留为支持方向，不因为采用 SG 而排除，也不因本备忘而承诺任意 CLR 类型组合已可用。
实施触发：准备扩充 Schema 类型表达或开始自定义泛型 Capture/DTO 时，先读本文件再冻结分片。

最小验证建议让真实 SG 生成 Box<T>，先对 Box<int>、Box<string> 验证领域值到冻结值/ID 的不同投影，
再验证同一泛型定义的历史版本读取不会命中 current body；复合 struct 可在其 Schema/DTO 能力到位后加入。
数组组合遵守已选 rank/零下界范围。测试应涵盖 Capture 后领域变动不影响 DTO、不同领域实参即使
共享同一种 DTO CLR 表示也不会混淆绑定。该建议不是当前实施授权，亦不重排正在讨论的 Upgrade/Restore。

## 5. DB-037 提供的产品接缝

2026-09-07，非泛型 inline struct 已由 [DB-037](0037-inline-struct-state-slice.md) 接通产品。
[生成值 helper](../../src/DurableGraph.Generator/DurableSchemaGenerator.InlineState.cs) 将 exact 版本 DTO/body
与当前领域 struct 的 Capture/ref Hydrate 分开，已验证删除领域值声明后仍生成历史 DTO 并保留 owner 升级链。
SchemaKind 与 InlineSchema 只表达当前非泛型 exact 布局，不是一般 TypeExpr。
后续泛型可复用静态值操作的组合方式，但仍须显式建立领域参数与状态表示参数的对应关系，
并为闭合实参及历史版本设计身份/缓存；当前按 SchemaId/version 的 helper 名称不能代替这些工作。
