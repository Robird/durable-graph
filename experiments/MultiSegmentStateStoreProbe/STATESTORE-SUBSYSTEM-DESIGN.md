# 阶段 B：正式 StateStore Sub-System 晋升设计

> 状态：Stage B Started / Membership Head Map and Shared Serialization Leaf
>
> 最近校准：2026-09-04
>
> 启动条件：阶段 A G0-G4 全部闭合，并由用户明确启动产品化

本文记录如何把 [`TARGET-DESIGN.md`](TARGET-DESIGN.md) 已验证的 MultiSegment 语义晋升为
`src/DurableGraph.StateStore` 内的正式 StateStore 子系统。阶段 B 已由用户明确启动，并选择独立
`Atelia.DurableGraph.StateStore`、`Atelia.DurableGraph.StateStore.Storage` 与 BCL-only
`Atelia.DurableGraph.StateStore.Serialization` 程序集。StateStore 单向引用 Storage；Storage 引用 Serialization，
并持有跨仓库 substrate ProjectReference：`RbfSegmentStore` 以及地址模型直接使用的 `Data/SizedPtr`。
当前仍未选择 EventJournal、NuGet acquisition、head carrier 或正式 wire。Segment lifecycle/rollover 优先采用
当前 `RbfSegmentStore` 语义，但仍须用真实 I/O spike 闭合其 guardrails 与 recovery 缺口。

阶段 B 的基本原则是“从 Probe 抽取已证明的语义，重新实现正式子系统”，而不是把整个 experiment project、
benchmark runner、size-only payload 或 provisional codec 搬进产品。

## 1. 启动门槛

只有以下事实全部成立，才开始阶段 B：

- G0-G4 的 focused tests、MultiSegment subsolution、format verification 与 root build 全部通过；
- F1-F4 current reconstruction、OVD Base/Delta、ObjectVersion Base/Delta、shared prior 与 fail-close 已有
  MultiSegment 自身证据；
- rollover 已证明只改变 placement/encoding，不改变 frozen logical decisions；
- deterministic workload、all-Base/all-Delta/adaptive baseline 与 W/P/F/R/L evaluator 可稳定运行；
- 阶段 A 的 provisional 类型、格式与 product candidates 已被明确区分；
- `PROJECT-STATE.md` 已关闭 Probe roadmap，而不是把 filesystem 工作偷偷追加成 G5。

若以上任一项缺失，继续阶段 A；不要用真实 I/O 增加反馈周期或掩盖内存语义缺口。

## 2. 子系统定位

正式 StateStore 是 DurableGraph 内部的增量状态存储引擎：上层负责理解领域对象、Schema 与 payload，
StateStore 负责版本记录、地址、representation policy、Segment placement 与物理持久化。

```text
DurableGraph Graph / Schema / Codec
    object identity and reachability
    Schema version / upgrade
    Base serialization
    Delta generation / application
    CLR graph materialization
                   |
                   v
PreparedStateCommit / state read contract
                   |
                   v
StateStore Sub-System
    OVD authority
    ObjectVersion Base/Delta records
    Base-or-Deltify policy
    Segment placement / rollover
    RBF append / read / recovery
    candidate StateHead
    physical diagnostics
                   |
                   v
Outer DurableGraph CommitManifest publication
```

这是逻辑子系统边界。首批产品程序集已经建立在：

```text
src/DurableGraph.StateStore/
src/DurableGraph.StateStore.Storage/
src/DurableGraph.StateStore.Serialization/
```

由仓库公共属性得到对应程序集名和根命名空间 `Atelia.DurableGraph.StateStore`、
`Atelia.DurableGraph.StateStore.Storage` 与 `Atelia.DurableGraph.StateStore.Serialization`。当前项目引用方向是：

```text
DurableGraph.StateStore -> DurableGraph.StateStore.Storage -> RbfSegmentStore
                                                        \-> Data (SizedPtr)
                                                        \-> DurableGraph.StateStore.Serialization -> BCL only
```

