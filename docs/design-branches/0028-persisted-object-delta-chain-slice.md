# DB-028：持久对象 Delta、exact prior 与原始重建链

> 状态：Proposed / 待用户评审，尚未实施。
> 日期：2026-09-06；规划源码基线 `0201c9a`，工作区起点干净。
> 本轮授权为规划。本文是推荐的下一工作分片，不是已完成能力或自动施工授权。

## 1. 问题与最小成功判据

将 DB-027 产生的真实 Delta bytes 保存下来，丢弃原领域对象、DTO 和临时 payload 后，
能否从指定 Revision 找到正确的 Base + Delta 链，交给已生成的 codec 重建同一份状态，
并从实际记录取得策略将来需要的累计对象重建成本 H？

最小成功判据：跨 Segment 追加 Base → Delta → Delta，关闭并重新打开文件；
以 exact Revision/ObjectId 读取链，用 SG Read/Apply 重建 DTO，其完整 Base bytes
与捕获时的独立期望一致。错误 prior 必须在链来源检查中拒绝，而非依赖 Apply 碰巧报错。
追加新的 Base 后，该对象内容重建从新 Base 开始，H 同时重置。

本片完成的是 **原始对象版本链 + 真实 typed 消费见证**，不是通用图 Load、完整 Save 或发布协议。

## 2. 证据与候选比较

当前源码事实：

- [生成器](../../src/DurableGraph.Generator/DurableSchemaGenerator.BinaryBody.cs)已有各历史 Vn 的
  PrepareDelta/ApplyDelta；[PreparedDelta](../../src/DurableGraph.StateStore.Serialization/Serialization/PreparedDelta.cs)
  拥有可重复使用的 bytes，但不证明 prior 身份。
- [StateRevision](../../src/DurableGraph.StateStore.Storage/StateRevision.cs) 的 local 内容只有 Base；
  ObjectHeadMap Base/Delta 已独立成立，真实对象 Delta 仍留有 TODO。
- [StateRevisionStore](../../src/DurableGraph.StateStore.Storage/StateRevisionStore.cs) 已能按 exact Revision
  找到指定 ID 的 containing Frame，并要求该 Frame 有真正的 local record，不从其 parent 补内容。
- [materializer](../../src/DurableGraph.StateStore.Storage/LiveObjectHeadMapMaterializer.cs) 校验地址严格向前，
  但完整 head map 的 external heads 仍是浅声明，不验证整段身份历史。
- [策略输入](../../src/DurableGraph.StateStore/ObjectSaveEstimate.cs)需要 H；
  [策略](../../src/DurableGraph.StateStore/ReadAmplificationBaseBudgetPolicy.cs)尚不读取内容或执行计划。

| 候选 | 收益和代价 | 建议 |
|---|---|---|
| 持久 Delta/prior/H | 消费刚完成的真实 codec，补上保存策略前最直接的存储缺口 | 本片 |
| 同时接对象列表比较、policy、Save | 收益更完整，但还要处理跨 Schema Update、Parent 基线和发布 | 后续独立切片 |
| 先 TypeCodec/SchemaStore/roots 目录 | 解决持久解释权威，但会混合 Schema 格式与目录设计 | 保留下一候选；本片 typed 见证显式提供解释元数据 |
| struct、一般引用、Restore | 都有独立价值，但不会解决已有 Delta 无处落盘的问题 | 继续保留，不设为本片前置 |

主代理与独立设计 subagent 讨论后推荐此范围；事实调查另由只读 subagent 核对。

## 3. 推荐合同

### 3.1 同 Frame 的两种对象内容

local record 统一表达 `ObjectId + Base/Delta kind + 可选 prior + owned body`。
Base 有完整 body，没有重建 prior；Delta 有真实差异 body 与 required prior FrameAddress。
prior 的 ObjectId 隐含为本条记录的 ObjectId，因此 `(prior frame, ObjectId)` 唯一定位前一条内容。
不增加 incarnation 字段、仅含 ID 的 Delta 占位、任意 SchemaToken 或通用 codec registry。

StateRevision 的本地集合按 ObjectId 唯一、升序、冻结；local IDs 从全部 records 派生。
同一个 ID 不能同时出现在 Base/Delta、external heads 或本帧 Removes 中。
ObjectHeadMap Base/Delta 与对象 Base/Delta 两个维度保持独立：完整 map 也可以含 local Delta。
只要有 local Delta，Revision 就必须有 Parent，不论 map kind 是什么。

API 名称施工时按最小 diff 收敛，推荐统一 ObjectVersionRecord 与 LocalObjects，
不保留无消费者的旧形状兼容层。ReadObjectBase 保留“head 必须为 Base”的含义；
不能悄悄把它改成返回 Delta bytes，或返回链底部而非当前对象的完整状态。

### 3.2 prior 的来源检查

