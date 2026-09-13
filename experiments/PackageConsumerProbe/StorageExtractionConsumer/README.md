# Storage 拆仓的旧数据 witness

`Run-StorageExtractionProbe.ps1` 消费已经打好的包，不从当前源码打包。两个 lane
使用相同四个 DurableGraph 包、相同 Generator、相同 Consumer/Model 源文件，
只切换五个 Storage 包。没有 DB-071 的旧 namespace、类型替换或条件编译分支。

`Consumer.csproj` 是复制到隔离目录后使用的模板；通过下面的 runner 执行。
它有意直接精确 pin 五库，以控制迁移变量；普通用户仅引用 EventJournal 的传递依赖
消费证明由 atelia-storage 自己的包 smoke 承担。

## 分阶段执行

P0 从固定旧产品源码准备五个 Storage 包和四个 DG 包，再运行一次 Seed。若两组包
版本或位置不同，可额外指定 `-DurableGraphVersion` 和 `-DurableGraphPackageSource`。
这两个参数默认使用旧 Storage 的版本和 feed。

```powershell
./experiments/PackageConsumerProbe/Run-StorageExtractionProbe.ps1 `
  -Stage Seed `
  -OldStoragePackageSource E:/repos/Atelia-org/.storage-extraction/old-feed `
  -OldStorageVersion 0.1.0-extraction-old.20260913.1 `
  -WorkRoot E:/repos/Atelia-org/.storage-extraction/cold-witness
```

P3 准备迁出后的五包，使用完全相同的 WorkRoot 续跑。新 Storage 版本须满足冻结
DG 包的依赖下限；runner 不抑制 NU1605，也不改写旧包的 nuspec。

```powershell
./experiments/PackageConsumerProbe/Run-StorageExtractionProbe.ps1 `
  -Stage Complete `
  -OldStoragePackageSource E:/repos/Atelia-org/.storage-extraction/old-feed `
  -OldStorageVersion 0.1.0-extraction-old.20260913.1 `
  -StoragePackageSource E:/repos/Atelia-org/.storage-extraction/new-feed `
  -StorageVersion 0.1.1-dev.20260913.1 `
  -WorkRoot E:/repos/Atelia-org/.storage-extraction/cold-witness
```

已同时具备旧/新包时可用 `-Stage All` 和一个全新 WorkRoot。Complete 每次在新的
attempt 目录复制冻结样本；即使构建或续写失败也不重建旧 writer、不修改旧样本。

## 验证内容与产物

- 各 lane 私有九包 feed、NuGet 配置/缓存、props/targets 边界、构建输出；restore
  必须解析指定九个包。日志记录九个实际加载 DLL 的路径和 SHA256，并与包缓存比对；
  四个 DG DLL 与 Generator 在两个 lane 间必须相同。
- 冻结旧 `bin/Release` writer、源码、包、两份 schema history、样本和帧清单。
  Seed 后保存文件哈希；Complete 前后都检查冻结内容。
- 真实 `EventHistoryRepository` 写出 Schema、State、Journal event、ref-op 与 ref-object
  文件；模型含 cycle、alias、inline struct、数组、List、Dictionary，以及 Base/Delta。
- 新包分别在三个进程中只读旧数据、恢复 PendingEvent 并续写 Delta/NoChange、再次冷读。
  比较 branch/ref ID、Event parent 链与地址、head 角色、Graph revision/root 和领域结果。
  两次只读阶段检查全部数据库文件内容/长度/修改时间不变。
- 续写后逐段验证旧完整 frame 的地址与字节；允许文件追加和 branch head 正常前进。
  结果保存在 `attempts/<id>/result.txt`，详细 DLL 来源在同目录的各 mode 日志，
  包来源哈希位于各 lane 的 `package-provenance.json`。

这是库交付边界和实际旧数据兼容性验证，不提供断电保证，不遍历所有 payload policy、
segment rotation 或故障组合。底层默认 recovery 与 DG 严格打开的既有测试继续保留执行。