StateStore 不直接引用 RBF substrate，Storage 不反向引用 StateStore；StateStore 当前也不直接引用
Serialization，等第一个真实 ObjectVersion consumer 出现再加入这条依赖。独立 leaf assembly 让 Storage wire
可复用无上下文 binary primitives，同时保持 RBF 文件逻辑与上层 VersionedSchema/对象 codec 的单向边界。
当前 membership-only 纵切不预先裁决完整 public API 或正式 wire compatibility。

### 2.1 Storage 地址模型切片

`DurableGraph.StateStore.Storage` 已建立最小产品地址模型：

- runtime `FrameAddress` 始终保存 1-based `UInt32 FileNumber` 与真实 `SizedPtr FrameTicket`；
- `FileScope` 以 containing/current FileNumber 在 absolute FileNumber 与 `BackwardFileDistance` 之间换算；
- distance `0` 表示当前文件，future absolute file、file zero 与 distance underflow fail closed；
- 产品内存模型没有 `RelativeFrameTicket`；相对距离只允许作为后续 wire codec 的瞬时短编码值。

### 2.2 Live Object membership Storage 切片

`StateRevision` 当前只建模 live Object membership metadata，不包含 Object payload：

```text
StateRevision
    ParentRevisionAddress?
    BaseObjectIds
    DeltaObjectIds
    ObjectHeadMap Base  { ExternalObjectHeads }
                  Delta { RemovedObjectIds }
```

- `ObjectId` 暂用 provisional 1-based `UInt32`，不裁决正式 `DurableId` identity/encoding；
- 本 Revision 的 Base/Delta ObjectIds 都隐式以 containing Revision Frame 作为 current head，不重复持久
  `BindSelf`；Base/Delta object representation 与 ObjectHeadMap Base/Delta 是正交轴；
- ObjectHeadMap Delta 从 exact parent 继承，应用 Removes，再由本地 ObjectIds 覆盖/增加；未出现的 ID 隐式
  inherit，不持久 `InheritSet`；Remove 的完整性由上层调用方负责，Storage 不从对象图猜测遗漏；
- ObjectHeadMap Base 的 `local ObjectIds + ExternalObjectHeads` 是完整 live map，current membership 在此早停；
  non-genesis Base 的 Parent 仍可供后续 lineage 使用，但 live-head materializer 不读取它；
- genesis 没有可供 Delta ObjectVersion 引用的 prior version，因此只允许 local Base ObjectIds；所有 required
  FrameAddress 必须含非空 ticket，并且 Parent/External head 必须严格早于 containing Revision Frame；
- immutable model 会 canonical sort，并拒绝 zero、duplicate、local Base/Delta overlap、local/external overlap 与
  local/remove overlap。

当前 provisional membership wire 有固定 RBF tag/version，ObjectIds 使用 canonical VarUInt32；FrameAddress 只在
wire 中编码为 `VarUInt32 BackwardFileDistance + VarUInt64 SizedPtr.Serialize()`，decode 后立即 absolute-normalize。
`StateRevisionWireReader/Writer` 与 `FrameAddressWireCodec` 通过 leaf assembly 的 `BinaryPayloadReader/Writer`
统一这些 primitive；Storage 自有 `CanonicalVarUInt` 已删除。迁移前后的既有 v1 golden bytes 相同，这是本轮
回归证据，不是正式 compatibility 承诺。composite FrameAddress decode 失败不推进外层 reader cursor。
reader 拒绝 unknown version/kind、bad presence marker、overlong/overflow/truncated VarUInt、非升序/重复 ID、
count 越界、future-file/underflow、集合语义冲突与 trailing bytes。wire codec 不接收 containing Frame offset，
也不判断 same-file chronology；这是依赖 containing/target 两个完整 absolute addresses 的 graph invariant，
由 `LiveObjectHeadMapMaterializer` 在消费 Parent/External head 时验证。parent chain 每步严格下降已经排除 cycle，
因此 materializer 不维护 visited set。当前 wire writer/reader 对每个集合采用 provisional 1,000,000 项硬上限，
先拒绝再分配；该值不是正式格式承诺。

