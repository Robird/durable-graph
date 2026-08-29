# TwoLegRotationProbe 活跃工作集

> 状态：Active Research Context
>
> 最近校准：2026-08-29
>
> 读者：继续研究本子项目的 Coding Agent 与维护者

本文只保存当前目标、已选不变量、最近路线和未闭合事项，用来抵抗会话上下文压缩与跨会话失忆。
它不是实现事实或设计 authority；若与当前源码、测试或用户最新决定冲突，以后者为准，并随手修订本文。

维护本文时：

- 替换过时结论，不按时间顺序追加工作日志；历史由 Git 和 `docs/DurableGraph-lab-notebook.md` 保存；
- “已具备”只保留理解当前路线所需的摘要，不复制测试清单或 README；
- 路线只保留未来几个依赖明确的切片，完成后删除或压缩；
- 明确区分硬约束、当前选择、待验证假说和开放问题；
- 不记录私密推理、凭据、临时命令输出或可从代码直接重建的细节。

## 目标

本探针研究一种没有明显短板的渐进双腿文件轮转策略。

每个 `SaveStep` 是一组彼此独立对象的 `Create` / `Update` / `Remove`；本项目不建模对象之间的引用、
reachability 或 GC。策略应利用每次自然 Save：

1. 为 changed objects 选择 Base 或 Delta；
2. 可顺带把一部分仍依赖 Previous file 的 unchanged objects 以同状态 Base 迁入 Current；
3. 逐步降低 latest PublishedRevision 的 Previous-file reconstruction dependency；
4. 在合适时机创建 Next file，把 `(A=Previous, B=Current)` 轮转为 `(B=Previous, C=Current)`；
5. 避免把集中 full checkpoint 变成明显的单次写入峰值，同时不过度牺牲总写入、读取或文件利用率。

当前不追求无 workload/SLO 前提下的唯一标量最优解；先寻找在多类确定性 workload 上不被明显支配的
Pareto 候选。

## 最小心智模型

在一个 A/B epoch 内：

```text
A = Previous / 后脚
B = Current  / 前脚，承载 latest PublishedRevision

ADebtSet = terminating reconstruction Base 仍位于 A 的 live objects
```

`head@B -> Delta@B -> Base@A` 仍属于 A debt。历史 lineage 即使经 shared anchor 回到 A，也不属于
current reconstruction dependency。

普通 B Save 对 debt set 的作用：

- Create@B：不加入 A debt；
- Remove：若对象在 debt set 中则移除；
- changed object 写 Base@B：移除 debt；
- changed object 写 Delta@B：保留原 debt 状态；
- selected unchanged debt object 写 same-state Base@B：移除 debt。

因此同一 epoch 内 debt ObjectId set 单调不增，但 debt 对象的完整 Base bytes、exact terminal-C cost
和 B headroom 未必单调。

规划前把 parent snapshot 与本次 changes 归一化为四个互斥集合：

```text
Insert / Update / Remove / NoChange
PostLive = Insert + Update + NoChange
```

Remove 先从 post-Save live set 删除，后续沿 candidate bindings 统计时自然不可达；它没有 ObjectVersion
保存动作，但仍改变 Meta ObjectMap/OVD、debt 和 whole-candidate size。NoChange 不属于 foreground
workload，却必须作为 parent snapshot 的显式派生集合参与 cold migration 和 C evacuation。

StateJournal 的 `_objectMap` 说明了可整合形状：unchanged objects 不写新版本，changed/new 更新 map，
unreachable keys 删除，ObjectMap 自己再按普通 VersionChain 增量保存。Probe 当前以 runtime OVD 承担
同类 live-binding authority，并把 Meta bytes 纳入 candidate 定尺。

外层 `Commit(rootObject)` 可以处理 CLR graph/session validation、diff 与 payload freezing；TwoLeg 策略
边界只消费相对同一 parent snapshot 验证过的 immutable、canonical object facts。parent 的 contextual
relative locator 在进入规划 view 时 absolute-normalize；NoChange/live facts 由 PublishedRevision OVD
authority 派生，caller cache 只能加速而不能成为第二 authority。

Rotate-C Save 必须把本次领域变化与轮转动作合在同一个 candidate 中：剩余 A debt 写完整 Base@C；
B-contained objects 可 External，必要时也可选择 same-state Base+Self；C 写 full OVD Base，并以 B
PublishedRevision 为 shared prior-snapshot anchor。accepted new head 的 current reconstruction closure
必须只涉及 B/C。换腿后 debt 相对新 FileScope 重新形成锯齿，而不是永久归零。

## 已选择的不变量

- runtime OVD materialized from PublishedRevision 是 live ObjectId -> head address 的唯一 authority；
- 内存地址 absolute-normalize，写 candidate 时相对目标 Revision 编码；
- current reconstruction 与 historical lineage 分层；策略 debt、admission 和 score 不使用 lineage IO；
- one Revision / one provisional RBF Frame；frame-start、Payload+TailMeta、TailMeta 与地址范围是硬 gate；
- whole-candidate `ProvisionalRevisionV0Estimator` 是当前唯一尺寸 authority，不建立 per-object additive
  savings authority；
