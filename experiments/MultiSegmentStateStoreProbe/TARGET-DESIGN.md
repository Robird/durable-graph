# MultiSegment StateStore 目标设计

> 状态：Selected Target Design / Probe Implementation Guide
>
> 最近校准：2026-09-02
>
> 适用范围：`experiments/MultiSegmentStateStoreProbe`

本文定义 MultiSegment StateStore 探针要逐步实现和验证的完整目标。它是后续编码的主设计入口，
但不是当前实现事实，也不冻结 DurableGraph 的公开 API、正式 wire bytes 或产品 durability 协议。

文档职责分工如下：

- 本文回答“目标系统如何工作、哪些不变量必须由代码证明”；
- [`DB-014`](../../docs/design-branches/0014-multi-segment-backward-file-distance.md) 记录为什么从
  TwoLeg 转向多历史 Segment；
- [`PROJECT-STATE.md`](PROJECT-STATE.md) 只维护当前已完成事实、下一切片和未闭合事项；
- [`README.md`](README.md) 只介绍当前可运行能力；
- [`TwoLegRotationProbe`](../TwoLegRotationProbe/PROJECT-STATE.md) 是可借鉴的冻结技术储备，不是本项目依赖。

若本文与当前代码或测试不一致，差异表示“尚未实现”，不能把目标描述成现状。若实验否定本文，应先
记录证据并修订设计，而不是扭曲实现来维护文档正确。

## 1. 问题与一句话模型

StateStore 物理上由一串单调编号、append-only 的 RBF 文件组成。最新 Published Revision 可以引用同一
Store 目录内任意更早文件中的 Revision 或 ObjectVersion；文件达到 soft target 时只切换 append
destination，不搬迁冷对象，也不改变 Base/Deltify 决策。

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
- filesystem/reopen 阶段所需的逻辑 publication 顺序与 durability exit gates。

### 2.2 运行假设

- 单 Store 单 writer；宿主负责串行化 Save，不在探针内发明 multi-writer 协议；
- 一次 Save 从唯一 exact parent PublishedHead 派生；不支持 stale-snapshot Save、branch merge 或 import；
- workload 中对象彼此独立，ObjectId 稳定且不复用；对象引用图、reachability 与 GC 属于 DurableGraph
  外层，不在本探针重复建模；
- 首轮以 in-memory、size-only 模型闭合语义，再接本地 `Atelia.Rbf`；
- 所有已发布历史文件可以永久保留，v1 不承诺总空间、文件数或冷读 fan-out 有界。

### 2.3 明确非目标

- TwoLeg 的 A/B/C、Stay/Rotate、A-debt、evacuation、NoChange migration 与 two-file closure；
- 自动删除、refcount、incremental cleaner、在线 compaction 或冷热分层；
- Extent 或一个 Revision 跨多个 Frames；
- 多 writer、lease、分布式 CAS 或长寿命并发 reader 协议；
- Product `CommitManifest` 的最终布局、公开 API、格式兼容和旧 probe bytes 迁移；
- Schema codec、CLR graph traversal、ArtifactStore 或 Four-Stores-One-Commit 的完整实现；
- 标量总分、排行榜、产品默认阈值或 cold-read SLO。

## 3. 术语与唯一身份

### 3.1 Store directory 与 Segment

v1 中一个 Segment 就是一个 canonical-named RBF file，不增加 `SegmentId`、`FileId` 或路径 catalog：

```text
filePath = storeDirectory / FormatCanonicalFileName(fileNumber)
```

`FileNumber` 是 Store directory 内 1-based `UInt32`：

- `0` 非法；
- 新文件号单调增加，任何已发布文件号不得复用；
- `UInt32.MaxValue` 后 fail closed，不 wrap；
- path formatting/parsing 必须是一一对应的 canonical function；
- 当前十位十进制 `.rbf` 名称只是 probe grammar，尚未冻结为产品文件名。

目录边界就是 Store scope。把一个数据文件手工混入另一个目录不是受支持操作；当前不为此增加
`StoreId`/`FileId` 文件头。

### 3.2 Frame 与 Revision

首版一个 Revision 对应一个 RBF Frame。Frame 包含：

```text
RevisionFrame
    PriorRevision
    ObjectVersion records [0..N]
    ObjectVersionDictionary version
    TailMeta directory / offsets
```

RBF Frame 是 append、完整性校验和物理读取粒度。`IRbfFile.ReadTailMeta` 的 L2 preview 只能提供路由提示；
OVD 或 ObjectVersion 成为 authority 前，必须完成整 Frame 的 L3 验证。

### 3.3 PublishedHead 与 append destination

`PublishedHead` 是 `None` 或一个 exact Revision `AbsoluteFrameAddress`。空 Store 可以使用 `None`；首次成功
Save 写 OVD Base。测试 fixture 可以创建 bootstrap Revision，但它不是目标格式强制要求。

