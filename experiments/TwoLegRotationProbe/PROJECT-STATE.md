# TwoLegRotationProbe 活跃工作集

> 状态：Active Research Context
>
> 最近校准：2026-08-31
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
  horizon 的 admitted outcomes 之间进行：exact `W/P/F/R` ties 归为等价类，四项均不差且至少一项更好
  才构成支配；typed inadmissibility 保持在排序之外；
- canonical report 保持 typed outcome 与 admitted `W/P/F/R/L/T`/scope/settlement 摘要；R 是每个成功
  workload Save 后 empty-cache cold load 的累计，L 是对应 post-live Base bytes 累计，T 是 terminal 诊断；
  per-Save samples 与 Frame provenance 留在 evaluator 内部/owning tests；
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
- role-disjoint 2x2 interaction witness：changed `{10,20,30}` 与 migration-only `{1,2,3}` 共居一个 A Frame，
  四个命名 treatment 证明两种机制在该 trace 上逐步退休 old-A debt 的集合互斥且可加；只有组合格在第三次
  Stay 后释放共享 Frame，换腿后各自在 B 写过的对象按新 scope 形成 Previous debt；
- current-reconstruction source-layout evidence：两侧使用同构的 metadata-only full-OVD A anchor，shared
  将六个 payload Base 共置一个 Frame，split 以唯一 accepted chain 把 cold/changed 分置两个 payload
  Revisions；相同 object-debt 轨迹可有不同 required unique Previous Frames。fixed-horizon 2x2 进一步
  证明同 treatment 的 Shared/Split 终点 W/P/F/T 可以相同，同时仍保留不同的 intermediate live-object
  Previous payload-Frame closure；新的累计 R 正是为了保存这种周期内差异；
- fixed-cadence continuation witness：前两 epoch 让 no-migration/paced 都以 8 Commits、两次换腿到 3/4，
  并以相同 live state/debt IDs/`H/B`/Current tail、不同 shared/split Frame provenance 收尾；两侧再共同采用
  paced selector 跑第三 epoch，终点 `W/P/F/T` 同为 `812/348/812/792`，但 workload cold-load 序列为
  `848,1104,792` 与 `792,792,792`，故累计 R 为 `2744` 与 `2376`。物理历史在被覆盖前的影响现已进入
  canonical 读取指标；该 cadence 是实验控制，不是自动 trigger；
- read-amplification + Base-budget policy v0：只读 payload projection 冻结 `G/E` 与 per-object `H/D/B`，
  pure selector 实现 strict ratio/rotation thresholds、weak dominance、soft budget 与 NoChange-first progress；
  exact threshold/apply、policy-selected capacity rejection、realized accepted-head `H/B` diagnostic 与两组
  parameter witness 已闭合，未改 planner、harness 或 canonical report schema；
- migration membership 因果组：equal-byte source topology 证明同成本 membership 会改变即时 Previous-Frame
  closure；payload skew 暴露本步少写与退出更多 old-A full-Frame bytes 的局部冲突；known-future、等尺寸
  hot/cold oracle 则证明把迁移预算投给下一步会 Update 且被对照强制写 Base 的对象，会错过本 trace 内
  保持不变的 cold debt。三者都保留
  terminal-C/换腿后压力反转，不选择 winner，也不冒充在线温度推断；
- caller-selected B same-state Base migration plan/append witness；
- relay-free immediate A/B -> B/C plan/append、shared anchor、B/C closure witness；
- 多批 B migration 使原本放不下的 C evacuation 可编码的容量 witness；
- grouped-foreground Frame-envelope coupling witness：同一 canonical facts、固定 Stay-B target 下，三项
  foreground Update 全写 Base 时 exact candidate 只剩不超过 32B envelope slack；再加入一个 10B
  optional same-state migration 后命中 typed `PayloadAndTailMetaLength` rejection。失败不 fallback、不改变
  Store/cursor；foreground-only 分支经 completion/apply 后只向 B 追加其 exact Frame；
