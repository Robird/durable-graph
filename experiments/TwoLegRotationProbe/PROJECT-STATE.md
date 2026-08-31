# TwoLegRotationProbe 活跃工作集

> 状态：Active Research Context
>
> 最近校准：2026-09-01
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

本探针研究没有明显短板的渐进双腿文件轮转策略，并将逐步收敛为统一 workload、统一 evaluator、
多策略独立实现的确定性实验赛场。Arena 负责规范输入、物理执行与测量；策略只负责在线选择。

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
- selected Stay-B 只有在存在具体、有限、可重放的 `CanPrepareAndRotate` continuation certificate 时才接受；
  Rotate-C 可直接 apply。evaluator 在 workload 后另行执行 canonical terminal settlement。当前不要求完备
  solver；找不到有限路径只能称 `RejectedUnproven`，不能称一般无解；
- 当前策略 profile 不持有跨 Commit 可变状态，只读取当前 `NormalizedSaveFacts` 的派生 projection；不得读取
  step index、未来 workload、candidate feasibility 或 observation 后再选择 target/decisions；只有命名反例
  证明这组输入不足时，才考虑增加一个最小事实；
- profile 比较只在同 manifest（因而 protocol/layout/read identities 相同）、同 workload、同 evaluator
  horizon 的 admitted outcomes 之间进行。效率比较使用 workload-only W/P 与 R；F 是包含 terminal settlement
  checkpoint 的 closed-horizon 文件容量/活性 guardrail，可能保留终相位敏感性，不与前三者合成标量。
  typed inadmissibility 保持在排序之外；
- canonical report 保持 typed position 与七个 admitted metric 整数：`Wworkload`、workload Delta/Base payload
  references、workload-only P、F、R、L。references 不是物理 baseline、界或 score。`Wterminal` 与
  `Wtotal = Wworkload + Wterminal` 的守恒校验、terminal cold-head T、final cursor、settlement 细节、可由
  manifest 推导的 metric counts、per-Save samples 与 Frame provenance 留在 evaluator 内部/owning tests；
- 文件被物理删除后的历史不可导航不属于格式需要抵抗的故障模型。

## 当前具备的实验积木

- 固定种子、可冻结重放的 Field/List 独立对象 workload；当前 corpus 有十七条可调 trace，包括针对
  oversized unmotivated NoChange 的白盒对抗样本；
- runtime OVD、absolute StateMap projection、symbolic reconstruction 与 provisional RBF v0.40
  one-Revision/one-Frame estimator；
- 从 PublishedRevision authority 派生的 immutable
  `Insert / Update / Remove / NoChange` facts，以及 payload-only `G/E/H/D/B` strategy view；
- caller-explicit Stay-B / Rotate-C planning、paired evaluation、exact hard-capacity filter、
  completion certificate 与无 fallback apply；
- evaluator v1：fresh Store、typed outcomes、一次真实 terminal settlement，以及
  canonical `Wworkload`、workload-only P、F、累计 R/L、Delta/Base references；terminal/total write
  accounting 继续在 evaluator 内部验证 closure 与守恒；
- `Arena <- Baselines <- Tests` 单向程序集边界；candidate 只作在线选择，Arena 持有
  normalize、定尺、admission、apply、settlement、状态验真和 metrics；
- 两个 active Adaptive bindings：read-amplification/Base-budget `(3,5%)` 与 `(4,4%)`，同名 profile
  component identity 已随本次行为修正升至 version 2；
- typed capacity/no-fallback/zero-mutation witnesses 与核心 evaluator/report determinism tests；
- no-migration 与 paced-one-debt 的完整 profile、64-case corpus 和专属相位诊断已退出主线，
  由 Git tag `research/no-migration-paced-baselines-20260901` 保存。test-local 的“不迁移/迁一个”
  动作可以继续作为机制对照，但不再是 profile、manifest case 或正式结果行。

## 当前研究焦点

