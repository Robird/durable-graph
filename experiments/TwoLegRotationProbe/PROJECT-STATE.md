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
- selected Stay-B 只有在存在具体、有限、可重放的 `CanPrepareAndRotate` continuation certificate 时才接受；
  Rotate-C 可直接 apply。evaluator 在 workload 后另行执行 canonical terminal settlement。当前不要求完备
  solver；找不到有限路径只能称 `RejectedUnproven`，不能称一般无解；
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
- current-reconstruction source-layout discriminator：两侧使用同构的 metadata-only full-OVD A anchor，
  shared 将六个 payload Base 共置一个 Frame，split 以唯一 accepted chain 把 cold/changed 分置两个
  payload Frames；同一四格的 object-debt 轨迹完全相同，而 required unique Previous Frames 不同；
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
  realized Revision checkpoint 跟踪 F；最终 `FinalHeadColdLoad` 以 OVD materialization 与所有 live-object
  reconstruction full Frames 的去重并集计算 R。fresh-file 4B header 计入写入，typed rejection 不进入指标；
- evaluator v1 closed-horizon session：每次 run 在独立 Store fork 上消费 caller-selected workload Commits，
  用互斥 typed outcome 分开 success、selected capacity、`RejectedUnproven` 与 incomplete；完整 workload 后
  无条件执行一次 `DirectRotateElseAscendingSingleDebt-v1` terminal settlement。直转优先，否则 ObjectId 升序
  单对象迁债；全部 preparation+Rotate 真实 apply 在同一个 synthetic Commit 中并计入 W/P/F，成功还验证
  terminal source A 已退出 final B/C current reconstruction；
- benchmark-v1 consumer：closed registry 把 trace step0 Create-only population 共置为一个 full-OVD A Frame，
  再写 metadata-only B anchor，evaluator 只消费 steps[1..]；manifest 固定 fixture/trace/generator/seed、expanded
  trace SHA-256、target/decision treatment 与 evaluator/settlement/accounting/layout/grammar/read-schedule identity；
  batch runner 只在 session Store 上 normalize/evaluate/apply，typed rejection 不 fallback；canonical UTF-8
  manifest/report 使用固定 tokens/order、16位 hex seed、manifest+trace SHA-256，只有 admitted 输出 W/P/F/R、
  final cursor 与 settlement 摘要；首批 handwritten paced 和 seed12345 mixed no-migration case 均可确定重放；
- terminal sizing 反例：high-ticket External 不支配 zero-payload Base+Self。

Stay-B 与 Rotate-C 已接入同一 per-Save facts、paired evaluation、显式 apply、保守 completion proof 与
连续多轮转调用节奏。当前连续 witness 仍是 test-local caller script，不是自动策略或通用 Runner。
当前对照覆盖外部固定日程与故意保守的 `DebtZeroThenRotate` trigger，仍不代表自动 pressure-aware
rotation trigger 已解决。

## 当前研究焦点

benchmark-v1 的 manifest/batch/canonical raw report seam 已由两个 smoke cases 纵向跑通，并以 literal
manifest/report/trace SHA-256 goldens 防止 schema-v1 和 corpus identity 静默漂移。当前两 case 使用不同 trace 与
decision treatment，只证明 consumer wiring，不构成策略优劣对照。

当前焦点转向第一个 matched comparison group：在相同 source fixture、expanded trace、target treatment 与
evaluator protocol 下，同时运行 no-migration 和 paced-one-debt，直接比较两组 admissibility 与 W/P/F/R vectors。
在此之前不建立 scalar score，也不把 smoke corpus 的数值解释成 winner。

## 下一编码切片

把现有两个 smoke traces 各自扩成 no-migration / paced-one-debt matched pair，冻结共同输入与唯一 treatment
差异，产出四个 canonical raw outcomes；随后检查这些结果是否暴露新的 horizon/source-layout 偏差。暂不建立
scalar score、candidate archive、产品 policy API 或 `/goal` 循环。

## 近期 roadmap

1. **形成 matched comparison**：同 fixture/trace/target 下并跑 no-migration 与 paced-one-debt；
2. **扩展 corpus topology**：只按已知 causal witness 加 shared/split Frame 与更多 fixed-seed family；
3. **审视 horizon bias**：对照 terminal settlement、完整 epoch 或长周期，确认尾债不会系统性偏袒策略；
4. **再启动自动优化**：只允许修改窄 policy seam，保留 Pareto candidates/counterexamples，允许 `no winner`。

## 未闭合事项

- `DirectRotateElseAscendingSingleDebt-v1` 关闭 terminal source epoch，却会形成新 scope 的 Previous debt；需要用
  complete-cycle/long-run 对照量化 terminal liability bias，不能把它宣传成全局 debt-zero；
- 无 workload SLO 时采用 Pareto frontier，还是先给 peak/file/read guardrail 再主优化 total write；当前不接受
  裸加权和或会用 1B 总写收益购买任意峰值的严格字典序；
- batch consumer 已形成 experiment-only runner/report seam，但仍不自动证明产品 API 边界；
- v1 report 只保存 comparable W/P/F/R 与 admissibility/final-scope/settlement 摘要，不复制完整 cold-read Frame
  diagnostics；需要时应另建 diagnostics artifact，不能悄悄扩张 comparable schema；
- v1 writer 尚无外部 parser、文件落盘或 CLI publication；manifest/report 是一对以 SHA-256 关联的 canonical
  byte artifacts，而非 durable product format；
- `trace-step0-single-a-full-base-then-b-anchor/1` 把 initial payload 共置一个 A Frame，会掩盖局部 Frame release；
  后续 source-layout corpus 必须使用不同 versioned fixture identity；
- 旧 rotation-comparison reduction 中的 counterfactual terminal 仍只投影 final-C append/result 与 preparatory
  Stay count，不聚合互斥未来，也不声称已观测 preparatory writes 或 terminal-source pressure；evaluator v1
  的 terminal settlement 是另一条已真实执行并计费的路径；
- evaluator/benchmark 冻结后，哪些历史可见 pressure facts 足以驱动 rotation/migration，以及何时才有证据
  为具体 rejection 加入 bounded repair/explorer；
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
- evaluator v1 admissibility/settlement/指标合约：[`EVALUATOR-V1.md`](EVALUATOR-V1.md)
- 活跃设计分叉：[`../../docs/design-branches/0007-adaptive-two-leg-rotation-policy.md`](../../docs/design-branches/0007-adaptive-two-leg-rotation-policy.md)
- Plan/容量分层：[`../../docs/design-branches/0011-two-phase-save-planning-and-capacity.md`](../../docs/design-branches/0011-two-phase-save-planning-and-capacity.md)
- StateStore 基础约束：[`../../docs/state-store-base-design.md`](../../docs/state-store-base-design.md)
- 地址 authority：[`../../docs/state-store-addressing-design.md`](../../docs/state-store-addressing-design.md)
- 阶段历史：[`../../docs/DurableGraph-lab-notebook.md`](../../docs/DurableGraph-lab-notebook.md)
