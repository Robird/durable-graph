# DB-007：自适应双腿轮转与 Rebase/Deltify 策略

> 状态：Open
>
> 创建日期：2026-08-28
>
> 更新日期：2026-08-29
>
> 当前方向：先建立统一 per-Save candidate，把领域变更、same-Revision B migration 与 terminal C
> 合成一个动作 authority；随后跑通连续多 Save、多次换腿并比较简单策略。bounded explorer 只在
> 出现具体保守拒绝或疑似 false-negative 后介入，不先冻结 heuristic 或调优参数。

## 问题

在 current Revision 的 reconstruction closure 至多引用两个相邻文件的硬约束下，能否用同一组
无权重事实量决定：

- ObjectVersion 写 Base 还是 Delta；
- 普通 Save 是否顺带迁移 cold objects；
- 何时 A/B → B/C 迈腿；
- 如何平衡 read、write、storage、lineage 与单次 pause。

当前没有证据表明 `TotalPersistBytes`、Previous-file ratio 或任一单标量是充分统计量；允许最终
不存在唯一 winner。

## 已选择的硬约束

1. 新 ObjectVersion 只写入 CurrentFile。
2. published current Revision 的 reconstruction closure 只涉及 Current/Previous。
3. A/B → B/C 时，terminating Base 位于 A 的 live objects 必须在 C 写完整 Base。
4. C Revision 的 shared prior-snapshot anchor 落在 B；Base 不保存 per-record parent，Delta 仍指
   exact parent。
5. OVD 内存使用 absolute address；输出时相对目标 Revision 编码。
6. 约 512 GiB frame-start、RBF one-frame 与 TailMeta 上限是格式 gate，不是调优参数。
7. 当前选择 `CanPrepareAndRotate` 作为成功 Save 的 liveness admission invariant：必须保留有限、
   容量合法的 B Base migration + C evacuation 完成路径。它不是既有格式是否可读的格式定律。
8. 文件被物理删除后不可访问不属于格式需要抵抗的故障模型。

## 待比较策略

- **对象局部成本**：依据 Base/Delta bytes 与当前 reconstruction cost 一步选择；简单但看不到
  shared-frame 与 rotation debt。
- **Previous/Current 压力**：根据 unique reconstruction frames/bytes 判断后腿负担；直观但
  不直接证明 C evacuation 可编码。
- **渐进 cold migration**：普通 Save 在 B 提前写少量 Base，平滑正式轮转写峰；必须证明
  消耗 B 空间后仍收敛。
- **统一边际规划**：比较 Base/Delta/Stay/Rotate 的事实性变化；不得把隐藏权重伪装成算法事实。

## 无权重观测量

- File/Frame：A/B/C tail、candidate start、payload/TailMeta/padding/fence、full-frame reads、cache hit；
- Object：head/Base address、chain depth/bytes/unique frames、Base/Delta bytes、shared-anchor lookup、
  距离上次领域修改/Base 的 Saves；
- Revision：live/changed/unreachable/evacuation/migration counts、OVD/index bytes、B/C capacity、
  completion plan、materialized logical graph equality。

比例与 score 都从原始量离线派生，不能反过来污染 trace 或持久状态。

## Correctness oracle

```text
Materialize(candidate) == expected logical graph
ReconstructionFiles(candidate) ⊆ {Current, Previous}
CanPrepareAndRotate(successful post-state) == true
all frame starts/tickets/layouts are representable
failed plan leaves published state unchanged
```

`CanPrepareAndRotate == true` 必须由实际有限 plan 见证，而不只是检查 C 当前是否放得下
EvacuationSet。caller-selected B migrations 现在可以与 final C rotation 组合成具体 witness，但不是
决策过程；未选中某条完成路径或 bounded explorer 返回 `NotFoundWithinBounds`，都不能解释为一般无解。

未来 reference explorer 必须显式标注边界，并在捕获的小状态上 canonical 枚举 B migration
batches 及 terminal C action，再用同一 runtime OVD/reconstruction/layout oracle 复验。便宜
heuristic 不能自行宣称 completeness，但 explorer 不再作为首个连续策略循环的前置依赖。

## 当前 executable baseline

- deterministic Field/List workload generation 与 replay；Create/Update/Remove 尺寸态连续且禁止
  ObjectId reuse；
- one Revision/one in-memory RBF Frame、exact v0.40 envelope、runtime OVD authority、absolute StateMap、
  symbolic Delta apply 与 current/lineage 分层；
