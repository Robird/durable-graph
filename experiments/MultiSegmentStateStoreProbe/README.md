# MultiSegmentStateStoreProbe

本探针验证 DurableGraph 的产品候选地址路线：Revision 和 ObjectVersion 可以引用同一 Store 目录内任意
更早的 Segment 文件；文件只在下一次 Save 前按 existing tail 与 soft rollover threshold 切换，不再要求
TwoLeg evacuation，也不把 threshold 冒充严格文件上限。

当前已用 in-memory probe 验证：

- 1-based `FileNumber` 与 canonical filename direct addressing；
- strong FrameTicket、runtime absolute address 与 probe-only `BackwardFileDistance + FrameTicket` codec；
- same/previous/远距引用、same-file strictly-earlier、canonical Base128 和 fail-close 边界；
- append-only Segment/Frame store、唯一 provisional envelope estimator；
- crossing append 留在当前 Segment、下一 Save 在 render 前轮转、final origin single render；
- 显式 multi-origin witness 仍证明 relative reference 必须随 containing origin 重编码；
- empty oversize、single-Frame hard bound、FileNumber overflow 与 reject 不发布；
- Revision shared Prior、OVD Base/Delta、ObjectVersion Base/Delta 与 canonical bindings；
- F1-F4 exact-head current reconstruction：冷 ObjectVersion 可留在 F1，热 Delta 链独立推进；
- current-required missing、non-earlier、cycle、wrong ObjectId/parent state/ordinal 的 fail-close。
- exact-parent normalization、origin-free RevisionPlan、whole-candidate admission 与 OVD Base historical-head
  reencode；
- shared-prior Base lineage、append-before-publish orphan 与 publish 后 cache failure；
- deterministic workload/generator/composer 与 A/B-free ReadAmplification+BaseBudget pure policy。
- admitted-only W/P/F/R/L、Delta/Base references、shared-Frame cold-read de-duplication；
- SameStateRebase 的 W/R 因果 witness，以及同一 corpus 的三种 deterministic raw outcomes。

这些 codec、Frame envelope 和 synthetic plan 都是 provisional size-only 模型；当前不声称已经实现正式
RBF/OVD wire、filesystem/reopen 或持久 StateStore。Stage-A commit session 只从 empty Store 启动。

当前 canonical corpus 的 raw report：

```text
all-delta: W=2440 P=424 F=692 R/L=11052/2418 DeltaRef=1139 BaseRef=1796 Segments=4
all-base: W=2808 P=424 F=744 R/L=6692/2418 DeltaRef=1139 BaseRef=1796 Segments=4
adaptive-r3-b5pct: W=2436 P=424 F=692 R/L=11044/2418 DeltaRef=1139 BaseRef=1796 Segments=4
```

canonical runner 的 rollover threshold 是 512 bytes；`F > 512` 是 crossing append 被允许后留下的预期
观测，不是 hard-bound violation。这些是原始观测，不是 score、rank 或 winner 声明。阶段 A 到 G4 已完成并
冻结；阶段 B 已建立 `DurableGraph.StateStore -> DurableGraph.StateStore.Storage -> RbfSegmentStore` 的空壳
程序集依赖链，并完成绝对 Frame 地址与 `FileScope` 换算的首个产品切片；尚未实现正式 wire 或持久行为。

运行：

```powershell
dotnet test experiments\MultiSegmentStateStoreProbe\MultiSegmentStateStoreProbe.slnx
```

当前决策、roadmap 和未闭合事项见 [`PROJECT-STATE.md`](PROJECT-STATE.md)，地址路线见
[`DB-014`](../../docs/design-branches/0014-multi-segment-backward-file-distance.md)。阶段 A（In-Memory
Probe，G0-G4）的目标与关键不变量以 [`TARGET-DESIGN.md`](TARGET-DESIGN.md) 为主入口；真实
filesystem/RBF 与产品化只记录在 [`STATESTORE-SUBSYSTEM-DESIGN.md`](STATESTORE-SUBSYSTEM-DESIGN.md)。
已完成的阶段 A 施工边界与验证要求保留在 [`GOAL-G0-G4.md`](GOAL-G0-G4.md)。