- preference Plan 可以暂时假设目标文件可容纳；accepted candidate 仍必须在 append 前通过唯一 exact
  feasibility filter。容量不参与首版组合优化，不等于放松 hard gate；
- 固定 target、membership、OVD Self binding 与其他 decisions 时，若合法 Base 的完整 encoded domain
  record 不长于 Delta，则 Base 在当前一步的 append、current reconstruction 与 debt 上弱支配 Delta；
  该规则不外推到正式 wire、CPU/内存或任意未来 continuation；
- failed candidate 不改变 accepted store/head；
- accepted post-Save state 必须有具体、有限、可重放的 `CanPrepareAndRotate` continuation certificate。
  当前不要求完备 solver；找不到证书只能称 `RejectedUnproven`，不能称一般无解；
- 文件被物理删除后的历史不可导航不属于格式需要抵抗的故障模型。

## 当前具备的实验积木

- 固定种子、可冻结重放的 Field/List 独立对象 workload；
- single-file `AlwaysBase`、`AlwaysDeltaWhenLegal`、local read-amplification=3 基线；
- runtime OVD、absolute StateMap projection、symbolic reconstruction 与 raw read/write observations；
- provisional RBF v0.40 whole-frame estimator；无 bytes writer/parser；
- 从 PublishedRevision OVD authority 派生的 immutable canonical
  `Insert / Update / Remove / NoChange` facts；
- 独立 `NormalizeMaintenanceOnly` 入口复用同一 source inspection，并在不放宽 `SaveStep` 非空约束的
  前提下产生全 NoChange facts；
- caller-explicit Stay-B candidate：同一 B Revision 合并 domain changes、Update Base/Delta、OVD Remove 与
  selected unchanged A-debt same-state Base；
- caller-explicit Rotate-C candidate：同一 fresh-C Revision 合并 domain changes、mandatory A-dependent
  Bases、B-contained Update Base/Delta 与 NoChange External/optional Base，并写 full OVD Base；
- caller-explicit paired evaluation：两侧从同一 facts 独立尝试，成功保留 exact plan/candidate/estimate
  identity，合法但撞到已知 Frame/address hard gate 时返回 typed bounded rejection；
- 无权重 raw candidate observations：从既有 estimate 的 domain records 与冻结 reconstruction paths
  派生 foreground/maintenance record bytes、PostLive full-frame reads 和相对结果 FileScope 的 Previous debt；
  candidate 不重估，source stored layout 则用同一唯一 estimator 验证 provisional provenance；
- caller-owned volatile probe cursor 与强类型 Stay-B / Rotate-C apply seam：变异前重验 source、tail、facts、
  candidate state/layout/anchor，失败不追加；不写 Store head，不声称并发、crash 或 durable publication；
- 保守 `CanPrepareAndRotate` certificate：在 exact scratch fork 上先试零迁移 Rotate-C，再按 ObjectId 升序
  每次迁一个 A-debt object；成功冻结完整 exact candidate chain，容量受阻只返回 `RejectedUnproven`；
- caller-scripted 连续 Save witness：只组合现有 normalized facts、pair、certificate 与 apply seam，已跑通
  `A/B -> B/C -> C/D`，并观察到 Previous debt `{10} -> {20} -> {} -> {20}` 的换腿锯齿；
- probe-only 无状态单步 policy harness：caller 显式选 target 后，统一处理 selected capacity、Stay completion
  admission 与 exact apply；typed rejection 不 fallback，cursor 仍由 caller 显式传入/接回；
- 固定 `[Stay, Stay, Rotate]` 的 cold-migration 因果对照：no-migration 与 paced-one-debt 共享同一 trace、
  target 和 foreground，只改变 Stay 的迁债集合；paced 降低单次/Rotate append 峰值，但换腿后留下更多
  Previous debt 与 frame bytes；
- caller-selected B same-state Base migration plan/append witness；
- relay-free immediate A/B -> B/C plan/append、shared anchor、B/C closure witness；
- 多批 B migration 使原本放不下的 C evacuation 可编码的容量 witness；
- terminal sizing 反例：high-ticket External 不支配 zero-payload Base+Self。

Stay-B 与 Rotate-C 已接入同一 per-Save facts、paired evaluation、显式 apply、保守 completion proof 与
连续多轮转调用节奏。当前连续 witness 仍是 test-local caller script，不是自动策略或通用 Runner。
当前新增对照只研究外部固定轮转日程下的迁债节奏，不代表自动 rotation trigger 已解决。

## 当前研究焦点

固定日程对照已证明 pacing 的 write-peak / post-rotation debt tradeoff。下一步把 target trigger 单独作为
实验变量，先研究一个明确但不保证优秀的 `DebtZeroThenRotate` 基线：

```text
same all-cold frozen trace
    -> no-migration caller can defer forever
    -> paced-one-debt caller can reach source debt zero
    -> Rotate only on a later Save whose source debt is already zero
    -> classify progress/stall without treating deferred writes as a win
```

