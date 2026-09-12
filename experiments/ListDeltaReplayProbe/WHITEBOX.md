# List matcher white-box experiment

Question: can small, explainable edits expose different failure modes in the current bounded
LocalResync and BoundedMyers implementations, and how much survives the Base/Delta policy?

Scope: two workload families, controls at their boundaries, unique Int32 elements, current product
matchers and decoder, fresh repositories. No algorithm, budget, format or default changes.

1. Insert a block of 32 or 33 new elements at the head and change the last element. The last edit
   prevents common-suffix trimming from solving the insertion before matching. Also run a pure
   33-element insertion as a control. A 33-position shift is outside LocalResync's 32-element window;
   Myers needs only 35 insert/delete edits, counting replacement as delete plus insert.
2. Distribute 64 or 65 insertions through the first half and the same number of deletions through
   the second half, including deletion of the last original element. Each local displacement is
   easy to find; the complete insert/delete distance is 128 or 130. Equal total lengths avoid
   Myers' immediate length-difference rejection. Original retained elements are unique and remain
   ordered, so the exact edit distance is known without trusting either matcher.

Acceptance: check coordinate coverage and unchanged-pair counts, observe comparisons independently
of timing, verify every candidate Delta and both persisted revisions, retain same List identity,
and report raw Delta bytes separately from actual policy-selected ObjectVersion bytes. Controls
must distinguish common-suffix handling, local-window failure and global search fallback. Timing
has no pass threshold. Search-budget exhaustion may occur before depth exhaustion; measure this
rather than assuming which bound fired. These are targeted adverse examples, not a proof of an
absolute worst-case bound or an estimate of occurrence frequency.

The executable's white-box assertions describe these current mechanisms. If an algorithm later
improves, update those diagnostic expectations while retaining the input fixtures, independent
LCS oracle and correctness checks; preserving poor compression is not a product requirement.

## 2026-09-09 验收与复现

两个家族、三个控制，共 5 个 case；N=4096/16384，3 次独立仓库重复，Position/LocalResync/BoundedMyers
交错顺序，每次独立 Diff 取 31 个样本。X=8/Y=5；每库初始保存加一次编辑。
**90 个测量仓库、180 个历史 Revision** 全部恢复验证通过，另有 15 个 warmup 库、30 次 Commit。
每个候选 Delta 均 Apply 验证，包含实际被策略改选为 Base 的候选；prior/current、领域实例和共享 List 身份不变。