- experiment-only raw metric accounting：显式 outer-Commit 边界按全文件 tail 增量计算 W/P，并在每个
  realized Revision checkpoint 跟踪 F；每个成功 workload Save 后用 empty-cache OVD+object reconstruction
  full-Frame 去重并集形成一个 cold-load sample，累计为 R/L；terminal head 另记 T。fresh-file 4B header
  计入写入，typed rejection 不进入指标；
- evaluator v1 closed-horizon session：每次 run 在独立 Store fork 上消费 caller-selected workload Commits，
  用互斥 typed outcome 分开 success、selected capacity、`RejectedUnproven` 与 incomplete；完整 workload 后
  无条件执行一次 `DirectRotateElseAscendingSingleDebt-v1` terminal settlement。直转优先，否则 ObjectId 升序
  单对象迁债；全部 preparation+Rotate 真实 apply 在同一个 synthetic Commit 中并计入 W/P/F，成功还验证
  terminal source A 已退出 final B/C current reconstruction；
- benchmark-v1 consumer：workload-only corpus 接收 organizer 提供的 strategy bindings；trace step0 Create-only
  population 共置为一个 full-OVD A Frame，
  再写 metadata-only B anchor，evaluator 只消费 steps[1..]；manifest 固定 fixture/trace/generator/seed、expanded
  trace SHA-256、atomic selection profile 与 evaluator/settlement/accounting/layout/grammar/read-schedule identity；
  batch runner 只在 session Store 上 normalize/evaluate/apply，typed rejection 不 fallback；canonical UTF-8
  manifest/report 使用固定 tokens/order、16位 hex seed、manifest+trace SHA-256，只有 admitted 输出 W/P/F/R/L/T、
  final cursor 与 settlement 摘要；manifest schema 2 以单一 `selectionProfile` 取代 target/decision 双栏，
  corpus revision 12 当前在十六条 trace 上各运行 no-migration、paced、Adaptive `(3,5%)`、Adaptive `(4,4%)`，
  共 64 cases；identity-only
  manifest case 不可执行并 fail-close；
- 多策略 Arena vertical proof：项目已拆为 `Arena <- Baselines <- Tests` 单向依赖；四个现有策略的完整运行
  delegate 与 Adaptive 实现位于独立 Baselines 程序集。public `StrategyStepViewV1` 保留 `G/E/H/D/B`、
  Insert/Update/Remove/NoChange 与 parent debt，`StrategyRunContextV1` 只逐步开放当前 Save，并由 Arena
  构造 final Store、workload Commit receipts、final checkpoint 与 typed termination。策略不声明 W/P/F/R；
  原有稳定 fixtures 继续保留 typed outcomes 与 exact vectors；新 active-hundred workload 暂不设 dedicated
  unit test 或 literal hash，以便反馈式调参；
- canonical parameter evidence：前两条 trace 遮蔽 Adaptive 参数；threshold-band 隔离 read limit，但新累计
  R 显示 `(3,5%)` 在该 trace 被 `(4,4%)` 支配，旧 T 差异曾掩盖这一点；debt-share dilution 仍让共享
  `G=1000,E=40` source 的 Adaptive pair 产生严格 5%/4% target 分叉；
- canonical locality/ObjectId pair：两条 trace 的首个 `StrategyStepViewV1` 与 selection 完全相同，候选看不到
  next Update。no-migration 对 low/high 置换不变，paced 与 Adaptive 会改变；这只证明当前
  ObjectId-first assignment 对 next-update locality 敏感，不是长期 hot/cold、温度推断、旧
  singleton-Frame oracle 复制或 winner；
- canonical size-skew pair：两条 ordinary trace 仅交换 20B/100B payload 与低 ObjectId 的绑定；
  no-migration 对置换不变，ObjectId-first paced/两组 Adaptive 在各 trace 内同结果，但 low-id-large
  以更高 W/F/R 且相同 P 显示尺寸到 ID 绑定对 immediate-vs-terminal placement 的敏感性。
  ordinary bootstrap 把两个 Base 共置一个 A Frame，因此 workload checkpoint 两种迁移都不释放该 Frame；
  这不是旧 singleton-Frame release oracle 的复制；
