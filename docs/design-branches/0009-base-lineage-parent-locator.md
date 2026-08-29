# DB-009：Base lineage parent 是 direct ObjectVersion 还是 Revision locator

> 状态：Open
>
> 创建日期：2026-08-29
>
> 更新日期：2026-08-29
>
> 当前方向：Revision-locator runtime discriminator 已通过，relay-free 是领先候选；在
> `ImmediateRotationPlanner` 改由可回放 OVD authority 驱动前，不删除旧 direct-relay
> planner，也不把候选宣称为最终格式。

## 问题

Delta 的 parent 是当前值重建依赖，适合直接指向 exact previous ObjectVersion。Base 已包含
完整状态，其 parent 只用于 lineage。Base 的 lineage-only parent 是否也必须 direct，决定了
A/B → B/C 轮转是否真的需要为每个 `head@A` object 在 B 创建 relay helper。

不可约约束：

- current reconstruction 遇 Base 停止；Base 后的 parent 不计入 two-file reconstruction gate；
- retained files 存在时，Base lineage 必须能定位 prior exact ObjectVersion；
- 当前 head 与历史 binding 都必须来自同一份可回放 OVD authority，不能由 caller StateMap 补洞；
- `LogicalVersionOrdinal` 只在领域状态变化时递增，物理维护边保持相同；
- relative binding 必须按承载它的 Revision scope 解码并立即 absolute-normalize；
- ObjectVersion hops、OVD parent-resolution reads 与 current reconstruction reads 分开观测。

## 三个必须区分的形状

### A0：当前 direct relay planner

当前 `ImmediateRotationPlanner` 生成：

```text
B.RelayRevision:
    AA = zero-payload Delta -> A.AA
    OVD Delta(parent = old B head) { }  // empty

C.RelocatedBase(AA) -> exact B.RelayRevision/AA
```

C Base 直接以 `(FrameAddress, ObjectId)` 找 relay，所以 relay OVD 不安装 helper。该形状已完成
尺寸 planning，但尚未 materialize/append；planner 的 source authority 仍是 caller StateMap。

### A1：用户澄清的 relay + OVD Self + locator

用户原设想实际是：

```text
B.RelayRevision:
    AA = zero-payload Delta -> A.AA
    OVD Delta(parent = old B head) { AA = Self }

C.RelocatedBase(AA)
    -> B.RelayRevision locator
    -> LookupLive(B.RelayRevision, AA)
    -> exact B.RelayRevision/AA
```

这里 `Self` 不是重复信息：Base parent 的语义已经改成 Revision locator，必须由 OVD 把 AA
安装到 relay record。若 relay OVD 仍为空，lookup 会继承 old B OVD 并跳过 relay。

### B：relay-free Revision locator

不创建 dedicated relay。C Base 指 old B PublishedRevision，lineage 导航以隐含 ObjectId
做 OVD point lookup：

```text
ResolveBaseParent(parentRevision, objectId)
    = LookupLive(parentRevision, objectId)

B.OVD[AA] -> A.Base(AA)
B.OVD[BA] -> B.Delta(BA)

C.RelocatedBase(AA) -> B.PublishedRevision locator -> A.AA
C.RelocatedBase(BA) -> B.PublishedRevision locator -> B.BA
```

C current reconstruction 对 AA/BA 都在自身 Base 停止。该形状可删除 dedicated
RelayRevision、RelaySet、B relay capacity debt 与 `CanCompleteRelay` 子问题；它不会删除 C
evacuation Base、same-version relocation、C full OVD 或 B/C reconstruction closure gate。

## S1g 可执行证据

`TwoLegRotationProbe` 现有显式 nullable runtime OVD：`null` 表示 raw/legacy Frame 未建模
authority，不能冒充 authoritative empty Base。OVD 支持 Base/Delta 与 Self/External/Remove；
`LookupLive` 不接收 StateMap，并执行：

- Self、External、Remove decisive stop；
- Delta 缺项才跟 parent，Base 缺项得到 `AbsentAtBase`；
- 每层 binding 按自身 Revision origin 解码；
- External 必须落到含同 ObjectId record 的 earlier Frame；
- null/missing/non-earlier/wrong-object/alias 与不闭合 inspection fail closed。

canonical A/B/C fixture 由 C full OVD 作为 current authority：AA/BA 是 C Self，BB 是
External(B)。所有 current heads 都先从 `LookupLive(C, ObjectId)` 取得，没有 caller StateMap。

观察：

| 形状 | AA exact ObjectVersion hops | AA Base-parent OVD reads | B maintenance write |
|---|---|---|---|
| A1 relay + Self | `C -> Relay -> A` | `Relay` | 一个 O(N) relay Revision |
| B relay-free | `C -> A` | `B -> A` | 无 |
| relay record + empty OVD | `C -> A` | `Relay -> B -> A` | 有，但 helper 被跳过 |

A1 与 B 的 current state、`LogicalVersionOrdinal`、lineage root 相同。A1 当前唯一观察到的优势是
可能缩短历史 OVD lookup；没有 current correctness 消费者要求每次物理跨文件都暴露一个
no-op ObjectVersion hop。因此 B 成为领先候选，relay 降为待测的 lineage-read optimization，
而不是 correctness 必需机制。

该证据仍是 synthetic size-state、内存 Frame probe；没有真实 bytes、publication、reopen 或
planner materialization。完整 probe 为 194 tests。

## 未裁决的对象重新接入

若对象离开 live StateMap 后仍以同一 DurableId 重新接入：

```text
R1: OVD[X] = X1
R2: Remove(X)
R3: X reachable again
```

`LookupLive(R2, X)` 必须在 Remove 处得到 `Removed`，不能复活 X1。若 R3 要延续旧 lineage，
则需要明确不同的 `LookupHistoricalPredecessor`；显式 relay 在创建时同样需要这项能力，不能
自动裁决问题。本轮不实现 historical lookup。

## 后续裁决门

- 普通 simulation Revision 由同一 runtime OVD authority 产生 current heads；
- `ImmediateRotationPlanner` 从 PublishedRevision OVD 派生 source state，不再接收 caller map；
- relay-free plan materialize 后仍满足 A/B → B/C reconstruction closure 与 lineage oracle；
- 记录真实 workload 的 lineage OVD read amplification；只有稳定读劣势才重访 relay/checkpoint；
- Base per-record locator 与 Revision 共同 prior-snapshot anchor 的进一步化简见
  [`DB-010`](0010-base-lineage-anchor-scope.md)。

## 重访触发条件

- planner source authority 迁移到 runtime OVD；
- locator 读放大在真实 lineage consumer 中成为可测问题；
- DurableId 离开 StateMap 后重新接入的语义被裁决；
- merge/import/rescue 需要一个 Revision 内的 Base 来自多个 prior snapshots。
