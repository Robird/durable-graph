# DurableGraph 目标与设计约束

> 本文维护长期目标与已选设计约束，不描述完成情况，不冻结 API 或持久格式。
> 产品现状：[src/PROJECT-STATE.md](../src/PROJECT-STATE.md)。
> 尚未完成及待裁决事项：[路线图](DurableGraph-research-roadmap.md)。
> 早期完整草稿：[2026-09-06 归档](archive/2026-09-06/DurableGraph-target-design-v0.md)。

## 1. 要解决的问题

DurableGraph 面向长期存在、持续演化的 C# 领域世界，例如角色与物品关系、共享规则、
HistoryLog，以及由这些权威事实派生的索引和认知摘要。应用直接编写普通强类型 C# 领域逻辑，
不必把模型手工翻译成通用字典树，也不依靠 setter hook、代理或 MarkDirty 报告修改。

目标闭环是：领域对象图 → 捕获版本化状态 → 比较已提交状态 → 追加对象版本 →
明确发布 → 按 exact Schema 读取 → 显式升级 → 恢复领域图与可重建状态。
Source Generator 负责可在编译期确定的类型知识与机械代码，框架负责保存、加载和身份管理。

首版接受按存活对象图进行 O(live graph) 扫描。是否增加指纹、局部扫描或其他优化，
应由真实对象数量、baseline I/O 和提交耗时决定；不把业务维护 dirty 信息作为前提。

这不是任意 CLR heap、执行栈或 continuation 的 checkpoint，也不以 ORM、查询语言、
分布式多写者或透明回滚任意 C# 副作用为目标。delegate、Task、线程、文件句柄、Socket
等执行或外部资源不因可被 CLR 引用就自动具有持久化语义。

## 2. 已选设计约束

本节汇总已由用户选择、并持续约束产品切片的方向；具体支持范围只查产品工作集。

### 捕获状态与领域行为分离

- 保存先从领域图捕获与 VersionedSchema 配对的 Versioned DTO 和对象列表；后续比较、
  估算、Base/Delta 选择和编码都消费该候选状态。领域对象随后变化不能改变候选内容。
- 自定义领域类型的 DTO 与 Capture 由 SG 生成；受支持 CLR/BCL 引用类型使用预制或可组合适配。
  string 不可变，非空内容可以直接保留原实例；一般容器不能据此假定浅拷贝已隔离可变内容。
- 同一个候选状态在实际发布成功后才可成为提交基线；不能重新捕获领域对象来冒充已提交结果。
  基线是可丢弃、可从权威状态重建的比较投影，不是第二个持久权威来源。
- DTO 的 CLR 字段命名或物理展开方式不决定 Schema。历史 DTO 从已接受的 Schema/history
  再生成，不要求永久保留所有旧领域 CLR 类，也不另存一套 DTO 源码历史。
- 同版 DTO 的 Delta 准备融合变化判断与编码，结果持有变化判定和可复用 bytes，Delta body 大小从实际长度取得；
  选择 Delta 后复用该结果，避免再次比较和编码。临时缓冲所有权独立于可变领域对象。
- MVP 同样提前 PrepareBase：复用强类型 Write 生成独立拥有的 Base body，以实际 body 长度计量，
  决策后直接复用选定 bytes。先接受全部 live Base 准备的 CPU/内存成本，优化留待 MVP 后测量；
  Frame 大小上限不代表候选集合的内存上限。Storage envelope 开销另按其格式计入。

设计来源：[DB-022](design-branches/0022-versioned-state-dto-capture.md)、
[DB-024](design-branches/0024-reference-capture-and-reusable-object-ids.md)、
[DB-027](design-branches/0027-generated-same-schema-delta-body-slice.md)、
[DB-029](design-branches/0029-prepared-object-revision-planning-slice.md)。

### 统一引用身份，值类型嵌套

- 所有受支持引用类型统一进入对象列表，包括自定义 class、string、数组与 BCL 容器。
  成员中的引用只保存 ObjectId，对象本体独立保存；共享和循环是整体恢复目标。
- 当前 CLR 图按引用相等语义登记。内容相等的不同非空 string 实例不能合并；唯一明确例外是
  所有零长度 string 在 Capture 和读取两端都规范化为 string.Empty。null 仍与空串区分。
- ObjectId 是指定 StateRevision 内的查找编号。相邻保存中持续存活的对象保留编号，
  回收后的数字可以复用；跨 Revision 的裸编号相同不代表同一实体。
  新占用者从 Base 开始，不能继承旧占用者的 Delta 链。
- 引用槽经正在加载的目标 Revision 解析，即使 owner body 沿用更早的记录；旧 Revision
  使用自己的视图。已解析的 CLR 对象图缓存不能直接跨视图复用。
