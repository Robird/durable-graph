# DB-014：多历史 Segment 与 BackwardFileDistance 地址

> 状态：Chosen
>
> 创建日期：2026-09-02
>
> 边界：本文记录 DurableGraph 产品路线的下一持久化探针，不代表当前 runtime 已实现持久 StateStore，
> 也不否定独立 `TwoLegRotationProbe` 的研究价值。
>
> Authority：本文 supersede `state-store-base-design.md` 中相邻两文件 closure/evacuation/rotation 的
> 产品部分，以及 `state-store-addressing-design.md` 的 1-bit same/previous external-reference grammar。
> 旧文档保留为 TwoLeg 可执行研究说明；one-frame、OVD、Base/Delta 与 lineage 等可复用部分仍需逐项验证。

## 裁决

产品候选不再要求 latest Revision 的 current-reconstruction closure 只涉及相邻两个文件。Revision OVD、
ObjectVersion Delta parent 与其他 required Frame reference 可以指向同一 Store 目录内任意更早的 Segment
文件。文件切换只受应用配置的目标尺寸与 RBF hard bounds 控制，不要求同时 relocation 冷对象。

持久语义使用 1-based `UInt32 FileNumber`：

- `0` 非法并可保留给字段局部的 None 语法；
- FileNumber 随新文件单调增加；任何 published 文件号不得复用；
- `UInt32.MaxValue` 后 fail closed，不 wrap；
- FileNumber 是 Store 目录内的逻辑文件号，不是进程级 handle。

文件路径由 Store 目录与 canonical filename 约定直接计算：

```text
filePath = storeDirectory / FormatCanonicalFileName(fileNumber)
```

不维护集中式 `FileNumber -> path` 映射，也不在文件头增加 `StoreId`/`FileId` identity。Store 作用域由
目录边界提供；完整目录可整体移动或复制，把单个数据文件拼入另一个 Store 不属于 v1 支持的操作。
Probe 冻结 exact filename grammar 后，reopen 扫描只接受 canonical 文件名，重复、非法或冲突路径
fail closed。

## 内存与 wire 表示

进程内 authority 仍是绝对地址：

```text
AbsoluteFrameAddress = FileNumber + SizedPtr
```

wire 中每个 required external Frame reference 保存：

```text
BackwardFileDistance : canonical VarUInt32
FrameTicketCode      : canonical SizedPtr encoding

targetFileNumber = originFileNumber - BackwardFileDistance
```

派生规则：

- distance `0` 表示 containing file；`1` 表示前一个文件；更大值表示任意更老文件；
- target 必须大于 0，禁止 future reference、下溢与 non-canonical VarUInt；
- 固定 `UInt16` horizon 被拒绝：它会在 65,535 个文件后重新引入强制 Base/relocation；
- decode 后立即 absolute-normalize；继承的内存地址不得按新 origin 重新解释；
- 写新 OVD Base/Delta 或移动 record 时，从 absolute address 相对新 origin 重新编码，不复制旧 raw bits；
- OVD 同 Frame binding 继续使用字段局部 `BindSelf`，不把 containing ticket 写入自身；
- optional None、BindSelf 与 required external reference 属于各自字段 grammar，不混成一个通用 union。

所有持久引用只允许指向 earlier state：target file 小于 origin，或同文件内目标 Frame 物理早于 origin
Frame。地址 codec 检查整数/canonical 边界；RBF reader 仍负责目标 ticket 存在、Frame 完整性和 L3
验证。

## 文件切换

`TargetFileBytes` 是应用配置的 soft target，不是地址格式常量：

```text
if CurrentFile is nonempty
    and appending candidate Revision would cross TargetFileBytes:
    create file CurrentFileNumber + 1

append the same candidate Revision
```

切换文件不改变 Base/Deltify、OVD membership 或对象 placement 决策。若单个合法 Revision Frame 本身
超过 soft target、但仍满足 RBF hard bounds，可写入一个 dedicated oversize file；后续 Revision 再创建
新文件。超过单 Frame hard bound 仍 fail closed，文件切换不替代未来 Extent 设计。

## OVD 与 ObjectVersion

- OVD Delta parent 可指向任意更老文件中的 exact prior Revision；
- OVD Base 可直接编码完整 live map 中的任意历史 ObjectVersion head，无需 relocation 对象；
- ObjectVersion Delta 继续指 exact prior ObjectVersion，Base 继续停止 current reconstruction；
- OVD 与普通对象各自依据 reconstruction/read amplification 决定 Base 或 Delta，不与文件切换绑定；
- Base lineage 是否经 prior Revision OVD 导航仍是 current/lineage 分层问题，不需要 C-to-A bridge。

因此旧 A-debt、EvacuationSet、Stay/Rotate paired candidate、preparatory NoChange migration、
`CanPrepareAndRotate` 与 two-file closure hard gate 不进入产品候选的正常 Save 路径。

## v1 明确保证与不保证

v1 保证：

- single writer 与 exact PublishedHead/CommitManifest authority；
- candidate dependencies durable 后才发布，未发布新文件只是 orphan；
- latest OVD 与所有 live ObjectVersion reconstruction chains 可跨历史文件完整恢复；
- future、missing、malformed、non-earlier 或循环 reference fail closed；
- 每个数据文件受 soft target 与 RBF hard bounds 约束。

v1 不保证：

- Store 总字节数或文件数有界；
- latest recovery closure 的文件数、Frame 数或 cold-read bytes 有界；
- 自动删除、segment cleaning、在线 compaction 或 backup packing；
- 一个小 live object 不会长期 pin 一个大历史文件；
- historical lineage 在人工删除文件后仍可导航。

在 GC/compaction 协议出现前，published recovery closure 中的文件不得删除。recovery closure、pinned
files 与 cold-read 分项只作为从 PublishedHead 派生的 inspection/diagnostic，不形成第二 authority。
首个回收方案优先考虑显式 `CompactToNewStore`；只有完整 compaction 的停顿或峰值出现真实问题，才研究
incremental segment cleaner、TwoLeg 或 tiered placement。

## 与 DB-013 / TwoLeg 的关系

多历史 Segment 天然允许冷对象留在旧文件，因此产品候选不需要为了热文件切换引入 Hot/Cold 双层 OVD。
[`DB-013`](0013-tiered-state-segments.md) 保留为 TwoLeg 研究路线或未来独立 placement consumer 的
Deferred 分支。

TwoLeg 仍购买一个强不变量：latest current reconstruction 只涉及常数个文件，因而文件 retirement、
backup 与 rescue dependency 更局部。若真实产品要求在线有界总磁盘、极小 dependency file count，且又
不能接受完整 compaction，TwoLeg 或增量 cleaner 应重新进入产品比较。

## 首个 Probe gate

在 `experiments/MultiSegmentStateStoreProbe` 隔离验证：

1. 1-based FileNumber、canonical filename 与无集中 catalog 的 direct addressing；
2. `VarUInt32 BackwardFileDistance` 的 same/previous/>65,535/max-distance round-trip 与 fail-close；
3. 冷 Base 留在 F1，热对象更新和 Revision 推进到 F2/F3/F4，latest state 仍可重建；
4. 新文件中的 OVD Base 可引用 F1 head 而不 relocation 冷对象；
5. soft target 切文件不改变 Base/Deltify decision；
6. missing historical file、future reference、非 canonical distance 与错误 filename fail closed；
7. 从 PublishedHead 派生 required files/Frames，区分 pinned 与当前可忽略的 orphan。

Probe 通过前不修改 `src/DurableGraph` 的产品 API 或冻结正式 wire bytes。
