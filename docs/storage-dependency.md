# atelia-storage 依赖工作流

五个存储包来自 [atelia-storage](https://github.com/Atelia-org/atelia-storage)。
[eng/StorageDependency.props](../eng/StorageDependency.props) 是本仓选择版本 S、来源 commit 和仓地址的唯一位置。
它与 DurableGraph 自身的包版本 G 独立。日常构建使用 PackageReference，不会探测旁边的目录后自动改用源码。

本次固定版的[用法入口](https://github.com/Atelia-org/atelia-storage/blob/09d979941d2c671a1e7a8ffabfa6e2b340e00f69/README.md)在首次 push 后可访问；当前可读 Prepare 检出的 `.artifacts/storage-source/<StorageSourceRevision>/README.md`。升级 pin 时同时更新此链接。

`StorageSourceRevision` 必须是对应包来源的完整 40 位 commit。集成或升级时同时填写版本和 revision，
再以正常 Prepare 完成固定来源验收。已有候选包或源码构建通过不能替代此项验收。

## 准备固定版本并构建

在仓根执行：

```powershell
./eng/Prepare-Storage.ps1
dotnet build DurableGraph.slnx -t:Rebuild
dotnet test DurableGraph.slnx --no-build
```

公开远端尚未可取时，第一次可以明确提供本地源码取得位置：

```powershell
./eng/Prepare-Storage.ps1 -SourceRepository E:/repos/Atelia-org/atelia-storage
```

Prepare 使用 pin 中的完整 commit，在 `.artifacts/storage-source/` 建立自己的干净检出。
它不修改用户指定的源码仓，也不把该仓的未提交修改或当前 HEAD 当作 pin；自己的检出 origin
设置为 pin 的正式仓地址供 Source Link 使用，再调用上游 `eng/Pack.ps1`。
输出为 `.artifacts/storage-feed` 中五个包及上游 `manifest.<S>.json`。
存在匹配 manifest 时校验 revision、正式仓地址以及全部五组包/符号文件哈希后复用；
同版本不同内容、缺失符号或无法证明来源的旧包报错。Pack 的成功输出只有 manifest 路径，进度写入信息流。
`-OutputDirectory` 可导出另一份 feed；正常构建仍使用根 `nuget.config` 的默认 feed，替代来源须明确配置。

根 NuGet 配置把五个精确 PackageId 映射到此本地 feed，其余依赖映射到 nuget.org。
升级时同时更新版本和 commit；不同包内容必须用新版本，不清空用户全局缓存来掩盖版本错误。

## 显式源码联调

```powershell
$storage = 'E:/repos/Atelia-org/atelia-storage'
dotnet restore DurableGraph.slnx -p:UseStorageSources=true "-p:StorageSourceRoot=$storage"
dotnet build DurableGraph.slnx --no-restore -p:UseStorageSources=true "-p:StorageSourceRoot=$storage"
dotnet test DurableGraph.slnx --no-build -p:UseStorageSources=true "-p:StorageSourceRoot=$storage"
```

`StorageSourceRoot` 必须是完整新仓的绝对路径；路径无效时清晰失败，不回退到包。
切回包模式先执行 `dotnet restore DurableGraph.slnx -p:UseStorageSources=false`，再 build/test。
两模式都要检查 restore assets 与实际解析程序集来源，不能用另一模式的旧 assets 作为验证。

`dotnet pack -p:UseStorageSources=true` 会在生成包前拒绝执行。需要联调实验包时，先在存储仓打出
唯一 S_dev，再明确选择该包版本来打 DG。包内存储依赖必须仍是 S，不能被下游 `PackageVersion=G` 覆盖。

## 包消费与 Agent 查阅

[根 README 的打包段](../README.md#从源码打包) 调用与当前 probes 相同的准备入口。
它将已准备的五个 S 包与四个 G 包组成 feed；各 probe 的 `-Version` 只表示 G。
显式传入完整 feed 的调用不再修改它；消费项目保持最少的 DG PackageReference，由 NuGet 解析存储依赖。

先运行 [EventHistory](../experiments/PackageConsumerProbe/Run-EventHistoryProbe.ps1) 和
[recovery](../experiments/PackageConsumerProbe/Run-EventHistoryRecoveryProbe.ps1) 包回归，
在独立包缓存下核对 nuspec、restore assets 和实际加载 DLL。源码构建不能替代包验证。
每个任务独立的 NuGet 配置清空继承的来源映射，使用 probe 明确传入的 feed，不更改用户配置。

读上游用法时先取得 pin 对应 commit，查看该版 `README.md`、`src/EventJournal/README.md`、
`src/RbfSegmentStore/README.md` 和 `docs/Rbf/`。Source Link 帮助定位源码，不自动把文档加入 Agent 上下文。
候选包来源以其 manifest 为准；公开发布后才验证远端 URL 能取到正确 commit。

## 历史例外

- `Run-OrganizationMigrationProbe.ps1` 的旧 lane 针对 DB-071 的 StateStore → Persistence 命名迁移，
  `Run-DurableBaseMigrationProbe.ps1` 针对旧接口迁移。它们继续使用调用者提供的旧包，不改写成此次拆仓测试。
- `docs/research/read-cache-probe/Run-Probe.ps1` 依赖旧 `DurableGraph.StateStore.Storage/Serialization` 源码形状，
  属于既有历史实验；复现应取其对应历史 revision，本次不恢复旧源码或把它当活动回归。
- 历史设计记录中的旧 `../atelia` 路径保留当时含义。当前生产项目及当前包准备入口不再依赖那些路径。

原有 append-only、严格打开、显式离线救援、lease 和资源生命周期规则保持，见 [AGENTS.md](../AGENTS.md)。
