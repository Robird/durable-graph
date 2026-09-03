# 阶段 A：MultiSegment In-Memory StateStore Probe 目标设计

> 状态：Selected Target Design / G0-G4 Implementation Guide
>
> 最近校准：2026-09-02
>
> 适用范围：`experiments/MultiSegmentStateStoreProbe`

本文定义 MultiSegment StateStore 内存探针要逐步实现和验证的完整目标，范围到 G4（Workload、policy 与
evaluator）为止。它是阶段 A 编码的主设计入口，但不是当前实现事实，也不冻结 DurableGraph 的公开 API、
正式 wire bytes、真实 filesystem/reopen 或产品 durability 协议。

文档职责分工如下：

- 本文回答“目标系统如何工作、哪些不变量必须由代码证明”；
- [`STATESTORE-SUBSYSTEM-DESIGN.md`](STATESTORE-SUBSYSTEM-DESIGN.md) 记录阶段 B 的正式 StateStore
  Sub-System 分层、复用调查和晋升条件；
- [`DB-014`](../../docs/design-branches/0014-multi-segment-backward-file-distance.md) 记录为什么从
  TwoLeg 转向多历史 Segment；
- [`PROJECT-STATE.md`](PROJECT-STATE.md) 只维护当前已完成事实、下一切片和未闭合事项；
- [`README.md`](README.md) 只介绍当前可运行能力；
- [`TwoLegRotationProbe`](../TwoLegRotationProbe/PROJECT-STATE.md) 是可借鉴的冻结技术储备，不是本项目依赖。

若本文与当前代码或测试不一致，差异表示“尚未实现”，不能把目标描述成现状。若实验否定本文，应先
记录证据并修订设计，而不是扭曲实现来维护文档正确。

## 1. 问题与一句话模型

本探针在内存中建模一串单调编号、append-only 的 Segment/Frame。最新 Published Revision 可以引用任意
更早 Segment 中的 Revision 或 ObjectVersion；每次 Save 在取得 writer 前仅按现有文件 tail 与 soft rollover
threshold 决定 append destination，随后在最终 origin 编码一次，不搬迁冷对象，也不改变 Base/Deltify 决策。

```text
exact PublishedHead
    -> Revision OVD Base/Delta chain
    -> live ObjectId -> absolute ObjectVersion head
    -> ObjectVersion Base/Delta chain
    -> current logical state
```

最小 authority 只有两层：

1. `PublishedHead` 选择唯一 accepted Revision；
2. 该 Revision 的 OVD 选择 live object membership 与 exact ObjectVersion heads。

文件目录、最大文件号、最后一个完整 Frame、内存 StateMap、append cursor、recovery closure 和统计信息
都只是派生事实，不能替代这两层 authority。

## 2. 范围、运行模型与非目标

### 2.1 当前目标

- 1-based `UInt32 FileNumber` 与无集中 catalog 的 direct file addressing；
- 进程内 absolute address 与持久化 backward-distance reference；
- append-only Segment/Frame store 和独立 soft rollover；
- one Revision / one RBF Frame；
- OVD Base/Delta、ObjectVersion Base/Delta 与 F1-F4 跨文件重建；
- exact PublishedHead、orphan 不可见性和 fail-close load；
- deterministic workload、Base/Deltify baseline policy 与原始 W/P/F/R/L 指标；
- 为阶段 B 提供可抽取的逻辑 publication、地址、planner 与 evaluator 证据。

### 2.2 运行假设

- 单 Store 单 writer；宿主负责串行化 Save，不在探针内发明 multi-writer 协议；
- 一次 Save 从唯一 exact parent PublishedHead 派生；不支持 stale-snapshot Save、branch merge 或 import；
- workload 中对象彼此独立，ObjectId 稳定且不复用；对象引用图、reachability 与 GC 属于 DurableGraph
  外层，不在本探针重复建模；
- 阶段 A 始终保持 in-memory、size-only/provisional-layout 模型；
- 所有已发布历史文件可以永久保留，v1 不承诺总空间、文件数或冷读 fan-out 有界。

### 2.3 明确非目标

