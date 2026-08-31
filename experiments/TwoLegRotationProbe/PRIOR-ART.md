# 双腿轮转与 Base/Delta 策略：相关成果参考

> 状态：Research Reference
>
> 最近核对：2026-09-01

本文整理与 `TwoLegRotationProbe` 最相关的前人成果，供设计策略、构造 benchmark workload 和解释实验结果时查阅。
它不是 DurableGraph 的设计 authority，也不意味着必须照搬某个系统。当前实现事实仍以源码、测试和实际运行结果为准。

## 问题定位

本探针研究的是一种对象级、Delta-aware、由自然 Commit 驱动、受双文件引用范围约束的渐进 compaction：

- latest Revision 最多从 `Previous=A` 与 `Current=B` 两个文件重建；
- changed object 可写 Base 或 Delta；
- Commit 可顺带把 unchanged A-dependent object 以同状态 Base 搬入 B；
- 当剩余 A debt 适合一次性撤离时，创建 C 并把 FileScope 从 A/B 轮转为 B/C；
- 目标是在不过度增加总写入与冷读的同时，平滑单次写入并限制单文件增长。

没有一个已知系统同时具备以上全部约束。最有用的研究方法不是寻找可直接移植的单一算法，而是分别借用：

1. 渐进 rehash / 复制式 GC 的搬迁节奏；
2. log-structured cleaning 的搬迁对象选择；
3. LSM compaction 的债务控制与多放大因素评价；
4. delta compression 的重建链约束；
5. 在线算法的未知未来决策模型。

## 对照总览

| 领域或系统 | 与 TwoLeg 的对应 | 可直接借鉴 | 不能直接照搬 |
| --- | --- | --- | --- |
| Redis incremental rehash | H1/H2 同时存在，普通操作顺带搬迁，完成后切换 | 显式 debt、每次操作有界推进、完成后换代 | bucket 成本较均匀；没有对象 Delta chain、Frame 共读或 append-only 文件 |
| Sprite LFS segment cleaning | 从旧 segment 搬 live data，产生可复用空间 | benefit/cost 选择、hot/cold 分离、trace simulation | 选择单位是 segment/block；TwoLeg 的对象选择具有共享 Frame 的组合收益 |
| RocksDB / LSM compaction | 旧版本和 sorted runs 带来读、写、空间放大 | compaction debt、soft/hard pressure、rate limit、read-triggered compaction | 多层 LSM 与两文件 FileScope 不同；后台 compaction 也不同于 Commit 内维护 |
| Git pack delta compression | Base 加若干 Delta，读取时逐级应用 | delta depth/read cost 上限，写入节约不能脱离解包成本 | Git 可离线全局 repack，且对象相似性搜索不是本探针焦点 |
| Kafka log cleaner | 按 key 保留最新记录并清理旧 segment | ratio trigger、maximum lag、cleaner throttle 的组合 | Kafka 不保留对象级历史重建链，清理也不受 B/C 原子 Revision 约束 |
| SQLite WAL checkpoint | 小事务积累后做 checkpoint | 作为集中维护造成尾延迟尖峰的对照 | WAL 回写主库，不是渐进对象搬迁策略 |
| Ski-rental / online algorithms | Delta 是暂时少写的“租”，Base 是重置链的“买” | 未知未来下的阈值策略、offline oracle 与 online policy 对照 | Delta/Base 成本变化且周期可重启，并有多对象共享 Frame 外部性 |

## 1. 渐进 rehash：双腿节奏的直接类比

Redis 的 dictionary resize 会建立第二张哈希表，并让普通 lookup/update 操作调用一次 rehash step，使 H1 在活跃使用期间逐步迁入 H2。源码还给单次 rehash 设置空 bucket 扫描上限，以免一次维护工作无界阻塞。

对应到 TwoLeg：

```text
Redis H1                         TwoLeg A dependency
Redis H2                         TwoLeg B
remaining buckets                A-debt objects
one rehash step per operation    one bounded migration slice per Commit
H1 empty, swap tables            debt suitable for evacuation, rotate A/B -> B/C
```

最应保留的规律是“进度与前台操作绑定，但每次工作有界”。对象数量不适合作为唯一预算单位，因为 TwoLeg 对象尺寸、Base/Delta 差额和 Frame 布局都不均匀；bytes 或 exact candidate envelope 更接近真实工作量。

