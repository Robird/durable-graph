# DB-025：string 对象内容解码与引用槽校验分片

> 状态：Open / 下一分片推荐，尚未实施或获实现裁决 — 2026-09-06。
>
> 本轮用户要求根据总体方向和实际进展规划。本文记录主代理与独立子代理收敛后的建议，
> 不把设计材料或前片实施授权当作本片已获裁决。

## 1. 当前缺口与选择理由

基线为 `327a425`；本轮只读核验源码，未重新运行历史测试。
[DB-024](0024-reference-capture-and-reusable-object-ids.md) 已实现 Capture/Seal、ID DTO、
string 独立内容条目与内存 Accept/Discard。`ReadVn` 仍只读 uint，没有解析或检查目标。

下一片推荐回答：**已冻结的 owner DTO 和 string 内容分别经过字节编码/解码后，
能否在一个加载视图中恢复正确的 string 引用关系，并拒绝无效引用？**

| 候选 | 当前收益 | 本片选择 |
|---|---|---|
| string 对象解码 + 引用槽校验 | 把刚完成的引用 Capture 接到读取侧，验证最小引用保真闭环 | 推荐 |
| current 领域 Restore | 可直接恢复业务对象，但同时要定构造、字段填充、Transient 和失败暴露规则 | 后续；技术上可做，并非被 readonly 字段卡住 |
| 自定义 struct | 扩大值布局，需 Schema/history 表达 exact inline 依赖 | 保留 DB-024 §8 TODO，非本片前置 |
| raw Base ObjectVersion 存取 | 让 Storage 开始保存真实内容 | 可独立做；按当前 codec-first 方向排在本片后复评 |

本片产物是可复用的 string 解码表与 SG 校验，而完整异构图编码/加载的组织先留在 typed 见证中。
不以增加类型数量替代引用语义闭环，也不为了一个见证建立完整 runtime codec registry。

## 2. 目标数据流

```text
稳定领域 roots
  -> 现有 Capture/Seal
  -> 冻结候选：owner 的 ID DTO + string 内容条目
  -> typed 消费者分别写 owner body / string body
  -> 独立字节输入（加载端不持有源图实例）
  -> 各版 ReadVn + 当前加载视图的 StringReadTable
  -> SG ValidateStringReferences
  -> typed 消费者按 ID 取 string，见证引用关系
```

DTO 中 string 槽继续保存 uint。**不另生成含 CLR string 的第二套 Versioned DTO。**
解析出的实例属于加载表；相同表内相同 ID 返回同一实例，不同 ID 不按内容合并。
单独读取 owner DTO 的成功不等于引用已经通过校验。

此处“引用恢复”仅指 string 实例与引用槽的关联；不返回 Character/Item 等领域根，
也不把内存 body bundle 称为文件格式或 StateRevision。

## 3. 最小职责与接口草图

### 3.1 Serialization：继续拥有 string 内容格式

复用 `StringPayloadCodec` 的现有 canonical 格式：严格 UTF-8 仅在更短时使用，否则 UTF-16LE；
平局使用 UTF-16LE，保留孤立代理项。非 null 空串内容为 `00`。
string 对象条目不需要 nullable header，null 已由引用号 0 表达。

推荐只公开现有 `BinaryPayloadWriter.WriteString` 与 `BinaryPayloadReader.ReadString`，
不公开整个内部 codec，也不为了打通调用而扩大友元列表。它们依然是内容 API，
不单独保证“每次调用返回不同实例”；对象身份保证由上层加载表承担。
每个独立 body 的消费者调用 `EnsureFullyConsumed`，拒绝尾随内容。

### 3.2 DurableGraph Runtime：视图局部的 string 解码表

概念接缝如下，命名/签名待实施前审定，不承诺已经存在：

```csharp
// 每条输入均是一个非 null string 对象的独立 body。
StringReadTable.Decode(IEnumerable<(uint Id, ReadOnlyMemory<byte> Body)> records);
string? table.ResolveString(uint id);
```

- Decode 同步消费本次输入；调用方在此期间保持输入稳定。逐条解码，一次成功后才返回表。
- 表不保留输入 byte buffer，不提供增删绑定、替换实例或可写字典入口。
- 输入 ID 必须非零且唯一；即使两个重复 ID 的内容一致也拒绝。
- 同一 ID 只解码一次；ResolveString(0) 返回 null；其他 ID 必须在本表中，否则拒绝。
- 需验证不同 ID 不被解码/分配机制合并到同一 CLR 实例；不能按 string 内容建身份缓存。
- 任意解码、ID 检查或分配失败，不返回半张表；不触碰 CaptureSession 的 parent/候选。
- 每次 Decode 建立独立解析上下文，不使用进程全局实例表，不跨视图按裸 ID 复用解析结果。
  本片不要求两个不同视图之间所有 CLR 实例互不相等；保证范围是各视图内部的引用拓扑。

表只证明 string 目标集合。对指向 Durable 对象的 ID 与根本不存在的 ID，
统一报告“不是本视图可解析的 string 引用”即可，不必为细分报错新增完整 ID/kind 目录。
**完整异构对象目录的唯一性、root 的合法性由 typed 见证预检，不是本 API 的保证。**
例如 Durable 与 string 两条记录占用同一 ID，需要外层完整目录拒绝；不能声称 string-only 表能发现它。
这里不创建两份可独立变化的完整 live map/string map，也不提前命名通用 GraphLoadContext。

