# DB-002：读取阶段的版本升级管线

> 状态：Open
>
> 创建日期：2026-08-27
>
> 当前方向：读取旧版本时尝试升级为请求的当前版本；Load 不隐式写 Store，后续显式 Save 才保存升级后的对象。

## 阶段目标

在继续使用 `InMemorySchemaStore`、`InMemoryStateStore` 和 boxed state 的前提下，跑通：

```text
stored State (Schema V1)
    -> exact historical Schema validation
    -> exact V1 serializer/materializer
    -> V1 CLR object
    -> explicit upgrade path
    -> current V2 CLR object
    -> caller-visible result

later explicit Save(V2 object)
    -> register Schema V2
    -> write State as V2
```

本阶段不处理增量序列化、统一 commit、多 Store durability、对象身份或 wire format。

## EXP-006 后的方向修订

`experiments/SourceGeneratorHistoryProbe/` 已观察到：

- Source Generator 的 `AddSource` 输出不会自行成为下一次 compilation 的 `AdditionalFiles`；即使旧 candidate 仍物理存在，未显式登记时也不是输入。
- opt-in post-compile publisher 可以在成功 build 后把 current shape 写到外部 Snapshot History；它只能在下一次独立 project evaluation 中被 Generator 读取。
- 只保留当前 V2 领域源码和显式 V1 history 时，fresh compilation 能生成 `SnapshotV1`/`SnapshotV2`，编译并执行强类型 `V1 → V2` 方法。

同时已经决定首轮只支持唯一相邻版本链。由此当前倾向收窄为闭世界 generated pipeline：

```text
explicit Snapshot History + current domain source
    -> generated exact historical Snapshots/materializers
    -> generated stored-version switch
    -> direct typed V1 -> V2 -> V3 calls
    -> current domain materialization
```

runtime historical binding registry、upgrade registry 和 object-erased edge adapter 暂缓。只有 runtime plugin、独立 migration package、多个程序集共同贡献版本或热安装修复成为真实消费者时再重访。

EXP-006 只证明 build hook/自制工具能够承担历史发布 side effect；它没有选定正式 history format、publisher owner，也没有批准普通 build 修改受版本控制的工作树。

## 必须保留的边界

1. State record 的 `(SchemaId, Version)` 决定唯一 historical Schema，不使用 latest fallback。
2. historical payload 只能交给与该 exact Schema shape 绑定的 serializer/materializer。
3. 同 SchemaId 才允许版本升级；不同 SchemaId 是类型不匹配。
4. 缺失 historical shape、缺失相邻升级实现、链不完整、handler 抛错或返回错误结果时 fail closed；可在生成期/编译期拒绝的情况不推迟到运行时。
5. Load 不修改 StateStore 或 SchemaStore authority；升级成功只返回新的内存对象。
6. 只有调用方随后显式 Save，升级后的当前版本才进入 Store。
7. 失败升级不得覆盖旧 State；旧 Schema 和旧 boxed fields 保持可再次读取/诊断。

## 为什么需要 historical serializer binding

当前 `InMemoryStateStore.Load<T>` 只拿到调用方提供的当前 `IDurableSerializer<T>`。当 State 指向 V1、调用方请求 V2 时，V2 serializer 不能安全解释 V1 fields。

升级前必须先找到一个与 stored exact Schema 绑定的 historical binding：

```text
ExactSchemaKey + exact Schema shape
    -> historical CLR type
    -> historical serializer/materializer
```

原始候选是假设加载协调器需要在运行时查找未知 historical CLR 泛型类型。EXP-006 之后，当前更小的闭世界方案是让 Generator 为每个 current durable type 生成完整的 historical version switch 和直接强类型调用；Store 只面对一个 version-aware current binding，不需要知道 Snapshot CLR 类型。

## 原始候选最小组件（runtime registry 已暂缓）

### Historical serializer registry

职责：

- 按 exact `(SchemaId, Version)` 查找 binding。
- 验证 binding.Schema 与 SchemaStore 中的 stored Schema 结构相等。
- 从 boxed fields 物化 exact historical CLR object。
- duplicate exact key 且 shape/type/serializer 不一致时拒绝注册。

不负责：