- canonical transient-lifecycle pair：两条 trace 使用相同 Create/Remove multiset、ObjectId、payload、horizon
  与最终状态，只交换 `Create101` 与 `Remove100` 的次序；这改变 transient 的生命周期重叠与驻留跨度，
  并使 peak live-set 从 overlap 的 2 变为 serial 的 1。
  八个 cases 均 admitted，以四个 workload Commits 加 direct settlement 得到 `M=5`。
  no-migration 在 pair 内都走 `Stay/Stay/Stay/Stay` 并终止于 scope `2/3`；ObjectId-first paced/两组
  Adaptive 都走 `Stay/Stay/Rotate/Stay` 并终止于 `3/4`。serial 的主观察是这三组策略的 F 降低
  408B；4B W 差是当前 layout fallout。这不外推 churn rate、lifetime prediction、GC、steady state
  或建议业务串行化；
- canonical Previous-debt granularity pair：ordinary step0 共置 `10/20/30=100B,40=300B`，两条 trace
  只交换 three-small 与 single-large 的首次 full rewrite 次序，并共享 1B sentinel、catch-up 与最终
  three-small reconvergence；operation multiset、最终 versions 和 horizon 相同。两组 Adaptive 在共同 pivot 上均为
  `G/E=601/300`，但 debt 分别为一个不可再分的 300B 对象与三个 100B 对象。该 pair 八个 cases 全部 admitted，
  本 pair 均为四个 workload Commits 加 direct settlement；endpoint T 曾让 no-migration 看似 exact tie、paced
  只差 4B P，但累计 R 进一步区分二者；两组 Adaptive 中 single-large 在 W/P/F/R 全部更低。这只证明当前 Adaptive one-object
  progress floor 对 debt granularity/indivisibility 敏感，不外推 arrival/service-rate pressure、steady state、
  starvation 或一般 size preference；
- canonical insert-burst pair：两条 trace 共享 bootstrap 与首个 full-rewrite Save，并把相同四项 300B
  Insert 分成 `3+1` 或 `2+2`；operation multiset、horizon、final versions 以及每个 profile 的 cadence/scope
  相同，所有 workload Frames 均小于 2 KiB。no-migration 只改变 P；paced/Adaptive 还通过当前 provisional
  layout 与后续 Rotate/settlement placement 把分组差异传播到 W/F，R 不变。outer Commit 分组由 caller
  决定，策略不能拆分；这不是 capacity witness，也不外推 batching、latency 或 steady-state 建议；
- canonical nested-prefix horizon diagnostic：`debt-zero-before-rotate` 精确复用既有 long trace 的前三个
  workload Saves；公共 prefix 的 view/selection 完全相同，public context 不暴露 horizon。paced/两组
  Adaptive 的 short cutoff 位于首次 natural Rotate 之前，long 则多一个 Save 并多跨一代；
  no-migration 是保持同一最终 scope 的 control。两条 trace 的 horizon 与最终状态不同，delta 同时包含
  真实额外 Save 和 terminal placement 变化；不做因果成本拆分、cross-horizon Pareto、归一化排名或 steady-state 推断；
- burst/capacity 分层裁决：round-1 corpus 不加入 near-limit performance trace。现有 typed tests 已冻结
  selected hard rejection、no fallback、zero mutation/no metrics；后续 candidate qualification 只需加入
  avoidable selected-capacity gate，即 foreground、alternate/reference 路径可行而 candidate 选择被拒绝；
- adjustable active-hundred mixed workload：fixed-seed generator 以 Field/List 1:1 权重创建 100 个持久对象，
  后续 64 个 workload Saves 每轮从全部 live objects 中确定性随机选择 60 个 Update；没有后续 Create/Remove。
  这同时保留大规模 A-debt backlog、持续 Delta 动机和每轮变化的 40-object NoChange pool。首轮观察中
  no-migration/paced 无 workload Rotate；Adaptive `(3,5%)` 在 Saves 25/47 Rotate，`(4,4%)` 在 31/61 Rotate；
  新累计 R/L 显示 `(3,5%)` 的平均冷读/放大率为 `45262.69/10.5799`，优于 `(4,4%)` 的
  `50300.94/11.7575`，并在 W/P/F/R 四项严格支配后者；参数仍可反馈调整，不视为 golden/steady-state 证据；
