# DurableGraph 后续研究与实现路线

> 状态：Living Roadmap  
> 更新日期：2026-08-28
> 用途：记录当前证据支持的研究顺序、每个切片的问题和可执行闸门。  
> 边界：本文不是当前实现事实、冻结 API 或持久格式规格；源码、测试和可复现输出优先，已完成实验的事实记录在 `DurableGraph-lab-notebook.md`。

## 1. 当前出发点

截至 EXP-014，仓库已经证明：

- 四种 scalar field 可以通过 generated boxed serializer 保存和加载；
- stored exact Schema 在 payload decode 前 fail closed；
- checked-in Snapshot History 可以重建历史 ordinary struct Snapshot；
- generated coordinator 可以用唯一一次 version switch 和静态相邻链把历史 Snapshot 升级到 current；
- Load 只在内存中升级，只有后续显式 Save 才推进 Store 中的版本。
- fixture-only EXP-011 已证明单类型 flat baseline、identity-aware traversal、whole-object delta、`RequiresRewrite` 与 success-only clean baseline 的逻辑状态律。
- isolated EXP-012 已证明 Source Generator 可以为单个 direct-self-reference 类型产生强类型 current Snapshot capture、durable equality 与同次字段读取得到的 reference visitation，并与 EXP-011 oracle 对齐。
- fixture-only EXP-013 已证明 mixed-version StoredGraphImage 可以在全表 exact preflight 后，通过强类型 decode/upgrade 归一化为保留完整 SourceRecordIds 的 current-Snapshot baseline；decode、upgrade 或 reference failure 不返回 partial baseline。
- fixture-only EXP-014 已证明 normalized current-Snapshot baseline 可以按 current root closure allocate-all/hydrate-all，恢复 scalar、sharing 与 cycles；disconnected source rows 不物化，allocation/hydration failure 不返回 root 且可重试。

当前尚未实现：

- `DurableId` 与对象身份分配；
- production Generator/runtime 中的 durable reference、共享引用、循环图和 reachability；
- production 对象图 baseline、Graph Delta 或 object version；
- binary wire format、持久 StateStore 或 SchemaStore；
- commit publication、并发、crash recovery 或 durability；
- durable value struct、collection、领域继承和 `RebuildTransient`。

因此，后续路线不能把 target design 中的完整系统描述成已经存在，也不应让尚无消费者的格式、缓存或兼容层先塑造核心语义。

## 2. 当前研究方向

### 2.1 当前优先验证 StateStore 双腿轮转策略

R1–R3 已经使 logical graph 的后续状态律相对清晰。当前最大设计不确定性转为 StateStore 的 two-leg file rotation：在每个 published current Revision 最多引用 current/previous 两文件的前提下，能否以渐进 Base/Delta/relay 行为避免集中 full checkpoint，并形成稳定的自适应策略。

首轮仍使用纯内存、deterministic 模拟，不绑定真实 RBF I/O。模拟必须把 two-file reconstruction closure、`RelativeFrameTicket` 可表示范围、one-frame bounds 与 relay completion 当作 correctness oracle；Base/Delta、cold migration 与 rotation 时机只是被比较的 policy。

相关基础设计与开放分叉：

- `state-store-base-design.md`
- `state-store-base-derived.md`
- `state-store-addressing-design.md`
- `design-branches/0007-adaptive-two-leg-rotation-policy.md`

### 2.2 继续闭合 logical graph 语义，再固定产品 bytes

R1/R2 已回答 current graph capture/delta，R3a/R3b 已闭合 historical records → normalized baseline → current CLR root。R4 仍将验证 logical StateMap、record reuse 与 repeated delta apply；它没有被否定，只是当前研究优先级让位于风险更高的 two-leg rotation 策略。

继续使用 test-only 内存逻辑值。`BinaryReader` / `BinaryWriter` 只在 logical Load/materialize/delta 状态律闭合后介入，避免过早冻结 framing、引用编码、canonical order 和 malformed-input contract。

### 2.3 历史版本在读取边界归一化

磁盘或逻辑 Store 中的 historical payload 必须先经过：

```text
exact Schema lookup and shape validation
    -> exact-version typed decode
    -> unique adjacent typed upgrade chain
    -> current-version typed Snapshot
```

进入内存 baseline 后，每个节点只暴露该 durable type 的 current Snapshot。Graph Delta comparer 不再知道 historical Snapshot 类型或版本分派。

若节点由历史版本升级而来，其 baseline entry 带 `RequiresRewrite`。只要它在下一次 Save 中仍可达，就必须写入完整 current Snapshot；若已不可达，则只从新图中移除，不为升级义务而保活。

### 2.4 逻辑图展平，引用只保存 DurableId

baseline 的逻辑形状是：

```text
RootId + Map<DurableId, BaselineEntry>
```

Snapshot 中的 durable reference slot 保存目标 `DurableId`，不嵌套另一份 Snapshot。这样共享引用只出现一次，循环图有限可表示，引用 equality 也只比较 ID。