PublishedHead 所在文件不必是物理 append-current file。append destination 由当前 Store inventory 与
rollover 规则决定；二者不能合并成一个 cursor authority。

未来产品中，外层唯一 `CommitManifest` 可以精确包含 StateStore PublishedHead。StateStore 不应再发布
第二个相互竞争的产品 authority。

## 4. 地址模型

### 4.1 Runtime authority

进程内所有已解析引用统一使用绝对地址：

```text
AbsoluteFrameAddress
    FileNumber
    FrameTicket          // 最终为 Atelia.Data.SizedPtr 或等价强类型
```

`FrameTicket` 必须能提供或可靠解码 Frame start/length，以便验证 same-file strictly-earlier、读取 Frame 和
计算物理指标。当前 `ulong FrameTicketCode` 只足以验证编码边界，不是最终 runtime model。

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

当前代码中的 `BackwardFrameReference` 是这一概念的第一切片。实现演进时应合并/重命名，而不是并存两套
relative authority。

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

### 6.3 Placement、重编码与定尺

正确顺序是：

```text
plan = FreezeLogicalDecisions(exact parent facts)

currentCandidate = RenderAndMeasure(plan, appendCurrentFile)
if appendCurrentFile is nonempty
    and currentCandidate.TailAfter > TargetFileBytes:
    targetFile = appendCurrentFile.Next()
    finalCandidate = RenderAndMeasure(plan, targetFile)
else:
    finalCandidate = currentCandidate

ValidateHardBounds(finalCandidate)
Append(finalCandidate)
```

若 Store 尚无数据文件，首次 placement 创建 F1。若最大号文件存在但尚无完整 Frame，则它是可复用的 empty
append destination；这里的 `nonempty` 指“至少含一个完整 Frame”，不是“RBF header 占用了字节”。

“same candidate”只表示同一个 logical plan、Base/Delta decisions 与 OVD membership；它不表示相同的 bytes、
relative tickets 或 layout estimate。文件号变化会改变 `BackwardFileDistance` 及其 VarUInt 宽度，因此
rollover 后必须重新 relativize、编码和定尺，绝不能重跑 policy。

### 6.4 Soft target 与 oversize

`TargetFileBytes` 是 placement trigger，不是 hard capacity：

- current file 非空且 append 后越过 target：最多切换一次到 next file；
- fresh/empty file 上的单个合法 Revision 即使超过 target 仍然 append；
- 下一次 Save 看到 nonempty oversize file 后自然切换到新文件；
- 不引入 `OversizeSegment` 类型或循环创建空文件；
- 若最终 candidate 超过 RBF single-Frame hard bounds，typed reject，不拆分、不 fallback、不改策略。

file switch 绝不能强制 Base、SameStateRebase、OVD Base 或 cold object relocation。

### 6.5 Admission、append 与 publication

最终 candidate 在 append 前必须完整通过：

- source head 仍等于 ExpectedParent；
- 所有 membership、address、canonical encoding 与 strictly-earlier rules；
- RBF payload、TailMeta、Frame start、`SizedPtr` 与算术 hard bounds；
- candidate 可从 exact dependencies 完整重建；
- append destination 与预计算 layout 一致。

目标 publication 顺序：

```text
append complete candidate Frame
durably flush all newly required data according to selected durability model
publish exact new head
durably publish the head
acknowledge Save
```

in-memory probe 只能模拟逻辑顺序，不能声称已经实现 crash durability。真实 head carrier、atomic replace、
文件和目录 flush matrix 仍是 filesystem gate。

失败语义：

- append 前 rejection：Store 与 PublishedHead 都不变；
- append 成功、publish 前失败：旧 PublishedHead 仍是 authority，新 Frame 是不可见 candidate/orphan；
- append outcome 本身未知或 publication outcome 不明确：session 进入 poisoned 状态，只能 reopen/reconcile，
  不能透明 retry；
- selected candidate hard reject 不自动换另一种 Base/Delta 或 OVD mode。

只有 publish 成功后，才能从新 PublishedHead 重新 materialize并安装 volatile cache。若 durable publish 已完成
而 cache 安装失败，commit 仍已生效；应丢弃 cache并重新 materialize，仍失败则保持新 head authority、将
session 标记为 poisoned，并返回“已提交但当前 session 不可继续”的 typed outcome。

## 7. Load、reopen 与 recovery closure

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

### 7.2 Reopen

filesystem reopen 至少区分：

1. 扫描 canonical filenames，验证重复/非法 FileNumber，并建立 volatile inventory；
2. 从唯一 authority carrier 获得 exact PublishedHead；carrier 必须显式区分 Empty/None 与缺失、损坏，
   不能把 HEAD metadata 丢失解释为空 Store；