讨论过的较宽方案是让工厂另接收 non-string 对象 ID，并立即合并成一份 ID→kind/实例表，
从而在 Runtime 直接拒绝跨 kind 重号。这并不必然造成双重权威，但增加了本片公开输入合同。
当前推荐先保持 string-only；当完整对象加载成为实际消费者时，再把目录校验提升到产品层。

### 3.3 Source Generator：依 exact 版本布局校验引用槽

推荐在现有 internal helper 为各 Vn 生成类似方法：

```csharp
internal static void ValidateStringReferences(in V1 state, StringReadTable table) {
    // 仅按 String schema 成员生成；同为 uint 的普通数值字段不能拿来解析。
    _ = table.ResolveString(state.Segment0Field1);
}
```

使用现有完整 exact 祖先字段布局，顺序与 body 一致；历史已删除 string 字段同样检查。
已知成员仍由 SG 静态选择，标量 Read/Write 不增加 Type 查表或 ValueSlotCodec 调用。
引用解析本身需要按 ObjectId 查表，这与按 CLR Type 寻找字段 codec 是不同职责。

历史/当前 DTO 不因解析而改写，当前领域 Capture 和历史 ReadVn 的边界保持。
本片不生成历史领域实例，不拼接 legacy boxed Upgrade，也不引入 DTO 升级。

### 3.4 typed 见证：承认尚无通用对象分派

真实 generator test 和 PackageConsumerProbe 显式配对 owner 的 exact Schema、Vn、Read/Write，
检查完整对象目录的 ID 唯一性后构建 string 表，再验证全部 owner 的引用槽。
整个过程成功才返回见证结果，不能先对外暴露解析了一半的对象列表。

输入仅携带冻结 DTO 生成的 bytes、string bodies、roots 与显式 metadata；
解码端不能读取源 `CapturedGraph.StringContent` 或复用原 string 实例充当恢复成功。
该 bundle 保留为测试/消费者组织，不进入产品长期格式。
`CapturedObjectKind` 仍是内存 tag，Schema TypeTag 仍是 schema 字段分类，均不得冒充 TypeCodec 编号。
以后从持久对象头自动选 codec 时，再设计 exact Schema 绑定与 TypeCodec。

## 4. 首个检查点：独立空串分配

源码事实：`StringPayloadCodec.ReadUtf16Le` 在空 payload 时直接返回 string.Empty。
现有测试证明空内容与 Capture 的身份登记，但没有证明两个独立空串对象的恢复。
DB-018 记载过私有分配入口的探索证据，那不是已选产品实现。

先做 .NET 10 可复跑的小见证：构造两个 ReferenceEquals 为 false 的空串输入，
编码为不同 ID 的相同 body，再验证恢复策略能得到两个独立实例；非空 UTF-8/UTF-16 路径也作身份对照。
见证必须记录实际使用的分配入口及其运行时依赖，不能用“未调用 Intern”代替身份断言。

推荐保留既定引用保真语义，优先寻找可接受的独立分配机制。若只有 private runtime hook 可用，
应单列其维护代价与明确拒绝多空串 ID 的限制方案，交由用户裁决；不能默默选择任一项。
若暂时做不到，记录具体阻碍，不把弱化输入合同包装成完整通过。

## 5. 工作包与验收

| 工作包 | 产物 | 依赖/完成证据 |
|---|---|---|
| A：空串机制见证与边界复核 | 分配事实、public API 最终提案 | 独立空串与非空身份对照；重要机制分支先裁决 |
| B：string 内容入口与解码表 | Serialization 窄入口、Runtime string-only 表 | A；canonical bytes、唯一 ID、完整消费、失败不暴露 |
| C：生成式引用校验 | 各 Vn 的静态 string 槽校验 | B 接缝明确后；真实 SG 编译执行、历史 exact 布局 |
| D：集成与独立审查 | typed body round-trip、单包消费、文档 | B/C；根 build、相关 tests、真实包消费者 |

B/C 可在接缝定下来后按文件所有权分派；主代理负责集成，另一代理审身份/边界断言。
不并行运行会争用同一输出目录的 build/pack。

核心验收包括：

1. 同实例多引用、相等内容不同实例、null、空串、孤立代理项；分别检查内容和引用关系。
2. 在 Seal 后修改领域引用，编码仍使用已封闭 DTO/string 条目；解码不接触源实例。
3. 对象记录顺序改变不改变解析结果；多个 owner 共享目标只分配一次。
4. 零 ID string 条目、重复 ID、损坏/非 canonical/截断/尾随 body 均拒绝。
5. 非零 missing ID 和指向 Durable root 的 ID 均拒绝；数值 uint 字段不被误认为引用。
6. exact Schema 不匹配由 typed 外层预检拒绝；历史 String 和旧 CLR 祖先消失后仍正确校验引用槽。
   覆盖合法升版的 String→UInt32：物理槽同为 uint，但只有旧版 String 槽需要解析。
7. 独立两次 Decode 不串用 ID 上下文；失败不改 CaptureSession，未通过完整见证时不返回结果。
8. 真实单一 PackageReference 可访问必要 Runtime/字节入口并运行生成代码，不靠测试友元打通。

## 6. 本片之后

不包含领域 Restore、Durable 互引/循环、数组/BCL、struct、DTO upgrade、ID 回收、
重新导入 CaptureSession、ObjectVersion 持久内容或 Save/发布/recovery。

完成后优先复评 raw Base ObjectVersion 内容接入与 current 领域 Restore 的顺序：
前者开始连接 Storage，后者闭合业务对象读取；当时依据实际接缝决定，本文不把后续路线冻结。
struct 的嵌套 DTO 与 inline exact Schema 传播继续保留为独立分片。
