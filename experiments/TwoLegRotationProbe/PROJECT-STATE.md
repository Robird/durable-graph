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
- caller-selected B same-state Base migration plan/append witness；
- relay-free immediate A/B -> B/C plan/append、shared anchor、B/C closure witness；
- 多批 B migration 使原本放不下的 C evacuation 可编码的容量 witness；
- terminal sizing 反例：high-ticket External 不支配 zero-payload Base+Self。

这些组件目前仍未接入同一条连续 Save 策略循环。当前三条 policy matrix 只是 Base/Deltify baseline，
不是 rotation-policy comparison。

## 当前研究焦点

把已经分离验证的 domain Save、B migration 和 C rotation 合成一个真实 Save 节奏下的纵向闭环，
并尽快画出：

```text
A debt 随连续 Save 变化
    -> 策略顺带迁移
    -> terminal C cost 下降
    -> A/B -> B/C
    -> 在新 FileScope 中继续 Save 和再次轮转
```

不再单独扩张 terminal planner、lineage 语义或 provisional wire grammar。

## 压缩后下一编码切片

先闭合 **normalized input + explicit-decision Stay-B candidate**，暂不同时实现策略选择：

```text
parent PublishedRevision@B + SaveStep
    -> immutable Insert / Update / Remove / NoChange facts
    -> caller-explicit Update Base/Delta modes
    -> caller-explicit unchanged A-debt migration IDs
    -> one pure runtime B Revision candidate at the current tail
```

最小验收 fixture 在同一个 Revision 中同时包含 Insert、Update、Remove 和一个 unchanged A-debt
same-state Base migration，并证明：OVD Delta 精确指向 source PublishedRevision；PostLive 与逻辑 replay
一致；Remove 无 domain record；NoChange 只在显式迁移时写 record；A debt 按预期减少；whole-candidate
estimate 与 runtime Frame 一致；planning failure 不修改 Store。

本切片只冻结下一依赖 seam，不先造策略接口或通用 planner framework。`BuildRotateC` 应在下一切片复用
同一 normalized facts 与 candidate value shape；heuristic、PreferredStayB/PreferredRotateC 比较、capacity
repair、completion search、publication/head 均暂缓。完成后删除或压缩本节。

## 近期 roadmap

1. **Normalized input + unified per-Save candidate**
   - 从 parent authority 派生 `Insert / Update / Remove / NoChange` 与 object reconstruction facts；
   - Stay-B：同一 Revision 合并领域变更、changed-object Base/Delta 与显式 unchanged A-debt migrations；
   - Rotate-C：同一 Revision 合并领域变更、mandatory A-debt Bases、B-local External/optional Base；
   - 复用现有 OVD、reconstruction、layout 与 no-mutation oracle；现有 immediate path 成为空变更特例。
2. **Two-phase plan / feasibility**
   - 假设可容纳，分别生成一个 PreferredStayB 与 PreferredRotateC；
   - 随后 exact-filter 地址、Frame/File 容量、closure 与 completion certificate；
   - 不搜索同一 target 的次优 capacity repair；两个偏好候选都失败时保守 fail closed。
3. **Scripted continuous runner**
   - 先由测试脚本显式给出动作，不声称 heuristic；
   - 至少跑通 `A/B -> B/C -> C/D`，验证每步 logical state、FileScope closure 和 role rollover；
   - 每个 accepted Save 附带保守的具体 completion certificate，不先建立一般搜索器。
4. **Rotation observations**
   - 记录 debt count/full-Base bytes、Previous unique frames/bytes、B tail/headroom、domain/migration bytes、
     exact terminal-C estimate、per-Save/rotation peak、leg length、rotation count 与保守拒绝；
   - 保留原始量，不预设总分。
5. **简单策略基线**
   - Lazy/no cold migration；
   - Touch/ChangedDebtFirst；
   - deterministic PacedCold（例如每 Save 一个）与 DebtZeroThenRotate；
   - 全部重放同一 frozen traces，报告 Pareto 与明显 dead-end/振荡。
6. **按证据加入 bounded explorer**
   - 仅在出现具体 `RejectedUnproven`、`RejectedCapacityUnsearched` 或疑似 heuristic false-negative 后，
     冻结该小状态；
   - 用同一 unified action builder 做 canonical bounded search；`NotFoundWithinBounds` 不外推一般无解。
7. **再研究自适应策略**
   - 根据连续运行暴露的压力、峰值和反例设计 capacity/pressure-aware 候选，而不是先冻结权重。

## 未闭合事项

- unified candidate 的最小输入形状，以及如何避免与现有 builders/planners 形成第二 authority；
- foreground change 与同一 ObjectId maintenance selection 的冲突规则；
- unbounded preference cost 如何比较两个 target，同时保持原始多目标事实而不偷渡权重；
- `RejectedCapacityUnsearched` 出现多频繁时值得加入次优 action menu 或 constraint-aware repair；
- rotation Save 中 changed object 何时允许 Delta，何时因其 reconstruction 仍触 A 而必须 Base；
- 初版保守 completion certificate 如何表达且不偷偷演化成通用 search framework；
- stable hot/cold、burst、size distribution 与长 trace 是否先用 handwritten fixture，何时扩充 generator；
- `PacedCold` 的 fixed-count baseline 之后，何种无隐藏权重的 pressure facts 最值得比较；
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
