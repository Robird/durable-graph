# MultiSegmentStateStoreProbe

本探针验证 DurableGraph 的产品候选地址路线：Revision 和 ObjectVersion 可以引用同一 Store 目录内任意
更早的 Segment 文件，文件切换只由目标尺寸控制，不再要求 TwoLeg evacuation。

当前第一切片只验证：

- 1-based `FileNumber` 与 canonical filename direct addressing；
- runtime absolute address 与 wire `VarUInt32 BackwardFileDistance`；
- same/previous/远距引用、canonical Base128 和 fail-close 边界。

`ulong FrameTicketCode` 暂时代表未来 `SizedPtr.Serialize()` 的 canonical 非零值；本切片不复制
TwoLeg provisional RBF layout，也不声称已经实现 byte writer、RBF reopen、OVD 或 StateStore。

运行：

```powershell
dotnet test experiments\MultiSegmentStateStoreProbe\MultiSegmentStateStoreProbe.slnx
```

当前决策、roadmap 和未闭合事项见 [`PROJECT-STATE.md`](PROJECT-STATE.md)，地址路线见
[`DB-014`](../../docs/design-branches/0014-multi-segment-backward-file-distance.md)。后续编码的全景目标与
关键不变量以 [`TARGET-DESIGN.md`](TARGET-DESIGN.md) 为主入口。