- TwoLeg 的 A/B/C、Stay/Rotate、A-debt、evacuation、NoChange migration 与 two-file closure；
- 真实 filesystem、RBF file handle、reopen、durable flush、head carrier 或 crash recovery；
- 自动删除、refcount、incremental cleaner、在线 compaction 或冷热分层；
- Extent 或一个 Revision 跨多个 Frames；
- 多 writer、lease、分布式 CAS 或长寿命并发 reader 协议；
- Product `CommitManifest`、正式 StateStore Sub-System、公开 API、格式兼容和旧 probe bytes 迁移；
- Schema codec、CLR graph traversal、ArtifactStore 或 Four-Stores-One-Commit 的完整实现；
- 标量总分、排行榜、产品默认阈值或 cold-read SLO。

## 3. 术语与唯一身份

### 3.1 Logical Store 与 Segment

模型中的一个 Segment 对应未来一个 canonical-named RBF file，不增加 `SegmentId`、`FileId` 或路径
catalog：

```text
filePath = storeDirectory / FormatCanonicalFileName(fileNumber)
```

`FileNumber` 是 Store directory 内 1-based `UInt32`：

- `0` 非法；
- 新文件号单调增加，任何已发布文件号不得复用；
- `UInt32.MaxValue` 后 fail closed，不 wrap；
- path formatting/parsing 必须是一一对应的 canonical function；
- 当前十位十进制 `.rbf` 名称只是 probe grammar，尚未冻结为产品文件名。

阶段 A 不访问真实目录；`FileNameConvention` 只验证 FileNumber 到 canonical name 的 direct mapping。
目录 scope、文件混入与 Store/File header 属于阶段 B。

### 3.2 Frame 与 Revision

阶段 A 建模一个 Revision 对应一个 provisional RBF-like Frame。Frame 包含：

```text
RevisionFrame
    PriorRevision
    ObjectVersion records [0..N]
    ObjectVersionDictionary version
    TailMeta directory / offsets
```

Frame 是 append、完整性校验和物理读取计量粒度。阶段 A 用 in-memory full-Frame validation 模拟 authority
边界；`IRbfFile.ReadTailMeta` 的 L2 preview、完整 L3 read 与真实布局在阶段 B 接入。

### 3.3 PublishedHead 与 append destination

`PublishedHead` 是 `None` 或一个 exact Revision `AbsoluteFrameAddress`。空 Store 可以使用 `None`；首次成功
Save 写 OVD Base。测试 fixture 可以创建 bootstrap Revision，但它不是目标格式强制要求。

PublishedHead 所在文件不必是物理 append-current file。append destination 由当前 Store inventory 与
rollover 规则决定；二者不能合并成一个 cursor authority。

产品中 PublishedHead 由谁持久发布、如何与外层 CommitManifest 协调，属于
[`STATESTORE-SUBSYSTEM-DESIGN.md`](STATESTORE-SUBSYSTEM-DESIGN.md) 的阶段 B 问题。阶段 A 只保留一次
原子的 in-memory head assignment。

## 4. 地址模型

### 4.1 Runtime authority

进程内所有已解析引用统一使用绝对地址：

```text
AbsoluteFrameAddress
    FileNumber
    FrameTicket          // probe-local strong value，提供 start/length
```

`FrameTicket` 提供 Frame start/length，以便验证 same-file strictly-earlier、读取模拟 Frame 和计算物理指标；
当前 G1 已实现 probe-local strong value。与真实 `Atelia.Data.SizedPtr` 的整合属于阶段 B。

ObjectVersion 的逻辑定位是 `(AbsoluteFrameAddress, ObjectId)`；是否建立单独 C# wrapper 由实现切片决定，
不形成另一套持久 identity。

### 4.2 Origin-scoped relative reference

持久 required Frame reference 只有一种领域含义：

```text
RelativeFrameTicket
    BackwardFileDistance : UInt32
    FrameTicket

targetFileNumber = originFileNumber - BackwardFileDistance
```

当前代码以 `RelativeFrameTicket` 实现这一概念，不再保留旧 `BackwardFrameReference` authority。

`FileScope` 是解释 relative value 的小型上下文：

```text
FileScope
    OriginFileNumber

Resolve(RelativeFrameTicket) -> AbsoluteFrameAddress
Relativize(AbsoluteFrameAddress) -> RelativeFrameTicket
```

