# DB-012：TwoLeg 多策略 Benchmark Arena 的最小边界

> 状态：Open
>
> 创建日期：2026-08-31
>
> 当前推荐：把现有探针逐步转成一个静态链接、确定性、策略无权接触 evaluator internals 的实验赛场；
> 先完成一个跨程序集 Baseline vertical proof，再扩展 workload corpus 和并行候选。暂不建立插件、排行榜或自动优化平台。

## 新需求

当前用户希望利用多个 Coding Agent 并行探索策略：

1. 建立一份有明确因果多样性的统一 benchmark workload suite；
2. 让 `TwoLegRotationProbe` 主要承担规范 bootstrap、运行、hard gate、terminal settlement 和 `W/P/F/R` 测量；
3. 把现有 Baseline/test policies 移出 Arena 实现程序集；
4. 每个 challenger 位于独立 C# project，只实现自己的策略；
5. 主线会话作为 organizer，统一复跑并审查 raw typed outcomes；
6. 首阶段不做标量 score、排行榜或产品默认策略，后续才进行半人工的多轮演进。

这让“跨程序集策略 seam”首次有了多个明确消费者，足以重启此前暂缓的通用策略接口问题，但不足以证明需要动态插件或稳定产品 ABI。

## 当前事实

- `experiments/TwoLegRotationProbe/TwoLegRotationProbe.csproj` 目前同时是实现与 xUnit test project；
- model、planner、estimator、evaluator、benchmark、policy 与 tests 都在同一程序集，关键类型基本为 `internal`；
- `BenchmarkV1SelectionProfileSelector` 用 closed registry/switch 解析四个内置 profile；
- `BenchmarkV1Corpus` 同时组合 workload 与固定 profile matrix；
- evaluator 已经拥有独立 Store fork、typed inadmissibility、无 fallback、closed horizon、canonical terminal settlement、exact `W/P/F/R` 和 canonical manifest/report；
- 当前策略只读取当前 `NormalizedSaveFacts` 的 payload projection，不读 step index、future trace、candidate feasibility 或 apply 后 observation；
- current exact candidate estimator/planner 是容量与物理写入的唯一 authority。

## 不可约比赛语义

所有候选必须共享：

- 同一 source fixture/bootstrap、expanded trace、evaluator horizon 与 terminal settlement；
- 每个 `{workload, strategy}` 独立、不可互相污染的 Store fork；
- 同一策略可见事实、action vocabulary、exact planner、frame grammar、read schedule 与 metrics；
- strategy identity/version 与确定性输出；
- capacity、`RejectedUnproven`、incomplete 等互斥 typed outcomes；
- rejected choice 不 fallback，失败不变异 Store/cursor；
- 只有 admitted outcome 才有 `W/P/F/R`，rejection 不转换为罚分；
- workload 全部完成后强制运行同一 terminal settlement，不能把未偿债务推到 horizon 外。

删除任一条都会产生具体的不公平：不同 bootstrap 会改变 Frame closure；复用 Store 会污染 tail；fallback 会给策略一次 feasibility oracle；取消 settlement 会奖励推迟维护；给 rejection 数值罚分会把不同失败语义混入排名。

## 推荐的最小程序集形状

以下是 vertical proof 通过后的目标依赖图，当前尚未实施：

```text
TwoLegRotationProbe.csproj                    Arena class library
├─ internal model/workloads/bootstrap
├─ internal exact planning/sizing/admission/apply
├─ internal evaluator/report machinery
├─ internal workload suite and organizer runner
└─ narrow public, immutable policy contract V1

Baselines/TwoLegRotationProbe.Baselines.csproj -> Arena
├─ DebtZeroThenRotate controls
├─ paced-one-debt control
└─ ReadAmplificationBaseBudgetPolicy profiles

Candidates/<slug>/<slug>.csproj                -> Arena
└─ one independently authored strategy

Tests/TwoLegRotationProbe.Tests.csproj          -> Arena + Baselines
                                                  + integrated candidates
```

首轮不单独建立 `Contracts` 程序集。Arena 只公开少量 policy contract/action types，其余 engine、workload、Store、runner 与 observation 全部保持 `internal`，已经能机械防止普通编译依赖越界。独立 Contracts 的重启条件是：

- 第二个 engine/runner 复用同一策略协议；
- contract 需要独立发布或稳定版本化；
- Arena public surface 无法保持窄小；
- 真实程序集依赖环出现。

这不是恶意代码沙箱。反射、修改 csproj、读取仓库源码等敌对行为不属于合作研究的故障模型。

## Policy contract V1

V1 是无状态纯选择：

```text
PolicySelectionV1 Select(PolicyStepViewV1 input)
```