3. 按上述 Load 流程验证它；
4. 独立派生 append destination，不能把最大文件号或最后 valid Frame当成 head；
5. 未被 exact head 当前 closure 访问的 Frame 只能称为 `UnreachableFromCurrentHead`。

仅凭当前 head 无法区分“从未发布 orphan”与“过去曾发布、现在不在 latest closure 的历史 Frame”。v1 不需要
这个区别，因为两者都不自动删除。

### 7.3 Derived recovery inspection

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

## 11. 最小端到端状态机

Store session 只需要：

```text
Closed
Healthy
Poisoned
```

单次 Save 内部阶段：

```text
Normalize
Plan
Render current origin
[Render next origin after one soft rollover]
Admit
Append
Publish
Install derived cache
```

这些阶段不必各自形成公开对象或 durable state。真正必须保留的边界是：logical decision 与
origin-dependent render 分开、append 与 publish 分开、authority 与 derived cache 分开。

## 12. 实现 roadmap 与 executable gates

### G0：地址切片（当前已具备）

- 1-based checked FileNumber；
- canonical probe filename；
- distance 0/1/>65,535/max round-trip；
- canonical VarUInt 与 underflow/future/zero-ticket fail-close。

### G1：Frame store 与 soft rollover

- 强类型 FrameTicket/布局，能验证 same-file earlier；
- in-memory append-only Segment/FileStore；
- nonempty crossing、empty oversize、hard-bound rejection；
- rollover 前后 logical plan 完全相同，但 encoded bytes/layout 可不同；
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

### G5：Filesystem/RBF reopen

- 替换 opaque ticket 为真实 `SizedPtr`/RBF layout；
- canonical inventory 与 exact-head reopen；
- L2 TailMeta 只路由、L3 Frame 才安装 authority；
- missing current-required historical file、orphan Frame 与 malformed head fail closed；
- 选择并用 fault injection 证明 head carrier、flush 与 poisoned-session contract。

### G6：产品整合

只有 G0-G5 的最小语义与恢复证据闭合后，才把必要地址/reader/planner contract 移入 `src/DurableGraph`。
不把整个 probe、benchmark runner 或 provisional wire 一并产品化。

## 13. 目标保证、当前证据与开放机制

| 项目 | 状态 |
|---|---|
| Multi-history address semantics、1-based FileNumber、canonical VarUInt | 已选择；地址切片已有 executable evidence |
| OVD authority、Base/Delta、absolute-normalize/relative-reencode | 已选择；主要证据目前来自 TwoLeg，待 MultiSegment F1-F4 重验 |
| rollover 与 Base/Deltify 解耦、origin-dependent re-render | 已选择；待 G1 executable evidence |
| shared PriorRevision 与 Base lineage | 已选择；待 G3 MultiSegment evidence |
| exact PublishedHead 与 durable-before-publish | 目标保证；in-memory 只能验证逻辑顺序 |
| head carrier、atomic publication、file/directory durability | 开放，必须在 G5 裁决 |
| exact filename literal grammar、正式 SizedPtr record framing | probe provisional，产品未冻结 |
| OVD Base/Deltify policy | 开放；首轮显式指定 |
| GC/compaction/backup packing | 暂缓；v1 保留 published history |

## 14. 未闭合事项与重访触发条件

近期必须通过实验裁决：

- actual `SizedPtr` encoding 与 `BackwardFileDistance` 的 record framing；
- same-file earlier validation 与 provisional RBF estimator 的最小共享边界；
- `TargetFileBytes` API、RBF hard bound 和 FileNumber overflow outcomes；
- OVD Base/Delta 的独立策略；
- exact head 的 filesystem carrier、Empty marker、ambiguous outcome detection 与 directory durability；
- crash 后 orphan/new FileNumber allocation 与 reopen append destination。

只有出现下列证据才扩大设计：

- 单 Revision 真实超过 RBF hard bound，才引入 Extent；
- 完整 `CompactToNewStore` 的停顿或写入峰值不满足真实 SLO，才研究 incremental cleaner/TwoLeg；
- latest recovery 的历史 file fan-out 不满足真实 SLO，才研究分层、placement 或 dependency consolidation；
- 多 writer 成为真实 consumer，才引入 lease/CAS/fencing；
- 两个独立 active probes 出现重复维护成本，才抽公共 experiment infrastructure；
- 正式格式发布或已有数据必须保留，才增加 compatibility/migration machinery。

首轮编码的正确终点不是“建成一个数据库”，而是用 executable evidence 证明：

> 同一逻辑 Save plan 可以在不改变 Base/Deltify 的情况下跨 Segment 重新编码并发布；latest OVD 与所有
> live ObjectVersion 可以跨任意历史文件完整恢复；冷对象不因文件切换被强制复制；所有 authority、
> missing dependency 与 durability 边界都能被明确解释并 fail closed。
