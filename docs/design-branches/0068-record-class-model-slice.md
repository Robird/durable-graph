# DB-068：record class 领域模型与统一引用资格

> 状态：**Proposed / 待用户采纳，未实施**。
> 日期：2026-09-12；调查基线：`8d89a35`（DB-067）。
> 来源：[DramaBoard 真实模型接入反馈 003](../../../drama-board/docs/feedback/durablegraph/003-real-model-integration.md)，消费者固定包来自 `f68388f`。
> 本轮仅调查和设计。当前能力以 [PROJECT-STATE](../../src/PROJECT-STATE.md) 和源码为准。

## 1. 问题、证据与最小交付

让消费者保留 C# 普通/positional `record class` 的构造、`with`、值相等和继承，
通过现有 Schema、版本化 DTO 与对象图管线保存恢复，减少手写领域样板代码。

最小成功标准：真实 PackageReference 应用使用一个普通 class World 和一条 positional record 事实继承链，
分别覆盖 E1 后退出→Resume 完成 S1，以及 S1 后重开 Pending 为空，并独立读取事件；值相等但引用不同的两个 record 不被合并，
共享引用仍保持共享；无需手写等价 DTO、Equals 或复制函数来绕过库的声明限制。
泛型、跨程序集继承与历史读取必须进入验收，不能只交付能生成简单 record 的语法补丁。

### 反馈裁决

| 反馈 | 本轮判断与处理 |
|---|---|
| 保存 API 已满足完整世界和独立事件 | 接受为下游报告的实际集成结果；本轮核对了 adapter、测试代码和固定包来源，没有重新执行下游测试或校验包产物 |
| record class 改普通 class 产生大量手工语义 | 采纳为下一片需求；直接支持 record class，并附可运行使用范式 |
| readonly List 不代表内容不可变 | 成立；范式明确防御复制和只读外观，框架不自动深复制事件闭包 |
| 单次 Debug 的耗时和分配 | 保留观察，不足以选择缓存、可变领域模型或新存储子系统；后续由较长轨迹和可重复测量触发 |
| local object records 全是 Base | 符合当前纯 fold 替换实例的身份模型。新实例产生新 ID/Base，未变实例可沿用旧 head，map Remove/Delta 仍为增量；不据此引入跨实例内容配对 |

已核对的需求出处：

- [FirstBoardDomain](../../../drama-board/src/FirstBoard/FirstBoardDomain.cs)：`BoardActor.With*`、
  `FirstBoardGameState.With*`、`FirstBoardWorld.With`；`BoardEventPayload` 和 `FirstBoardFact` 手写多态相等。
- [GraphSpatialFact](../../../drama-board/src/Spatial/Facts/GraphSpatialFact.cs)：抽象事实基类和各派生事实手写字段、构造及相等/hash。
- [消费 adapter](../../../drama-board/src/FirstBoard/Persistence/FirstBoardOccurrenceHistory.cs)、
  [行为测试](../../../drama-board/tests/FirstBoard.Persistence.Tests/FirstBoardPersistenceTests.cs)、
  [冷进程测试](../../../drama-board/tests/FirstBoard.Persistence.Tests/ColdProcessTests.cs)、
  [固定包来源](../../../drama-board/docs/worksets/durablegraph-package-source.md)。本轮源码枚举得到 64 个 `.dgschema` 文件。

注意：下游某些手写相等已包含集合内容比较；恢复 C# record 语法并不能自动替代这种额外业务语义。

## 2. 结构阻碍与候选选择

