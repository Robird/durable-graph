# DurableGraph 后续工作与未决问题

> 本文只维护尚未完成的能力、待裁决问题及延后条件，不维护完成历史或充当实施授权。
> 当前能力、焦点和证据入口：[src/PROJECT-STATE.md](../src/PROJECT-STATE.md)。
> 已选约束及完整产品目标：[目标设计](DurableGraph-target-design-v0.md)。
> 旧阶段安排与 R1–R7 详情：[2026-09-06 归档](archive/2026-09-06/DurableGraph-research-roadmap.md)。

## 1. 下一个分片如何选择

[DB-031](design-branches/0031-persisted-object-type-envelope-slice.md) 已闭合持久 Schema 注册、Base 类型引用
及显式 reader 的同版冷重建，接续现有统一准备和策略。下一片推荐
[DB-032 完整 exact-version 对象目录冷读](design-branches/0032-exact-revision-decoding-slice.md)（Proposed，未实施）：
先把现有静态历史 reader 接成完整目录读取，具体取舍和验收只维护在提案。
其后重新评估新 DTO 升级、roots/Restore、加载身份/基线导入和工作会话，不预定扩大为完整 Commit。
WorkingTree/GraphSession 的职责方向已采纳；发布/故障裁决尚未实现。不要把内存 Current、Schema
注册成功或 State Append 返回地址直接当作已发布基线。Schema 严格坏尾拒绝合同见 DB-031 §8。

current 领域 Restore、自定义 struct 和一般 durable 引用可以独立成片。
它们与存储推进的穿插顺序尚未冻结；不要恢复旧 R4 → R5 → R6 或 P0–P7 为强制流水线。
选片时给出一个可观察成功/失败判据，若触及 durable format 或 publication 则先记录设计裁决。

## 2. 已采纳方向中的未完成能力

此表只列仍需工作的增量。方向已选不代表每项 API、顺序和细节已经批准。
B/D/H 分别指 Base 写入字节、Delta 写入字节、当前对象重建字节；均按策略已定的对象自身 payload 口径。