Metrics identity `raw-wpfr/5`、report schema 5、Corpus revision 17 在十七条 trace 上运行两个
active Adaptive v2 profiles，共 34 admitted cases。
`DeltaReference` 与 `BaseReference` 是 strategy-independent 写入参照，替代退役 profile 的
“基线策略”职责。

`active-hundred-mixed` 当前提供最有用的长周期反馈：两组 profile 共享
`Delta/Base references=67206/165606` 与 `L=273804`；Adaptive `(3,5%)` 相比 `(4,4%)`
多 `1668B Wworkload`、workload-P 高 `24B`，但少 `32920B F` 与 `253048B R`。这证明当前是
自然写入/峰值与 closed-horizon 文件高水位/累计冷读之间的交换，也暴露 Adaptive 可能在持续活跃
对象上进行不必要 Base 写；它不是 winner、默认参数或 steady-state 结论。

Adaptive v2 已移除 unconditional progress 与 NoChange-first：Update 以严格 `(H+D)/B`、目标支持的
NoChange 以严格 `H/B` 产生 Base 动机；统一按放大率/ObjectId 排序并选择预算内最长前缀。Stay 只允许
首个有动机的不可分对象超出 `Q`；Rotate 的强制 `E` 先消费 `Q`，且仅 `E==0` 时允许可选首对象超额。
`Q` 是 payload pacing proxy，不是 exact physical P cap。对抗 workload 证明旧实现曾产生 `P=10052`
的 10,000-byte 无动机迁移；当前回归只固定 `P<10000`，避免锁定偶然度量值。

## 下一编码切片

先对白盒 workload 复审 Adaptive v2 的两个剩余结构性问题，再只选择一个最小独立改动：稳定 A debt
在没有严格 Base 动机时可能停滞，以及 `E/G` 只表示 Ready-to-Rotate、不能回答 Should-Rotate。
不得恢复 unconditional progress、增加未来视野或 feasibility oracle；复跑 34-case suite 并报告
typed outcomes、references 与 raw metrics，不合分、不排榜。

## 近期 roadmap

1. **隔离下一弱点**：用最小白盒 trace 区分稳定债务停滞与 Rotate hysteresis，确认哪一个先成为可量化短板；
2. **实现策略回应**：只对已证实短板构造一个最小独立 candidate，不改变 Arena contract 或让策略读取
   未来/feasibility；随后报告 typed outcomes 与 raw metrics，不合分、不排榜；
3. **按弱点而非目录扩容**：只有 candidate 复审指出新的具体 blind spot，才增加或调整最小 validation；
   不再按 generic axis 或 seed 数机械扩 corpus；
4. **候选成形后再封包**：闭合 determinism/order/artifact 与 qualification gates，冻结 `ROUND-1` packet/tag，
   再启动并行 candidate round。

## 未闭合事项

- 哪些互相正交的因果 workload 轴足以支撑“没有明显短板”仍未知；统一 suite 只提供可重复 synthetic evidence，
  不能声称代表生产；
- 无 unconditional progress 后，低放大率的稳定 A debt 可在 Stay 中长期不动；应由 hysteresis、债务年龄或
  其他最小机制解决，还是接受为读写交换，尚未裁决；
- 当前 `E/G` Rotate trigger 只证明迁移代价低于比例阈值，没有 leg age/write/file pressure，因此混淆
  Ready-to-Rotate 与 Should-Rotate；简单 hysteresis 的事实输入与收益仍待白盒验证；
- 当前 payload-only canonical toolkit 是否足以产生结构多样的首轮 candidate，需由首轮结果验证；Frame-aware
  facts 仅在 payload-identical 布局造成 canonical outcome 反转或 Frame-aware oracle 进入新 Pareto 点时进入 V2；
- 当前 profile-matrix manifest 可由 organizer 冻结候选后统一重跑；若出现分批 strategy report 缓存/比较
  consumer，再拆 strategy-neutral suite hash 与 per-strategy report identity；