`OriginFileNumber` 是承载该字段的 containing Frame 所在文件，不是 Store 当前 append file，也不是最新
PublishedHead 所在文件。它由 TwoLeg 的 `CurrentFileNumber` 概念演化而来，但改名以消除两种 current 的
歧义。

### 4.3 Strictly-earlier law

所有已编码 external edge 都必须指向 earlier state：

```text
target.FileNumber < origin.FileNumber
or
target.FileNumber == origin.FileNumber
    && target.FrameStart < origin.FrameStart
```

`FileScope.Resolve` 只负责文件号算术。reader acceptance boundary 必须同时接收 containing
`AbsoluteFrameAddress`，再验证 same-file physical order。仅检查 `BackwardFileDistance == 0`、ticket
存在或数值非零都不充分。

字段语义还决定 target 是否属于 current-recovery dependency：

- OVD Delta 的 PriorRevision、OVD live External binding 与 ObjectVersion Delta parent 是 current-required；
- OVD Base 的 PriorRevision 只服务 historical lineage，不进入 current recovery closure；
- 所有 edge 都先验证 canonical encoding 与 strictly-earlier；只有当前 traversal 需要的 target 才在本次读取中
  要求存在并完成 Frame validation；lineage-only target 在显式 lineage query 时才要求存在。

reader 必须拒绝：

- future、underflow、FileNumber zero；
- required zero ticket；
- non-canonical、overflow、truncated VarUInt；
- same Frame 或 same-file future Frame；
- current-required target 的 missing file、missing Frame、错误 tag 或未通过完整 Frame 校验；
- cycle 或任何绕过 strictly-earlier law 的引用。

### 4.4 Absolute-normalize 与 relative-reencode

decode 后立即得到 absolute address；OVD cache、Revision plan 与 policy facts 都不得长期保存祖先 raw
relative bits。

写入新 origin 时必须重新 relativize：

```text
F2 raw(distance=1, ticket=T) -> absolute F1@T
F4 rewrite                 -> raw(distance=3, ticket=T)
```

把 F2 raw bits 原样复制到 F4 会静默错指 F3。所有 inherited OVD binding、Delta parent 与 Revision prior
都遵循同一规则。

### 4.5 字段局部语法

不要建立一个同时容纳所有特殊值的通用 address union：

- required external reference：`BackwardFileDistance + FrameTicket`；
- optional `PriorRevision=None`：只属于该 optional 字段；
- OVD `BindSelf`：只属于 OVD binding，指 containing Revision Frame；
- OVD external binding：required relative reference；
- explicit external binding 若又指回 containing Frame，必须拒绝，以保持 canonical representation。

decode 后它们都归一化为 absolute runtime values。

## 5. Revision、OVD 与 ObjectVersion

### 5.1 Shared prior Revision

每个 non-genesis Revision 保存 exact `PriorRevision`，它就是本次 Save 所基于的 previous PublishedHead。

- OVD Delta 用它继承 prior live map；
- OVD Base 的 current materialization 不继承 prior map；
- ObjectVersion Base 的 historical lineage 通过它查询 prior OVD，而不是每个 Base 重复保存 direct parent；
- genesis 没有 prior；
- mixed-provenance Revision、stale Save 和 branch merge 不受支持。

这项 shared anchor 保证 Base 只终止 current reconstruction，不切断 object-level history。OVD Base 的 anchor
只做 encoding/earlier validation，不进入 current recovery closure；目标缺失只会使显式 lineage query 失败。
lineage inspector 可以晚于 current reconstruction 实现，但不得通过删除 target field shape 来回避已选择语义。

### 5.2 ObjectVersionDictionary

OVD 是 Published Revision 的 live membership 与 head binding 唯一 authority：

```text
ObjectId -> AbsoluteFrameAddress of latest ObjectVersion
```

持久版本分为：

- `Base`：保存完整 live map；materialization 在此停止，不继承 prior current map；
- `Delta`：先 materialize exact PriorRevision，再应用 Upsert/Remove mutations。

binding 分为：

- `BindSelf`：本 Revision 新写的 ObjectVersion；
- `External`：同文件 earlier 或任意更早文件的 existing head；
- `Remove`：仅存在于 OVD Delta mutation；OVD Base 直接省略 dead ObjectId。

