# DurableGraph 后续研究与实现路线

> 状态：Living Roadmap  
> 更新日期：2026-08-27  
> 用途：记录当前证据支持的研究顺序、每个切片的问题和可执行闸门。  
> 边界：本文不是当前实现事实、冻结 API 或持久格式规格；源码、测试和可复现输出优先，已完成实验的事实记录在 `DurableGraph-lab-notebook.md`。

## 1. 当前出发点

截至 EXP-012，仓库已经证明：

- 四种 scalar field 可以通过 generated boxed serializer 保存和加载；
- stored exact Schema 在 payload decode 前 fail closed；
- checked-in Snapshot History 可以重建历史 ordinary struct Snapshot；
- generated coordinator 可以用唯一一次 version switch 和静态相邻链把历史 Snapshot 升级到 current；
- Load 只在内存中升级，只有后续显式 Save 才推进 Store 中的版本。
- fixture-only EXP-011 已证明单类型 flat baseline、identity-aware traversal、whole-object delta、`RequiresRewrite` 与 success-only clean baseline 的逻辑状态律。
- isolated EXP-012 已证明 Source Generator 可以为单个 direct-self-reference 类型产生强类型 current Snapshot capture、durable equality 与同次字段读取得到的 reference visitation，并与 EXP-011 oracle 对齐。

当前尚未实现：

- `DurableId` 与对象身份分配；
- production Generator/runtime 中的 durable reference、共享引用、循环图和 reachability；
- 对象图 baseline、Graph Delta 或 object version；
- binary wire format、持久 StateStore 或 SchemaStore；
- commit publication、并发、crash recovery 或 durability；
- durable value struct、collection、领域继承和 `RebuildTransient`。

因此，后续路线不能把 target design 中的完整系统描述成已经存在，也不应让尚无消费者的格式、缓存或兼容层先塑造核心语义。

## 2. 当前研究方向

### 2.1 先验证 Graph Delta 语义，再固定 bytes

下一主线优先回答：在没有 ChangeTracker 的前提下，能否按持久身份遍历当前对象图，并相对上次发布状态的强类型投影，精确产生 whole-object Upsert 与不可达集合？

首轮继续使用内存逻辑值和 boxed scaffolding。`BinaryReader` / `BinaryWriter` 只在逻辑 IR 和状态转换律通过后介入，避免过早冻结 framing、引用编码、canonical order 和 malformed-input contract。

### 2.2 历史版本在读取边界归一化

磁盘或逻辑 Store 中的 historical payload 必须先经过：

```text
exact Schema lookup and shape validation
    -> exact-version typed decode
    -> unique adjacent typed upgrade chain
    -> current-version typed Snapshot
```

进入内存 baseline 后，每个节点只暴露该 durable type 的 current Snapshot。Graph Delta comparer 不再知道 historical Snapshot 类型或版本分派。

若节点由历史版本升级而来，其 baseline entry 带 `RequiresRewrite`。只要它在下一次 Save 中仍可达，就必须写入完整 current Snapshot；若已不可达，则只从新图中移除，不为升级义务而保活。

### 2.3 逻辑图展平，引用只保存 DurableId

baseline 的逻辑形状是：

```text
RootId + Map<DurableId, BaselineEntry>
```

Snapshot 中的 durable reference slot 保存目标 `DurableId`，不嵌套另一份 Snapshot。这样共享引用只出现一次，循环图有限可表示，引用 equality 也只比较 ID。

Map 使用哈希表、排序表还是其他索引属于实现与测量问题；确定性测试、诊断和未来编码在输出边界显式按 ID 排序，不依赖容器枚举顺序。

### 2.4 Authority 与派生 baseline 分离

`NormalizedBaselineGraph` 是由已发布状态产生的、detached、可丢弃重建的比较投影，不是 authority。

未来持久集成的边界应是：

```text
Exact published head
    + authoritative StateMap / object records
    + matching NormalizedBaselineGraph
```

unchanged object 的旧 record address 由 authoritative StateMap 提供；不要复制进每个 baseline entry。当前尚无持久 head，首个探针不伪造 revision、record address 或 source token。

## 3. 主线依赖顺序

```text
R1 Graph Delta semantic probe (Concluded)
    -> R2 generated graph operations (Concluded)
    -> R3 normalized flat-graph Load and two-pass hydrate
    -> R4 in-memory StateMap and repeated logical delta apply
    -> R5 binary codec for the proven logical IR
    -> R6 persistent publication and recovery
    -> R7 measurement-driven optimizations
```

每一步只提升已经由前一步证明的概念。阶段编号表示依赖顺序，不是发布日期承诺。

## 4. 分阶段实验

### R1：手写单类型 Graph Delta 语义探针

状态：Concluded（EXP-011）。实现与 21 个聚焦测试位于 `tests/DurableGraph.Tests/GraphDeltaProbe*.cs`；它们只构成 fixture evidence，不是 runtime product API。

