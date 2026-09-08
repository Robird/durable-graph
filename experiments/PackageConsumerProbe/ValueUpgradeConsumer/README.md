# Composable value Upgrade package consumer

DB-039 的真实 `PackageReference` 消费者，由
[Run-ValueUpgradeProbe.ps1](../Run-ValueUpgradeProbe.ps1) 顺序构建并执行三个版本。
项目不引用源码项目，不手工导入 analyzer、build targets 或历史文件。

```powershell
pwsh -NoProfile -File experiments/PackageConsumerProbe/Run-ValueUpgradeProbe.ps1
```

已有匹配本地依赖包时，同时传入 `-PackageSource` 和 `-Version`。
运行产物位于忽略目录 `obj/value-upgrade-*`，不覆盖旧结果。

| 构建 | 观察条件 |
|---|---|
| V1 | 两个 `Box<int>`、`Box<Point>`、`Box<Pair<Point>>` 与 World 中的 `Pair<LegacyPoint>`；首次 Base、泛型对象 Delta、冷重开和 exact DTO 读取 |
| V2 缺 Point 规则 | exact DTO 读取仍可用；声明的值依赖缺失使相应 owner 在首次业务 callback 前拒绝；runner 核对仓库文件集合与 SHA256 不变 |
| V2 | 一个开放 Box owner 复用 KeepExact、Point 叶子和开放 Pair 组合；没有闭合 `Box<Point>` owner；嵌套值回调保留各对象的完整 owner Schema 端点，局部同名 key 不串 scope |
| V3 | 排除 `LegacyPoint.cs` 后保留其 Family DTO 值规则；从 V1 执行两条 owner 边与显式嵌套转换，从 V2 执行一边；升级强制 Base，续写 NoChange/Delta，当前 head 冷读不再 Upgrade |

Point 转换被 `Box<Point>` 与 `Box<Pair<Point>>` 复用；Pair 的同一开放规则又组合 Point 和 LegacyPoint。
用户函数通过 Context 显式调用工具，规则登记不主动遍历或转换字段。
Box 和 Pair 故意同时采用局部 key `value`，其完整槽语义由各自的声明段与 FieldId 决定。
V3 删除的只有 inline 领域声明；独立 `Box<Point>` 行仍保留其 current CLR 闭合所需的 Point 类型。

runner 检查新增 history 格式 v4，数量依次为 5、10、10、12，并核对先前 `.dgschema` 的存在性与 SHA256。
三个版本通过磁盘 repository 交接，每次运行都是独立进程。
Box 还保存稳定的创建时间 ticks，各版本 owner Upgrade 透传它，历史与当前读取均检查其值。
这让单字段编辑的 Delta 相对于真实完整状态节省字节，而不依赖旧类型名称头的额外尺寸；
DB-045 表示 ID 头使用原生产策略，未增加 body padding 或放宽 Delta 验收。
更细的歧义、错误完整槽语义、依赖深度及异常不回退由产品 Runtime/SG 单元测试覆盖。

严格匹配的预期输出为：

```text
ValueSeed:True:OwnerDelta:True:NestedInline:True:ExactHistory:True
MissingValueRule:True:ExactDecodeAvailable:True:NoInvalidOwnerCallback:True
ValueUpgrade:True:OpenOwnerAndPair:True:ReusedLeaf:True:ScopedContexts:True:ForcedBaseThenDelta:True
ValueHistory:True:DeletedInlineDomain:True:RetainedValueRule:True:AdjacentContexts:True:StableResave:True
```