普通 exact-parent Save 中，OVD Delta 的 canonical mutations 只有新写 record 的 `BindSelf` Upsert 与
`Remove`；NoChange 通过省略 mutation 继承。`External` 只用于 OVD Base 的完整 map，避免为同一 Delta
状态引入冗余表示或暗中支持 rollback/import。

写 OVD Base 只是重编码完整 live map，不等于 relocation 对象。F4 OVD Base 可以直接把冷对象 binding 指回
F1，而不在 F4 产生该对象的 Base record。

内存 StateMap 只能是 `MaterializeLive(PublishedHead)` 的可丢弃 cache；它没有独立更新或发布协议。

### 5.3 ObjectVersion

```text
ObjectVersion Base
    ObjectId
    LogicalVersionOrdinal
    full payload

ObjectVersion Delta
    ObjectId
    LogicalVersionOrdinal
    exact prior ObjectVersion reference
    delta payload
```

- Base 包含完整 current state，current reconstruction 在 Base 停止；
- Delta 必须指 same ObjectId 的 exact prior ObjectVersion，而不是“某个 prior Revision 中大概的版本”；
- domain change 使 `LogicalVersionOrdinal` 严格 `+1`；
- `SameStateRebase` 写 Base 但保持 ordinal 不变；
- Insert 写首个 Base；Remove 不写 domain record，只修改 OVD；
- Base 不保存 direct parent，historical lineage 经 containing Revision 的 shared prior anchor 查询。

payload 在本探针中可以继续是 size/value stand-in。Schema、真实 serializer 与 upgrade 是 DurableGraph 外层
消费者，不能反向改变这里的版本链与 authority 语义。

## 6. Save pipeline

### 6.1 输入与 normalization

一次 Save 接收：

```text
SaveStateWorkload
    Inserts
    Updates
    Removes

ExpectedParent = exact PublishedHead
```

所有 facts 必须从同一个 ExpectedParent materialized snapshot 派生：

- Insert：无 prior，必须 Base；
- Update：有 frozen post-state Base size、Delta size 与 source reconstruction facts；
- Remove：从 post-live OVD 消失；
- NoChange：不是 caller 输入；它由 parent live map 减去 Inserts/Updates/Removes 后派生，默认继承 exact
  prior head，也可被 policy 选择为 `SameStateRebase`；
- ObjectId membership、source heads 与 policy inputs 在本次 planning 期间不可变化。

### 6.2 冻结 origin-free logical plan

policy 只产生逻辑决策：

- 每个 Update 是 Base 或 Delta；
- 每个 NoChange 是 Inherit 或 SameStateRebase；
- OVD 是 Base 或 Delta；
- OVD membership 与 mutations。

随后构造一个 origin-free `RevisionPlan`。其中所有 external heads、Delta parents 与 PriorRevision 都保持
absolute address。此时还没有持久 relative bytes，也没有选择新的策略分支。

### 6.3 Placement、编码与定尺

正确顺序是：

```text
plan = FreezeLogicalDecisions(exact parent facts)

if appendCurrentFile.TailOffset >= RolloverThresholdBytes:
    targetFile = appendCurrentFile.NextChecked()
    targetTail = RbfHeaderOnlyTail
else:
    targetFile = appendCurrentFile
    targetTail = appendCurrentFile.TailOffset

candidate = RenderAndMeasure(plan, targetFile, targetTail)
ValidateHardBounds(candidate)
AppendExactlyOneRevisionFrame(candidate)
```

若 Store 尚无数据文件，首次 placement 创建 F1。若最大号文件存在但尚无完整 Frame，则它是可复用的 empty
append destination。`RolloverThresholdBytes` 必须 4-byte aligned、严格大于 header-only tail，并且不超过
最大可表示 Frame start，因而 header-only Segment 不会被连续跳过，正常 writer 也不会在轮转前进入不可寻址
的 Frame start。

文件选择不读取 candidate 大小，也不重新运行 policy。文件号变化会改变 `BackwardFileDistance` 及其 VarUInt
宽度，因此必须先确定最终 Segment，再从 plan 中保存的 absolute addresses 生成 relative references。显式
multi-origin renderer witness 继续证明：同一个 plan 在不同 origin 的 raw bytes/layout 可以不同，但解析后必须
回到相同 absolute targets；正常 Save 只在最终 origin render 一次。

### 6.4 Soft rollover threshold 与 overshoot

