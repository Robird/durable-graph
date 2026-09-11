# DurableGraph

**带 Schema 版本历史的 C# 对象图增量序列化库。**

直接修改普通领域对象，然后 Commit；不需要代理、setter hook 或手工 MarkDirty。
DurableGraph 捕获独立的版本化状态 DTO，比较上次提交状态，以 Base/Delta 保存变化；
加载时按落盘的 exact Schema 读取、执行显式 Upgrade，再恢复共享引用和循环引用。

目前是快速演进的 **.NET 10 原型**，API 和格式尚未冻结。本文面向首次接入的应用开发者和 Coding Agent，
只介绍当前可用入口；详细能力边界见 [产品工作集](src/PROJECT-STATE.md)。

公开入口是 [EventHistory](docs/design-branches/0063-event-history-journal-slice.md)：记录 Event 快照，随后保存处理结果 State。
Journal 的命名 branch ref 是唯一发布点；恢复读取已保存的结果，不重新执行历史业务处理器。

## 最短接入路径

需要 .NET 10 SDK 和一份匹配当前源码的本地 NuGet feed。首次准备 feed 见文末
[从源码打包](#从源码打包)；这里不假定某个版本已发布到 nuget.org。

创建一个独立控制台项目，例如 `QuickStart/QuickStart.csproj`：

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <OutputType>Exe</OutputType>
    <TargetFramework>net10.0</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
    <DurableGraphGenerateDefinitions>true</DurableGraphGenerateDefinitions>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="Atelia.DurableGraph" Version="$(DurableGraphPackageVersion)" />
    <PackageReference Include="Atelia.DurableGraph.StateStore" Version="$(DurableGraphPackageVersion)" />
  </ItemGroup>
</Project>
```

示例通过命令行传入 `DurableGraphPackageVersion`；正式接入时可将实际版本固定在项目属性或统一包版本文件中。
`Atelia.DurableGraph` 包同时携带 Runtime、Source Generator 和 history 构建集成；StateStore 包提供持久化会话。
声明模型的每个项目都应**直接引用 Runtime 包**。无需手工添加 Analyzer、AdditionalFiles 或 Import，
也不要仅以 Runtime 项目的 ProjectReference 代替完整包接入。

### 1. 定义领域模型

保存为 `Models.cs`。Schema ID 在模型集合中稳定且唯一；此处短名称便于展示，真实应用可采用自己的前缀。

```csharp
using Atelia.DurableGraph;

namespace QuickStart;

[DurableType("Character", 1)]
public partial class Character : DurableBase {
    [DurableField(1)] public string Name = "";
    [DurableField(2)] public int Hp;
    [DurableField(3)] public Character? Partner;
}

[DurableType("World", 1)]
public partial class World : DurableBase {
    [DurableField(1)] public Character Hero = null!;
    [DurableField(2)] public List<Character> Characters = [];
    [Transient] private Dictionary<string, Character> _byName = new();

    public void RebuildTransient() {
        _byName = Characters.ToDictionary(character => character.Name, StringComparer.Ordinal);
    }

    public Character Find(string name) => _byName[name];
}

[DurableType("DamageEvent", 1)]
public partial class DamageEvent : DurableBase {
    [DurableField(1)] public int Amount;
}
```

领域 class/struct 使用顶层 `partial` 声明。class 继承链最终到 `DurableBase`；每个参与持久化的声明显式标记
`DurableType`。实例字段用 `DurableField` 或 `Transient` 明确分类；FieldId 是该声明内的稳定正整数，
基类和派生类可以分别有自己的字段 1。方法不参与序列化，普通 class 自动属性目前不等价于受支持字段。

### 2. 保存、重开、继续修改

保存为 `Program.cs`，替换控制台模板的内容：

```csharp
using Atelia.DurableGraph.StateStore;
using QuickStart;

if (args.Length != 1) {
    throw new ArgumentException("Provide one repository directory path.");
}

string path = Path.GetFullPath(args[0]);
var models = new StateModelRegistry();
Atelia.DurableGraph.Generated.DurableDefinitions.Register(models);

using var repository = Directory.Exists(path)
    ? EventHistoryRepository.OpenExisting(path)
    : EventHistoryRepository.CreateNew(path);
using var session = repository.ListBranches().Contains("main")
    ? repository.Resume<World>("main", models)
    : repository.CreateBranch("main", NewWorld(), models); // 已保存初始 S0。

World world = session.State;
world.RebuildTransient(); // Load 不调用构造器/字段初始化器，也不自动执行此方法。
if (!ReferenceEquals(world.Hero, world.Characters[0]) ||
    !ReferenceEquals(world.Hero.Partner!.Partner, world.Hero)) {
    throw new InvalidOperationException("Object identity was not preserved.");
}

if (session.PendingEvent is null) {
    session.CommitDomainEvent(new DamageEvent { Amount = 1 });
}
DamageEvent pending = session.GetPendingEvent<DamageEvent>();
world.Hero.Hp -= pending.Amount;
var frame = session.CommitDomainState();
Console.WriteLine($"{world.Find("Alice").Name}: Hp={world.Hero.Hp}; Revision={frame.RevisionAddress}");

static World NewWorld() {
    var alice = new Character { Name = "Alice", Hp = 100 };
    var bob = new Character { Name = "Bob", Hp = 80 };
    alice.Partner = bob;
    bob.Partner = alice;
    return new World { Hero = alice, Characters = [alice, bob] };
}
```

在 PowerShell 中设置 `$feed` 为本地 feed 绝对路径、`$version` 为其中的匹配版本，然后运行：

```powershell
# 在包含 QuickStart/ 的目录运行；数据库路径保持相同，并放在源码版本控制之外。
$data = Join-Path (Get-Location) "quickstart-world"
dotnet restore QuickStart/QuickStart.csproj --source $feed -p:DurableGraphPackageVersion=$version
dotnet run --project QuickStart/QuickStart.csproj --no-restore -p:DurableGraphPackageVersion=$version -- $data
dotnet run --project QuickStart/QuickStart.csproj --no-restore -p:DurableGraphPackageVersion=$version -- $data
```

第一次输出 `Hp=99`，第二个进程重开后输出 `Hp=98`。后续修改继续使用同一个 session 的 `State`；
成功提交 State 保留领域实例及比较基线。若进程在 Event 发布后中止，Resume 交付前一个 State 和 PendingEvent。
Dispose **不自动保存，也不撤销领域修改**。`CommitDomainState(nextState)` 也支持替换同 exact 类型根，发布后才切换 `session.State`。
这段程序每次运行会完成一条已有或新建的事件；处理失败后的恢复应采用下文的[仅完成 PendingEvent 入口](#事件快照与失败恢复)，
不要把再次运行“创建新事件”的程序当作透明重试。

省略策略参数即可使用库的默认保存策略。需要调优时，在某次 CreateBranch 或 Commit 的 `parameters` 参数传入
`new ReadAmplificationBaseBudgetParameters(3, 5)`；两个整数分别表示对象级读取放大倍率阈值与可选 Base 预算百分比。
当前默认值也是 `(3, 5)`，可能随原型演进调整。覆盖只对该次调用生效，后续省略参数不会继承上次的覆盖值。
这是性能策略，不是事务大小上限；新增/升级等必要 Base 不受该可选预算限制。
无需自己估算尺寸、挑 Base/Delta 或调用 DTO 的二进制 body。

### 3. 独立浏览与分支

关闭 writer 后，可以只加载某个 Event；不需要先构造 World，也不必注册完全不在该 Event 图中的模型。
每次操作仍需要所选图的完整 reader/Upgrade 能力。

```csharp
using var history = EventHistoryRepository.OpenReadOnlyExisting(path);
var events = history.ReadEvents("main");
foreach (var eventFrame in events) {
    var damage = history.ReadEvent<DamageEvent>(eventFrame, models);
    Console.WriteLine(damage.Amount);
}
var lastEvent = events.Last(); // 此示例已经保存过事件。
var before = history.GetPreviousState(lastEvent);
var pair = history.ReadPair(before, lastEvent, models);
```

`ReadFrames` / `ReadEvents` 按本次取得的 branch head 返回**从旧到新的完整逻辑链**，排除未连入该链的 orphan 和其他分支独有记录。
返回值已完整物化；`Reverse().Take(n)` 不会减少底层枚举量。枚举只取得 frame，不恢复领域图；ReadEvent 等操作才恢复对象。
严格打开仓库仍校验全历史及相关元数据，包括 orphan；不要把只读一个 Event 等同于跳过这些检查。
需要零写入浏览时使用示例中的 `OpenReadOnlyExisting`；可写打开下的枚举可能保存 Journal 派生缓存。

`ReadPair` 是实验性只读快照 API：按输入顺序返回 First/Second，两边成功后才交付。
默认返回两个 `DurableBase`，保留各自实际类型；通过输入 frame 的 `Kind` 判断 State/Event，通过模式匹配使用具体领域类型。
两个输入无需相邻，也不要求一份 State、一份 Event；已知类型时仍可使用 `ReadPair<TFirst,TSecond>` 进行返回类型校验。
它在本次操作内复用相同 ObjectVersion 的解码结果，并可共享完整引用闭包都一致的领域实例。
**两份结果都必须按只读快照使用**；不要依赖跨图 `ReferenceEquals` 判断业务身份或版本，也不要假定两图可隔离编辑。
可写 Resume 只复用不可变 DTO/string，Event/State 的可变对象分别恢复。热路径由用户保持 Event 内容只读；持久 DTO 冻结不会冻结原 CLR 对象。

可写仓库在**没有活动 session**时支持 `CreateBranch("fork", selectedFrame)` 和
`MoveBranch("main", expectedHead, targetFrame)`；随后从目标分支 Resume。
handle 从 `GetHead`、`ReadFrames` 或提交结果取得，只能用于签发它的这一次打开实例；不要跨库或跨重开复用。
历史链是 `S0 → E1 → S1`，E1 与 S1 的 Revision Parent 都是 S0，Event 不成为 State 的增量比较基线。

## 事件快照与失败恢复

事件若需要记录角色“当时的 HP 和观察”，应保存所需的只读业务快照，通过业务 ActorId 找到当前角色进行修改。
可以从可变 `List<string>` 复制为快照私有的 `string[]`，共享不可变 string；readonly 字段本身不会冻结数组或可变元素。
领域采用不可变对象替换时，也可以让事件保留旧对象，不必额外维护一套快照类型。

[可运行的快照与恢复示例](experiments/PackageConsumerProbe/EventHistoryRecoveryConsumer/README.md) 展示
`E1.TargetSnapshot.Hp == 10`、`S1` 中角色 HP 为 7，热处理、冷 Resume 和独立浏览都保持事件观察值。
它区分“提交新事件”和“仅完成已有 PendingEvent”两个入口，并与故障测试共用恢复判断。

失败后先结束当前尝试，关闭 session/repository，再 OpenExisting、Resume 并重新取得 State/PendingEvent：

- 有 PendingEvent：重建新 State 的 Transient，再从这份 State 处理该事件并保存结果。
- 没有 PendingEvent：恢复入口交付当前 State，不创建新事件或再次应用旧事件。S 可能已发布，只是调用方没收到成功返回。
- 打开或恢复失败：报告并停止；不自动退回旧 head 或修复文件。

没有 PendingEvent 只表示当前没有待完成的事件，不能独自证明某个外部请求已经完成；E 发布前失败也可能得到这一结果。
外部命令是否重新提交由应用决定。库不回滚内存修改，也不保证文件之外的业务副作用只执行一次。

## Schema 演化：保留 history，显式写转换

正常本地构建会把当前 Schema 模板发布到模型项目的 `DurableGraphSchemaHistory/*.dgschema`。
**将这些文件与模型源码一起提交 Git**；它们用于重新生成旧版 DTO/reader，不是可清理的 obj 缓存。
Repository 内的 `schemas.rbf` 则记录实际保存时的完整布局；两者职责不同。

首次接入建议显式设置 `DurableGraphGenerateDefinitions=true`，固定使用本文的 Family 登记外观。
生成的代码默认位于项目 `obj` 下，可搜索 `DurableGenericStates.g.cs` 查看 DTO 和登记入口；不要编辑生成文件。

以上 World 示例若增加天数字段：先将 `[DurableType("World", 1)]` 改为版本 2，
再添加 `[DurableField(3)] public int Day;`，并新建 `Upgrades.cs`：

```csharp
using Atelia.DurableGraph;
using WorldStates = Atelia.DurableGraph.Generated.Family_576F726C64;

namespace QuickStart;

public static class WorldUpgrades {
    [DurableUpgrade(typeof(World), 1)] // 相邻边：V1 -> V2。
    public static void Upgrade(in WorldStates.V1 prior, out WorldStates.V2 next,
        UpgradeContext context) {
        next = new(prior.Segment0Field1, prior.Segment0Field2, 1);
    }
}
```

这里 `576F726C64` 是 ID `World` 的 UTF-8 十六进制编码。V1 的两个字段都是引用 DTO 槽 `ObjectId`；
转换保留原引用，给新增 Day 赋业务默认值 1。重新构建并用原数据库路径运行即可加载升级。
升级读取本身不回写；下一次 Commit 为仍存活的升级对象写 Base。

升版规则：

- 同一个 Schema ID/版本的字段布局必须一致；不能修改已有 history 来伪装成从未变过。
- 基类或嵌套值类型布局变化，会要求受影响的 owner/派生类型升版；普通引用目标自身升版不传播到引用方。
- 升级转换的是完整对象 DTO；框架不自动额外执行每层基类的 Upgrade。
- Upgrade 只转换当前这个对象的字段，不读取其他对象或分配新 ObjectId。不能假设缺失转换会使用默认值补齐。
- 支持旧数据的程序需要保留相应模板、reader 和升级能力；详细的泛型/值/容器升级请参考下方真实示例。

CI 可设置 `ContinuousIntegrationBuild=true`，或显式验证而不发布 history：

```powershell
dotnet clean QuickStart/QuickStart.csproj -p:DurableGraphPackageVersion=$version
dotnet build QuickStart/QuickStart.csproj --no-restore -p:DurableGraphPackageVersion=$version -p:DurableGraphSchemaHistoryMode=Verify
```

初次 Publish 后再 Clean/Verify，可同时验证 history 确实能作为下一次编译的输入。
构建属性及诊断说明见 [Build 工具](src/DurableGraph.Build/README.md)。

## 当前能放进模型的内容

| 内容 | 当前边界 |
|---|---|
| 标量 | bool、byte/sbyte、short/ushort、int/uint、long/ulong、char、Half/float/double、Guid、decimal、TimeSpan、DateOnly、TimeOnly、DateTimeOffset |
| 领域类型 | 显式 Durable class、struct、record struct、enum；支持泛型、继承、private/readonly 字段及跨程序集组合 |
| 值组合 | inline struct 嵌套布局、Nullable；record 自动属性的存储使用 `[field: DurableField(...)]` / `[field: Transient]` |
| 引用与数组 | string、已登记的实际派生实例、共享/循环；零下界 `T[]` 和 rank 2–4 多维数组、交错数组 |
| 容器 | exact BCL `List<T>` 和实验性 `Dictionary<TKey,TValue>`；按内容保存。复合 Key 与当前 comparer 的边界见[真实字典示例](experiments/PackageConsumerProbe/CompositeDictionaryConsumer/README.md) |
| 尚不支持 | ValueTuple、DateTime、record class、CLR 嵌套类型/ref struct、任意 object/interface 字段、boxed value 身份、数组协变、非零下界及非 SZ rank-1 数组、其他未适配 BCL 容器 |

支持的类型可以在字段、泛型参数和容器元素中组合。仍需遵守各类型限制；例如 Dictionary 根 Nullable Key 不支持，
容器子类/接口字段不自动当作 BCL 内容对象。string 保留非空实例的引用身份，空串统一为 `string.Empty`。

恢复不执行领域类/struct 的构造器、实例字段初始化器、属性 getter/setter；不要求无参构造器。
Transient 索引/缓存由应用在 Load 交付完整图后重建。自定义字典 comparer 使用当前业务代码，
不得依赖尚未重建的 Transient 或尚未完成填充的引用目标内容。

## 多模型库与运行约束

每个模型库保有自己的 history，并以公开 facade 包住该库 internal 的 `Generated.DurableDefinitions.Register`。
宿主在 CreateBranch/Resume 前登记所需模型库；不依赖程序集自动扫描，也不把别人的 `.dgschema` 复制到本库。
跨库接法见 [跨库继承示例](experiments/PackageConsumerProbe/InheritanceLibraryConsumer/README.md)。

当前外层 API 是**一个 Repository、一个活动 EventHistorySession、单 writer**；每份 Event/State 图选一个非空 durable 根。
同步 Commit 期间宿主须停止对领域图的并发修改；DTO 冻结不提供任意并发读写下的快照隔离。
没有自动坏尾修复或完整 OS crash/power-loss 保证。文件布局为 schemas.rbf、state/、journal/ 和 repository.lock。
旧仓库/会话 API 与 publication.rbf 发布器已移除；原型没有旧格式迁移路径。

保存失败时不要一律重试：`GraphCommitException.Outcome` 区分 NotPublished / Unknown / Published，
同时检查 Repository/Session 的 `IsFaulted`。Unknown/Published 不可透明重试；faulted 实例须 Dispose 后重开，
按持久 head 判断结果。错误与现有领域修改不会自动回滚。通常无需直接操作 SchemaStore、Storage 或发布日志。

## 从源码打包

本仓库需要相邻的兄弟仓库 `../atelia`（Data、Primitives、RBF 等依赖）。从 DurableGraph 根目录执行；
每批使用新版本号，避免 NuGet 缓存复用同版本旧代码：

```powershell
$ErrorActionPreference = "Stop"
$version = "0.0.0-local.$([DateTime]::UtcNow.ToString('yyyyMMddHHmmss'))"
$feed = Join-Path (Get-Location) "artifacts/nuget/$version"
foreach ($project in @(
    "../atelia/src/Data/Data.csproj", "../atelia/src/Primitives/Primitives.csproj",
    "../atelia/src/Rbf/Rbf.csproj", "../atelia/src/RbfSegmentStore/RbfSegmentStore.csproj",
    "../atelia/src/EventJournal/EventJournal.csproj",
    "src/DurableGraph.StateStore.Serialization/DurableGraph.StateStore.Serialization.csproj",
    "src/DurableGraph/DurableGraph.csproj",
    "src/DurableGraph.StateStore.Storage/DurableGraph.StateStore.Storage.csproj",
    "src/DurableGraph.StateStore/DurableGraph.StateStore.csproj"
)) {
    dotnet pack $project --configuration Release --output $feed -p:PackageVersion=$version
    if ($LASTEXITCODE -ne 0) { throw "Pack failed: $project" }
}
```

将 `$feed` 和 `$version` 用于前面的 restore/run。也可直接运行带独立 feed、两代模型和历史校验的
[真实包实验](experiments/PackageConsumerProbe/README.md)，例如 `Run-InheritanceLibraryProbe.ps1`。

## 接下来查什么

| 需求 | 入口 |
|---|---|
| 更详细的生成 API、泛型/组合值升级 | [Runtime 包说明](src/DurableGraph/PACKAGE.md) |
| 跨库引用、固定值、继承 | [模型库](experiments/PackageConsumerProbe/CrossAssemblyConsumer/README.md)、[inline 库](experiments/PackageConsumerProbe/InlineLibraryConsumer/README.md)、[继承库](experiments/PackageConsumerProbe/InheritanceLibraryConsumer/README.md) |
| 泛型/嵌套值/容器迁移 | [泛型三代](experiments/PackageConsumerProbe/GenericConsumer/README.md)、[值升级](experiments/PackageConsumerProbe/ValueUpgradeConsumer/README.md)、[数组](experiments/PackageConsumerProbe/ArrayConsumer/README.md)、[List](experiments/PackageConsumerProbe/ListConsumer/README.md)、[复合字典](experiments/PackageConsumerProbe/CompositeDictionaryConsumer/README.md) |
| 实际能力、术语、未实现事项 | [PROJECT-STATE](src/PROJECT-STATE.md)、[术语表](docs/DurableGraph-glossary.md)、[路线图](docs/DurableGraph-research-roadmap.md) |
| 修改 DurableGraph 本身 | 先读 [AGENTS.md](AGENTS.md) 与产品工作集；根 `dotnet build DurableGraph.slnx` / `dotnet test DurableGraph.slnx`，包边界变动另跑相关包实验 |

下游试用时，优先按自己的领域需求建立一个小的真实 World。记录必须改写的模型、重复登记/升级代码、
需要翻内部源码才能完成的步骤及难以理解的诊断；反馈附最小复现、包版本和保留的 history。
遇到缺失功能先记录，不要仅为让示例通过而改掉原来的建模需求。
