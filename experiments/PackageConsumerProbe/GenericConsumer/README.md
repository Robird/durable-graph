# Generic history package consumer

DB-038 的真实 `PackageReference` 消费者；由
[Run-GenericProbe.ps1](../Run-GenericProbe.ps1) 顺序构建、执行三个版本。
项目不引用源码项目、不手工导入 analyzer/build targets/history 文件。

```powershell
pwsh -NoProfile -File experiments/PackageConsumerProbe/Run-GenericProbe.ps1
```

已有匹配的本地依赖包时，可同时传入 `-PackageSource` 和 `-Version`。
运行产物位于 PackageConsumerProbe 的忽略目录 `obj/generic-*`，无需覆盖或删除先前结果。

| 构建 | 观察条件 |
|---|---|
| V1 | `Box<int>`、`Box<Point>` 对象与 `Pair<LegacyPoint>` inline 值；首次 Base、单个泛型对象 Delta；冷重开与 exact DTO 解码 |
| V2 缺闭合转换 | 删除 `Box<Point>` 的业务 provider；exact 解码仍可用，editable Load 在该对象首次 callback 前拒绝；仓库文件集合与 bytes 不变 |
| V2 | 同定义统一升版；`Box<int>` 通用透传、`Box<Point>` 闭合业务转换、World 显式转换 inline 状态；强制 Base 后 NoChange 和普通 Delta |
| V3 | 编译中排除 `LegacyPoint.cs`；旧历史 DTO/显式 owner Upgrade 仍可工作；从 V1 执行两跳、从 V2 执行一跳；逐对象及逐边完整 Context Schema；再次保存稳定 |

runner 检查新增 history 使用 v3 格式，并逐阶段验证已有 `.dgschema` 的文件存在性和 SHA256 不变。
每代进程独立运行，通过磁盘 repository 传递状态。历史状态类型使用生成的独立 Family 宿主。

删除旧 inline 领域类型与删除独立对象族的恢复能力是不同边界：V3 删除 `LegacyPoint`，但保留独立
`Box<Point>` 行所需的 `Point` 与闭合模型。这个消费者不把“World 后来不引用某对象”视为跳过 source Normalize 的理由。
恶意中间 Schema、phantom 中间歧义和目录隔离由 DB-038 的产品 Runtime 测试补充；本消费者聚焦包交付与三代可执行历史。

2026-09-08 首次运行通过：八个 Release 依赖包、四次消费者构建与四个独立运行进程均成功，消费者构建零警告、零错误。
history 数量依次为 5、10、10、12，旧文件 SHA256 全部保持不变；缺闭合转换的运行保持 repository 文件集合及 bytes 不变。
四行严格匹配的结果为：

```text
GenericSeed:True:OwnerDelta:True:ClosedSchemas:True:InlineNoObjectIds:True
MissingClosedUpgrade:True:ExactDecodeAvailable:True:NoPointBusinessCallback:True
GenericUpgrade:True:ClosedBusiness:True:ForcedBase:True:NoChangeThenDelta:True:PerObjectContext:True
GenericThirdVersion:True:DeletedInlineDomain:True:StoredExactHistory:True:AdjacentContexts:True:StableResave:True
```