`RolloverThresholdBytes` 是下一次取得 writer 前的 soft trigger，不是文件大小上限：

- existing tail 小于 threshold：本次 Revision 留在当前文件，即使 append 后越过 threshold；
- existing tail 达到或超过 threshold：本次 Save 在 render 前 checked 切换到 next file；
- fresh/empty file 上的单个合法 Revision 即使超过 threshold 仍然 append；
- StateStore 每次 Save 单独借还 writer lease，且每个 lease 只 append 一个 Revision Frame，因此 overshoot 最多
  来自一个合法 RBF append envelope；
- 不引入 `OversizeSegment` 类型或循环创建空文件；
- 若 candidate 超过 RBF single-Frame hard bounds，typed reject，不拆分、不 fallback、不改策略。

file switch 绝不能强制 Base、SameStateRebase、OVD Base 或 cold object relocation。

### 6.5 Admission、append 与 logical publication

最终 candidate 在 append 前必须完整通过：

- source head 仍等于 ExpectedParent；
- 所有 membership、address、canonical encoding 与 strictly-earlier rules；
- RBF payload、TailMeta、Frame start、`SizedPtr` 与算术 hard bounds；
- candidate 可从 exact dependencies 完整重建；
- append destination 与预计算 layout 一致。

阶段 A 的 logical publication 顺序：

```text
append complete candidate Frame to the in-memory Store
atomically assign exact PublishedHead
install derived cache
```

这里的 atomic 只指一次 in-memory assignment，不宣称 process/OS/power-loss durability。真实 head carrier、
flush ordering、ambiguous I/O outcome 与 reopen 全部移入阶段 B。

失败语义：

- append 前 rejection：Store 与 PublishedHead 都不变；
- append 成功、publish 前失败：旧 PublishedHead 仍是 authority，新 Frame 是不可见 candidate/orphan；
- selected candidate hard reject 不自动换另一种 Base/Delta 或 OVD mode。

只有 publish 成功后，才能从新 PublishedHead 重新 materialize 并安装 volatile cache。若 cache 安装失败，
new head 仍是本次模拟运行的 authority；runner 应丢弃 partial cache并从 exact head重试 materialization，
不得回滚成旧 head或重复 apply。真实“已提交但 session 不可继续”与 poisoned session 属于阶段 B。

## 7. Load、materialization 与 dependency observation

### 7.1 Load current state

```text
1. 获得 exact PublishedHead；None 表示空 Store。
2. 读取并完整验证 head Revision Frame。
3. materialize OVD：Base 停止，Delta 追 exact PriorRevision。
4. 将所有 binding 立即 absolute-normalize。
5. 对每个 live ObjectVersion：Base 停止，Delta 追 exact prior ObjectVersion。
6. 每一 hop 验证 canonical、strictly-earlier、存在、Frame integrity、ObjectId 与 cycle。
7. 全部成功后一次性暴露 materialized state。
```

任一 required dependency 缺失时整个 PublishedHead fail closed。禁止跳过坏对象、产生 partial live map、搜索
“较新可用 Frame”或退回另一个未经 authority 选择的 Revision。

### 7.2 In-memory head 与 orphan observation

阶段 A 不扫描目录或发现 head。caller/runner 显式持有 `PublishedHead=None|Address`，并验证：

1. `None` 精确表示本次模拟的 empty Store；
2. 最大 FileNumber、最大 ticket 或最后 appended Frame 都不能推导 PublishedHead；
3. append 后未 publish 的 Frame 不得被 materialization 看见；
4. 未被 exact head 当前 closure 访问的 Frame 只能称为 `UnreachableFromCurrentHead`。

仅凭当前 head 无法区分“从未发布 orphan”与“过去曾发布、现在不在 latest closure 的历史 Frame”。v1 不需要
这个区别，因为两者都不自动删除。

### 7.3 Derived dependency inspection

一次成功 current-state materialization 可附带收集：

- unique required Frames / Files；
- OVD replay Frames；
- ObjectVersion reconstruction Frames；
- per-object reconstruction bytes；
- 最大 backward distance；
- current-head closure 所 pin 的历史文件；OVD Base 的 lineage-only PriorRevision 不计入 current closure。

这些都是从 PublishedHead 派生的 observation，不落盘为 refcount/pin authority，也不授权删除其余文件。

### 7.4 Historical lineage