`StateRevisionStore` 每次 Append 先取得 `RbfSegmentStore.OpenActiveWriter()` 的 final Segment，再通过
`IRbfFile.BeginAppend()` 将 wire 直接编码到 `RbfFrameBuilder.PayloadAndMeta`，最后 `EndAppend()` exactly one
Frame；Read 从 exact `FrameAddress` 取得完整 RBF Frame、校验 tag/TailMeta 后 decode。
`Read` 只验证单 Frame 完整性、canonical wire 与 record-local shape；
`ReadLiveObjectHeads(exactRevisionHead)` 是唯一 membership replay authority，并验证跨 Frame graph chronology。
它返回 immutable、ObjectId 升序的 shallow `{ObjectId -> absolute FrameAddress}` map：local ID 指向 containing
Revision Frame，Base external ID 保留记录的旧地址；该 API 不读取或验证目标是否为 ObjectVersion record。
chronology 不认证 ticket 的文件 provenance，跨文件误配仍须由未来真实需求决定是否引入 store identity 或更强的
resolve-time identity validation。
真实文件测试已覆盖 append/read、dispose/reopen 冷读、多 Revision inherit/update/add/remove/no-change/
reappearance，以及 tail-triggered 跨 Segment Parent/External checkpoint 的 exact head values；编码或提交前校验
失败会由 builder Dispose 执行 Auto-Abort，不会提交 partial Frame。若 writer acquisition 先触发轮转，失败可
留下 header-only active Segment；测试证明它会被后续 append 复用。

当前仍未实现 Object payload、自动 checkpoint policy、skip、durable publication 或正式 wire compatibility。

### 2.3 StateStore payload serialization primitive 切片

独立、BCL-only 的 `Atelia.DurableGraph.StateStore.Serialization` assembly 承载五个 internal primitives：
`BinaryPayloadReader`、`BinaryPayloadWriter`、`CanonicalVarInt`、`StringPayloadCodec` 与
`NullablePayloadHeader`。它们作为 Storage wire 及后续 Base/Delta Object payload codec 的无上下文底层工具，
覆盖 canonical unsigned Base128、signed ZigZag、little-endian fixed-width floating point、Boolean、count/bytes
与 adaptive string；不带入 StateJournal 的 Symbol、Revision、pool、Tagged 或 `asKey` 语义。

string wire 沿用“UTF-8 严格更短才选择，否则 UTF-16LE”的 header 布局，并扩充为 strict UTF-8、tie 固定
UTF-16LE、unpaired surrogate 经 raw UTF-16 code units 无损往返，以及 reader 对非最短 alternate encoding 的
fail-close。assembly 只通过 `InternalsVisibleTo` 向 Storage、Serialization.Tests 与 Storage.Tests 开放这些
类型；专属 Serialization.Tests 的 65 个 cases 约束 primitive golden、canonical/fail-close 与 cursor rollback。
StateStore 尚未接入 ObjectVersion，也没有对 Serialization 的直接项目引用；当前格式不是正式 compatibility
承诺。

## 3. 上下层职责

### 3.1 DurableGraph 上层

- 从 root 遍历 CLR graph，维护稳定 ObjectId；
- 计算 reachability，并把不再可达对象归类为 Remove；
- 按 exact Schema 生成 canonical Base payload；
- 为 Update 生成 Delta payload，并在 Load 时应用 Delta；
- 管理 Schema Version、historical Snapshot 与 upgrade handler；
- 验证领域 invariant、恢复 sharing/cycle，并执行 transient rebuild；
- 协调 SchemaStore、StateStore、ArtifactStore 与外层唯一 CommitManifest。