问题：最小的 identity-aware、cycle-safe、reachability-based diff 是否成立？

最小切片：

- 一个 sealed、自引用的 fixture-only `ProbeNode`；
- 一个临时正整数 `ProbeId`，不冻结正式 `DurableId`；
- scalar field、两个 nullable durable references 和一个 transient field；
- flat current-Snapshot baseline 与 `RequiresRewrite`；
- iterative traversal、whole-object Upserts、Unreachable 和 resulting RootId；
- test-only `AcceptForAssertion`。

可执行闸门：

```text
AcceptForAssertion(B, Diff(B, current))
    == CaptureCleanForAssertion(current)
```

并覆盖 no-op、leaf locality、共享引用、循环、duplicate ID、root replacement、升级后强制重写、不可达 upgraded node、失败重试和成功后再次 Save 为空。

本轮不回答：正式 ID、异构图、Generator、Store、bytes、commit、并发机制或性能。

### R2：Generator 产生图操作

状态：Concluded（EXP-012）。实现是 Generator 程序集内默认不可发现的 internal probe generator；它只由测试显式运行，不是 package/runtime capability。

问题：Generator 能否复刻手写 oracle，而不把 weak payload 或 wire format 泄露给正常路径？

已验证的 provisional generated seams：

```text
CaptureCurrent(value, Func<T, TIdentity>, out Snapshot, out CapturedReferences)
DurableEquals(in Snapshot, in Snapshot)
VisitReferences(in CapturedReferences, Action<T>)
```

`TIdentity : struct` 由调用方提供；Generator 不识别 identity field、不定义 allocator。Snapshot 的 self-reference slot 保存 `TIdentity?`，CapturedReferences 保存具体 CLR child。Capture 先按 FieldId 把每个 durable field 读入一次 local，再用同一 reference local 同时形成 ID slot 与 child slot。

可执行结果：

- 10 个新增动态编译 cases 覆盖 shared two-node cycle、重复 alias visitation、四种 scalar、transient、same-ID/different-child、child locality、child replacement、unsupported reference shape、reserved member 和 FieldId-order output；
- generated-driven delta 对 no-op、transient-only、child scalar change 与 same-valued child replacement 同时满足 literal expected results 和 EXP-011 oracle；
- Roslyn symbol/syntax/semantic checks证明 generated Snapshot、captured refs、signature、local 与 conversion 不进入 `object` / `dynamic` registry；
- default `DurableSchemaGenerator` 仍以 DG0007 拒绝 self-reference，probe class 没有 `[Generator]`，因此 package analyzer discovery 与严格 1...4 Snapshot History 未改变。

本轮只证明单个 self-referential durable type 的代码生成 seam。`TIdentity`、`Func`/`Action`、private generated names 和 visit order 都是 provisional；正式 `DurableId`、Reference TypeTag、产品 Generator 接入、异构 dispatch、polymorphism 与跨程序集引用继续暂缓。

### R3：读取归一化与两阶段对象图物化

问题：能否把 exact historical records 全量归一化成 current Snapshot table，并恢复共享引用和循环 CLR graph？

为保持失败定位清楚，本阶段包含两个依赖明确的小切片。

#### R3a：StoredGraphImage → normalized baseline

R3 首轮仍可沿用 test-only logical IDs/Snapshots 研究 Load 状态律；这不要求先把 EXP-012 probe 接入产品 Generator，也不授权定义 Reference TypeTag 或 wire bytes。

输入是 test-only immutable `StoredGraphImage`：显式 RootId、完整 `SourceRecordIds` 和 exact logical records；它不是 authoritative StateMap 或 persistent head。

最小流程：

1. 从 StoredGraphImage 取得 root 和完整 `SourceRecordIds`；
2. 每个 record 先 exact Schema validation，再 decode/upgrade 到 current Snapshot；
3. historical record 对应的 entry 标记 `RequiresRewrite`；
4. 任一节点失败时不暴露 partial baseline；
5. 升级后的每个 non-null durable reference 必须仍指向 `SourceRecordIds` 内的 ID；引用 source table 之外的旧 record、垃圾 record 或新 ID 均视为 unsupported/dangling，all-or-nothing fail。

baseline 的 ID set 保留 `SourceRecordIds`。升级可能删除引用，使其中部分 source nodes 相对 current root 已不可达；这些节点留待下一次 Save 进入 `Unreachable`，不能在归一化阶段静默丢失。升级创建新 durable node 或重新接入 source table 之外的 ID 继续暂缓。

可执行闸门：mixed stored versions 全部变成 current Snapshot；只有历史节点 flagged；unknown version、shape mismatch、source-external reference 均 all-or-nothing fail。

#### R3b：normalized baseline → current CLR graph

最小流程：

