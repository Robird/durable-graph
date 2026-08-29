# DB-009：Base lineage parent 是 direct ObjectVersion 还是 Revision locator

> 状态：Open
>
> 创建日期：2026-08-29
>
> 当前方向：runtime probe 继续使用 direct ObjectVersion parent；在扩大 relay completion
> planner 前，用同一份可回放 OVD authority 单独验证 Revision locator，不以 caller StateMap
> 冒充历史 OVD。

## 问题

Delta 的 parent 是当前值重建依赖，适合直接指向 exact previous ObjectVersion。Base 已包含
完整状态，其 parent 只用于 lineage。Base 的 lineage-only parent 是否也必须 direct，决定了
A/B → B/C 轮转是否真的需要为每个 `head@A` object 在 B 创建 relay helper。

不可约约束：

- current reconstruction 遇 Base 停止；Base 后的 parent 不计入 two-file reconstruction gate；
- retained files 存在时，Base lineage 必须能定位 prior ObjectVersion；
- 内存 StateMap 和历史 OVD 不能成为两份可漂移的 authority；
- `LogicalVersionOrdinal` 只在领域状态变化时递增，物理维护边保持相同；
- relative binding 必须按承载它的 Revision scope 解码并立即 absolute-normalize。

## 候选 A：direct ObjectVersion parent + B relay helper

当前 Working Design 与 `ImmediateRotationPlanner` 使用：

```text
C.RelocatedBase(X)
    -> B.Relay(X)
    -> A.PreviousObjectVersion(X)
```

优点：与当前 `Frame.ObjectVersions[ObjectId]` 和 `PhysicalStateOracle` 闭合；parent 始终是
exact `(FrameAddress, ObjectId)`。代价是 O(N) relay records、TailMeta、B append/durable flush、
容量 preflight、orphan/reopen 规则和未来 multi-frame completion planning。

runtime probe 已在 synthetic size-state 模型中证明无需新增 kind：same-version zero-payload
Delta 可表达 transparent Relay，same-version Base 可表达 RelocatedBase；logical equality
只观测 `(BasePayloadBytes, LogicalVersionOrdinal)`，地址和 parent 链已经表达物理顺序。

## 候选 B：仅 Base 使用 Revision-scoped lineage locator

Delta 继续 direct；非首 Base 的 parent ticket 指向一个 earlier Revision。lineage 导航以隐含
ObjectId 对该 Revision 的 OVD 做 point lookup：

```text
ResolveBaseParent(parentRevision, objectId)
    = LookupLiveObjectVersion(parentRevision.OVD, objectId)
```

canonical AA/BA/BB 推演中，旧 B PublishedHead 可以直接充当中继：

```text
B.OVD[AA] -> A.Base(AA)
B.OVD[BA] -> B.Delta(BA)

C.RelocatedBase(AA) -> B.PublishedHead.OVD[AA]
C.RelocatedBase(BA) -> B.PublishedHead.OVD[BA]
```

C current reconstruction 对 AA/BA 都在自身 Base 停止；历史 lineage 分别导航到 A 和 B。
该形状可删除 dedicated RelayRevision、RelaySet、B relay capacity debt 与 `CanCompleteRelay`
子问题。它不会删除 C evacuation Base、same-version relocation、C full OVD 或 B/C closure gate。

## 为什么尚未选择候选 B

当前 probe 的 `Frame` 不保存可回放 OVD；V0 OVD 只是尺寸 grammar，planner 仍由 caller
StateMap 提供当前 authority。用这份 StateMap 同时回答历史 `B.OVD[ObjectId]` 会伪造第二
authority，不能构成证据。

诚实探针必须至少实现：

1. OVD Base/Delta 的单一 decoded authority；
2. object-specific point lookup，命中 decisive Self/External/Remove 后立即停止；
3. 每层 binding 按自身 Revision origin absolute-normalize；
4. missing/cycle/non-earlier/future binding fail closed；
5. current reconstruction frames、parent-resolution frames 与 lineage hops 分开观测。

这套 OVD reader 本来就是未来 persisted StateMap 的依赖；届时复用它可能使候选 B 的总复杂度
显著低于 relay。但在它出现前，候选 B 只是有力的化简假说，不是当前实现事实。

## 未裁决的对象重新接入

若对象离开 live StateMap 后仍以同一 DurableId 重新接入：

```text
R1: OVD[X] = X1
R2: Remove(X)
R3: X reachable again
```

`LookupLive(R2, X)` 必须在 Remove 处得到 absent，不能错误继承 X1；但若 R3 要延续旧
lineage，则 Base parent 需要不同的 `LookupHistoricalPredecessor` 语义，遇 Remove 时继续向
OVD parent 搜索。候选选择不能偷偷决定“重新接入获得新 ID”“断开 lineage”或“延续旧
lineage”；这是后续 DurableId 生命周期实验的独立输入。

## 可执行裁决门

- AA/BA/BB 使用 OVD Delta inheritance，无 caller StateMap 副本；
- lookup 命中 B decisive binding 时不继续读取 A；
- Base lineage locator 可解析 A binding，而 current Base reconstruction 仍只读 C；
- Remove 的 live lookup definitive absent；若支持 historical lookup，两者结果明确分离；
- OVD Base absence、cycle、missing parent、future binding 与同 ObjectId record mismatch fail closed；
- dedicated relay 与 locator 对同一 frozen workload 输出相同 logical state/lineage，分别记录
  B write bytes、TailMeta debt 与 lineage OVD read frames。

## 重访触发条件

- 首个可回放 OVD Base/Delta reader；
- `ImmediateRotationPlan` 开始真实 materialize/append；
- multi-frame relay completion planner 准备扩展；
- DurableId 离开 StateMap 后重新接入的语义被实验裁决。