- `ObjectPayloadOnly` 与 size-only `ProvisionalRevisionV0` 两种 accounting scopes；后者计入 domain
  headers、OVD、TailMeta directory、relative VarUInt 与 exact envelope，但不是 byte codec；
- `AlwaysBase`、`AlwaysDeltaWhenLegal`、`ObjectPayloadReadAmplification3` 三条基线；最后一条保留
  StateJournal ratio=3 的局部形状，但不移植 one-object-per-frame overhead；
- relay-free immediate A/B→B/C 已完成 plan、in-memory first-frame registration、`MaterializeLive(C)`、
  reconstruction 与 lineage；DB-010 已选择 Revision shared prior-snapshot anchor；
- explicit caller-selected nonempty A-debt B migration 已完成 plan/append：B 写 same-state、
  same-logical-ordinal Base，OVD Delta 指向 source PublishedRevision；plan 与 append 前都复验全部 live
  reconstruction 和 A/B closure，lineage 不作为 current-state gate；
- `3 x 140,000,000` payload witness 证明 immediate C 与合并两对象 B batch 均失败；两个单对象 B
  batches 后 C 成功，一批后仍失败。`head@B / Base@A` 也已覆盖；
- terminal C exact-sizing discriminator 使用合法最大 Previous ticket（10-byte VarUInt）：mandatory
  payload `268,435,390` 时 External total 恰为 `268,435,428`，加一后溢出；同一加一候选改用
  zero-payload same-state Base + Self 后 total 为 `268,435,427`，padding 后 frame 恰达上限，address
  tokens `21 -> 12`。

该 discriminator 只证明当前 provisional v0 grammar 下 External 不支配 optional same-state
relocation；未实现 runtime optional action，不证明扩大 `CanPrepareAndRotate` 可达集、planner
completeness 或未来 wire format。尺寸裁决始终以 whole-candidate estimator 为唯一 authority，不从
单对象 token 差推导 additive savings。

当前 provisional matrix（`modeled file / final full-frame read`）：

| Workload | AlwaysBase | AlwaysDelta | Local ratio=3 |
|---|---:|---:|---:|
| hot-one/cold-eight | 1864 / 1132 | 1428 / 1396 | 1500 / 1132 |
| fixed-seed mixed | 516 / 176 | 460 / 444 | 460 / 296 |

这些数字只证明 write/read tradeoff 可观测，不选择 winner。完整 probe 当前 221/221。

## 未闭合事项与顺序

1. 建立 unified per-Save candidate：同一 B Revision 合并 domain changes 与显式 cold migration；同一 C
   Revision 合并 domain changes、mandatory evacuation 与 optional B-local relocation；
2. 以 scripted actions 跑通连续多 Save 和至少两次换腿，并记录 A debt、headroom、写峰值与读取原始量；
3. 接入少量明确命名的策略基线，在同一 frozen workloads 上比较拒绝、振荡、Pareto frontier 与 pause；
4. 捕获具体 `RejectedUnproven` 或疑似 heuristic false-negative 后，再建立 small-state bounded/canonical
   explorer；找到的 witness 可证明 true，`NotFoundWithinBounds` 不冒充一般无解；
5. 只有策略结论确实依赖 byte-level 差异时，再做 provisional writer/parser；one-frame 真实容量频繁
   撞墙时才引入 Extent。

尚无真实 workload/SLO 时，不要求用户预填 read/write/pause 权重；当多个不可支配策略必须选择
产品默认值时再请求裁决。

## 最小 workload 矩阵

- 大量 cold + 少量 hot、单 hot 高频小 Delta；
- 持续增长/缩小/振荡、突发批量修改后稳定；
- 大量小 records 共享 Frame、少量大 records 造成 co-read；
- head@B/Base@A 与 head@A/Base@A；
- 接近 frame-start、Payload/TailMeta 边界；
- 连续多次文件轮转。

## 历史证据

旧 direct/Relay/relay+OVD-Self/relay-free discriminator 由 annotated tag
`research/relay-vs-relay-free-20260829` 与 DB-009 保存；阶段性实验细节留在实验簿，不在本 live
branch 重复累积。

## 重访触发条件

- reference oracle 发现 hard gate 不可满足或 heuristic 稳定 false-negative；
- 策略出现明显振荡或稳定 read/write/pause 劣势；
- 真实 RBF read log 提供 cache/locality 数据；
- lazy/partial Load 产生 chain-summary 消费者；
- one-frame Revision 出现真实容量失败。