| 方案 | 取舍 |
|---|---|
| 只保存 Revision Parent，读取时推导对象 prior | 少写一个地址，但无法检查调用方准备 Delta 时声明的 prior 是否已经过时 |
| 显式保存对象 prior，并与 Parent 当前 head 对照 | 多写一个 locator，换取明确的内容依赖和 stale-prior 拒绝；本片推荐 |
| 显式 prior 只检查“更早同 ID” | 验证便宜，但允许接错分支或旧占用者，拒绝此方案 |

对 containing Revision R 内每条 Delta `(id, prior)`：

1. R 的 exact Parent P 必须存在，且地址严格早于 R。
2. 物化 P 的 live map，id 必须存活，且 `P.head[id] == prior`。
3. prior 地址必须严格早于 R；prior Frame 必须有 id 的 **local** record。
4. 若该 record 仍是 Delta，沿其自己的 Parent/prior 重复检查，直到 Base。

因此 prior 不是“任意更早且恰好同 ID”的记录，也不是“前一帧”。
连续若干 Revision 不改该对象时，prior 可以跳过它们，指向 Parent 仍在使用的内容 head。
不需要线性搜索所有历史来猜来源；地址包含完整 ticket，不能只比较 offset 或文件号。

Append 在取得 writer/builder 前，对本次所有 local Delta 做直接 Parent/head/local-record 预检。
它只验证直接 prior edge，不遍历验证整条祖先对象链，也不解码 body；
它不证明调用者确实用该 prior DTO 计算了 payload。
链读取仍逐边检查，不能依赖“数据一定经过本进程 Append”；损坏输入测试应能绕过正常 Append 构造。
Read/ReadLiveObjectHeads 保留格式读取/浅 membership 的边界，不升级成全图或全历史验证。

删除后再插入的 id，在 Parent 中缺席，因此只能从 Base 开始；新 Base 后续 Delta 只能接新 head。
跨 Schema 的 typed Delta 也不受本片支持；typed 消费者必须拒绝解释不一致的链，显式完整 Base 可开启新链。
完整 Save 如何表达必须 Base 的 Update，留待其分类合同，不能伪装 Insert 或极大 D 欺骗 policy。

**校验边界：** 当前 ObjectHeadMap Base 可以直接声明 external heads。本片不新增“每个 external
head 必须沿 Parent 连续继承”的全历史校验。若调用者主动把旧记录重新放回完整 map，
后续 Delta 与这个 Parent 声明一致并不足以识别领域身份误用。
本片证明相对于 exact Parent 声明的连续性，不宣称证明裸 ID 的全局实体身份。
将来完整 Save 必须约束 candidate 的身份与 head 来源；不能把这个前置条件遗漏掉。

### 3.3 原始重建链与 typed 消费者

推荐新增 `ReadObjectVersionChain(revisionHead, objectId)`，返回拥有自有内容的不可变结果：
目标 ID/head、从 Base 到最新 Delta 的有序 records、每条 exact locator 与累计 H。
只有全部结构与来源检查完成后才交付结果；不暴露部分成功列表或存活 RBF lease。
使用迭代遍历；严格向前检查拒绝 self/forward/cycle。无需先引入全局 cache 或递归深度框架。

Storage 不调用 SG、不解释 CLR/Schema，也不接收“任意 bytes → bytes”的通用 patch callback。
首个消费者放在现有 Generator 集成测试：

1. 用 fixture 显式保存的 **每条 exact locator → kind/exact Schema/codec 合同** 元数据预检整条链。
2. 静态调用正确 Vn 的 Read，再按顺序 Apply；每条 body 都检查 EnsureFullyConsumed。
3. 重建 string 表后，对目标 Revision 的完整 DTO 验证引用，包含从 prior 继承的未改槽。
4. 所有校验成功才返回结果；再次读取旧 Revision 仍得到其自己的状态与引用视图。

这延续 [raw Base typed 见证](../../tests/DurableGraph.Tests/RawBaseStorageGeneratorTests.cs) 的明确边界：
类型与 roots 元数据并未持久化，不能宣称文件已经自描述、Schema 已认证或可直接恢复领域图。
同 Schema 的错误 prior 内容仍可能被 Apply 接受；链地址校验不验证 payload 的生成过程。
不为填补这一边界添加测试专用的产品 Schema ID 注册器。

### 3.4 wire 与 H 的计数

推荐将当前原型 wire v2 替换为 v3，旧版本明确拒绝，不写兼容读取器。
共享 Revision/map 部分沿用现有结构；local entries 改为：

```text
ObjectId
representation kind
仅 Delta：prior（以本记录所在 Frame 的 FileScope 编码）
body length + body
```

保留 canonical varint、排序/数量/剩余长度约束与完整消费检查；kind 的具体常量和独立 golden
在施工时一起确定。外层 v3 固定本轮记录布局，不能将来无版本变动地替换其解释。
它仍是 opaque body 存储格式，不替代未来 TypeCodec 与 exact Schema 的持久绑定。