`DurableBase` 当前是空 abstract class。C# record class 只能继承 record class 或 object，
不能继承这个普通 class。因此仅删除 SG 的 `IsRecord` 拒绝条件不可行。
语言依据：[C# record reference](https://learn.microsoft.com/en-us/dotnet/csharp/language-reference/builtin-types/record)。

| 方案 | 收益与成本 | 推荐 |
|---|---|---|
| 共同 `IDurableObject` 标记；保留 `DurableBase` 实现它 | class/record 共用泛型和非泛型入口；需要系统迁移 CLR 约束，但保留编译期领域对象资格 | **采用此方案作为施工提案** |
| 再加 `DurableRecordBase` | record 仍受人为祖先约束；两种根最终仍需要共同接口或 object | 不增加第二个框架基类 |
| 全部改 `where T : class` / object | 已有 object 内核可用，最终登记仍可校验；但公共入口失去领域对象意图，误传只能更晚发现 | 不作为公开外观；内核继续用 object |
| 仅文档/不可变 class 范式 | 能指导快照所有权，无法消除 `with`、positional 和继承 equality 的改写 | 作为交付附件，不替代功能 |
| 独立发布前置 marker 重构，再单独做 record | 有利于内部拆包，但第一批本身没有消费者收益 | 同一分片分门实施，不额外创造无功能的发布阶段 |

共同 marker 不是序列化能力本身：生成声明仍需 DurableType/字段分类；执行仍需显式绑定、
完整 Schema 和 exact CLR 检查。手工绑定沿现有显式登记合同，不变成程序集扫描或自动 POCO 序列化。

## 3. 建议的领域/API 形状

以下代码是**拟实现接口**，当前包不能使用：

```csharp
public interface IDurableObject { }
public abstract class DurableBase : IDurableObject { }

[DurableType("Fact", 1)]
public abstract partial record Fact(
    [field: DurableField(1)] string Actor) : IDurableObject;

[DurableType("Damage", 1)]
public sealed partial record Damage(
    string Actor,
    [field: DurableField(1)] int Amount) : Fact(Actor);

[DurableType("World", 1)]
public partial class World : DurableBase {
    [DurableField(1)] public Fact? LastFact;
}
```

`Damage.Actor` 复用基类属性，没有自己的持久字段；FieldId 按声明 Schema 分段，
`Fact` 和 `Damage` 各自的 FieldId=1 合法。属性实际声明在哪一层，就在哪一层保存一次。
`Damage` 的 Actor 参数不重复标 `[field: DurableField]`，不能把没有实际字段的参数误收为数据。

- `IDurableObject` 是用户定义引用对象的资格标记，无成员，不进 DTO/Schema；不覆盖内建 string/数组/容器。
- `DurableBase` 继续是无状态的可选便利壳。声明根可直接继承 object 并实现 marker；这同样允许普通 partial class 使用 marker。
  普通 class 的字段发现政策保持，不随本片开放一般自动属性序列化。
- 顶层 public/internal partial record class，支持 positional/body、abstract/sealed、开放泛型；
  跨程序集依赖沿 DB-059–061 的 public 导出/显式登记规则。派生类沿 C# 规则只能继承 record。
- 任何真正的领域中间祖先仍须有 DurableType 和完整 history；只有框架 DurableBase 与 object 不进祖先 Schema。
  不能借 marker 跳过一个有未知状态的未标记基类。
- 字段/元素复用现有槽闭包。record 是 ReferenceObject，占 ObjectId；不是 inline record struct。
  不开放 object/接口通配持久字段、boxed struct、未登记派生类型或 string/容器产品根。
- 所有强类型领域引用 API 统一 `where T : class, IDurableObject`。
  `PendingEvent`、`CommitDomainEvent` 的擦除边界及非泛型 `ReadPair` 两个返回值使用 `IDurableObject`。
  读入口可请求这个共同接口，实际 root 必须仍是已登记的具体 StateModelBinding；
  Create/Resume 的 State exact 类型和根替换规则保持。
- 实现接口的 struct 不得因装箱混入图。Runtime 的 ReferenceObject 定义必须检查实际 CLR class、marker 和既有 kind/arity；
  不把接口本身登记为对象模型。marker 不表达任意接口槽的 nominal Schema。
- 现有 `: DurableBase` 模型继续可用；显式依赖 PendingEvent/ReadPair 返回 DurableBase 的调用点需按共同接口重编译，
  不承诺旧二进制 ABI，也不新增两套发布/恢复外观。真实消费者和包内 XML 必须同步迁移。

## 4. 生成与恢复规则

复用 [DB-056](0056-record-struct-state-slice.md) 的实际字段分类与 Family 路径，
复用 [DB-061](0061-cross-assembly-inheritance-slice.md) 的声明层 Capture/Hydrate 与 base projection。

1. SG 的声明形状分开 `record class`、`record struct`、普通 class/struct，不以 IsRecord 单独决定生成壳。
2. 显式字段沿原分类；positional/自动属性/C# 14 field-backed 属性通过 `[field: DurableField]` 或 `[field: Transient]`
   分类真实 backing storage。无存储的计算属性和 EqualityContract 不入 Schema。
3. 继承 positional 参数、属性隐藏/覆盖、属性替换等只按 Roslyn 的实际声明字段建模。
   对没有命中实际字段的持久属性标记、未分类 backing、未知隐式存储、字段式事件和 helper 冲突明确诊断。
   真有基/派生两份独立存储则各自分类，不能凭同名合并。
4. backing accessor 使用 MetadataName，但**引用类型 receiver 是 `value`，值类型才是 `ref value`**。
   当前 GenericProjection 的 backing 读取无条件 `ref Unsafe.AsRef(in value)`，必须修正这条 struct 专用假设。
5. 冷恢复仍 RuntimeHelpers 分配未初始化实例，然后按 base→derived 填充实际字段和连接引用。
   不执行主构造器、copy constructor、初始化表达式、属性 setter/init 或任何业务校验方法。
6. SG 只增加静态/类型级支持，不给 record 添加参与 C# equality 的隐藏实例状态；
   不生成自己的业务 Equals/GetHashCode/clone/with 替代品。
7. current 形状选择 Family，历史 DTO/reader/Upgrade 不依赖旧 record CLR 壳。
   ReferenceObject 的精确父链、inline 传播、相邻升级及 reader 保留合同保持。

## 5. 身份、相等与快照所有权

以下三件事分别验收：

- **领域相等**：遵循用户/C# 当前 Equals。编译器合成相等可能包括 Transient 存储，
  对数组/List 不会自动变成内容相等；不把业务相等当持久状态相等。
- **引用身份**：Capture/恢复的实例字典继续 ReferenceEqualityComparer。两个 `new Damage("A", 3)`
  即使相等也获得不同 ID；同一个实例被多处引用只捕获一次。`with` 创建的新实例不能继承原 ObjectId。
  只读 ReadPair 的共享仍受既有 head、完整状态 proof 和引用闭包约束，不因 IsRecord 就认定安全。
- **冻结状态**：DTO 仍按全部持久字段/引用 ID 比较和生成 Delta，忽略 Transient；业务 Equals 为 true 也可能有持久变化。
  业务 Equals 抛错或循环，不应被普通对象 Capture、Delta 或图身份查重调用。

`with` 通常做浅复制；用户自定义 copy constructor 仍是用户代码。readonly/init 和只读集合接口不保证深不可变。
范式应同时展示一个纯值/字符串 positional fact（保留自动相等），以及含 private List 的事件快照
（输入复制、对外只读视图、必要时自行提供集合内容相等）。不宣称所有手写业务相等都能删掉。
record 也可以有 mutable 字段；支持声明不等于提供冻结 hot PendingEvent 的能力。
本片不扩张 Dictionary 的非 string 引用 key 支持范围。

## 6. Schema、格式与源码迁移

record/class 外观、marker、backing field 名、C# equality/copy 方法均不是持久布局。
相同定义 ID、版本、祖先、FieldId 和槽应生成相同 Schema/history；不新增 TypeTag、SchemaKind、
RepresentationId 类别、wire 版本或 record 专用 codec。

需要真实见证：V1 普通 class 的手写字段 → 同版 record 的对应 backing 字段，在整个继承链合法转换且
持久布局相同的情况下，读旧数据并继续保存，不虚构业务 Upgrade。涉及继承的模型库和宿主一起重编译，
不要求旧 CLR 普通 class 基库与新 record 派生库二进制混用。
源码 DTO helper 可能由普通生成路径切到 Family，内部类型别名需随之迁移；这与持久格式不变不同。
再用真实字段变化的 V2 证明仍需显式升版、历史 reader 和 Upgrade；基类变化漏升派生必须拒绝。
如果当前导出合同确实缺少必要的执行信息，实施前先给出最小失败例再修订此提案，不能静默增加格式。

## 7. 施工门与可委派范围

| 门 | 工作 | 最小出口 |
|---|---|---|
| G0 共同资格 | 冻结 marker 名称/根判定/公共签名，迁移 Runtime 和 StateStore 泛型及擦除入口 | 普通模型回归通过；class+marker 可显式绑定；boxed/interface/错误根/未登记模型拒绝，exact/发布/故障检查保留 |
| G1 SG 声明与投影 | record class 接纳、actual backing 分类、receiver 修正、同库继承/泛型，外部 nominal/export 准入 | positional record 真正 Capture/Hydrate；构造器/getter/init 零调用；继承字段一次且仅一次；DB-056 struct 回归保持 |
| G2 图与历史 | shared/distinct identity、完整状态比较、容器组合、两代升级与跨程序集 record 基类 | 真实冷恢复、记录等值不合并、历史 Schema 不变量、explicit Upgrade/漏升诊断可观察 |
| G3 消费者交付 | 新独立 PackageReference record 示例、旧恢复 helper/README/XML 的共同类型迁移 | E/S 热冷、Pending、只登记事实读取、with 后保存、两代包/history Publish/Verify 均执行；更新当前能力及剩余路线 |

G0→G1 依赖顺序由主代理控制。签名冻结后，可分别委派 Runtime/外观、SG、历史与跨库测试、真实包示例；
同一文件有唯一编辑者。独立审阅重点为 marker 伪造/放宽准入、引用身份、隐式存储丢失与旧 history。
Windows .NET 构建和测试由主代理串行执行；不能让包测试/生成测试并行争用 DLL。

关键源码入口：

- [DurableBase](../../src/DurableGraph/DurableBase.cs)、[StateModelBinding](../../src/DurableGraph/StateModelBinding.cs)、
  [StateDefinitionBinding](../../src/DurableGraph/StateDefinitionBinding.cs)、[StateBaseProjection](../../src/DurableGraph/StateBaseProjection.cs)。
- [CaptureContext](../../src/DurableGraph/CaptureContext.cs)、[ObjectReadTable](../../src/DurableGraph/ObjectReadTable.cs)、
  [BuiltinStateValues](../../src/DurableGraph/BuiltinStateValues.cs)、[StateModelSnapshot](../../src/DurableGraph.StateStore/StateModelSnapshot.cs)。
- [Ancestry](../../src/DurableGraph.Generator/DurableSchemaGenerator.Ancestry.cs)、[CrossAssembly](../../src/DurableGraph.Generator/DurableSchemaGenerator.CrossAssembly.cs)、
  [Records](../../src/DurableGraph.Generator/DurableSchemaGenerator.Records.cs)、[GenericProjection](../../src/DurableGraph.Generator/DurableSchemaGenerator.GenericProjection.cs)、
  [GeneratedState](../../src/DurableGraph.Generator/DurableSchemaGenerator.GeneratedState.cs)。祖先终点须统一，不能只改诊断入口。
- [EventHistoryRepository](../../src/DurableGraph.StateStore/EventHistoryRepository.cs)、[EventHistorySession](../../src/DurableGraph.StateStore/EventHistorySession.cs)、
  [GraphReader](../../src/DurableGraph.StateStore/GraphReader.cs)、[WorldWorkspace](../../src/DurableGraph.StateStore/WorldWorkspace.cs)。
- [RecordStructGeneratorTests](../../tests/DurableGraph.Tests/RecordStructGeneratorTests.cs)、
  [RecordStateProjectionTests](../../tests/DurableGraph.Tests/RecordStateProjectionTests.cs)、
  [PackageConsumerProbe](../../experiments/PackageConsumerProbe/README.md)。旧 record-class 拒绝测试应按新的合法/非法边界改写，不能整组删除。

## 8. 验收矩阵与范围封顶

除各门出口外，必须覆盖：

- 非 positional/positional、abstract/sealed、generic record；普通 class 与 record 相互引用；
  现有 Nullable/inline/generic 槽和数组/List/Dictionary value 中至少各有组合见证。
- 基类复用 positional property、派生独有字段、private/init/readonly backing、C# 14 field-backed；
  错目标 attribute、未分类实际 storage、未标记中间基类、未知 concrete subtype 和 counterfeit marker/attribute 拒绝。
- 同库及真实跨库 generic base；基类和派生各自 history/export/注册，base 改动漏升派生诊断。
- 自环/互环不触发业务 Equals；两个等值不同 record 的身份与共享引用均正确；同实例 mutable record 的持久字段变化产生真实 Delta，
  而 `with` 替换产生新实例身份，不以业务 key 自动接续 ID。
- 用户 Equals 忽略某持久字段、或 Transient 导致业务不等时，DTO 比较仍只按完整持久状态；
  ReadPair 的 proof/闭包和冷 Resume 的可变图隔离保留。
- 真包 S0/E1/S1 与独立进程重开；Pending 完成后第二次恢复不重放；event-only 登记不依赖 State 根模型；
  公共非泛型返回可 pattern match record 与普通 class。
- 同布局 class→record 的历史复用，以及 V1→V2 字段升级/强制 Base/后续 Delta；Publish/Verify，已有普通模型包消费者保持可重编译运行。

产品变更后运行 `dotnet build DurableGraph.slnx`、Runtime/Generator、StateStore、相关 Build/history tests 与真实包消费者；
按变更影响选择 Storage 回归，不把设计时只读调查写成已通过产品验收。
文档的签名/样例必须以实际消费包验证，不能靠 ProjectReference 代替 NuGet 边界。

不做：自动深复制/深不可变集合、业务 comparer 合成或历史保存、引用 record Dictionary key 扩张、
跨实例业务 ID 合并、持久 hash、ValueTuple、通配 object/interface 字段、任意 POCO、普通 class 一般属性支持、
程序集重排、新缓存/新 diff/新发布器。下游没有被要求马上迁回 record；它可继续使用当前已工作模型。

## 9. 设计审阅状态

本轮分别核对了下游源码/包钉扎和上游生成/运行边界，并由独立设计审阅比较 marker、双基类与 object 外观。
共同意见：用 marker 消除 CLR class 专属假设合理；仍须完成整条生成、继承和包消费验证。
本轮不声称 record class 已可用，不把下游单次性能样本升级为性能结论。