- 首轮采用会话内单调分配，失败或放弃候选可以烧号。允许未来复用不要求立即实现回收器；
  CLR 实例映射清理、可达集合变化、编号回收与历史文件物理 GC 是不同动作。
  publication 不确定也不能当作确定失败释放候选身份。
- struct 等复合值没有独立身份，采用嵌套布局。ref accessor 的作用是让值 codec 共用字段、
  数组元素等真实槽位，配合 Writer/Reader 读写；不要求统一读写模式的 visitor。
- BCL 容器按内容保存和重建，不以其 bucket、capacity 等内部实现代替持久内容合同。

设计来源：[DB-018](design-branches/0018-generated-graph-codec-shape.md)、
[DB-024](design-branches/0024-reference-capture-and-reusable-object-ids.md)、
[DB-025](design-branches/0025-string-object-decoding-slice.md)。

### Schema 依赖与强类型代码

- Schema 具有稳定类型标识、版本和声明层字段编号；同版本不能静默改写已有布局。
- class 按 base-first 组合，FieldId 在声明 Schema 段内编号。派生 Schema 绑定 exact base；
  祖先 exact 依赖改变时，受影响的派生版本也必须递增。
- inline struct 也是 exact 布局依赖，其版本变化沿 inline/base 依赖传播到 owner。
  引用成员使用稳定 nominal 类型约束，不因引用目标升版而递归升版整个引用图。
- 引用对象头表达实际 exact 类型/Schema。TypeCodec 表达受支持类型经数组或泛型构造的组合；
  类型表达能力与是否存在相应 codec 是两个条件，可表达不等于可读写任意 CLR 类型。
- SG 已知字段/元素类型时直接绑定字节原语或静态值 body，不为每个已知槽位增加 Type 查表、
  委托或虚调用。运行时开放组合的绑定接缝不能反过来支配静态成员的生成形状。
- DynamicMethod 是可评估的工具，不是已经选择全面翻新旧 IL 后端的承诺；若有多个生成后端，
  它们必须共享 Schema 解释，不能各自发明持久语义。

设计来源：[DB-018](design-branches/0018-generated-graph-codec-shape.md)、
[DB-019](design-branches/0019-schema-ancestry-implementation-slice.md)、
[DB-020](design-branches/0020-typed-slot-array-binding-slice.md)。

### 对象表示与存储职责

- MVP 固定 ReadAmplificationBaseBudgetPolicy。整数 X 倍通过对象级冷读放大产生 Base 动机，
  整数 Y% 控制可选 Base 的软预算；它不是所有写入的硬峰值保证。
  精确比较、预算和强制/可选分类以 [DB-015](design-branches/0015-statestore-object-representation-policy.md)
  与对应代码为准，后续执行层不能自行改变策略语义。没有合法 Delta 的更新显式强制 Base，
  不伪造 Delta 估算；它与 Insert 一样属于必需写入，不消耗可选 Base 预算。
- 策略消费完整保存后 live 集合的估算，产生稀疏表示计划。真实 parent、对象变化分类、
  reachability 和 Removes 由保存调用方提供，策略不能证明这些输入完整。
- Storage 使用多历史 Segment 地址与 BackwardFileDistance；rollover 是 soft threshold，
  不因此强制冷对象 Base。对象内容 Delta 与 ObjectHeadMap 的 membership Delta 是不同层次。
- Storage 不解释 CLR 字段、Schema 升级或可达性。Append 产生 candidate address，
  外层拥有最终发布 head；raw body 读取不等价于类型或完整图验证。
- 对象 Delta 显式引用同 ObjectId 的 prior record，并与其 containing Revision 的 exact Parent 当前 head 对照。
  新 Base 截断对象内容重建链；这不等于截断 membership 读取、历史查询或允许删除旧文件。
- B/D/H 采用统一对象 payload 口径，包含对象独有 kind/prior/length/body，未来类型头亦应计入；
  排除 ObjectId key、共享 membership/Frame/对齐。H 从原 Frame 实编码累计，是成本代理而非总物理 I/O。

地址方向来源：[DB-014](design-branches/0014-multi-segment-backward-file-distance.md)；
内容边界来源：[DB-026](design-branches/0026-raw-base-object-content-slice.md)、
[DB-028](design-branches/0028-persisted-object-delta-chain-slice.md)。

## 3. 保留的长期产品目标

本节保留整体产品定位。它们不是本轮实现清单；机制、格式和故障承诺仍需各自切片验证。

### 长期 Schema 可读与显式演化

盘上对象版本应能找到 exact Schema 事实，不能依赖当前程序集恰好还理解旧字节。
持久 Schema 需要规范表示与一致性校验；当前运行时 GetHashCode 不能充当持久 SchemaHash。
规范表示不能依赖反射顺序、metadata token、MVID、AssemblyVersion 或进程随机 hash。

