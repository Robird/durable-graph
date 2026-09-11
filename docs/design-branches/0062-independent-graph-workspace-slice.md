# DB-062：独立图读取与可由外层发布的工作区

> 状态：Proposed，2026-09-11；本轮仅源码调查与设计，未实施。
> 消费者来源：[DramaBoard 需求稿](../../../drama-board/docs/research/event-journal-state-store-draft.md)。该稿中的其他路径按 DramaBoard 仓库解释。
> 后继：[DB-063 EventHistory 外观](0063-event-history-journal-slice.md)、[DB-064 合并读取](0064-shared-revision-decoding-design.md)。

## 1. 问题与最小成功标准

现有库能保存一个固定 World，但真实消费者需要独立保存 Event 的快照闭包和完整 State，
由外层 Journal 决定哪个保存结果已经发布。还需接纳返回新根的 immutable replacement 更新方式。

本片先解耦“准备/追加图”和“发布某个 head”，不引入事件业务协议。
最小见证：从 S0 的同一活动工作区保存一个独立快照 E1，再保存 S1；
E1 不推进或破坏 S0 比较基线，S1 正确产生相对 S0 的 Delta；E1 独立读取不 typed 解码无关的 World/Bob。
原 GraphRepository/GraphSession 的固定根外观与发布失败合同继续通过回归。

## 2. 当前实现证据

| 代码 | 事实及需要改变的接缝 |
|---|---|
| [WorldWorkspace](../../src/DurableGraph.StateStore/WorldWorkspace.cs) | 拥有固定 World、模型快照、Capture 与 NormalizedRevision；Load 把解码、升级、实例化和可写基线组装放在一起；Stage 校验固定根 ID |
| [CaptureSession](../../src/DurableGraph/CaptureSession.cs) | Accept 替换整个实例绑定集合；Discard 保留已消耗的 cursor，却不推进 Current。不能把 E Accept 到 State 的捕获会话 |
| [LoadedRevisionPlanner](../../src/DurableGraph.StateStore/LoadedRevisionPlanner.cs) | 支持 candidate 是 source 的不同可达子集，并保留完整 source provenance、Upgrade 强制 Base 义务 |
| [ObjectRevisionPlanner](../../src/DurableGraph.StateStore/ObjectRevisionPlanner.cs) | 有 Parent 时固定输出 map Delta 及完整差集 Removes；需要另一种目录输出方式，正文策略不用替换 |
| [StateRevision](../../src/DurableGraph.StateStore.Storage/StateRevision.cs) | 已允许有 Parent 的 map Base、local Delta 与 external heads；这种组合无需新 Storage 格式 |
| [RevisionDecoder](../../src/DurableGraph.StateStore/RevisionDecoder.cs) | 解码指定 Revision 全部 live 行；只要 E 的 membership 本身独立，既有路径即可避免解码无关 State 行 |
| [GraphRepository](../../src/DurableGraph.StateStore/GraphRepository.cs) / [PreparedWorldSave](../../src/DurableGraph.StateStore/PreparedWorldSave.cs) | 已有数据屏障、单次候选、发布后安装的核心顺序；专用 publication Parent 必须等于 Revision Parent 的校验仅适用旧外观 |

上述结论来自当前源码与测试阅读，不是本片已通过的执行结果。

## 3. 推荐保存拓扑：事件快照从活动 State 分出

Journal 的顺序由 DB-063 定义；这里仅决定比较基线：

```text
                    ┌── E1（仅事件闭包，map Base）
S0 ─────────────────┤
                    └── S1 ──┬── E2
                             └── S2
```

- E1 和 S1 的 StateRevision Parent 都为 S0；E2 和 S2 都为 S1。
- E 保存使用活动 State 的冻结 DTO 和实例-ID 绑定；未变且实际复用的对象可以指向已有 ObjectVersion。
- E 保存成功后释放临时候选，保留 State baseline、State 根和原 CLR 实例；不 Accept E。
- S 保存成功发布后才安装 S candidate、RootId、新根与 baseline；失败不能用当前 CLR 状态重新 Capture 代替原候选。
- 首个 S0 无 Parent；无已发布 State 时不允许 E。快照保存与 State 保存共用一个串行 cursor，失败可以烧号。

这里借用 State 的身份，不按内容把另建的 Snapshot 对象认成原领域实例。
如果 AliceSnapshot 是新对象，即使字段相同，也写自己的对象；它引用的现有 string/只读定义仍可复用。

### 独立快照目录

快照先完成同一套 typed Prepare 与 Base/Delta 策略，再选择 map Base：

1. 保留计划实际选出的 local records，包括策略为 NoChange 主动写出的 Base。
2. 对 candidate 中没有 local record 的 ID，从 exact Parent 取其 head，放入 external heads。
3. map Base 的 membership 恰好是 candidate IDs；不写整份 State 的 Removes。
4. 保留 Parent，用于 local Delta 的 prior 验证；不得把 Parent 改成 null 来伪装独立全量保存。

State 保存继续使用 map Delta。两种目录只改变 membership 的表达，
不改变 `ReadAmplificationBaseBudgetPolicy`、对象 body、Schema 目录或 Delta prior 规则。
首片可仍扫描/验证完整 State baseline；独立浏览的合同不要求保存侧 O(event closure)，
也不声称 map Base 免除所有历史 I/O。

### 已知成本与不承诺的连续性