### 3.2 StateStore 子系统

- 维护 Revision、OVD 与 ObjectVersion 的 append-only record model；
- 从 exact parent StateHead 归一化本次 membership/change facts；
- 在上层提供的 opaque Base/Delta candidates 中选择 representation；
- 冻结 origin-free Revision plan；每个 Save 新借 writer，让 `RbfSegmentStore` 先按 existing tail 选择最终
  Segment，再相对 lease origin 编码和 exact-size 一次；
- 通过 RBF hard gates 后 append candidate；
- 返回 exact candidate StateHead 与 dependency/durability information；
- 从 exact StateHead 读取 OVD 和 raw ObjectVersion chains；
- 提供 W/P/F/R/L、recovery closure 与 corruption diagnostics；
- 对 malformed/missing/current-required reference fail closed。

StateStore 不理解 CLR field、Schema upgrade、领域 equality 或对象 reachability，也不从目录扫描猜 logical head。

### 3.3 两种 GC 不得混淆

- logical graph GC：上层 reachability 产生 Removes；
- physical storage GC：回收已无任何 authority 依赖的历史 files/frames。

阶段 B 首版只要求前者。后者继续保留所有 published history；只有真实空间/SLO 证据出现后，才研究
`CompactToNewStore`、incremental cleaner、TwoLeg 或 tiered placement。

## 4. Prepared State Commit 边界

概念上，每次 Save 的 post-state 分成：

```text
Inserts
Updates
Removes
NoChanges
```

推荐的物理输入不重复传完整 NoChanges authority：

```text
PreparedStateCommit
    ExpectedParentStateHead
    Inserts: ObjectId + BasePayload
    Updates: ObjectId + BasePayloadCandidate + DeltaPayloadCandidate
    Removes: ObjectId
```

StateStore 从 exact parent OVD 与显式变化集合派生/验证 NoChanges。这样 caller 无法漏报一个旧对象并让
lower layer 产生第二套 membership interpretation。

Storage 层不遍历对象图，也不判断调用方是否漏报 Remove；它只把上层已经冻结的 local ObjectIds、Removes
与显式 ObjectHeadMap Base/Delta 忠实写入并重放。

payload 对 StateStore 是 opaque bytes，但至少携带策略所需的长度和 codec/schema fence。具体 fence 是
SchemaKey、SchemaHash、codec identity 还是外层 record metadata，必须在产品 vertical slice 中裁决。

### 4.1 SameStateRebase 边界

NoChange 被 policy 选为 `SameStateRebase` 时，需要完整 current Base payload。Probe 可以直接拿到 size/value
stand-in，产品中则必须选择一种真实机制：

1. 上层为全部 live objects 预先准备 Base payload；
2. StateStore 先选择 motive，再通过窄 callback/second pass 向上层请求被选对象的 Base payload；
3. 首个产品版本禁用 NoChange SameStateRebase，只允许 Update Base/Delta。

这是真实的分层选择，不能在阶段 A 用 size-only 便利性替产品暗中裁决。优先依据序列化成本、对象数量与
实际 R 收益选最小方案。

## 5. Read contract

为保持层间互不依赖，StateStore 首选返回 raw reconstruction information，而不是反向引用 Schema runtime：

```text
StateReadResult
    StateHead
    Live ObjectId -> ObjectVersion head
    per-object Base + ordered Delta payload chain
    exact schema/codec fences carried by records
```

上层用对应 historical codec 解码、升级并 materialize CLR graph。StateStore reader 仍负责：

- address、Frame、ObjectId、ordinal 与 chain ordering；
- OVD current membership；
- current-required missing/corruption/cycle；
- full-Frame integrity boundary；
- unique Frame caching 与 physical read accounting。

若真实性能证明把完整 chain 跨层传递代价过高，再考虑让上层提供 codec callback；不要在没有 profile 前让
StateStore 依赖 Schema/Generator assemblies。