Map 使用哈希表、排序表还是其他索引属于实现与测量问题；确定性测试、诊断和未来编码在输出边界显式按 ID 排序，不依赖容器枚举顺序。

### 2.5 Authority 与派生 baseline 分离

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
    -> R3a normalized flat-graph Load (Concluded)
    -> R3b two-pass CLR hydrate (Concluded)

Current priority research track:
    S1 in-memory adaptive two-leg rotation simulation
        -> S2 RelativeFrameTicket / one-frame layout probe
        -> S3 RBF publication and reopen fault probe

Product vertical sequence retained:
    R4 in-memory StateMap and repeated logical delta apply
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

状态：R3a Concluded（EXP-013）；R3b Concluded（EXP-014）。

问题：能否把 exact historical records 全量归一化成 current Snapshot table，并恢复共享引用和循环 CLR graph？

为保持失败定位清楚，本阶段包含两个依赖明确的小切片。

#### R3a：StoredGraphImage → normalized baseline

R3 首轮仍可沿用 test-only logical IDs/Snapshots 研究 Load 状态律；这不要求先把 EXP-012 probe 接入产品 Generator，也不授权定义 Reference TypeTag 或 wire bytes。

实现采用 immutable `StoredGraphImage`，其 record-table keys 是唯一 `SourceRecordIds` authority。V1/V2 payload 是封闭强类型 variants；test-only exact Schema descriptor 使用 `Int32`/`Reference` logical kind，不修改产品 TypeTag。

输入是 test-only immutable `StoredGraphImage`：显式 RootId、完整 `SourceRecordIds` 和 exact logical records；它不是 authoritative StateMap 或 persistent head。

已验证流程：

1. 从 StoredGraphImage 取得 root 和完整 `SourceRecordIds`；
2. 先按 ID 对全表执行 SchemaId、known version、exact shape 与 payload variant preflight，期间不调用 Decode；
3. 每个 record typed decode；V1 通过 `void(in ProbeSnapshotV1, out ProbeSnapshot)` 升级到 current，V2 直接得到 current Snapshot；
4. historical record 无论值是否改变都标记 `RequiresRewrite`，current record 不标记；
5. current/upgraded Snapshot 的每个 non-null reference 必须属于完整 `SourceRecordIds`，包括 disconnected source entries；
6. 全部成功后才一次性构造并返回 `NormalizedBaselineGraph`。

baseline 的 ID set 保留 `SourceRecordIds`。升级可能删除引用，使其中部分 source nodes 相对 current root 已不可达；这些节点留待下一次 Save 进入 `Unreachable`，不能在归一化阶段静默丢失。升级创建新 durable node 或重新接入 source table 之外的 ID 继续暂缓。

可执行结果：9 个聚焦 tests 覆盖 mixed/reversed records、value-changing/value-preserving upgrades、全表 schema-before-decode、unknown version、payload variant mismatch、missing handler、decode/upgrade late failure 与同 image retry、current/upgraded/default external reference、结构/defensive-copy gate，以及升级删边后保留 source entry 并交给 R1 Save 判为 Unreachable。

本切片只保证 loader 不修改输入、不返回 partial baseline；用户 decode/upgrade hook 自身的外部副作用不具备回滚语义。该 current-Snapshot baseline 随后成为 R3b 输入；StoredGraphImage 与 probe Schema 仍未提升为产品 API。

#### R3b：normalized baseline → current CLR graph

已验证流程：

1. 从 normalized current references 计算 `CurrentReachableIds`；
2. allocate-all reachable CLR placeholders；
3. hydrate-all scalar fields and references；
4. 验证共享引用和循环；
5. 全部成功后只返回 root，不暴露 placeholder map。

可执行结果：7 个聚焦 tests 覆盖 R3a→R3b→R1 组合路径、allocate-all-before-hydrate-all、shared alias、self-cycle、two-node cycle、disconnected source skip、constructor/initializer bypass、transient zero、one-time identity bind、重复物化不缓存，以及 late allocator/hydration failure 的 no-root 与 retry。invalid/dangling baseline 由 `NormalizedBaselineGraph` 在进入 materializer 前拒绝。

materialized root 是可丢弃 working graph，不是 baseline、StateMap 或第二 authority；`RequiresRewrite` 不进入 CLR object，只影响后续 Save。当前 phase hook 与 allocator 都是 test-only fault-injection seam，且不暴露 placeholder。未来若加入 `RebuildTransient` 或 graph invariant validation，root exposure boundary 必须顺延到这些阶段全部成功之后。

### S1：内存自适应双腿轮转策略模拟

状态：In Progress。当前优先研究切片；已建立 deterministic workload generation/replay substrate，以单文件 preparatory baseline 跑通 `AlwaysBase` / `AlwaysDeltaWhenLegal` 到 Frame、absolute live StateMap 与 symbolic materialization 的逐 Save 前缀闭环，并加入 exact RBF v0.40 envelope over explicitly incomplete `ObjectPayloadOnly` accounting。尚未接入 ObjectVersion/OVD/index codec、自适应 BaseOrDeltify、two-file rotation、完整 representability 或 `CanPrepareAndRotate`；不修改 R1–R3 已验证结论，也不把 StateStore working design 描述为产品实现事实。