每个 case/step 的输入都由 Arena 构造并冻结。`G/E` 只统计 post-live objects；逐对象事实则必须保留
pre-Save source 信息，使现有策略能够区分 parent debt 与 post-live debt：

- ObjectId-sorted `Insert / Update / Remove / NoChange`；
- post-live graph all-Base payload total `G`；
- A-dependent evacuation Base payload total `E`；

| Kind | Source facts | Result facts |
| --- | --- | --- |
| Insert | none；必定不是 parent Previous debt | post-Save Base payload `B` |
| Update | `IsPreviousDependent`、source Base payload、current reconstruction payload `H` | post-Save `B`、Delta payload `D` |
| Remove | `IsPreviousDependent`、source Base payload、`H` | none；不进入 `G/E` |
| NoChange | `IsPreviousDependent`、source Base payload与 `H` | 同一状态的 post-Save `B` |

因此 `HasParentPreviousDebt` 可由 Update/Remove/NoChange 的 source facts 派生；不能只看 `E` 或 post-live
objects。最小回归反例是 parent 只有 A-dependent object 10，本步 `Remove(10)+Insert(20)`：现有
`DebtZeroThenRotate` 仍因 pre-Save parent debt 选择 Stay，而 Adaptive 的 post-live `E` 为零。V1 seam 必须
在不混淆这两个概念的情况下保持所有旧 profile goldens。

输出沿用现有规范 action vocabulary，而不是建立第二套等价 DTO：

- selected `Stay-B` 或 `Rotate-C` target；
- 所有 Update 在 Stay candidate 中的 Base/Delta 决定；
- Stay 的 unchanged A-debt migration membership；
- 所有 B-contained Update 在 Rotate candidate 中的 Base/Delta 决定；
- Rotate 的 optional B-contained NoChange Base membership。

Arena 随后独占 exact pair construction、sizing、admission、apply 与计量。策略不得看到或声明 Store、absolute address、candidate size、feasibility、W/P/F/R、step index、workload identity、future trace 或运行后 observation。

V1 暂不暴露 Frame-sharing/provenance facts。当前 Shared/Split evidence 证明 intermediate read closure 可不同，却尚未证明在 canonical evaluator 中导致 admissibility 或 Pareto 反转。以下任一证据出现时直接设计可破坏式 V2，不预建 capability negotiation 或 adapter：

1. payload-only view 相同、Frame grouping 不同，并使相同选择的 canonical outcome 反转；
2. test-local Frame-aware oracle 进入新 Pareto 点，而任一 V1 deterministic policy 均不可达；
3. intermediate restart/read schedule 正式成为 metric 或 hard guardrail。

同理，V1 不支持跨 Commit mutable policy state。真实 temperature/read statistics 证明无状态输入不足时，再定义 fresh-per-case lifecycle；不预建 callbacks。

首轮 submission entry 也保持最窄：candidate 暴露一个 public、deterministic、non-capturing static
`Select(PolicyStepViewV1)`；organizer registration 唯一拥有并绑定 `StrategyIdentity/version + Select delegate`，
candidate 不另建 identity registry。Arena 的 repeatability gate 在 fresh forks 上重复同一 case，并改变 case
执行顺序，要求 typed outcome、metrics 与 canonical output 不变。contract 禁止跨调用 mutable state；
static entry 消除 instance lifecycle，而 repeatability gate 用来发现并拒绝可观察的 static-state 泄漏。在当前
非敌对合作模型下，这不是对隐藏 static state 的机械完备隔离，也不因此引入 DI、进程沙箱或 AssemblyLoadContext。

## Workload suite 与策略矩阵

Arena 的 source fixture、trace 与 horizon 必须先形成 strategy-neutral 的内存/API 概念：

```text
Run(strategyNeutralSuite, resolvedPolicyBinding)
```

workload suite 由 organizer 独占；candidate 可以自带 unit fixture，但不得修改或替换 official suite。组织者显式形成 `workloads × policies`，不做 reflection、MEF、assembly scanning 或目录插件发现。

当前持久 manifest 把 selection profile 放入 case，并把完整固定策略矩阵绑定到一个 hash。首个 vertical proof 可以继续由 organizer 冻结全部候选后统一重跑，从而保持当前 artifact schema；不顺手实现 parser/archive 或兼容迁移。

如果随后需要保存并机械比较分批独立 submission，才把 artifact 演化为两级身份：

```text
suite identity
  = workloads + trace hashes + fixture/bootstrap
    + evaluator/settlement/accounting/layout/read-schedule identities

strategy run identity
  = suite identity + strategy identity/version
```

届时 strategy-neutral suite manifest 拥有稳定 hash，每份 strategy report 绑定该 hash 并为每个 workload 提供一个 typed outcome。不能简单让旧 manifest hash 忽略 strategy，因为它目前正确绑定整个执行矩阵。

## Corpus 多样性纪律