current reconstruction 不跨越 ObjectVersion Base。显式 lineage 查询则可以：

```text
Delta -> exact per-object parent
Base  -> containing Revision.PriorRevision
      -> LookupLive(prior OVD, ObjectId)
```

lineage root 与失败语义：

- genesis `PriorRevision=None`：只有 ordinal 1 Base 可以成为 root；
- non-genesis prior OVD 返回 `AbsentAtBase`：只有本 Revision 新 Insert 的 ordinal 1 Base 可以成为 root；
- prior lookup `Found`：SameStateRebase 保持 ordinal，domain Base 必须严格 `+1`；
- `Removed`、non-genesis required prior 缺失、非法 ordinal 或 malformed anchor：lineage query fail closed。

这些失败不应让一个已经完整验证的 Base current payload 为了读取 current state而追溯旧 lineage。

## 8. Base-or-Deltify baseline policy

文件 rollover 与 object representation 是两个独立控制维度。首个可复用 baseline 从 TwoLeg
`ReadAmplificationBaseBudgetPolicy` 只保留对象级读写权衡。

### 8.1 输入量

```text
B(i) = post-Save Base payload bytes
H(i) = source head current-reconstruction payload bytes
D(i) = Update Delta payload bytes
G    = Sum(B(i), i in post-live graph)
Q    = Floor(G * BaseBudgetFraction)
```

这些是 synthetic payload proxies，不是 exact physical sizing 或 admission authority。

### 8.2 决策规则

- Insert：Base；
- Remove：无 domain record；
- Update 若 `B <= D`：Base weakly dominates，不受 discretionary budget 限制；
- 其他 Update 在 `(H + D) / B > ReadAmplificationLimit` 时有 Base motive；等号仍为 Delta；
- NoChange 默认 Inherit；在 `H / B > ReadAmplificationLimit` 时可有 `SameStateRebase` motive；
- motivated Update 与 NoChange 按 amplification 降序、ObjectId 升序竞争 Q；
- 选择完整 longest prefix，遇到首个不适配即停止；不做 0/1 knapsack；
- 第一个不可分割 motivated object 可以独自穿透 soft Q；
- Q 只约束 read-motivated Base；Insert 与 `B <= D` dominant Base 不消费 Q。

零尺寸按 TwoLeg 已验证语义处理：`H=0,B=0` 的 amplification 视为 1；`H>0,B=0` 视为正无穷并参与
统一排序。Update 的 `B=0` 已先被 `B <= D` dominance 规则选择为 Base，不进入除法。

`SameStateRebase` 是一次用写入换未来冷读的 maintenance，不是 migration：

- logical version ordinal 不前进；
- 不参考 FileNumber、distance、文件年龄或 rollover；
- 不保证清理旧文件，也没有 NoChange 类别优先级；
- 未选中时精确继承 prior head。

### 8.3 明确删除的 TwoLeg 语义

- `IsADependent`、A/E debt totals；
- `SelectTarget`、Stay-B/Rotate-C 双分支；
- mandatory evacuation 与 progress floor；
- rollover 强制 Base/NoChange rewrite；
- policy 对 append file、OVD mode、capacity、retry 或 fallback 的所有权。

OVD Base/Deltify 是独立决策轴。第一阶段由测试或 caller 显式指定 mode；出现真实 read/write evidence 后再设计
OVD policy，不能把 object policy 偷用为第二种 authority。

## 9. Workload 与评价协议

### 9.1 Workload

优先借鉴 TwoLeg 中已验证的：

- `WorkloadChange`、`SaveStep`、`WorkloadTrace`；
- Create/Update/Remove consistency；
- 固定 seed 的 Field/List object behavior；
- aligned-channel composition；
- deterministic replay 与 ObjectId lifecycle。

复用采用复制/改写代码片段和测试意图，不建立 MultiSegment -> TwoLeg project reference。第二个消费者已经证明
某些边界稳定后，再评估是否抽公共 experiment support assembly。

### 9.2 Canonical raw metrics

只对完整 admitted workload 产生指标；hard rejection 没有虚构罚分。

```text
W = WorkloadPhysicalWriteBytes
P = PeakWorkloadCommitWriteBytes
F = MaxSegmentTailBytes
R = TotalWorkloadColdReadBytes
L = TotalWorkloadLogicalBasePayloadBytes
```