- 当前 `StrategyRunProductV1` 由 Arena context 认证，不接受候选任意手工构造的 Store/ledger；若首轮策略确实
  需要绕过 canonical planner/apply toolkit，须先实现 exhaustive Frame enumeration、逐 Commit prefix-state
  validation、layout/ticket/OVD closure 检查与 offline `Wworkload/Pworkload/F/R/L` recomputation，并独立复核
  internal terminal/total write conservation；
- `StrategyBindingV1` 每 case 调用 executor factory，防止意外复用 captured instance；static mutable state
  仍不是机械隔离的故障模型，首轮 packet 需加入 fresh-fork repeat 与 case-order permutation gate；
- 产品若最终必须发布唯一默认 profile，仍需要真实 workload/SLO 给出 Peak、file tail 与 read guardrails；
  在此之前只报告 per-workload Pareto 与 `no winner`，不使用裸加权和或严格 Wworkload-first 字典序。

## 明确暂缓

- object references、reachability、GC、serialization 与 durable graph product integration；
- byte writer/parser、正式 wire format、Extent/multi-frame Revision；
- durable publication、crash/reopen、concurrency、store identity 与文件 GC；
- 完备 feasibility solver、一般图搜索、动态策略插件/MEF/assembly scanning；
- 更多 historical lineage 功能或查询优化；
- benchmark manifest/report parser、artifact file I/O、CLI publication 与 benchmark/product API promotion；
- per-Save cold-read vector artifact、Peak/P95 read guardrail 与非均匀 restart 权重；仅在出现真实冷启动 SLO 时重启；
- secondary source-layout corpus；仅在真实布局分布可用，或 Shared/Split 导致 profile admissibility/Pareto
  关系反转时重启；
- 新 policy-visible pressure facts 与 bounded repair；保持 current facts-only、selected rejection 不 fallback，
  直到冻结 workload 证明存在系统性错误选择或已知可行但被排除的 candidate；
- weighted score、leaderboard、跨 workload 总分、自动参数搜索、自动进化循环与 frontier archive；并行独立
  candidate round 已获方向授权，但自动循环仍需冻结的代表性 suite、executable stop rule 与新授权；
- 独立 Contracts package、NuGet/ABI compatibility、hostile-code sandbox、隐藏测试平台；仅在第二
  engine/runner、独立发布或真实对抗性执行需求出现时重启；
- 在没有测量依据时预设 read/write/pause 权重。

## 证据入口

- 已实现模型与运行方式：[`README.md`](README.md)
- evaluator v1 admissibility/settlement/指标合约：[`EVALUATOR-V1.md`](EVALUATOR-V1.md)
- 当前候选策略契约：[`READ-AMPLIFICATION-BASE-BUDGET-POLICY-V0.md`](READ-AMPLIFICATION-BASE-BUDGET-POLICY-V0.md)
- 前人成果与算法参考：[`PRIOR-ART.md`](PRIOR-ART.md)
- 活跃设计分叉：[`../../docs/design-branches/0007-adaptive-two-leg-rotation-policy.md`](../../docs/design-branches/0007-adaptive-two-leg-rotation-policy.md)
- 多策略 Arena 边界：[`../../docs/design-branches/0012-two-leg-strategy-benchmark-arena.md`](../../docs/design-branches/0012-two-leg-strategy-benchmark-arena.md)
- Plan/容量分层：[`../../docs/design-branches/0011-two-phase-save-planning-and-capacity.md`](../../docs/design-branches/0011-two-phase-save-planning-and-capacity.md)
- StateStore 基础约束：[`../../docs/state-store-base-design.md`](../../docs/state-store-base-design.md)
- 地址 authority：[`../../docs/state-store-addressing-design.md`](../../docs/state-store-addressing-design.md)
- 阶段历史：[`../../docs/DurableGraph-lab-notebook.md`](../../docs/DurableGraph-lab-notebook.md)
