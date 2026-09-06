# MultiSegmentStateStoreProbe

> 已完成的可执行机制储备。本页保留阶段 A 的实现模型与证据，恢复研究前先读
> [冻结边界](PROJECT-STATE.md)；产品后续工作见 [src/PROJECT-STATE.md](../../src/PROJECT-STATE.md)。

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

上述 Stage-A codec、Frame envelope 和 synthetic plan 都是 provisional size-only 模型；Probe 项目本身不实现
真实 RBF/OVD wire、filesystem/reopen 或持久 StateStore。Stage-A commit session 只从 empty Store 启动；
与之分离的 Stage-B 产品切片状态见下文。

当前 canonical corpus 的 raw report：

```text
all-delta: W=2440 P=424 F=692 R/L=11052/2418 DeltaRef=1139 BaseRef=1796 Segments=4
all-base: W=2808 P=424 F=744 R/L=6692/2418 DeltaRef=1139 BaseRef=1796 Segments=4
adaptive-r3-b5pct: W=2436 P=424 F=692 R/L=11044/2418 DeltaRef=1139 BaseRef=1796 Segments=4
```

canonical runner 的 rollover threshold 是 512 bytes；`F > 512` 是 crossing append 被允许后留下的预期
观测，不是 hard-bound violation。这些是原始观测，不是 score、rank 或 winner 声明。阶段 A 到 G4 已完成并
冻结。阶段 B 产品项目的当前能力、待定设计与下一步统一维护在
[src/PROJECT-STATE.md](../../src/PROJECT-STATE.md)。

运行：

```powershell
dotnet test experiments\MultiSegmentStateStoreProbe\MultiSegmentStateStoreProbe.slnx
```

本 Probe 的完成状态与证据导航见 [`PROJECT-STATE.md`](PROJECT-STATE.md)，地址路线见
[`DB-014`](../../docs/design-branches/0014-multi-segment-backward-file-distance.md)。阶段 A（In-Memory
Probe，G0-G4）的目标与关键不变量以 [`TARGET-DESIGN.md`](TARGET-DESIGN.md) 为主入口；真实
filesystem/RBF 与产品化的早期分层讨论保留在 [`STATESTORE-SUBSYSTEM-DESIGN.md`](STATESTORE-SUBSYSTEM-DESIGN.md)。
已完成的阶段 A 施工边界与验证要求保留在 [`GOAL-G0-G4.md`](GOAL-G0-G4.md)。
