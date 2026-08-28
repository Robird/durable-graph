# DB-007：自适应双腿轮转与 Rebase/Deltify 策略

> 状态：Open
>
> 创建日期：2026-08-28
>
> 当前方向：先建立纯内存模拟模型，比较统一策略；不先冻结固定链长、目标文件大小或搬迁字节阈值。

## 问题

在 current Revision 的 reconstruction closure 至多引用两个相邻文件的硬约束下，能否用同一组无权重事实量，同时决定：

- 单个 ObjectVersion 本次写 Base 还是 Delta；
- 普通 Save 是否顺带迁移 cold object；
- 选择哪些 objects 渐进迁移；
- 何时创建下一文件并完成 A/B → B/C 的迈腿；
- 如何在 read、write、lineage、storage 与单次暂停之间取得自适应平衡。

这是一项待验证假说。当前尚不能宣称 `TotalPersistBytes`、Previous-file ratio 或任何单一标量已经足以统一这些决策。

## 已选择的硬约束

以下来自主设计，不属于本分叉的策略选项：

1. 新 ObjectVersion 只写入 CurrentFile。
2. 每个 published current Revision 的 reconstruction closure 至多引用 Current/Previous 两个相邻文件。
3. 从 A/B 轮转到 B/C 时，Base 位于 A 的 live objects 必须在 C 产生完整 Base。
4. latest version 仍位于 A 的搬迁对象通过 B 中 RelayRevision 保持 lineage parent 可编码。
5. ObjectVersionDict 内存使用 absolute address；输出时相对于目标 frame 编码。
6. `RelativeFrameTicket` 的约 512 GiB frame-start 范围、RBF 单 Frame 和 TailMeta 上限是格式安全门，不是调优参数。
7. 任意成功 Save 后必须满足 `CanPrepareAndRotate`：B 中仍可完成所需 published Base/relay maintenance，且最终 C Revision 能容纳剩余 EvacuationSet 的完整 Bases、OVD 与 index。
8. 物理删除文件后的数据不可访问不属于格式需要抵抗的故障模型。

## 候选策略族

### 候选 A：对象局部一步成本

沿用 StateJournal 风格，以 Base/Delta 编码大小和已有 chain cost 做局部经济选择。

优点：简单、O(1) 决策形状清楚。

风险：局部最优未必为未来 relay/rotation 留出可完成路径；多 ObjectVersion 共享 Revision Frame 时，per-object bytes 也不等于整图物理读取。

### 候选 B：Previous/Current 重量平衡

估算 current Revision 的 unique reconstruction frames 中，有多少读取来自 Previous、多少来自 Current。当后腿较轻时迈步。

优点：直接贴近“两条腿交替承重”的直觉。

风险：Previous read ratio 不直接等于 evacuation Base bytes，也不能单独保证 Current 仍有空间完成 relay。

### 候选 C：渐进 cold migration

每次普通 Save 额外选择少量 cold objects，在 Current 中提前建立 Base 或 relay，使未来正式轮转不出现集中 full-copy 高峰。渐进 relay 必须随同一次普通 published Revision 写入，并由该 Revision 的 ObjectVersionDict 安装为对应 ObjectId 的 latest binding；未被 authority 引用的 relay 只是 orphan。

优点：有机会平滑写入与 pause。

风险：迁移本身会消耗 Current 空间；选择顺序和每轮数量若不收敛，可能制造更多 lineage/storage overhead。

### 候选 D：统一的边际收益/成本规划

对每个候选动作计算其对当前与未来状态的事实性变化：

```text
Base(O)
Delta(O)
Relay(O)
Stay
Rotate
```

模拟器记录各动作造成的 read/write/lineage/frame-layout 变化，再尝试无固定阈值的排序、竞争或全局规划。

优点：最接近统一策略目标。

风险：可能只是把多个未验证权重藏进一个复杂 score；必须用简单策略作对照。

## 不带权重的原始观测量

### File / Frame

