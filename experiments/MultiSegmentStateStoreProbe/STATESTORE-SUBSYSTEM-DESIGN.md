# 阶段 B：正式 StateStore Sub-System 晋升设计

> 状态：Stage B Started / Empty Product Assembly
>
> 最近校准：2026-09-03
>
> 启动条件：阶段 A G0-G4 全部闭合，并由用户明确启动产品化

本文记录如何把 [`TARGET-DESIGN.md`](TARGET-DESIGN.md) 已验证的 MultiSegment 语义晋升为
`src/DurableGraph.StateStore` 内的正式 StateStore 子系统。阶段 B 已由用户明确启动，并选择独立
`Atelia.DurableGraph.StateStore` 与 `Atelia.DurableGraph.StateStore.Storage` 程序集；前者单向引用后者，只有
Storage 持有跨仓库 substrate ProjectReference：`RbfSegmentStore` 以及地址模型直接使用的 `Data/SizedPtr`。
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

这是逻辑子系统边界。首批产品空壳程序集已经建立在：

```text
src/DurableGraph.StateStore/
src/DurableGraph.StateStore.Storage/
```

由仓库公共属性得到对应程序集名和根命名空间 `Atelia.DurableGraph.StateStore` 与
`Atelia.DurableGraph.StateStore.Storage`。已冻结的项目引用方向是：

```text
DurableGraph.StateStore -> DurableGraph.StateStore.Storage -> RbfSegmentStore
                                                        \-> Data (SizedPtr)
```

StateStore 不直接引用 RBF substrate；Storage 不反向引用 StateStore。该边界用于隔离上层
Serialization/VersionedSchema、StateStore 语义与下层 RBF/Segment 文件存储依赖；当前空壳不预先裁决
public API 或正式 wire。

### 2.1 首个 Storage 地址模型切片

`DurableGraph.StateStore.Storage` 已建立最小产品地址模型：

- 空壳 `StateRevision` 只占据后续 Revision 内容模型的位置；
- runtime `AbsoluteFrameAddress` 始终保存 1-based `UInt32 FileNumber` 与真实 `SizedPtr FrameTicket`；
- `FileScope` 以 containing/current FileNumber 在 absolute FileNumber 与 `BackwardFileDistance` 之间换算；
- distance `0` 表示当前文件，future absolute file、file zero 与 distance underflow fail closed；
- 产品内存模型没有 `RelativeFrameTicket`；相对距离只允许作为后续 wire codec 的瞬时短编码值。

该切片尚未定义 wire bytes、same-file strictly-earlier Frame 校验、Revision 内容或任何文件 I/O。

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