“足够多样”不能用 case 数量定义。一个 generator 的大量 seeds 可能只是同一模型的重复样本。每个 workload family 应说明隔离的因果轴与命名失败，例如：

- Update locality 与 hot/cold cadence；
- Base/Delta size ratio；
- object size skew；
- lifetime、Remove 与 churn；
- A-debt share 与 migration service pressure；
- rotation threshold band；
- grouped foreground burst/capacity；
- source Frame sharing（当前只作 test-local diagnostic 与 Policy V2 trigger，不进入首轮 official suite）；
- horizon/epoch phase；
- 只有正式纳入评价目标后才使用的 restart/read schedule。

多个 seed 是敏感性样本，不自动算新的因果类别。公开 synthetic suite 只能说明策略在该模型上的行为，不替代真实 workload/SLO，也不支持唯一产品默认值。

第一轮所有候选使用同一冻结公开 suite 即可。第二轮中，凡 candidate 已依据先前结果修改，或准备宣称
“扩展前沿”，应先冻结实现，再由 organizer 加入一个未参与该实现的新命名 workload/family 并重跑相关
contenders。不受先前结果影响的独立假设仍可先在冻结 suite 上探索。这是 post-freeze validation，不是假装
保密的 hidden test，也不形成总分。

## Organizer 与并行 Agent 边界

一轮并行开发前，organizer 冻结 competition packet：

- policy contract/version；
- workload suite/revision；
- evaluator/settlement/accounting/layout/read-schedule identities；
- prior-art reference；
- candidate 目录与允许修改范围；
- 必须返回的策略说明与 candidate-local unit tests；可选返回 selection diagnostics。

official typed outcome 与 `W/P/F/R` 只由 organizer 集成 candidate 后通过 Arena 产生。candidate assembly
无权调用 internal official suite/runner，也不自行声明 official result。

各 agent 只编辑 `Candidates/<slug>/`，不修改 Arena、corpus、shared registry、solution 或其他 candidate。提示词应分配不同假设族，而不是七次近似同样的“自由优化”，例如 payload knapsack、read-amplification reset、debt-pressure、rotation threshold、size-skew fairness 等。

Codex subagents 默认共享 worktree 和 Git index。最简单的首轮做法是让 agent 不并发提交，由 organizer 统一检查、登记、复跑并提交；若必须保留每个 agent 的独立 commit，则为每个 agent 建独立 Git worktree。

Organizer 可以在 owning tests 或临时分析中：

1. 先按 typed inadmissibility 分栏；
2. 合并 exact `W/P/F/R` ties；
3. 计算同一 workload 内的 componentwise Pareto。

这不是 scalar score、跨 workload 排榜或 canonical report 新字段。正式报告仍保持原始 typed outcomes。

## 最小迁移顺序

1. 先闭合已经在途的 natural parameter-discriminating workload；不在遗留未闭合点上开始大重构。
2. 在当前程序集内引入 payload-only Policy V1 seam，让现有四个 profiles 全部改走该 seam；旧 typed outcomes、`W/P/F/R` 与 canonical hashes 必须不变。
3. 把当前项目拆为 Arena class library、Tests 与一个 Baselines project；删除 Arena 对具体策略实现和 closed behavior switch 的了解。
4. 把 corpus 重构成 workload-only suite，再由 organizer 显式组合 policies；先不改持久 artifact schema。
5. 按因果轴逐步增加最小 workloads，冻结首轮 competition packet。
6. 启动多个独立 candidate projects；organizer 统一集成、复跑和报告，不排名。
7. 第二轮中，对依据已有结果修改或准备宣称扩展前沿的 candidate 使用 post-freeze validation；只有证据
   需要时才演化 contract facts、artifact schema 或自动化。

## 明确暂缓

- dynamic plugin discovery、MEF/DI、assembly scanning 与 ABI plugin protocol；
- CLI、manifest/report parser、artifact database、submission service；
- weighted score、leaderboard、跨 workload 总分与生产 Pareto archive；
- 自动参数搜索、自动进化循环和 `/goal` optimizer；
- 独立 Contracts package、NuGet、兼容 shim 与 DLL hash；
- hostile-code sandbox、防作弊与隐藏测试平台；
- strategy-side exact estimator、feasibility feedback、fallback 或 retry；
- Frame-aware V2 facts 与 stateful lifecycle，直到上述证据触发。

## 尚缺证据

- workload suite 的哪些因果轴足以支撑“没有明显短板”仍未知；
- payload-only V1 是否足以产生结构多样的首轮候选，需要 vertical proof 与首轮产出验证；
- 分批 strategy report 是否值得稳定 suite manifest/hash，要等首轮是否需要跨批缓存/比较；
- 产品默认 profile 仍需要真实 workload 与 Peak/file-tail/read SLO。