- E-only 新实例不会留在 State 的长期绑定表；之后进入 State 可获得新 ID，后续 Event 也可能重写它。
- E 不与前 E 专门比较；跨事件而不在 State 的公共子图没有额外身份目录。
- 冷 Resume 仅从所选 S 导入编辑绑定与完整 source max+1 cursor。ObjectId 仍是 Revision-local；
  不要求与另一快照的数字区间互斥，也不增加仓库全局高水位或 ID 回收日志。
- 两份 Revision 中相同数字而不同 head 绝不能合并；跨视图地址与共享规则见 DB-064。
- Event 主动引用整个 World 时，整个 World 就属于其快照闭包。不会以字段名字猜测裁剪。

这些边界换来小而明确的会话所有权。若实际记录显示 E-only 内容大量重复，再增加可丢弃的共享身份索引；
不在第一片引入全局存活表、内容去重或跨 lane Delta。

## 4. 内部职责与外层 API 边界

先在现有 StateStore 程序集中形成可复用内部核心，不为独立发布提前开放通用 publisher 插件接口：

- 文件资源所有者：匹配的 SchemaStore/StateRevisionStore、严格只读/可写打开与故障状态；不拥有业务 head。
- 读取管线：明确 `(RevisionAddress, RootId)`，exact Decode → Normalize → 引用验证 → Allocate/Hydrate；
  可返回只供读取的领域图，也可把同一结果交给受控编辑工作区。只读加载不创建持久 publication 或 Capture 候选。
- 工作区：一份 State 根与对应 baseline、冻结模型目录、实例绑定、分配 cursor。只容许一个未决候选。
- 保存候选：区分“推进工作区”和“独立快照”。持有真实冻结图、准备内容及安装数据；追加成功尚不授权安装。
- 发布编排：旧 GraphRepository 使用 PublicationLog；DB-063 使用 EventJournal ref。二者分别拥有自己的外观，
  同一物理仓库不得同时打开这两个发布器。

内部可以重构 WorldWorkspace/PreparedWorldSave 的泛型所有权，不必保留偶然类形状。
旧 public GraphSession 的固定 World 约束仍由旧外观检查；新外观才能显式提交替换根。
不添加公开 `Accept(address)` 或允许调用方拼装“Parent + DTO + ID map”来冒充合法基线。

根替换先 Stage(nextState)，成功后更新工作区根；保留子实例继续沿用 ID，旧根不可达则退出新成员集。
同样的新根字段不意味着同一根身份。未知 actual runtime 类型拒绝，不退回静态基类 codec。
事件允许不同已登记 durable class 作为根；读取支持请求基类，先恢复 exact actual model，再校验可赋值性。
State 根是否必须 exact TState 由新外观统一限定；首片建议固定其 exact 类型、允许同类型根实例替换。
无通用 object/interface 字段、null 根、命名多根或 boxed value 扩张。

只读是 API 的使用合同，不是把普通 CLR 图自动变成不可变对象。
独立读取默认分别分配可变对象；跨图共享见 DB-064，不让只读入口自动加入可写工作区的实例表。

## 5. G0–G3 施工与验收

| 阶段 | 内容 | 必须可观察的结果 |
|---|---|---|
| G0 | 提取资源/读取/工作区职责，先保持旧外观 | 原保存同实例、RootId、Upgrade Base、失败/重开测试无退化；无第二份 DTO 编码实现 |
| G1 | 新根 Stage 与推进式安装 | 同类型新根+复用 child；只在发布后切换根；准备/追加/发布失败均保留旧已提交基线 |
| G2 | 从 State 工作区保存独立快照及 map Base | E membership 不含 World/Bob；unchanged child/string 真实 external head；changed child 可对 S 写 Delta；NoChange 主动 Base 不重复出现在 external |
| G3 | 独立读取和可写恢复组装 | 冷开后仅 Read(E) 时 World/Bob typed reader/Upgrade/Allocate/Hydrate 计数均为 0；图内共享/环正确；S1 Parent=S0 且 Bob 不丢 |

补充反例：先加载历史 S 并 Upgrade，再保存 E、S；升级且仍存活的对象两次都保持各自的强制 Base 义务，
E 成功不得清除 S 的 RequiresRewrite。测试 E 捕获错误、未知根、后续 State 捕获错误与候选重入。

实现后运行根 build、有关 StateStore/Runtime tests，并复跑 GraphRepository/LoadedWorld/发布故障回归。
新增外观跨包验证集中在 DB-063；G0 可由受控测试发布器见证内部接缝，不把假发布当真实 durability。

## 6. 备选与选择理由

| 备选 | 评价 |
|---|---|
| Journal 和 Revision 都线性 S→E→S | 单个 Capture.Accept 会忘掉 State-only bindings，完整差集制造大量 Remove；要保留多个基线/ID 表才能避免，未比推荐方案简单 |
| S→S、E→E 两条独立 lane | 正确且可做，State 增量很自然；但相同 CLR 只读内容首次在两 lane 各写 Base，常见 E/S 合并加载几乎无同 head 可共享。推荐直接采用从 State 分出的快照 |
| 每次 E 全新无 Parent 图 | 实现最少，但全部重新分配/写 Base，放弃现成 State 版本复用；可作对照测试，不作为主方案 |
| PairRoot 或永久把全部 Event 塞进 World | 违背独立读取和有限可达闭包的消费者目标，不作为默认模型 |
| 新的公共事务/任意多 root 框架 | 当前两种发布外观尚不需要；内部共用核心即可，避免把 provisional 接缝过早变成下游维护义务 |