## 6. Product authority 与 publication

Probe 中的 in-memory PublishedHead 是独立 simulation authority。产品中必须保持 Four Stores, One Commit：

1. StateStore append candidate data，并完成要求的 data durability barrier；
2. StateStore 返回 exact candidate StateHead，不自行成为第二 product authority；
3. 外层 CommitManifest 精确引用 candidate StateHead 以及 Schema/Artifact inputs；
4. 外层完成唯一 manifest/head publication；
5. 成功返回后才安装与该 exact manifest 对应的内存 baseline。

文件存在、最大 FileNumber、最后 valid RBF Frame 或 StateStore 独立 reflog 都不能替代外层 authority。

正式实现必须裁决并用 fault injection 证明：

- candidate data files 的 durable flush；
- head/manifest carrier 与 atomic publication；
- directory metadata durability；
- append 或 publication outcome ambiguous 时的 poisoned session；
- reopen/reconcile 如何区分 old published、new published 与 orphan candidate；
- orphan 后 FileNumber allocation 是否允许复用；
- exact PublishedHead Empty/None 与 metadata missing/corrupt 的区别。

## 7. 优先复用的 Atelia 资产

进入阶段 B 后，先重新审查当前 checkout，而不是依赖旧印象：

```text
E:\repos\Atelia-org\atelia\src\RbfSegmentStore
E:\repos\Atelia-org\atelia\src\EventJournal
E:\repos\Atelia-org\atelia\src\Rbf
```

当前角色裁决：

- `RbfSegmentStore`：多 Segment append、file inventory、tail-triggered rollover/reopen 的首选底座；
- `Rbf`：Frame append/read、`SizedPtr`、L2/L3 integrity 与 hard bounds 的基础事实；
- `EventJournal`：exact head、candidate/durable/publish、poison/reconcile 与 fault-injection 的参考，不直接成为
  StateStore 或外层 CommitManifest authority。

若 `RbfSegmentStore` 的 recovery/failure 行为不能满足 outer-authority-aware reopen，应先补窄 substrate seam，
而不是恢复 candidate-aware placement 或复制整套 Segment manager。若 EventJournal 的 event semantics、head
shape 或 record contract 会扭曲 OVD StateStore，应只摘取其 publication/recovery 思想；不要为复用而改写
领域模型。

### 7.1 已选择的 rollover 语义

产品直接采用 `RbfSegmentStore.OpenActiveWriter()` 的 existing-tail trigger：

```text
plan = FreezeLogicalDecisions(exact parent facts)
writer = OpenActiveWriter()
    // existing TailOffset >= RolloverThresholdBytes 时才 checked rotate
candidate = RenderOnce(plan, writer.SegmentNumber, writer.File.TailOffset)
AppendExactlyOneRevisionFrame(candidate)
```

- crossing Revision 留在当前 Segment；下一次 Save 才轮转；
- StateStore 每个 Save 单独借还 lease，每个 lease 只 append 一个 Revision Frame；
- 因此文件允许最多一个合法 RBF append envelope 的 overshoot；threshold 不是严格文件上限；
- threshold 必须 4-byte aligned、严格大于 header-only tail，并不超过 `SizedPtr` 最大 Frame start；
- next SegmentNumber 必须在 dispose/create/mutation 前 checked；
- writer acquisition 已创建新 Segment 后若 render/admission 失败，可以留下 header-only active Segment；它不获
  authority，后续 Save 直接复用，不为回滚该文件重新引入 prospective sizing/explicit rotate；
- 若未来 Extent/multi-frame Revision 或严格 `file <= N` consumer 出现，重访本裁决。

这删除了“先按 current origin render/measure、crossing 时显式轮转、再按 next origin render”的双 candidate
路径。origin-free plan 仍保留，但正常 Save 只在 lease 选定的最终 origin 编码一次；RBF append 继续拥有
single-Frame hard capacity 的最终 gate。