未知版本、相同身份/版本却不一致的 Schema、缺失升级器、损坏引用或来源不匹配时，
应明确拒绝，不猜测并不回退到 latest。升级由显式类型知识和函数承担，不自动推断业务迁移。
读取升级本身不隐式写回 Store；升级失败不修改权威状态。历史数据成为可编辑 current 状态、
比较基线及后续重写的具体衔接，由专门切片收敛。

### 可达对象图与独立对象版本

每次保存有显式 roots，可达对象构成本次 live 集合。对象内容变化只产生其自身的新版本；
父对象的引用身份没有变化时，不应仅因子对象变化而制造业务差异。
不再可达的对象退出新视图，不能因此改写历史 Revision；编号复用仍按各自 Revision 解释。

比较忽略 transient，引用按身份比较，Artifact 引用按 exact address 比较；值和集合的 durable
equality 必须明确，不能仅凭非密码学 hash 判相等。同版 DTO 的 Half/float/double 持久状态比较采用按位相等：
相同 NaN 位无变化，不同 NaN payload 和正负零保留为变化；这不替领域对象定义业务 Equals。

### 四类 Store 的逻辑职责

| Store | 长期职责 | 关键边界 |
|---|---|---|
| SchemaStore | 保存版本化 Schema 事实 | 对象能精确绑定其解释；CLR 名称不代替持久身份 |
| StateStore | 保存对象版本及每个 Revision 的 live 绑定 | 追加新事实表达逻辑变化；历史视图不被静默覆写 |
| ArtifactStore | 保存不可变、可寻址的历史内容或大对象 | State 持 exact 引用，按需加载，不使完整历史常驻领域图 |
| DerivedStore | 缓存可以从权威输入和 recipe 重建的结果 | 可整库删除；不能反向成为 Schema/State/Artifact authority |

Artifact 面向 HistoryLog、LLM 消息、附件与历史输入输出等内容，允许只读 View/Query。
Artifact 引用不把完整内容并入 State 对象可达闭包，恢复时不允许模糊 latest fallback。
地址采用内容 hash、append 地址或其他组合，属于待裁决机制。

Derived 面向 Recap、DynamicMemory、embedding、搜索索引等昂贵派生内容。命中必须绑定精确
State/Artifact/Schema 输入及 recipe/builder 版本；围栏不匹配应为 miss/stale，不能通过全库
扫描挑选“最新结果”冒充本次派生状态。上述职责不要求立即拆出四个程序集。

### 单一发布权威与明确故障结果

长期目标是让 Schema、State、Artifact 的共同引用有一个可裁决的发布点，而不是各自发布
无法协调的 head；Derived 不充当权威提交的参与者。CommitManifest 是候选表达形状，
不预先冻结字段表或原子发布实现。

只有已发布的 exact head/manifest 引用的 candidate 才取得权威。文件存在、时间戳或最新编号
都不够。被引用数据应先完成规定的 durability barrier，再发布；具体 process/OS crash、
power loss、torn write 和目录元数据保证必须用所选底层与故障注入说明。

publication 结果不明确时，不能假装确定失败并透明重试，应能够 reopen/reconcile 精确裁决。
发布成功后若内存 cache 安装失败，应从权威状态重建。物理 GC、保留策略和 orphan 回收不能
从“append-only”一句话推导出来，也不由当前内容 append 能力证明。

### 恢复、Transient 与宿主边界

完整加载的目标是验证 exact 输入、解码和升级、处理共享/循环、验证领域图、重建 transient，
全部成功后才向应用暴露 roots；具体分配、填充和 hook 顺序不是本文冻结的 API。

Transient 指索引、缓存、反向查找等非持久内存状态。其重建应便宜、确定、幂等，不依赖网络、
LLM 或外部文件副作用，不写权威 Store；失败不能交付半重建图。
昂贵的 LLM 摘要或 embedding 属于 Derived builder，不能混进同步 transient 恢复。

初期按单 Repository 单 writer、捕获期间领域图静止的模型探索，由宿主协调 mutation 与 Capture。
DTO 冻结边界不自动保证并发 Capture 的一致性。commit 决定哪个候选成为持久权威，
不承诺撤销已经执行的任意 C# 领域操作。

## 4. 成功标准与依据

可用产品应能用普通 C# 修改领域图，在无 ChangeTracker 的条件下保存真实差异，
跨进程保持共享/循环及规定的身份语义，按 exact Schema 显式升级历史数据；
Artifact 可精确引用并 lazy load，Derived 可删除重建，故障后只有一个可裁决权威状态。
这些是整体成功目标，不能因为某个局部 codec 或 Probe 已通过就宣称全部达成。

旧 StateJournal、SessionJournal 和 DramaBoard 分别提供图持久化、权威/派生分层和实际领域压力。
复用其可验证机制，不继承专用字典 DSL、ChangeTracker、旧 API 或格式兼容负担。
活跃证据与历史实验的入口见 [设计索引](design-branches/README.md)；
本文只在目标或长期设计决定改变时更新，实施进度不追加到这里。