| 工作项 | 最小应回答的问题 | 设计或证据入口 |
|---|---|---|
| 工作会话与 exact Parent baseline | 已选 Repository 受控创建/加载的 WorkingTree/GraphSession；如何建立、安装、冷重建 Parent / DTO / 实例身份绑定，收敛 Commit API 与失败行为 | [目标约束](DurableGraph-target-design-v0.md#单一发布权威与明确故障结果)、[DB-030 接缝](design-branches/0030-captured-object-preparation-slice.md#4-exact-parent-接缝明确留到后片) |
| TypeCodec 与 exact Schema 绑定 | 一般类型组合、nominal 引用约束、内建复合类型与自动 reader 分派；现有 string/durable Base 引用及显式 typed reader 不等于完整 TypeCodec | [DB-031 接缝](design-branches/0031-persisted-object-type-envelope-slice.md)、[DB-018](design-branches/0018-generated-graph-codec-shape.md)、[DB-001](design-branches/0001-schema-authority-and-runtime-representation.md) |
| DTO 升级与领域 Restore | stored exact 版本如何分派、升级为 current DTO，再构造领域对象；失败时不交付半成品 | [DB-022](design-branches/0022-versioned-state-dto-capture.md)、[DB-002](design-branches/0002-read-time-version-upgrade-pipeline.md) |
| 一般 durable 引用图 | 递归登记、共享/循环、nominal 约束、多态实际类型、完整目录及 roots 可达闭包如何共同成立 | [DB-018](design-branches/0018-generated-graph-codec-shape.md)、[DB-024](design-branches/0024-reference-capture-and-reusable-object-ids.md) |
| 自定义 struct | exact inline Schema/history 与 owner 升版，嵌套 DTO/布局及字段和数组元素的 ref body 复用 | [DB-024 struct TODO](design-branches/0024-reference-capture-and-reusable-object-ids.md)、[DB-020](design-branches/0020-typed-slot-array-binding-slice.md) |
| 完整数组对象 | identity、shape/下界、分配与全 rank 元素循环如何组成 codec；不能把现有元素模板视为完整数组支持 | [DB-020](design-branches/0020-typed-slot-array-binding-slice.md) |
| reopen 后身份接续 | 加载实例怎样绑定到所选 Revision 的 ID；如何恢复分配高水位及隔离失败候选 | [DB-024](design-branches/0024-reference-capture-and-reusable-object-ids.md) |

## 3. 尚待裁决的机制

| 问题 | 现有依据与裁决边界 |
|---|---|
| 对象版本解释与保存来源 | Base exact Schema 已可持久解析；自动 reader 绑定、完整 head map 的 external heads 来源、候选对象身份连续性仍需产品 Save/Load 合同，不能由 Parent 声明一致推导全局身份认证 |
| 保存相等性与真实估算 | 同版标量 DTO 的浮点按位、引用槽按 ID 已采纳；未来复合值/容器相等性另定。已准备 body 与当前 v3 envelope 计量见 [DB-029](design-branches/0029-prepared-object-revision-planning-slice.md)；Base 类型头已计入 B/H。未来新增类型头/容器布局时继续按实际对象 payload 计量 |
| 历史升级后的比较和重写 | DB-006/R3 已见证 normalized baseline 与 RequiresRewrite；产品同版重建接缝已就绪；升级须先按 Base Schema 完整还原旧链再进行，仍 live 的升级对象即使值未变也必须 BaseOnlyUpdate。新 DTO 自动升级、义务导入/发布后清除与删边清理尚未接通，读取不回写 |
| 完整 source 目录与 current 可达集合 | 升级可能删边。研究见证保留 source rows，再由 Save 移除不可达项；产品保存视图怎样表达需与候选/Parent 衔接 |
| Schema 规范表示和持久引用 | canonical 注册批次与逻辑 SchemaKey 已闭合；未来 SchemaHash、紧凑引用及一般类型家族约束随消费者裁决，不用 GetHashCode 作持久身份 |
| Restore 的分配和阶段边界 | allocate-all / hydrate-all 有循环见证；构造器、readonly 字段、升级引用重绑定、验证/transient hook 的具体可见性和顺序待选 |
| 开放泛型/数组组合绑定 | SG 静态 body + runtime 按需闭合是推荐路线；具体 generic factories、局部 DynamicMethod 或其他后端尚待消费场景裁决，不据此扩建通用 registry |
| 跨程序集与一般类型形状 | 继承 helper 可见性、外部历史祖先、generic durable 类型、boxed value identity、enum/nullable/decimal/native int 等支持范围 |
| 多态与运行时注册 | exact runtime 类型到 Schema/DTO/codec 的绑定、nominal assignability、未知实现 fail closed；不为尚无消费者的插件体系预制完整注册框架 |
| 捕获复合值的所有权 | 含引用 struct/数组/容器如何真正冻结候选，不能从 scalar readonly DTO 推导浅复制足够 |
| 数组完整形状与分配 | 明确非零下界、非 SZ rank-1、一般 rank 的类型/shape 编码与分配，保留元素按 ref 读写 |
| 根与持久目录 | roots 如何与 Revision 持久绑定、加载完整对象目录并自动分派 reader；Base 类型引用不能替代根目录或联合 manifest |
| 多个空串 ID 的会话导入 | 读取允许多个 ID 解析到同一个 Empty；reopen 后如何绑定/合并别名及接续保存尚待裁决，不重开独立空串实例分配 |

设计证据：[DB-006](design-branches/0006-flat-graph-delta-prototype.md)、
[旧路线图 R3/R4](archive/2026-09-06/DurableGraph-research-roadmap.md)、
[运行时绑定见证](design-branches/0018-runtime-binding-witness.md)。
这些实验选择不自动成为新 DTO 产品路径的 API 或强制前置项目。
DB-009/010 的旧 no-reuse 前提不能沿用；借用 Base 共享 prior 等结论时也需重新检查 ID 新占用者边界。

## 4. 明确延后及重访条件

| 延后项 | 何时重访 / 届时要回答的问题 |
|---|---|
| ObjectId 数字回收 | 单调分配配合其他机制开发后，再定义候选隔离、retire/reuse 时机与恢复；可评估 StateJournal SlabBitmap/SlotPool，不能复用旧对象 Delta 链 |
| BCL 集合 | 基础引用/值和对象恢复形成消费者后；逐类型定义内容、顺序、comparer、共享和 key/index 建立时机 |
| SchemaStore 后续能力 | MVP 单调注册已实现；联合 Commit/Ref 及复用 StateStore 的演进候选见下节，Dictionary 与内建类型 codec 完整后重访。多 writer、压缩/GC 另待真实需求 |
| Schema 日志自动修复/分段 | 遇到真实坏尾恢复或容量需求时；无额外确认水位不能自动区分未完成尾部和已确认末帧损坏，当前严格拒绝。重访时先冻结故障模型，不绕过完整注册一致性 |
| 完整 Save、发布与恢复 | 内容链和保存输入闭合后；确定 expected parent、durability barrier、publication 不确定结果、reopen/reconcile、基线安装及故障模型 |
| ArtifactStore | 真实 HistoryLog/消息/附件消费者出现；比较地址方案、chunk、历史 view、嵌套引用与 Schema 复用，不强迫 State 常驻完整历史 |
| DerivedStore | 真实昂贵派生消费者出现；定义 exact 输入围栏、recipe/builder/model 身份、stale/missing 及可删重建 |
| Transient 重建 | 首个领域 Restore 消费者需要索引/缓存时；比较单对象 hook、全局 registry、两阶段或依赖调度，失败不交付 roots |
| 物理 GC、compaction、历史保留 | 出现真实空间或 recovery-closure 问题后；与 CLR 映射清理和数字 ID 回收分开裁决 |
| TwoLeg / incremental cleaner | 多历史 Segment 无法满足实际有界 dependency file count、在线退休、backup/rescue 或 compaction SLO 时重访，见其 [技术储备](../experiments/TwoLegRotationProbe/PROJECT-STATE.md) |
| 性能优化 | MVP 后有具体测量再优化全量 Base 准备、缓冲复制、cache、typed buckets 或指纹；DB-028 先 object-first 直读 RBF，Frame cache 只减少重复 I/O/解码，重复完整 map 物化需另评估 map cache/单 ID 查询，必要时再按 Frame 合并批量读取 |
| 并发、分支与跨 Repository | 宿主提出真实 consumer 后；分别定义 concurrent Capture、snapshot isolation、branch/fork/multi-writer 和跨 Store/Repository identity，不扩大当前单 writer 假设 |
| 升级创建新对象或外部副作用 | 当前升级/图恢复闭合之后，有具体需求再讨论新 ID、source table 外引用与失败隔离，不借普通升级默认授权 |
| 历史工具/升级调用优化 | 有 package/history 或升级调用的真实限制后，再重访 DB-003 的 Try/result/ABI 和 DB-004 的多 writer/多 TFM 与批次原子性，不顺带做兼容框架 |

### 4.1 SchemaStore 复用 StateStore 与联合版本视图

2026-09-07 用户提出的未来技术路线：StateStore 作为基础，在 Dictionary 等 BCL 内容 codec
可用后，评估用一个不含用户自定义类型的 StateStore 实例保存 Schema 定义集合，复用增量保存、
历史视图、回滚与分叉。该路线合理且值得保留，尚未选择具体数据表示或替换当前 MVP 实现。
联合 Commit/Ref 的长期约束见[目标设计](DurableGraph-target-design-v0.md#单一发布权威与明确故障结果)。

重访触发：受支持的 Dictionary 内容保存/重建、所需内建类型组合编码，以及 StateStore 的
Revision/加载能力足以表示元数据集合；建设联合 Commit/Ref 时也应重新检查本条。

届时需闭合：

- 自举：2026-09-07 用户补充“员工通道”思路，推荐由框架显式内建的类型码及 Schema/codec
  解释基元、受支持 BCL 构造和 DurableGraph 元数据类型，仅用户定义类型通过 SchemaStore
  取得定义；不能以程序集/namespace 归属自动豁免所有类型。内建复合类型不必退化为字典树，
  可以用内建 SchemaDefinition/FieldDefinition 等强类型数据表示（名称为示意，尚未实现）。
  若 Dictionary 的 key/value 和实际内容均属于内建类型闭包，即可在没有已加载 SchemaStore
  的情况下读回目录；Dictionary<string, UserClass> 则仍需解析 UserClass 的 Schema。
  目录中的用户 Schema 引用首先是元数据值，不能在解码这些描述数据时递归启动用户对象加载。
- 内建合同：豁免的是 SchemaStore 查询，不是持久布局的版本管理。内建类型身份及其 codec
  版本仍须由明确格式规则确定，不能直接依赖当前 CLR fields 布局；版本可由外层格式约定，
  不预先要求每个对象增加版本字段。引用类型继续共用对象身份、Capture 和 Base/Delta 流程。
- 逻辑职责：复用 StateStore 保存集合，不取消 Schema 的规范定义、exact 依赖与同 key 冲突检查；
  不让一般字典写入绕过 SchemaStore 语义。
- 联合视图：Commit/Ref 绑定各 Store 的 exact roots/Revision，加载与整体回滚使用同一组引用。
  Schema 记录物理保留不等于在所有 Commit 中可见；不要将 MVP 的全局注册索引直接当作未来视图。
- 冲突作用域：回滚/分叉后，同 `(SchemaId, Version)` 的一致性是仓库全局约束还是视图约束，
  须单独裁决。不能隐式允许同 key 不同定义而仍以无视图的 key 查找；任何选择都要保证历史 State
  永远解析到原 exact 定义。本条不授权 MVP 放宽冲突检查。
- 迁移与恢复：从 MVP 注册日志转入版本化目录时，定义旧 Schema 引用的解析、联合发布顺序和
  故障恢复；届时再实施，不现在预建迁移或双写框架。

## 5. 后续切片验收素材

每轮只挑与问题有关的见证，不重复搬运全部历史闸门：

- 保存状态律：unchanged 沿用旧记录，changed/new 追加，不可达退出新视图；
  成功后再次保存无伪变化，失败不能替换已提交基线。
- 类型/图读取：exact Schema 预检、typed decode/upgrade、引用目标合法性、共享/循环，
  late failure 不暴露半成品；升级删边与 source membership 的区别必须可观察。
- 格式：独立 golden bytes、canonical 编码、截断/未知标签/溢长/尾随数据与错误 prior 拒绝。
- 持久发布：在明确故障阶段注入错误并 reopen，证明 parent 或 exact candidate 的可裁决结果，
  不能仅用 Append 正常返回或内存 Accept 宣称 durability。

历史 logical graph、TwoLeg 和 MultiSegment 的材料由
[设计索引](design-branches/README.md)与[实验笔记入口](DurableGraph-lab-notebook.md)按需访问。
它们保留可复用状态律和反例；产品继续开发不默认重跑全部实验，也不照搬 fixture API。

## 6. 维护方式

- 新问题只在本表或当前分片设计中选一个详细维护位置，另一处放链接。
- 获得裁决后，将长期约束归入目标设计；本表保留尚未实现的增量与验收问题。
- 完成后删除对应待办或收窄剩余部分，产品工作集更新能力入口，完整结果进入历史证据。
- 延后项应有重访触发；不以日期顺序或旧阶段编号代替依赖关系。
- 新证据否定旧假设时替换原条目；历史理由由 Git 和归档保存，不在活跃正文不断叠加校准段落。