继续把 target 与 migration selection 留在 test-local caller。已提取的单步 harness 只负责 admission/apply，
不升级为有状态 Runner 或 policy interface。`CandidateRawObservation` 已支持首轮因果对照；target-trigger
实验若需要表达 trace 完成但未轮转，可先用 test-local progress projection，不预先冻结通用 run outcome。

## 下一编码切片

闭合一个 **DebtZeroThenRotate progress/stall probe**：

```text
source A-debt is empty -> Rotate-C
otherwise              -> Stay-B

no migration           -> completed trace without rotation / deferred debt
paced one debt per Stay -> reaches zero, then rotates on the next Save
```

两条 caller 重放同一 all-cold trace；target 从 source normalized facts 派生，不使用 candidate winner、容量
rejection 或 certificate 作为隐式 trigger。Lazy 的低写入不能进入 Pareto winner，因为它把轮转义务推迟到
观测窗口外。该切片只验证 progress/stall 语义，不在其中加入 pressure 权重或 fallback repair。

## 近期 roadmap

1. **DebtZeroThenRotate progress/stall baseline**
   - 显式证明 no-migration 在 all-cold trace 上不会清债，certificate 存在也不等于 policy 会前进；
   - paced-one-debt 清零后只在下一 Save 轮转，避免用 prospective action 偷改 trigger；
   - 保留 completed-trace-with-debt 与 realized-rotation 的区别，不做成本 winner 排名。
2. **Rotation observation reductions**
   - 记录 debt count/full-Base bytes、Previous unique frames/bytes、B tail/headroom、domain/migration bytes、
     exact terminal-C estimate、per-Save/rotation peak、leg length、rotation count 与保守拒绝；
   - 首先由现有 `CandidateRawObservation` 与 cursor/certificate 做 test-local value projection；只有报告或
     batch consumer 出现后才固化 run/epoch 类型；
   - 保留原始量，不预设总分，报告 Pareto 与明显 dead-end/振荡。
3. **扩充策略与 workload**
   - Touch/ChangedDebtFirst 与 pressure-aware target/migration 候选；
   - stable hot/cold、burst、size distribution 和更长 fixed-seed traces。
4. **按证据加入 bounded explorer**
   - 仅在出现具体 `RejectedUnproven`、`RejectedCapacityUnsearched` 或疑似 heuristic false-negative 后，
     冻结该小状态；
   - 用同一 unified action builder 做 canonical bounded search；`NotFoundWithinBounds` 不外推一般无解。
5. **再研究自适应策略**
   - 根据连续运行暴露的压力、峰值和反例设计 capacity/pressure-aware 候选，而不是先冻结权重。

## 未闭合事项

- unbounded preference cost 如何比较两个 target，同时保持原始多目标事实而不偷渡权重；
- `RejectedCapacityUnsearched` 出现多频繁时值得加入次优 action menu 或 constraint-aware repair；
- 除 `DebtZeroThenRotate` 这个故意保守的 baseline 外，哪些无隐藏权重的 pressure facts 足以触发轮转；
- selected action 的 `RejectedUnproven` 在 batch report 中如何表达；当前 harness 只 typed stop、不 fallback，
  何时值得另立 repair policy 或交给 bounded explorer 仍待证据；
- 第三个 caller 是否会证明需要 test-local trace runner/run outcome；当前只提取无状态单步 admission/apply；
- stable hot/cold、burst、size distribution 与长 trace 是否先用 handwritten fixture，何时扩充 generator；
- paced-one-debt baseline 之后，何种无隐藏权重的 pressure facts 最值得比较；
- 多个不可支配策略出现后，何时需要用户用真实 workload/SLO 选择产品默认值。

## 明确暂缓

- object references、reachability、GC、serialization 与 durable graph product integration；
- byte writer/parser、正式 wire format、Extent/multi-frame Revision；
- durable publication、crash/reopen、concurrency、store identity 与文件 GC；
- 完备 feasibility solver、一般图搜索、策略插件框架；
- 更多 historical lineage 功能或查询优化；
- 在没有测量依据时预设 read/write/pause 权重。

## 证据入口

- 已实现模型与运行方式：[`README.md`](README.md)
- 活跃设计分叉：[`../../docs/design-branches/0007-adaptive-two-leg-rotation-policy.md`](../../docs/design-branches/0007-adaptive-two-leg-rotation-policy.md)
- Plan/容量分层：[`../../docs/design-branches/0011-two-phase-save-planning-and-capacity.md`](../../docs/design-branches/0011-two-phase-save-planning-and-capacity.md)
- StateStore 基础约束：[`../../docs/state-store-base-design.md`](../../docs/state-store-base-design.md)
- 地址 authority：[`../../docs/state-store-addressing-design.md`](../../docs/state-store-addressing-design.md)
- 阶段历史：[`../../docs/DurableGraph-lab-notebook.md`](../../docs/DurableGraph-lab-notebook.md)