对象 payload 成本明确为 `kind + 可选 prior locator + body length + body` 的实际编码字节数。
ObjectId 归 local key/membership；集合数量、Parent、map、RBF、对齐与文件开销不计。
当前不存在的类型头不虚计；以后新增对象独有类型头时必须一致补入 B/D/H。

读 wire 时从 reader 消耗位置取得每条 payload 长度，避免另外实现 varint 估算算法。
H 用 checked long 累加从 Base 起的这些成本，不持久化第二份 H 权威：

```text
H(Base) = 该 Base 对象 payload 字节
H(Delta) = H(prior) + 该 Delta 对象 payload 字节
```

每条旧记录采用它自己的 Frame scope 与真实存储长度，不用最新 Segment 的距离重算历史成本。
PreparedDelta.Payload.Length 只是裸 body 的 D；未来策略接入时还须包含上述对象 envelope。
本片提供现有链 H，不承诺在 rollover 的目标 Frame 未定时就有精确候选 B/D，也不改 policy 参数或公式。

来源检查还会读取 Parent membership、共享 Frame 等。H 是既定对象 payload 口径的成本代理，
不是总冷读字节、I/O 次数或延迟；不能据此声称读放大已经被硬性限制。
Base 截断的是对象内容链：它不沿 prior 追溯更旧内容，但定位目标 head 所需的 map 链仍可能较长。
现有 Frame 读取会解析其全部 local records，因此读取 membership 仍可能顺带读入/复制旧 body。
物理 GC、历史 lineage 查询及移除文件的安全性不由这条截断性质保证。

## 4. 验收与施工分工

| 组 | 最小可观察结果 |
|---|---|
| 模型与格式 | 两种 map × 两种对象表示；重复/冲突/空 prior 拒绝；owned bytes；v3 独立 golden，未知版本/kind、每个截断前缀、非 canonical、尾随输入拒绝 |
| exact prior | 跳过未修改 Revision 的合法 prior；同 ID 的过时 head、其他分支 head、缺失 local record、self/forward 地址拒绝；Apply 不作为来源检测器 |
| ID 占用边界 | 移除后的 Delta 插入拒绝；Base 再插入成功；其 Delta 接新链，接旧占用者拒绝；旧 Revision 保持旧内容；另明确完整 map 重新指定旧 head 的浅声明边界 |
| Base 截断 | Base → Delta → Base → Delta 只返回最近 Base 起的链；不把更旧 record 作为内容重建依赖、不沿其 prior 追溯，H 重置；不混淆 map 读取或 GC |
| H | 独立实编码字节计数与链累计一致，含变长 prior 距离/长度边界及跨 Segment；long 累加 checked，不用最新 scope 重算旧记录 |
| typed 冷重开 | 真 Capture + Prepare 生成至少两次 Delta；清除原 bodies、DTO/领域引用后 reopen；Base Read + Apply 的输出 bytes 与独立期望一致 |
| 类型与引用 | fixture exact Schema/codec 不匹配在 Apply 前拒绝；历史 Vn；当前视图完整 string 引用校验，含未改槽的 missing/wrong-kind；失败不交付半成品 |

建议依赖顺序：

1. 主代理固定记录/prior/H 合同、错误边界和小型实施账本；先跑代码基线验证。
2. 子任务 A：record model + wire v3 + 格式/所有权测试。
3. A 的 API 稳定后，子任务 B：Append 预检、链读取、H 和真实文件测试。
4. 并行子任务 C：实际 SG typed 集成见证，复用现有测试编译工具，不把领域恢复搬进 Storage。
5. 独立审查检查 prior 身份反例、map/content 两种依赖和 H 口径；主代理整合 actual diff。
6. 根 solution build、Storage/Generator 相关 tests；若包交付依赖或生成 API 改变，追加真实 PackageConsumerProbe。

产品改动预期主要在 Storage；SG/Serialization/policy 原则上直接复用。
本片不新增程序集，不顺带统一 namespace，不改底层 RBF，也不引入 pool/指纹/通用类型 registry。

## 5. 后续与本轮规划证据

完成后可选：对象列表比较与计划执行，或先持久类型头/目录。
前者仍需处理 exact Parent 基线、不可 Delta 的 Update、B/D 的目标 scope 与失败安装；
后者处理本片显式留在 fixture 的解释权威。完整发布/恢复必须另定故障模型。
struct、一般引用、数组/BCL、Restore、ID 回收继续按[路线图](../DurableGraph-research-roadmap.md)保留。

本轮只读源码与既有测试并讨论，未运行新的产品 build/tests，不把 DB-027 的 570/570 当成本轮重验结果。
独立审查收紧了 Append 仅检查直接 edge，以及 Base 截断不保证免读同 Frame 旧字节的表述；
主代理已核对 integrated diff、98 个本地文件链接及新增 §3 链接锚点，状态均为 Proposed。
实施后在本文补实际证据，不覆盖规划时事实。