问题：在不先引入固定 `MaxLogicalChainBytes`、`TargetFileBytes` 或 migration-byte budget 的情况下，能否用无权重事实量设计并比较 Base、Delta、渐进 cold migration、RelayRevision 与正式 rotation 的候选策略？

最小模型：

- A=OldPrevious、B=Current、C=Next 的纯内存文件与 Revision frames；
- per-object Base/Delta chain、latest head、terminating Base 与 absolute addresses；
- one Revision/one frame 的真实 payload/TailMeta/frame-start estimator；
- B 中 per-ObjectId relay forwarders与 C 中 evacuation Bases；
- ObjectVersionDict absolute-normalize / relative-encode 的模拟；
- 一个不含物理 I/O 的 logical graph oracle。

硬闸门：

```text
Materialize(candidate) == expected logical graph
ReconstructionFiles(candidate) ⊆ {Current, Previous}
CanPrepareAndRotate(successful post-state) == true
all frame starts/tickets/layouts are representable
failed plan leaves published state unchanged
```

模拟记录原始 bytes、frame sets、lineage、relay debt、useful/unused reads 与布局事实；所有比例和加权 score 后算。至少比较 AlwaysBase、AlwaysDelta-when-legal、StateJournal-style local cost、Previous-ratio、渐进 cold migration 与统一策略候选。

已完成的 S1 preparatory baseline 明确区分：StateMap 保存 `AbsoluteFrameAddress`，ObjectVersion 内 parent 保存由承载 frame 的 `FileScope` 解释的 `RelativeFrameTicket`；Create 写 Base，Update 由两条固定基线选择 Base/Delta，Remove 只删 live binding 但仍产生空 Revision Frame。Delta 以 `(ExpectedParentBasePayloadBytes, ResultBasePayloadBytes)` 构成可校验的尺寸态变换，`PayloadBytes` 只表示写成本。每个 Save prefix 均先 materialize 并与唯一 logical replay cursor exact compare，再成为下一步输入；整次 Run 失败不暴露 private candidate Store。当前只具备 object-payload-only 必要容量 preflight，没有完整 Revision capacity/rotation legality gate，因此在剩余输入内 `AlwaysDeltaWhenLegal` 仅等价于 AlwaysDelta，不代表一般 legality planner。

S1b 将 `FrameTicket` 推进为 offset/length，并按本地 RBF draft v0.40 精确建模 HeaderFence、24-byte frame fixed overhead、4B padding、trailing Fence、TailOffset、native start 与 DurableGraph 512 GiB relative-start 边界。Simulation 的输入仍严格标记为 `ObjectPayloadOnly`：只把 synthetic ObjectVersion payload 放入 RBF Payload，TailMeta=0，明确排除 ObjectVersion headers、OVD、index、VarUInt 与 self-ticket fixed point。因此当前可比较的是 synthetic payload write、frame sharing、reconstruction closure 与 co-read；不能从这些数字推出完整 Revision bytes、真实容量安全或策略 winner。write metrics 是逐 Save event；post-save read metrics 是状态快照，默认不跨 Save 求和。

本切片不实现真实 `DurableFlush`、atomic HEAD、reopen/truncate 或文件删除。逻辑策略收敛后，S2/S3 分别验证地址/layout 与 filesystem publication；文件被物理删除后不可访问不属于格式需要抵抗的故障模型。

### R4：内存 StateMap 与重复逻辑 delta apply

问题：在没有 bytes、head 和 crash model 的情况下，root、record reuse 与多次顺序 delta apply 的语义是否闭合？

最小切片：

- current logical StateMap，保存 ID 到 logical object-record 的绑定；
- 与该 StateMap 同源的 normalized projection；
- 初始 source StateMap 允许包含因 read-time upgrade 改边而相对 current root disconnected 的 rows，以及对应 rewrite obligations；
- unchanged ID 继承旧 record，Upsert 产生新 record，Unreachable 不进入新 StateMap；
- delta 按测试给定的顺序依次 apply；
- candidate failure 不替换旧 StateMap/baseline；成功 apply 后安装 exact result-root closure 与 clean baseline。

可执行闸门：

```text
SequentialApply(base, delta1, ..., deltaN)
    == expected logical StateMap N

LoadNormalized(logical StateMap N)
    == expected current Snapshot table

Materialize(LoadNormalized(logical StateMap N))
    == expected current CLR root closure
```

R4 的首个组合场景应从“disconnected source rows + reachable rewrite obligations”开始，证明 apply 后 StateMap 恰好成为 result-root closure。该阶段只验证逻辑 state transition，不让 delta 脱离当前顺序独立应用，也不承诺 revision、文件布局、原子 publish 或 durability。

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
