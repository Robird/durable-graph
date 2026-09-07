# DurableGraph 后续工作与未决问题

> 本文只维护尚未完成的能力、待裁决问题及延后条件，不维护完成历史或充当实施授权。
> 当前能力、焦点和证据入口：[src/PROJECT-STATE.md](../src/PROJECT-STATE.md)。
> 已选约束及完整产品目标：[目标设计](DurableGraph-target-design-v0.md)。
> 旧阶段安排与 R1–R7 详情：[2026-09-06 归档](archive/2026-09-06/DurableGraph-research-roadmap.md)。

## 1. 下一个分片如何选择

DB-034 领域引用图与首次准备的实现、实际边界及验收从 PROJECT-STATE/其账本进入；
这里仅保留后续增量，不再把 nominal 引用、可达图恢复或公开 PrepareNew 列为未实现能力。

后续方向按依赖而非承诺日期安排：引用图 → 分别评估 inline struct、有限数组对象和泛型闭合；
BCL 内容恢复在所需引用/值/类型表达可用后逐类型推进。struct 可独立穿插，不强制等待全部引用能力。
DB-036 单 World/单 head 工作会话已实现；branch/Reset、联合 Store 视图及更强恢复保证仍独立排期。
MVP 库内加载顺序为 exact 重建 → 单对象 Upgrade → 分配实例 → 填充/连接引用 → 完整交付 World；
Transient 由用户在交付后处理，约束维护在[目标设计](DurableGraph-target-design-v0.md#恢复transient-与宿主边界)。
数组形状、升级、单根、Transient hook、boxed value，以及无需无参构造器/readonly 字段的支持选择见
[MVP 功能边界](DurableGraph-target-design-v0.md#mvp-功能边界)，不再作为开放范围反复讨论。
GraphSession 的正常同实例 Commit 与严格重开从 PROJECT-STATE/DB-036 查证；不再列为未完成能力。
当前只支持单活动会话、固定非空 World，发布故障范围为正常关闭/进程中止和明确的 I/O 异常。
inline struct、完整数组对象、泛型闭合的穿插顺序仍未冻结。

## 2. 已采纳方向中的未完成能力

此表只列仍需工作的增量。方向已选不代表每项 API、顺序和细节已经批准。
B/D/H 分别指本轮精确 Base payload、Delta payload 上界、已有对象重建链的实际 payload 字节；
均排除 ObjectId、ObjectHeadMap 目录与共享 Revision Frame 结构。

| 工作项 | 最小应回答的问题 | 设计或证据入口 |
|---|---|---|
| TypeCodec 与 exact Schema 绑定 | 一般类型组合与内建复合类型 codec；已有 nominal class 引用及 exact reader 分派不等于一般 TypeCodec，也不自动复活已删除模型族 | [DB-034](design-branches/0034-durable-reference-graph-batch.md)、[DB-018](design-branches/0018-generated-graph-codec-shape.md)、[DB-001](design-branches/0001-schema-authority-and-runtime-representation.md) |
| 复合类型的 DTO 升级与恢复 | 将单对象 Upgrade/Restore 扩展到复合值、数组与容器内容；保持完整 source 目录、强制 Base、当前版本 DTO 图的可达分析和失败不交付 | [DB-034](design-branches/0034-durable-reference-graph-batch.md)、[DB-018](design-branches/0018-generated-graph-codec-shape.md) |
| 自定义泛型 Schema/DTO | 保留 SG 开放模板 + 首次运行时闭合/缓存方向；领域 T 与冻结表示参数分离，接通泛型定义/实参身份及历史 exact reader；现有手写 body 见证不等于 SG 已支持 | [泛型 DTO 技术备忘](design-branches/0018-generic-dto-binding-followup.md)，扩充 Schema 类型表达或泛型 Capture 时重访 |
| 自定义 struct | exact inline Schema/history 与 owner 升版，嵌套 DTO/布局及字段和数组元素的 ref body 复用 | [DB-024 struct TODO](design-branches/0024-reference-capture-and-reusable-object-ids.md)、[DB-020](design-branches/0020-typed-slot-array-binding-slice.md) |
| 完整数组对象 | 在已选零下界 SZ/有限多维 rank 范围内，实现 identity、shape、分配与元素循环，并拒绝不支持的形状；不能把现有元素模板视为完整数组支持 | [MVP 边界](DurableGraph-target-design-v0.md#mvp-功能边界)、[DB-020](design-branches/0020-typed-slot-array-binding-slice.md) |

## 3. 尚待裁决的机制

| 问题 | 现有依据与裁决边界 |
|---|---|
| 对象版本解释与保存来源 | 已登记模型族可按 Base exact Schema 自动读取；完整 ObjectHeadMap 中 external object heads 的来源、候选对象身份连续性仍需产品 Save/Load 合同，不能由 Revision Parent 声明一致推导全局身份认证 |
| 保存相等性与真实估算 | 同版标量 DTO 的浮点按位、引用槽按 ID 已采纳；未来复合值/容器相等性另定。已准备 body 与当前 v3 envelope 计量见 [DB-029](design-branches/0029-prepared-object-revision-planning-slice.md)；Base 类型头已计入 B/H。未来新增类型头/容器布局时继续按实际对象 payload 计量 |
| Schema 规范表示和持久引用 | canonical 注册批次与逻辑 SchemaKey 已闭合；未来 SchemaHash、紧凑引用及一般类型家族约束随消费者裁决，不用 GetHashCode 作持久身份 |
| 开放泛型/数组组合绑定 | 已有 static-T 缓存与 typed ref 运行时组合证据；剩余为领域/DTO 表示参数闭合、版本缓存边界及注册初始化协议，不要求全面切换 DynamicMethod。摘要、取舍与首个 SG 验证见[技术备忘](design-branches/0018-generic-dto-binding-followup.md) |
| 跨程序集与一般类型形状 | 继承 helper 可见性、外部历史祖先、enum/nullable/decimal/native int 等支持范围；自定义泛型的具体形状/约束仍须分片确定，技术方向见上项；boxed value identity 已排除 MVP |
| 多态与运行时注册扩展 | 已标记 class 基类到登记派生实例按 DB-034 合同；interface/object 通配引用、开放组合与跨程序集发现仍后续裁决，不能自动回退成声明基类的 codec |
| 捕获复合值的所有权 | 含引用 struct/数组/容器如何真正冻结候选，不能从 scalar readonly DTO 推导浅复制足够 |
| 数组完整形状与分配 | 非零下界与非 SZ rank-1 已明确不支持；实施时在 rank 上界 3/4 中选择，确定有限 tag、元素类型和各维长度编码、分配及按 ref 遍历 |
| 根与持久目录扩展 | 单 WorldId/Revision 发布已闭合；后续仅在真实需求下选择 null/清空/替换、命名 branch 与 Reset，不建设多根 API |

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
| 发布恢复保证扩展 | DB-036 已闭合同实例 Commit、expected Parent、数据/发布屏障及严格重开；遇到真实可用性要求时再设计坏尾自动修复、OS crash/power loss 与目录持久性，不能默默回退旧 head |
| ArtifactStore | 真实 HistoryLog/消息/附件消费者出现；比较地址方案、chunk、历史 view、嵌套引用与 Schema 复用，不强迫 State 常驻完整历史 |
| DerivedStore | 真实昂贵派生消费者出现；定义 exact 输入围栏、recipe/builder/model 身份、stale/missing 及可删重建 |
| 框架 Transient hook | MVP 明确不做，用户在完整图交付后自行重建；MVP 后若多个宿主确有重复的重建协调需求，再比较 hook/依赖调度及失败边界 |
| 多根产品 API | MVP 单 World；应用根对象无法满足实际独立根管理需求时，再评估根列表、命名根与局部加载，不提前建设 |
| boxed value 持久身份 | MVP 拒绝领域图中的装箱值对象；实际模型需要通过引用槽保留装箱值身份时，再增加局部 codec/身份支持；不影响框架内部 DTO 装箱 |
| 物理 GC、compaction、历史保留 | 出现真实空间或 recovery-closure 问题后；与 CLR 映射清理和数字 ID 回收分开裁决 |
| TwoLeg / incremental cleaner | 多历史 Segment 无法满足实际有界 dependency file count、在线退休、backup/rescue 或 compaction SLO 时重访，见其 [技术储备（归档）](../experiments/ARCHIVE.md#two-leg "原路径：experiments/TwoLegRotationProbe/PROJECT-STATE.md") |
| 性能优化 | MVP 后有具体测量再优化全量 Base 准备、缓冲复制、cache、typed buckets 或指纹；DB-028 先 object-first 直读 RBF，Frame cache 只减少重复 I/O/解码，重复完整 map 物化需另评估 map cache/单 ID 查询，必要时再按 Frame 合并批量读取 |
| 并发、分支与跨 Repository | 宿主提出真实 consumer 后；分别定义 concurrent Capture、snapshot isolation、branch/fork/multi-writer 和跨 Store/Repository identity，不扩大当前单 writer 假设 |
| 跨对象升级与外部副作用 | MVP 仅单对象字段转换；读取其他对象、拆分/合并及创建持久新对象均延后。MVP 后有真实迁移案例时，再讨论图访问、新 ID 与失败隔离；不借普通升级默认授权 |
| 无 CLR 迁移壳的历史族 | 应用需要完全删除迁移壳且继续恢复含该族的旧 Revision 时，再设计独立状态族 Normalize/引用能力、显式退休声明和 history-only 生成；现有保留规则与包见证见 [DB-036 §4](design-branches/0036-working-session-and-history-capabilities.md#4-并行小线明确历史恢复能力合同) |
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
- 持久发布：在明确故障阶段注入错误并 reopen，证明 Revision Parent 或 exact candidate 的可裁决结果，
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