```powershell
$priorTiering = $env:DOTNET_TieredCompilation
try {
    $env:DOTNET_TieredCompilation = '0'
    ./experiments/ListDeltaReplayProbe/Run-Probe.ps1 -Suite whitebox `
        -Counts '4096,16384' -Repeats 3 -DiffRepeats 31
} finally {
    $env:DOTNET_TieredCompilation = $priorTiering
}
```

Release / .NET 10.0.5 / Windows 10.0.22000 / x64 / 16 logical processors / workstation GC。
关闭本次子进程的 tiered compilation，避免不同行进入优化编译阶段的时间不同；不改变产品算法与预算。
计数用独立的 CountingOps，计时使用实际 Int32StateOps。matcher 每组额外预热 32 次，计时内不含计数或验证。
完整 Diff 用保存后重新读出的 frozen states；计量包含 matcher 与 payload 准备，不包含 Capture/PrepareBase/IO。
下文时间为各库 31 样本中位数、再取三库中位数。分配为当前线程累计托管分配，**不是峰值内存**。

产物：

- 正式稳定计时：`obj/run-20260909091427-43328-6614b11c/{report.json,whitebox.json,steps.csv,whitebox.md,summary.md}`。
- 默认 tiering 的先行矩阵：`obj/run-20260909091157-47436-59c3843d/`，尺寸/比较/边界结论相同；不混入正式计时。
- 初始 smoke：`obj/run-20260909091101-20672-9adad694/`，15 库、30 Revision。
- 普通模式回归：`obj/run-20260909091315-19816-291092ca/`，原 15 库、225 Revision 通过。
- 根 solution build、probe Release build 均 0 warning/error；相关 List matcher/range/body/binding 测试 **74 通过**；独立审查无阻塞项。

正式报告 source baseline 为 `3d575a2+working-tree-changes`；UTC `2026-09-09T09:14:59Z`。
SHA-256：

| 程序集 | SHA-256 |
|---|---|
| Probe | `3B1523D985D6B402378BB774BE4DF4802C4DB6BB1EBDFE34F8C0B0368B4A995B` |
| Runtime | `88710BC8E96BA223EA0505042C68F8FC2C810410D20ADAAA65478B7E583ED758` |
| StateStore | `4D91C0B6CCF5C3EECDE0A1138D9CD628506B0583D0F8E285950AB6C79EA3EF26` |

## 结果：两个不同的失效边界

下表为 **N=4096**，实际写入只计 List ObjectVersion payload，含对象 envelope，
不含 membership/共享帧/Schema/publication。三次重复尺寸相同。

| 编辑 | LocalResync 实际写入 | BoundedMyers 实际写入 | 对照意义 |
|---|---:|---:|---|
| 头插 32 项 + 改末项 | Delta 53 B | Delta 53 B | 窗口内，两者均保留全部 4095 个相等配对 |
| **头插 33 项 + 改末项** | **Base 8169 B** | **Delta 54 B** | Local 窗口越界 |
| 仅头插 33 项 | Delta 48 B | Delta 48 B | 公共后缀能独立解决；纯插入不是这个坏例子 |
| 分散插入、删除各 64 项 | Delta 768 B | Delta 768 B | 最短插删距离 128，两者均完整匹配 |
| **分散插入、删除各 65 项** | **Delta 778 B** | **Base 8132 B** | Myers 深度越界 |

Local 的位移达到 33 后，逐位置前进仍保持相同位移，窗口内找不到相等锚点。
本例从原本可匹配 4095 对退化到 0 对，并耗完比较预算：**69,898** 次 StateEquals
（69,896 次额外搜索加首尾各一次失败）。Myers 只需 **4,725** 次，raw Delta 为 47 B；
Local raw Delta 为 16,236 B，策略选较小 Base。

Myers 的第二例则不耗尽比较预算：**12,387** 次总比较远低于 69,632 次额外额度，
但深度最多 128，不能完成 D=130 的路径，已搜索出的中间匹配整体被丢弃，按位置回退后 0 对相等。
单独解除比较预算仍是 0 对；仅把深度放宽到 130，即恢复全部 **4031** 对。
Local 只需 **4,226** 次比较，保留全部这些配对。这些放宽测试仅作诊断，未参与真实保存。

默认深度 128 的 trace 整数数组内容至多 33,540 B，达不到 1 MiB trace 限制；此次失败不应归因于 trace 容量。
Position 在两个坏例子中分别产生与失效算法相同的 raw Delta 和实际 Base，是无搜索成本的回退对照。

## 扩大规模后，主要代价在哪里

两种坏例子在 **N=16384** 都重现，说明它们不是小列表的偶然数值边界：

| 编辑 | 算法 | matcher 比较次数 | matcher ms | matcher 分配 B | 完整 Diff ms | 完整 Diff 分配 B | raw Delta B | 实际写入 |
|---|---|---:|---:|---:|---:|---:|---:|---|
| 头插 33 + 改末项 | LocalResync | 266506 | 0.678 | 104 | 2.180 | 6570600 | 73582 | Base 40938 B |
| 同上 | BoundedMyers | 17013 | 0.046 | 4952 | 0.093 | 6064 | 50 | Delta 58 B |
| 插删各 65 | LocalResync | 16514 | 0.101 | 6296 | 0.156 | 9032 | 777 | Delta 786 B |
| 同上 | BoundedMyers | 24580 | 0.093 | 41264 | 1.536 | 6611664 | 73481 | Base 40839 B |

**共同的大头是失配后的 payload 准备。** matcher 本身仅分配 104 B 或 41 KB，
但按位置配对把大量仍存在的值当成变化，随后逐元素 PrepareDelta 并合并 patch，
完整 Diff 分配达到约 6.6 MB。[body 实现](../../src/DurableGraph/Runtime/Containers/ListStateReader.cs)与计量一致。
Base 策略兜住的是最终写入，不会撤销已完成的搜索、元素编码和临时分配。

在本轮所选的两个坏例子里，Local 的最坏完整 Diff 更慢，Myers 的搜索阶段分配更多；
两者的失败输出和完整 Diff 分配处于相近规模。这不能推出全局最坏情况排名：
两个 workload 的编辑数量、成功 Delta 大小不同，Base/成功 Delta 的比值不适合作为算法总体风险评分。
此处也未穷举重复值诱导的错误锚点、比较预算先耗尽的 Myers 样本或昂贵 struct 比较。

完整 Commit 继续记录，但本轮三次重复存在明显波动：例如头插 33+改末项、N=16384，
Local 为 24.715 ms（23.834–104.565），Myers 为 15.476 ms（15.399–32.306）。
它还包含 Capture、Base 准备、读链、持久化屏障；此次不以它单独判定 matcher 胜负，也不把全部波动归因于 SSD。

## 日常触发条件与后续判断

下面是操作形态对应关系，**不是观测到的业务频率**：

- Local 的坏例子类似一次批量粘贴/导入超过 32 项，再修改列表靠后的另一项，同一次 Commit 保存。
  不需要恶意重复值；一次普通批量编辑就可能满足条件。频繁保存、纯追加或只做一处插入，可能让首尾裁剪直接解决。
- Myers 的坏例子类似两次 Commit 之间累积多处分散新增/删除；这里总共 130 个插删编辑使全局上限失效，
  每一处局部对齐仍很容易。更长保存周期或批量集合刷新更可能累积这种形态。
  **超过 128 次编辑本身不等于坏结果**：还要有中间位移使按位置回退不合适。
  大量原位更新仍可能适合位置 patch；引用对象内部的 child-only 更新也不计入 List 槽编辑距离。

这轮明确了应研究的回退问题：窗口外位移如何保留大段相等内容，以及全局搜索失败后是否仍能利用廉价的局部匹配。
两者都属于可替换的 matcher 策略，不需要改变 wire-format。**当前仍保持 LocalResync 默认和 Myers 可选**，
不凭刻意构造的两例估计发生率或自动切换默认；后续若研究回退，应把这两组和原普通轨迹一起作为对照。

可执行入口：[Whitebox.cs](Whitebox.cs)；算法权威：[ListDeltaMatcher.cs](../../src/DurableGraph/Runtime/Containers/ListDeltaMatcher.cs)；
上一轮普通轨迹：[RESULTS.md](RESULTS.md)。
