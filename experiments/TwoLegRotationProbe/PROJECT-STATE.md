# TwoLegRotationProbe 活跃工作集

> 状态：Active Research Context
>
> 最近校准：2026-08-30
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

因此同一 epoch 内 debt ObjectId set 单调不增，但 debt 对象的 Base payload bytes、exact terminal-C cost
和 Current-file next-start slack 未必单调。

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
- test-local `DebtZeroThenRotate` 进展基线：target 只读本次 Save 之前的 source A-debt；四步 all-cold
  trace 中 no-migration 完成有限前缀但保留 deferred debt，paced-one-debt 前三步清债并只在第四次 Save
  真正轮转；completion certificate 明确保持为反事实可行性证据，不算策略进展；
- test-local rotation observation reductions：每个 realized step 冻结各自 scope 下的 source/result debt、
  live-object reconstruction Previous Frames、Current tail/next-start slack 与写入分项；连续 SourceScope
  归成 observed epoch，Rotate 属于并关闭旧 epoch；certificate terminal-C 单独标记为 counterfactual，
  不进入 realized totals/peaks；一个两 epoch witness 已验证 `A/B -> B/C -> C/D` 的跨步连续性；
- test-local decision selector：只从 canonical facts 投影完整 Stay-B/Rotate-C decisions，与 target selector
  正交；changed A-debt 固定对照已验证 Base@B 能把旧 A evacuation 分摊到自然 Update，但换腿后会成为
  新 B/C scope 的 Previous debt；
- caller-selected B same-state Base migration plan/append witness；
- relay-free immediate A/B -> B/C plan/append、shared anchor、B/C closure witness；
- 多批 B migration 使原本放不下的 C evacuation 可编码的容量 witness；
- terminal sizing 反例：high-ticket External 不支配 zero-payload Base+Self。

Stay-B 与 Rotate-C 已接入同一 per-Save facts、paired evaluation、显式 apply、保守 completion proof 与
连续多轮转调用节奏。当前连续 witness 仍是 test-local caller script，不是自动策略或通用 Runner。
当前对照覆盖外部固定日程与故意保守的 `DebtZeroThenRotate` trigger，仍不代表自动 pressure-aware
rotation trigger 已解决。

## 当前研究焦点

changed A-debt write-mode discriminator 已闭合：同一四步 trace、固定 `[Stay, Stay, Stay, Rotate]` 下，
Delta control 把 608 B mandatory evacuation 集中到最终 Rotate，峰值 append 为 668 B；Base treatment
把三个完整值作为 foreground 分散到前三次 Stay，最终 Rotate append 为 60 B，峰值为 344 B。处理组的
旧 A debt 依次 `600 -> 500 -> 300 -> 0`，但对象共居同一个 A Frame 时，Previous-frame bytes 在最后
一个依赖消失前不下降。换腿后三个 Base@B 又成为新 scope 的 600 B / 3 Frames / 720 B Previous debt。
这些都是当前 provisional grammar 的因果事实，不构成总分或 winner。

下一步研究自然 foreground rebase 与 paced cold migration 的 **交互**：在同一 mixed Update/NoChange
trace 与固定 target 日程下做命名的 2x2 treatment，判断两种清债机制是替代、互补，还是只移动写入峰值
与换腿后的 debt 锯齿。继续保留原始事实，不引入自动 target trigger。

## 下一编码切片

闭合一个 **changed-write × cold-migration interaction probe**：

```text
same frozen mixed Update/NoChange trace + same fixed target schedule
    changed A-debt: legal Delta | Base
    optional unchanged A-debt migration: none | paced one
    compare four named treatments with existing raw reductions
```

优先验证 treatment assignment 互不重叠、四条运行共享 trace/targets/逻辑状态，以及各自 completion
certificate 仍可比较；不先引入交互 score、winner、通用 policy interface 或有状态 Runner。若 2x2 fixture
开始重复大量 expectation plumbing，再按证据提取 test-local projection helper。

## 近期 roadmap

1. **扩充策略与 workload**
   - 先做 changed A-debt Base/Delta 与 paced cold migration 的 2x2 交互对照；
   - 随证据加入 pressure-aware target/migration 候选；
   - stable hot/cold、burst、size distribution 和更长 fixed-seed traces。
2. **按证据加入 bounded explorer**
   - 仅在出现具体 `RejectedUnproven`、`RejectedCapacityUnsearched` 或疑似 heuristic false-negative 后，
     冻结该小状态；
   - 用同一 unified action builder 做 canonical bounded search；`NotFoundWithinBounds` 不外推一般无解。
3. **再研究自适应策略**
   - 根据连续运行暴露的压力、峰值和反例设计 capacity/pressure-aware 候选，而不是先冻结权重。

## 未闭合事项

- unbounded preference cost 如何比较两个 target，同时保持原始多目标事实而不偷渡权重；
- `RejectedCapacityUnsearched` 出现多频繁时值得加入次优 action menu 或 constraint-aware repair；
- 除 `DebtZeroThenRotate` 这个故意保守的 baseline 外，哪些无隐藏权重的 pressure facts 足以触发轮转；
- selected action 的 `RejectedUnproven` 在 batch report 中如何表达；当前 harness 只 typed stop、不 fallback，
  何时值得另立 repair policy 或交给 bounded explorer 仍待证据；
- test-local decision selector 已有三个结构不同的 caller；是否提取稳定 runner/run outcome 仍等待报告、
  CLI 或 batch consumer，不能仅因下一 fixture 变长就升级为产品 API；
- successful-run reduction 尚不表达 selected capacity / completion `RejectedUnproven`；出现真实 batch
  termination consumer 时应另建 outcome，而不是伪造没有 result scope 的 realized step；
- counterfactual terminal 当前只投影 final-C append/result 与 preparatory Stay count，不聚合互斥未来，
  也不声称已观测 preparatory writes 或 terminal-source pressure；
- stable hot/cold、burst、size distribution 与长 trace 是否先用 handwritten fixture，何时扩充 generator；
- 2x2 interaction 之后，何种无隐藏权重的 pressure facts 最值得驱动 target/migration treatment；
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