1. 从 normalized current references 计算 `CurrentReachableIds`；
2. allocate-all reachable CLR placeholders；
3. hydrate-all scalar fields and references；
4. 验证共享引用和循环；
5. 最后才允许未来的 transient rebuild / invariant phase 介入。

可执行闸门：sharing/self-cycle/two-node cycle 在 materialize 后恢复 `ReferenceEquals`；缺失目标 ID 不暴露 partial graph；R3a 中因升级变得不可达的 source entries 不进入 exposed current root closure。

### R4：内存 StateMap 与重复逻辑 delta apply

问题：在没有 bytes、head 和 crash model 的情况下，root、record reuse 与多次顺序 delta apply 的语义是否闭合？

最小切片：

- current logical StateMap，保存 ID 到 logical object-record 的绑定；
- 与该 StateMap 同源的 normalized projection；
- unchanged ID 继承旧 record，Upsert 产生新 record，Unreachable 不进入新 StateMap；
- delta 按测试给定的顺序依次 apply；
- candidate failure 不替换旧 StateMap/baseline。

可执行闸门：

```text
SequentialApply(base, delta1, ..., deltaN)
    == expected logical StateMap N

LoadNormalized(logical StateMap N)
    == expected current Snapshot table
```

这一阶段只验证逻辑 state transition，不让 delta 脱离当前顺序独立应用，也不承诺 revision、文件布局、原子 publish 或 durability。

### R5：为已证明的逻辑 IR 增加 binary codec

问题：能否为 Schema、object record、StateMap 和 logical delta 定义 deterministic、bounded、fail-closed 的 bytes，而不改变 R1-R4 的语义？

开始本阶段时重访 DB-001，裁决 Schema integrity/reference binding 是否需要 hash、完整 descriptor 或其他表示，并明确字段顺序、数字和字符串编码、null、length、reference ID、limits、unknown tag/version 与 trailing data。

可执行闸门：

- logical value round-trip；
- golden bytes；
- independent reader/writer agreement；
- truncation、oversized length、unknown tag、Schema mismatch 和 dangling reference fail closed；
- decode 后运行与 R4 相同的 materialization/delta laws。

### R6：持久 Store、publication 与故障恢复

问题：candidate records、StateMap 与 head 在明确故障模型下何时成为 authority？

本阶段才比较并裁决：

- object records 是否 append-only；
- exact head / expected-parent publication；
- 失败 candidate 如何分类、保留或回收；
- publication outcome 不明确时是否以及如何 reopen/reconcile；
- cache 与 exact head、current schema-set identity 的绑定；
- SchemaStore / StateStore 的真实 commit 边界。

可执行闸门必须来自 fault injection 和 reopen，而不是只看正常返回。publication 明确成功后才能安装 clean baseline；若 head 已成功但内存 cache 更新失败，丢弃 cache 并从 authority 重建。

### R7：有测量依据的优化

只有真实性能或容量数据出现后，才分别实验：

- 不物化完整 baseline、直接读取 delta chain；
- baseline cache、typed buckets 或更紧凑索引；
- per-object fingerprint / graph fingerprint；
- field-level sparse patch；
- frozen subgraph 快路径；
- hash table、sorted table、paged index 的替换。

优化不得改变 exact authority、reachable live set、reference-by-ID equality 或失败时 baseline 不变的语义。

## 5. 保持独立的研究分支

以下能力不横向塞进主线阶段；出现真实 consumer 后各自建立小实验：

- durable value struct 与递归 value codec；
- collection identity、ordering、comparer 和共享实例；
- durable inheritance flattening（DB-005）；
- heterogeneous graph、polymorphism 和同 ID Schema identity gate；
- multi-root、跨 root 共享和 ownership；
- `RebuildTransient` 与 graph-level invariant validation；
- upgrade handler 创建新 DurableId / 新 durable node；
- concurrent mutation、async Save 与 snapshot isolation；
- branch/fork、multi-writer 和跨 Repository identity。

## 6. 路线图维护规则

- 每一阶段开始前写清问题、最小成功/失败判据和明确非目标。
- executable evidence 成立后，把结论写入实验簿；路线图只保留尚未完成的依赖关系，不演化成完成历史。
- 若实验推翻当前模型，优先修改或删除后续阶段，而不是增加兼容层保存偶然原型形状。
- 任何会冻结 durable format、public identity 或 publication semantics 的选择，都应先记录竞争方案和重访触发条件。

## 7. 相关材料

- `docs/design-branches/0006-flat-graph-delta-prototype.md`
- `docs/design-branches/0001-schema-authority-and-runtime-representation.md`
- `docs/design-branches/0002-read-time-version-upgrade-pipeline.md`
- `docs/design-branches/0005-durable-inheritance-flattening.md`
- `docs/DurableGraph-lab-notebook.md`
- `docs/DurableGraph-target-design-v0.md`