- `W`：所有成功 workload Saves 的实际物理 append bytes；
- `P`：单次外层 workload Save 的最大物理 append burst；
- `F`：初始状态及每个 accepted checkpoint 上，任意 Segment tail 的最大值；
- `R`：每次成功 workload Save 后，从空 Frame cache 加载当前 PublishedHead 所需 unique full-Frame bytes 之和；
- `L`：与 R 同一采样点上的 live graph all-Base payload bytes 之和；
- `R/L` 是 aggregate physical read amplification，报告 exact integer pair而不是预先舍入；
- shared Frame 在同一次 cold load 中只计一次，下一次 Save 后 cache 重新为空；
- bootstrap 可单列，MultiSegment 没有 TwoLeg terminal settlement。

W/P 必须计入本次 Save 导致的新 Segment header、Frame header/trailer、TailMeta、padding 与 fence 等全部物理
增长，以保持 file-tail conservation。`L == 0` 时 `R/L` 未定义；R 仍可因 OVD Frame 为正。

继续报告 workload-only Delta/Base payload references，帮助解释 W；它们不是物理上下界。可增加
`SegmentCount`、required file/frame count、maximum distance 等 raw diagnostics，但不合成 scalar score。

## 10. TwoLeg 复用边界

| 处理 | 可复用资产 | 约束 |
|---|---|---|
| 直接复制/改名 | workload core、generator、composer、stable random、policy parameters | 更新 identity，不建立程序集依赖 |
| 改造复用 | `FileScope`、Frame/FileStore、OVD reader、reconstruction oracle、normalizer、estimator、W/P/R/L、read-amplification policy | 移除 adjacent-file 和 rotation 假设；relative fields 改为 backward distance |
| 明确拒绝 | 1-bit codec、`PreviousFileNumber`、Rotation 目录、paired candidates、A-debt、evacuation、completion certificate、terminal settlement | 不得进入 MultiSegment 正常 Save 路径 |

移植前先在 TwoLeg 中搜索同领域机制，理解测试证明的语义，再复制最小代码片段或设计思想。不得为了“复用”
保留已经失去消费者的类型层次、public API 或 provisional wire。

## 11. 最小端到端执行循环

阶段 A 不建立 filesystem session lifecycle。一个隔离 simulation run 只维护 in-memory Store、exact
PublishedHead、workload cursor 与 evaluator accumulator。单次 Save 内部阶段：

```text
Normalize
Plan
Select append origin from existing Segment tail
Render selected origin once
Admit
Append
Publish
Install derived cache
```

这些阶段不必各自形成公开对象或 durable state。真正必须保留的边界是：logical decision 与 final-origin
render 分开、append 与 publish 分开、authority 与 derived cache 分开。

## 12. 实现 roadmap 与 executable gates

### G0：地址切片（当前已具备）

- 1-based checked FileNumber；
- canonical probe filename；
- distance 0/1/>65,535/max round-trip；
- canonical VarUInt 与 underflow/future/zero-ticket fail-close。

### G1：Frame store 与 tail-triggered soft rollover

- 强类型 FrameTicket/布局，能验证 same-file earlier；
- in-memory append-only Segment/FileStore；
- crossing append 留在当前 Segment，下一次 Save 在 render 前轮转；
- empty oversize、threshold equality、hard-bound rejection；
- final Segment 确定后只 render 一次；显式 multi-origin witness 独立证明 relative re-encode；
- rejection 不发布 head。

### G2：OVD 与 ObjectVersion reconstruction

- OVD Base/Delta、BindSelf/External/Remove；
- ObjectVersion Base/Delta；
- F1 冷 Base 保留，热对象和 Revisions 推进到 F2/F3/F4；
- F4 current state 可完整 materialize；
- current-required missing、future、same-file non-earlier、cycle 与错误 ObjectId fail closed。

### G3：Planning、apply 与 lineage

- exact parent normalization；
- origin-free RevisionPlan 与唯一 whole-candidate estimator；
- OVD Base 在新文件重编码 F1 historical heads，不 relocation 对象；
- every non-genesis Revision 冻结 shared PriorRevision；
- 最小 Base lineage witness 经 shared PriorRevision 找到 exact prior ObjectVersion；完整 lineage diagnostics 可后置；
- append-before-publish failure 只留下不可见 candidate。