- named fixed-two-scope-advances diagnostic：窄 `ExecuteCase` seam 复用 canonical benchmark-v1 执行路径，
  test-local continuation 只为 control 真实追加一个 zero-workload terminal settlement。两侧最终同为 scope 3/4；
  control `commits/W/P/F/T=6/944/680/680/752`、paced `5/1536/696/804/756`。control final Previous debt
  `{10,20,30}`，paced 为 `{1004}`，所以 equal scope 排除了结束文件代际差异，却没有中性化布局历史或尾债；
- terminal sizing 反例：high-ticket External 不支配 zero-payload Base+Self。

Stay-B 与 Rotate-C 已接入同一 per-Save facts、paired evaluation、显式 apply、保守 completion proof 与
连续多轮转调用节奏。当前连续 witness 仍是 test-local caller script，不是自动策略或通用 Runner。
当前还具备外部固定日程、故意保守的 `DebtZeroThenRotate` 与自动 payload-share v0 trigger；后者只有有界
synthetic evidence，仍不代表一般 pressure-aware rotation trigger 已解决。

## 当前研究焦点

revision 12 当前是十六条 trace、64 admitted cases，并采用 workload-cycle cumulative cold-load R。最新
`active-hundred-mixed` 把原先两个过短、过纯的
adversarial trace 合并成一个可调的长程混合 workload；它用 100 个持久对象、64 轮和每轮 60% Update，直接
观察 backlog pacing 与 active-Update debt 在同一运行中的交互。当前焦点转向 strategy response/candidate，
而不是冻结更多 goldens 或继续补 generic axes。

## 下一编码切片

针对 active-hundred mixed 暴露的 pacing/active-debt 行为设计首个独立 candidate，并在当前 suite 上复跑。

## 近期 roadmap

1. **实现策略回应**：由 active-hundred mixed 的白盒证据驱动一个最小独立 candidate，
   不改变 Arena contract 或让策略读取未来/feasibility；
2. **复跑与复审**：在当前 revision 12 上报告 typed outcomes 与 raw `W/P/F/R/L/T`，检查改进是否只是把代价
   转移到既有 workload；不合分、不排榜；
3. **按弱点而非目录扩容**：只有复审指出新的具体 candidate blind spot，才增加或调整最小 validation；
   不再按 generic axis 或 seed 数机械扩 corpus；
4. **候选成形后再封包**：闭合 determinism/order/artifact 与 qualification gates，冻结 `ROUND-1` packet/tag，
   再启动并行 candidate round。

## 未闭合事项

- 哪些互相正交的因果 workload 轴足以支撑“没有明显短板”仍未知；统一 suite 只提供可重复 synthetic evidence，
  不能声称代表生产；
- 当前 payload-only canonical toolkit 是否足以产生结构多样的首轮 candidate，需由首轮结果验证；Frame-aware
  facts 仅在 payload-identical 布局造成 canonical outcome 反转或 Frame-aware oracle 进入新 Pareto 点时进入 V2；
- 当前 profile-matrix manifest 可由 organizer 冻结候选后统一重跑；若出现分批 strategy report 缓存/比较
  consumer，再拆 strategy-neutral suite hash 与 per-strategy report identity；
- 当前 `StrategyRunProductV1` 由 Arena context 认证，不接受候选任意手工构造的 Store/ledger；若首轮策略确实
  需要绕过 canonical planner/apply toolkit，须先实现 exhaustive Frame enumeration、逐 Commit prefix-state
  validation、layout/ticket/OVD closure 检查与 offline W/P/F/R recomputation；
- `StrategyBindingV1` 每 case 调用 executor factory，防止意外复用 captured instance；static mutable state
  仍不是机械隔离的故障模型，首轮 packet 需加入 fresh-fork repeat 与 case-order permutation gate；
- 产品若最终必须发布唯一默认 profile，仍需要真实 workload/SLO 给出 Peak、file tail 与 read guardrails；
  在此之前只报告 per-workload Pareto 与 `no winner`，不使用裸加权和或严格 W-first 字典序。

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
