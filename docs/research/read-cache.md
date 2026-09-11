结论先给：**这个重复读是结构性的、真实存在，值得做缓存；但第一步不是写缓存，而是先测量重复率**。真要加，我推荐挂在 `DurableGraph.StateStore.Storage` 的 `StateRevisionStore`（缓存“解码后的 `StateRevision`”），而不是 `RbfSegmentStore`。理由从代码事实展开。

## 1. 现有缓存分层：已经有 3 层，缺的是“解码结果”这一层

| 层 | 缓存对象 | 现状 | 对本问题的作用 |
|---|---|---|---|
| OS page cache | 文件页 | 已有（`RandomAccess.Read` 走 pread 缓冲路径） | 省物理 I/O，不省用户态拷贝与解析 |
| `Rbf` 每文件页缓存 `ReverseReadCache` | 4KB 页 × 2–64 槽（默认 `Slots16`=64KB，Clock 驱逐） | 已有，随 `RbfCacheMode` 配置；为反向扫描的 20B 小读设计；append 有 `InvalidateCacheFrom`/`NotifyFileLengthChanged` 失效钩子 | 单帧超过 2 页时基本 passthrough；64KB 装不下几个热修订帧 |
| `StateStore.RevisionReadSession` | 单操作内 `(ObjectId, head)` 的 DTO 行 | 已有 | 不阻止 `StateRevision` 线格式被重复解码 |
| **解码后 `StateRevision`（按 `FrameAddress`）** | — | **缺** | ← 正是你说的“父链单点查询反复读同一帧” |

注意 `ReverseReadCache` 自己的调研文档里已写明它服务的是反向小读；大帧随机读不是它的目标场景。

## 2. 重复读的代码证据（不是假想）

`StateRevisionStore.Read`（`StateRevisionStore.cs:50` 的 TODO 处）每次调用都是全流程：租 reader lease → `ReadPooledFrame`（ArrayPool 租借 + 整帧 memcpy + framing/CRC）→ wire 解析 → 一大堆分配。而调用侧的重复是结构性的：

- `RevisionDecoder.ReadCore`：先物化一次 head map，然后**逐对象** `ReadObjectVersionChain`（`RevisionDecoder.cs:51,65`）；每个对象链读取内部又 `ResolveObjectHead` → 再物化一次同一个 map（`:185`），链上每一步还要 `ReadLiveObjectHeadMap(parent)` + `Read(prior)`（`:149`）。N 个对象 → 同一批帧被解码 **N+1 次量级**。
- 同样形态出现在 `LoadedRevisionPlanner`、`ObjectRevisionPlanner`、`CapturedRevisionPlanner`、`EventHistoryRepository`。
- `ValidateDirectPriors`（`:172`）里多条 Delta 记录可以共用同一个 prior 帧，单次 Append 内也会重复 `Read`。
- DB-028 §3.3、PROJECT-STATE 都记录了“先直读、测量后再评估解码缓存”的 TODO。

## 3. 为什么不该放在 `RbfSegmentStore`

1. **缓存物的生产层在 Storage**：Rbf 按设计不解释 frame 语义（README 明说不替上层 ReadFrame），它只能缓存字节，不能缓存解码结果——而重复成本的大头是 wire 解析与分配（托管堆），不是 I/O。
2. **和已有页缓存职责重叠**：Rbf 已经有一层页缓存；再加“帧字节缓存”等于在同一层放两种粒度、两套驱逐/失效策略，命中率互相干扰，收益还边际。
3. **可复用性是假说**：要证明 `RbfSegmentStore` 层值得放通用缓存，需要第二个消费者也反复读同一批帧（EventJournal 是顺序写/顺读为主，我没看到反证前只能算未知）。按 `AGENTS.md` 的纪律，没有当场的实验或消费者，不加通用抽象。
4. OS 类比：文件系统不会把 inode cache 做进块设备驱动。页缓存（OS/Rbf）管“字节”，解析后元数据缓存（Storage）管“重复解析”，派生索引（head map）是再上一层——各层只缓存在本层生产、且上层还会重复生产的东西。

如果未来测量显示 **I/O + 池化拷贝 + CRC 校验**才是主导、且出现第二个同型消费者，正确形状也是在 `Rbf`/段层做“owned buffer 的帧缓存”，而不是扩大页缓存。

## 4. 若加在 `StateRevisionStore`，设计要点

- **键值**：`FrameAddress → StateRevision`。已核实 `StateRevision` 与 `ObjectVersionRecord.Body`（`ReadOnlySpan` 包装私有数组）都是不可变、owned 的，跨调用共享安全——正好满足 TODO 里“Cache owned revisions, never pooled Frame views”。
- **作用域**：建议先做**存储实例级**（=一次仓库打开）。依据：仓库独占 `repository.lock`、单写者、**append-only**；DurableGraph 在 `GraphResources.StrictOptions` 里强制 `RecoverActiveTailOnOpen=false`（打开期间不会有截断/recovery）→ 已缓存帧永不失效，新 append 只产生 miss。且复用距离很短：连续 Commit 反复校验同一个父修订、连续浏览操作命中同一批帧。操作级作用域可以退而求其次，但会丢掉跨操作复用。
- **预算与驱逐**：字节记账（body 字节 + 粗略开销）+ Clock/LRU，小预算即可（复用距离短）；做成 internal 类型，带内部命中统计供测试。
- **纪律**：只缓存成功解码的结果；不做负缓存；沿用单线程契约、不加锁；**不做跨打开进程级缓存**（锁已释放，文件可变，失效权威不存在）。
- **第二步再评估**：head map memo 或单 ID head 早退查询（DB-028 已留这条）；`ResolveObjectHead` 目前的“全 map 物化只为取一个 ID”本身也可以先做算法优化，`ReadObjectVersionChain` 也可以接受调用方已物化的 map/head 以避免顶层重复。

## 5. 最小验证实验（建议的下一步）

- **问题**：一次 N 对象解码 / 一轮 Commit 中，`Read(address)` 的重复率是多少？
- **最小判据**：唯一帧数 : 总解码次数 ≥ 1:3，或重复解码占主导 → 上缓存；否则先做算法去重。
- **做法**：给 `StateRevisionStore` 加临时内部计数器（或测试里装饰 `IRbfSegmentStore` 统计 `OpenReader` 与帧读取），跑 `RevisionDecoder` 的 N 对象场景和连续 Commit 场景；顺带试一下把 `CacheMode` 提到 `Slots64` 看页缓存能吃到多少。
- **验收断言**：加缓存后“每个唯一 `FrameAddress` 每实例最多 wire 解码一次”；预算驱逐、共享安全（缓存对象不被改写）、失败不缓存都有测试。

需要的话，我可以按这套思路帮你起一个小的测量切片（记录问题、判据、范围，按仓库惯例补进 PROJECT-STATE/设计分支），先拿数据再定缓存形状。