- A/B/C 的 FileNumber 与 TailOffset before/after；
- candidate Frame start；
- payload、TailMeta、padding、fixed overhead、fence 与 total bytes；
- 是否还能开始下一 Frame；
- TailMeta entry count/bytes；
- full-frame read bytes、useful ObjectVersion bytes 与 cache hit。

### Object

- ObjectId、latest head 与 terminating Base 的绝对地址；
- reconstruction closure 的 FrameTicket 集合；
- Base/Delta candidate encoded bytes；
- lineage parent/relay entry encoded bytes；
- logical chain bytes、depth 与涉及的 unique frames；
- 距离上次领域修改和上次 Base 的 Save 数；
- 本次选择及事实性 reason tags。

### Revision / Rotation

- live、changed、new、unreachable、evacuation 与 relay object counts；
- A/B/C unique reconstruction frame sets 与 bytes；
- ordinary write、Base、Delta、relay、ObjectVersionDict 与 index bytes；
- post-save relay debt；
- deterministic relay completion plan 的 Frame 数与最终 TailOffset；
- `CanCompleteRelay`、`CanEncodeEvacuationRevision`、`CanPrepareAndRotate` 与 `CanRotateNow`；
- materialized logical graph hash/equality oracle。

所有比例、权重和评分均从这些原始量离线派生，避免模拟数据被首个候选算法污染。

## 不变量 oracle

策略比较必须共享同一组 correctness oracle：

```text
Materialize(candidate) == expected logical graph
ReconstructionFiles(candidate) ⊆ {Current, Previous}
CanPrepareAndRotate(successful post-state) == true
all frame starts/tickets/layouts are representable
failed plan leaves published state unchanged
```

`CanCompleteRelay` 只覆盖 B 中 relay 可完成性，`CanEncodeEvacuationRevision` 覆盖 C 中 full Bases/OVD/index，二者均不能单独推出 Store 可继续前进。首版让 deterministic frame-layout estimator 实际构造 `CanPrepareAndRotate` completion plan，以覆盖渐进 B maintenance、单帧 payload、TailMeta、padding、fence、frame-start 与 ObjectVersionDict 开销。

## 最小 workload 矩阵

- 大量 cold objects + 少数 hot objects；
- 单个 hot object 高频小 Delta；
- 对象持续增长、持续缩小与大小振荡；
- 突发大批修改后长时间稳定；
- 大量小 ObjectVersions 共享 Revision Frame；
- 少量大 ObjectVersions 造成整帧无效读取；
- latest head 在 B、但 terminating Base 在 A；
- latest head 仍在 A，需要 B relay；
- 接近 512 GiB frame-start、256 MiB frame 与 64 KiB TailMeta 边界；
- 连续多次 A/B → B/C 轮转。

每个 workload 都至少比较：AlwaysBase、AlwaysDelta-when-legal、StateJournal-style local cost、Previous-ratio、渐进 cold migration 与统一候选策略。

## 当前裁决

- `TotalPersistBytes` 可作为待测的 object-local 事实量，但当前不进入 wire，也不视为统一充分统计量。
- Previous/Current ratio 先作为观测量，不取得 correctness authority。
- 固定 `MaxLogicalChainBytes`、`TargetFileBytes` 与 migration-byte budget 暂缓。
- 约 512 GiB representability、one-frame bounds 与 `CanPrepareAndRotate` 是不可调的格式 gate，必须从第一个模拟器开始建模。
- 第一项实现工作应是纯内存、deterministic、可穷举小状态的策略模拟，而不是直接绑定真实 RBF I/O。

## 重访触发条件

- 模拟表明无参数策略不能保证 `CanPrepareAndRotate` 或产生明显振荡；
- 某类 workload 中 read/write/pause 出现稳定劣势，需要引入显式运营目标；
- 真实 RBF read log 提供 frame cache/locality 数据；
- lazy/partial Load 产生持久 chain-summary 消费者；
- one-frame Revision 碰到真实容量失败，开始设计 Extent。

## 相关材料

- `docs/state-store-base-design.md`
- `docs/state-store-base-derived.md`
- `docs/state-store-addressing-design.md`
- `E:/repos/Atelia-org/atelia/src/StateJournal/Internal/VersionChainStatus.cs`