### 7.2 获取方式

活跃跨仓库共同开发阶段，`ProjectReference` 最接近 editable source workflow。接口稳定、需要验证真实消费和
依赖边界后，再发布单调递增版本的本机 NuGet dev packages。

本地包方案必须保证 `Data`、`Primitives`、`Rbf`、`RbfSegmentStore`、`EventJournal` 等传递依赖均可解析；
不要覆盖复用同一 dev version，否则 NuGet global cache 可能继续提供旧快照。CI/团队可复现性最终需要固定
package source/version、固定 commit checkout 或其他明确 acquisition contract，不能只依赖某台机器的 feed。

## 8. 晋升方法

### B0：Atelia substrate audit

- 读取当前 EventJournal/RbfSegmentStore/Rbf source、tests 与 package metadata；
- 建立能力/缺口矩阵：Segment naming、append、rollover、head、flush、reopen、fault model；
- 用最小 spike 验证 threshold range、checked FileNumber overflow、header-only/torn newest Segment 和 outer-head
  reopen；不增加 prospective-size/explicit-rotate adapter framework。

### B1：产品子系统契约

- 定义 internal `PreparedStateCommit`、candidate StateHead 与 read result；
- 冻结 dependency direction，确保 StateStore 不依赖 Schema/CLR materialization；
- 决定 SameStateRebase payload acquisition；
- 先在 `DurableGraph.StateStore` 与 `DurableGraph.StateStore.Storage` 的已选单向边界内形成 vertical slice。

### B2：真实 RBF/Segment persistence

- 在 `DurableGraph.StateStore.Storage` 中用真实 `SizedPtr` 和 RBF layout 替换 probe stand-ins；
- 实现 canonical Segment inventory、append、reopen 与 current-required dependency read；
- 保持 origin-free plan，并在 `OpenActiveWriter()` 选定的 final origin render/append 一次；
- 对照 Probe vectors 做 differential tests。

### B3：外层 commit integration

- StateStore 只返回 candidate StateHead；
- 外层 CommitManifest 完成唯一 publication；
- 加入 expected-parent、flush、ambiguous outcome、poison/reopen/reconcile；
- 用 fault injection 证明 old-or-new，无 mixed authority。

### B4：DurableGraph vertical slice

- 上层 graph diff/Schema/serializer 产生 PreparedStateCommit；
- StateStore 保存并由外层发布；
- reopen 后 raw chains 经 historical codec/upgrade 恢复 CLR graph；
- 成功 publish 后安装 clean baseline；失败不安装伪 current baseline。

## 9. 明确不直接移植的 Probe 资产

- experiment project/namespace 与 public shape；
- size-only payload、opaque ticket、provisional filename/wire；
- benchmark manifest、corpus、策略排行榜或 test-only oracle；
- 为 fault injection 建立的可变 in-memory shortcuts；
- all-Base/all-Delta control policy 作为产品 API；
-任何为了兼容未发布 Probe bytes 的 migration layer。

Probe 应长期保留为 executable specification、策略实验台和 differential oracle。

## 10. 阶段 B 成功条件

- 上下层 contract 不让 StateStore 依赖领域 Schema/CLR model；
- 正式实现保持阶段 A 的地址、OVD、Base/Delta、tail-triggered rollover 与 evaluator 语义；
- 真实 RBF reopen 能从外层 exact authority 恢复 current state；
- process/OS crash fault points 只产生可裁决 old/new 或明确 poisoned outcome；
- missing/corrupt current dependency fail closed，不产生 partial object graph；
- Atelia acquisition 对本地开发和 CI 均有可复现路径；
- Probe differential suite 与产品 integration tests 共同通过；
- 仍未出现空间/SLO 证据时，不顺手实现 compaction、multi-writer、Extent 或 public plugin API。