### G4：Workload、policy 与 evaluator

- 移植 deterministic workload/generator/composer；
- 简化 ReadAmplification + BaseBudget policy；
- 用独立 A/B-free trace 验证 SameStateRebase 保持 logical ordinal、增加一次 W 并降低后续累计 R；
- W/P/F/R/L、Delta/Base references 与 shared-Frame cold-read de-duplication；
- 用统一 workload 比较 all-Delta、all-Base 和 adaptive raw outcomes，不宣称 winner。

### 阶段 A 完成边界

G0-G4 全部闭合即停止本 Goal/Probe 阶段：

- 所有 executable gates 有聚焦测试；
- MultiSegment subsolution tests、format verification 与 root solution build 通过；
- 至少一个 deterministic corpus 可让 all-Delta、all-Base 与 adaptive baseline 产生 admitted raw report；
- F1-F4、tail-triggered rollover、multi-origin re-encode、OVD Base external head、SameStateRebase 与主要
  fail-close 反例都有证据；
- `PROJECT-STATE.md` 压缩为“G0-G4 complete / awaiting promotion decision”，不提前开始真实 filesystem/RBF；
- 工作树只包含本阶段有意改动，并形成可恢复 Git commit。

之后若用户明确启动产品化，转入 [`STATESTORE-SUBSYSTEM-DESIGN.md`](STATESTORE-SUBSYSTEM-DESIGN.md)；
不把整个 probe、benchmark runner 或 provisional wire 直接搬入 `src/DurableGraph`。

## 13. 目标保证、当前证据与开放机制

| 项目 | 状态 |
|---|---|
| Multi-history address semantics、1-based FileNumber、canonical VarUInt | 已选择；地址切片已有 executable evidence |
| OVD authority、Base/Delta、absolute-normalize/relative-reencode | 已选择；G2 已用 MultiSegment F1-F4 重验 current reconstruction |
| tail-triggered rollover 与 Base/Deltify 解耦、final-origin single render | 已选择；G1 已有 executable evidence |
| shared PriorRevision 与 Base lineage | 已选择；G3 已有 MultiSegment point-lookup lineage evidence |
| exact PublishedHead 与 append-before-publish | 已选择；G3 已验证 logical in-memory order 与 orphan invisibility |
| deterministic workload、policy 与 W/P/F/R/L evaluator | G4 已验证，含 SameStateRebase 与三策略 raw report |
| exact filename literal grammar、正式 SizedPtr/RBF record framing | 阶段 B，probe 不冻结 |
| OVD Base/Deltify policy | 开放；首轮显式指定 |
| GC/compaction/backup packing | 暂缓；v1 保留 published history |

## 14. 未闭合事项与重访触发条件

阶段 A 必须通过实验裁决：

- probe-local strong FrameTicket、same-file earlier validation 与 provisional estimator 的最小边界；
- `RolloverThresholdBytes` range、RBF hard bound 和 FileNumber overflow outcomes；
- 证明 OVD mode 是独立决策轴，并以显式 caller/test choice 覆盖 Base 与 Delta；自动选择 policy 暂缓；
- SameStateRebase 的可测 W/R tradeoff 与是否保留在 adaptive baseline。

阶段 A 明确不裁决：

- actual `SizedPtr`、正式 RBF grammar、real file inventory/reopen；
- exact head carrier、Empty marker、ambiguous outcome、poisoned session 与 directory durability；
- EventJournal/RbfSegmentStore acquisition、NuGet/ProjectReference、产品程序集/API；
- crash 后 orphan/new FileNumber allocation 与真实 append destination。

这些问题及晋升条件统一记录于
[`STATESTORE-SUBSYSTEM-DESIGN.md`](STATESTORE-SUBSYSTEM-DESIGN.md)。

首轮编码的正确终点不是“建成一个数据库”，而是用 executable evidence 证明：

> 同一逻辑 Save plan 可以在不改变 Base/Deltify 的情况下先选择最终 Segment、再编码一次并发布；latest OVD 与所有
> live ObjectVersion 可以跨任意历史文件完整恢复；冷对象不因文件切换被强制复制；所有 authority、
> current-required missing dependency 与 in-memory publication 边界都能被明确解释并 fail closed。