- 选择升级路径；
- 自动扫描所有程序集；
- wire-format decode；
- 持久化 registry。

### Upgrade registry

每条 edge 至少表达：

```text
SchemaId
FromVersion + source CLR type
ToVersion + target CLR type
Upgrade handler
```

handler 应是显式、同步、确定的对象转换；本阶段不允许网络、时间、LLM 或 Store side effect。

### Load orchestrator

建议流程：

1. 从 State record 取得 stored exact Schema key。
2. 从 SchemaStore 取得并验证 stored Schema。
3. 从 serializer registry 取得 exact historical binding，并再次核对完整 shape。
4. materialize historical object。
5. 若 stored version 已是 requested version，验证 CLR type 后直接返回。
6. 否则查找完整 upgrade path。
7. 逐 edge 执行 handler；每一步验证输入/输出 CLR type、SchemaId 和 Version。
8. 返回 requested current CLR object，不修改 Store。

## 路径规则候选

### 候选 A：唯一相邻版本链

只允许 `V1 -> V2 -> V3`，每个 `(SchemaId, FromVersion)` 最多一条 edge，且 `ToVersion = FromVersion + 1`。

优点：确定、易诊断、无需图算法，符合当前小实验。

代价：无法直接跳过没有 CLR 定义的中间版本，也不能表达多条迁移策略。

### 候选 B：一般有向图

允许 `V1 -> V3`、多条路径或分支，再定义 deterministic path selection。

优点：灵活。

代价：需要处理环、歧义、优先级和路径稳定性；当前没有消费者证明需要。

当前倾向：先用候选 A。出现真实跳版需求前不引入一般图。

## 首个演示场景

建议定义两个 sealed historical CLR type，共用 SchemaId：

```text
CharacterV1, Schema version 1
    Field 1: String display name

CharacterV2, Schema version 2
    Field 1: String display name
    Field 2: Boolean is active
```

演示：

1. 用 V1 generated serializer 保存 boxed State。
2. 注册 V1 binding、V2 binding 和显式 V1 → V2 handler。
3. 请求 Load V2，先 materialize V1，再升级得到 V2。
4. 证明 StateStore 仍是 V1，Load 没有隐式改写。
5. 显式 Save V2 后，证明 slot 改为 V2，后续可直接 Load V2。

## 必测失败路径

- State 指向未知 Schema version。
- Schema 存在但缺少 historical serializer binding。
- binding 的 Schema 与 SchemaStore exact Schema shape 不一致。
- 缺少 V1 → V2 edge，或链在中间断裂。
- handler 抛异常。
- handler 返回 null、错误 CLR type 或错误目标版本对象。
- SchemaId 不同却尝试升级。
- 任一失败后旧 State 保持不变，后续补齐 registry 后仍可重试。

## 对象自洽仍未解决

`RuntimeHelpers.GetUninitializedObject` 绕过构造器。即使字段类型与 Schema 匹配，物化对象仍可能违反领域 cross-field invariant，transient field 也保持零值。

本升级切片暂时只验证：

- exact Schema/type binding；
- handler 输入输出类型；
- upgrade path 完整性；
- Store 无副作用。

`RebuildTransient`、对象图 invariant validation 和 malformed payload 的“先验证全部字段再分配”仍是后续独立阶段。

## 下一轮需要裁决的最小问题

1. Snapshot History 的正式 source authority 与最小格式。
2. history publisher 由显式 CLI/code fix 触发，还是允许真实 build target 自动修改工作树。
3. generated version-aware serializer/binding 交给 `InMemoryStateStore` 的最小 API。
4. generated strong handler contract 使用 required partial method，还是其他编译期强制形式。
5. 同 key 异形、并行 build 和多文件发布的 fail-closed/atomicity 边界。

## 相关材料

- `docs/DurableGraph-lab-notebook.md` 的“当前自洽边界”、EXP-005 和 EXP-006。
- `docs/design-branches/0001-schema-authority-and-runtime-representation.md`。
- `experiments/SourceGeneratorHistoryProbe/README.md`
- `src/DurableGraph/InMemoryStateStore.cs`
- `src/DurableGraph/IDurableSerializer.cs`
- `src/DurableGraph.Generator/DurableSchemaGenerator.cs`