参考：

- [Redis `dict.c`: incremental rehash preparation and steps](https://github.com/redis/redis/blob/unstable/src/dict.c)

## 2. Sprite LFS：搬谁比搬多少更难

Sprite LFS 的 segment cleaner 面对 append-only 日志中的无效旧块。论文发现只挑 utilization 最低的 segment 会反复整理 hot data，于是按下式选择 segment：

```text
benefit / cost = (1 - utilization) * age / (1 + utilization)
```

`1 - utilization` 是可回收空间，`age` 用作数据未来稳定性的代理，`1 + utilization` 近似读取 segment 并回写 live data 的成本。cleaner 还按年龄重组 live blocks，使 hot/cold 数据逐渐分离。

对 TwoLeg 的启发不是机械复制公式，而是把 optional Base/migration 看成“付出额外写入，换取一段能保持多久的物理债务下降”：

```text
MigrationBenefit(S)
    = retired Previous-frame bytes
    + avoided expected cold-read bytes

MigrationCost(S)
    = extra exact write bytes versus inherit/Delta
```

这里的 `S` 可能是对象组。若多个 live objects 共享一个 A Frame，迁走最后一个依赖者才释放该 Frame；因此物理收益不一定能按对象相加。单对象 greedy ranking 可以作为简单 baseline，但不能被当作普遍最优算法。

参考：

- Mendel Rosenblum, John K. Ousterhout, [The Design and Implementation of a Log-Structured File System](https://web.stanford.edu/~ouster/cgi-bin/papers/lfs.pdf), 1992，尤其是 segment cleaning 与 cost-benefit policy。

## 3. LSM compaction：放大因素、债务和压力线

LSM 系统把 compaction 视为 write amplification、read amplification 与 space amplification 的权衡。leveled、tiered/universal 以及 Fluid LSM 选择了不同的合并频率与结构，因此不存在脱离 workload 的统一最优配置。

这支持 TwoLeg evaluator 当前的基本做法：在同一 manifest、workload 和 horizon 内保留 raw `Wworkload/Pworkload/F/R`，先报告 Pareto 与 `no winner`，而不是预设跨 workload 的加权总分。

几个特别可借鉴的工程机制：

- RocksDB 用 estimated pending compaction bytes 表示维护债务；达到 soft limit 时减速，达到 hard limit 时停写等待 compaction；
- compaction rate limiter 用 token/refill 控制后台 IO，并明确在平滑 IO 与允许 burst 之间取舍；
- LevelDB/RocksDB 可以由读取触发 compaction。LevelDB 的 `allowed_seeks` 用粗略 IO 等价关系，把已经发生的额外读取与整理成本换算；
- universal compaction 以 size amplification、sorted run 数量和 size ratio 等不同条件触发，不把所有目的压进一个分数；
- Dostoevsky/Fluid LSM 说明应由 workload 与硬件选择空间—时间权衡，而不是把一种合并频率固化成普遍答案。

对应关系：

```text
TwoLeg A evacuation bytes E       pending compaction debt proxy
Base budget per Commit            compaction rate/work budget
read-amplification limit          read-triggered compaction threshold
max file tail / capacity gate     space pressure / hard stop
```

参考：

- [RocksDB Tuning Guide: amplification factors](https://github.com/facebook/rocksdb/wiki/RocksDB-Tuning-Guide)
- [RocksDB Compaction](https://github.com/facebook/rocksdb/wiki/Compaction)
- [RocksDB Write Stalls](https://github.com/facebook/rocksdb/wiki/Write-Stalls)
- [RocksDB Rate Limiter](https://github.com/facebook/rocksdb/wiki/Rate-Limiter)
- [LevelDB `version_set.cc`: seek-triggered compaction](https://github.com/google/leveldb/blob/main/db/version_set.cc)
- Niv Dayan, Stratos Idreos, [Dostoevsky: Better Space-Time Trade-Offs for LSM-Tree Based Key-Value Stores](https://scholar.harvard.edu/files/stratos/files/dostoyevski.pdf), SIGMOD 2018。

## 4. Git pack：Delta chain 需要独立 guardrail

Git pack 会在有限 window 内寻找 delta base，同时用 `--depth` 限制最大 delta-chain 深度；文档明确指出链太深会增加 unpack 成本。

对应到 TwoLeg，可以区分：

- soft preference：预计下一次 Delta 的冷读放大超过阈值时倾向 Base；
- hard guardrail：若产品 SLO 最终要求最大 chain depth、reconstruction Frame count 或 cold bytes，则超限必须 Base，不能再交给综合 score 权衡。

当前 evaluator 已采用 synthetic `cold-load after each workload Save` 日程来累计读压力，但仍没有真实产品
cold-start 频率或 SLO；因此只有 soft preference 是合理的最小实现，不据此提前增加硬阈值。

参考：

- [Git `repack`: `--window` and `--depth`](https://git-scm.com/docs/git-repack.html)

## 5. Kafka cleaner：效率阈值、最长拖延与限速组合

Kafka log cleaner 同时提供：

- `min.cleanable.dirty.ratio`：积累到值得清理的比例；
- `min/max.compaction.lag.ms`：避免太新数据立即整理，也避免低流量日志无限期不整理；
- `log.cleaner.io.max.bytes.per.second`：限制 cleaner 平均 IO。

这表明一个简单、稳健的 TwoLeg 控制器可以分层，而不必寻找单一万能 score：

1. 正常情况下用 `E/G` 一类比例判断完整 evacuation 是否划算；
2. 用 per-Commit Base budget 平滑维护；
3. 用最小进度维持活性；
4. 只有真实 workload 暴露 starvation 后，再增加 max chain/tail/age backstop；
5. exact frame/address capacity 始终是独立 hard gate。

参考：

- [Apache Kafka design: configuring the log cleaner](https://kafka.apache.org/30/design/design/)
- [Apache Kafka broker configs](https://kafka.apache.org/33/configuration/broker-configs/)

## 6. SQLite WAL：集中 checkpoint 的对照

SQLite WAL 默认在 WAL 达到一定页数时自动 checkpoint。官方文档明确描述了它的性能形状：大多数 Commit 很快，但触发 checkpoint 的 Commit 可能明显更慢；checkpoint 频率同时影响读性能、平均写性能与 WAL 大小。

这正是 TwoLeg 渐进迁移希望改善的现象。SQLite 的价值主要是提供负面对照和 workload 维度：只看平均/总写入会掩盖偶发 checkpoint 峰值，因此 `Pworkload = PeakWorkloadCommitWriteBytes` 不能被 `Wworkload` 替代。

参考：

- [SQLite Write-Ahead Logging: checkpointing and performance](https://www.sqlite.org/wal.html)

## 7. 在线算法与预算选择

### Base/Delta 是带状态的 rent-or-buy

局部地看，Delta 用较少的本次写入换取更长的未来重建链，类似“租”；Base 立即多写并重置链，类似“买”。未来还有多少 Update、何时 cold-load 均未知，所以这是在线决策，而非能普遍局部最优的问题。

经典 ski-rental 提醒我们：

- 不知道未来时应讨论竞争性、regret 或相对 offline oracle 的差距；
- oracle 可以用于评价，不应把未来 workload 暴露给在线策略；
- 对象成本变化、周期重启和共享 Frame 使 TwoLeg 只与 ski-rental 相似，并不等价。

参考：

- [Rent, Lease, or Buy: Randomized Algorithms for Multislope Ski Rental](https://epubs.siam.org/doi/10.1137/100794018)

### 预算内 Base 集合是 knapsack 的近亲

若固定 Stay/Rotate target，且每个对象的成本和收益都可加，那么在 Base budget 内选对象接近 0/1 knapsack。当前按 amplification 排序并跳过放不下对象，是可解释的 deterministic heuristic，不具备 knapsack 最优保证。

一旦收益包含“完整释放共享 Previous Frame”，对象之间出现互补关系，问题更接近带组合收益的 budgeted set selection。此时最诚实的研究工具不是立即给产品策略加入复杂 solver，而是在小对象 workload 上用 test-local exhaustive oracle 量出简单 heuristic 的损失；只有自然反例证明损失重要，复杂化才获得依据。

## 8. 对 benchmark workload 的直接启发

一份有辨别力但不泄露未来的 corpus，至少应逐步覆盖以下正交维度。每个 workload 应只有一个明确动机，避免用一条复杂 trace 同时证明所有事情。

| 维度 | 需要辨别的策略能力 | 典型对照 |
| --- | --- | --- |
| Update locality | hot object 是否应长期 Delta，cold object 是否应早 Base | uniform、单 hot set、冷热分层 |
| Base/Delta size ratio | 写入节约与重建增长的阈值行为 | tiny delta、delta≈base、delta>base |
| Object size skew | 按对象数推进是否造成 byte burst 或饥饿 | many-small、few-large、heavy tail |
| Lifetime/churn | 刚迁移对象很快 Remove/Update 是否浪费 | stable cold、short-lived、periodic churn |
| Frame sharing | per-object 排序能否释放粗粒度冷读 | shared、split、最后依赖者效应 |
| Debt pressure | migration service rate 是否跟得上新 debt | steady、bursty、temporarily idle |
| Rotation boundary | `E/G` 邻域和 strict/equality 行为 | threshold bands、near-empty evacuation |
| Foreground burst | 维护工作是否把可行 Commit 推过 Frame envelope | small regular、grouped burst |
| Horizon phase | terminal liability 是否只是被推迟 | aligned closed horizon、不同起始 epoch phase |
| Restart schedule | terminal-only T 是否代表周期读取需求 | 当前采用 cold-load after each workload Save；未来可比较 no/periodic restart |

当前 canonical R 已明确采用最后一项的 synthetic schedule；它是统一比较协议，不是对生产 restart 频率的事实声明。

### Workload 设计纪律

- 固定 generator/version/seed，并冻结 expanded trace hash；
- 所有策略消费完全相同的 bootstrap、trace、horizon 和 terminal settlement；
- 在线策略只看当前 canonical facts，不看 step index、未来 trace、另一候选的 feasibility 或运行后 observation；
- typed inadmissibility 不转换成巨大罚分，也不与 admitted outcome 平均；
- 同 workload 内可比较 raw `Wworkload/Pworkload/F/R`；跨 workload 汇总前必须先明确归一化与产品权重；
- 隐藏/保留 workload 只有在策略作者不能读取其源码或结果时才真正防过拟合；同仓库并行 subagent 竞赛默认不是盲测。

## 9. 对当前研究路线的结论

当前 `ReadAmplificationBaseBudgetPolicy` 已经组合了有充分前例的最小机制：read-amplification threshold、normalized debt trigger 与 per-Commit Base budget。Update 和目标动作支持的 NoChange 只有超过读放大阈值才形成 Base 动机；动机按放大率排序后取预算前缀，Stay 或 `E==0` 的 Rotate 才会在预算筛选结果为空时整体放行首个不可拆分动机。旧实现的无条件 NoChange progress floor 会绕过预算，已由 `oversized-cold-nochange-tiny-clock` 反例暴露并移除；它是历史 bug，不是当前 baseline 机制。

最值得成为后续 challenger 或 contract-evolution probe 的方向依次是：

1. **payload knapsack / rent-or-buy**：在现有 `G/E/H/D/B` 事实内比较简单 greedy、预算组合与 chain reset；
2. **debt-pressure guardrail**：当冻结 workload 证明比例触发会 starvation 时，引入 soft/hard pressure，而非新权重；
3. **offline small-N oracle**：测量简单 greedy 距离同一 horizon 下可实现 Pareto frontier 的差距；
4. **LFS-inspired physical benefit/cost**：当 Frame grouping 对 canonical outcome 产生命名反例后，演化策略输入并评价对象组能释放的 Previous Frame bytes 与额外写入；
5. **read-triggered state**：只有真实 restart/read schedule 证明当前 synthetic cumulative R 与预测式 amplification 不足时，才引入跨 Commit read statistics。

当前仍有两个不同层次的开放问题：没有读放大动机时，稳定 A debt 可能长期不推进；`E/G` 只回答
“现在轮转有多贵”，尚未区分 Ready-to-Rotate 与 Should-Rotate。基于上一轮 Update 的热度修正和按
`Base - Delta` 边际成本选择，均保留为后续独立候选，不混入本次基线 bug 修复。

不建议在这一阶段直接引入 reinforcement learning、通用策略插件发现、自动参数搜索、标量排行榜或可持久化 candidate archive。先让统一 workload、公共策略契约、隔离运行和原始评价结果构成一个可重复的“测量台”；策略演进仍可由多个独立实现并行进行。
