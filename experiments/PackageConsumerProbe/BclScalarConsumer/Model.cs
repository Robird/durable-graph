using Atelia.DurableGraph;
#if HISTORY_V1
using Point = BclScalarPackageConsumerProbe.LegacyPoint;
#else
using Point = BclScalarPackageConsumerProbe.CurrentPoint;
using PointStates = Atelia.DurableGraph.Generated.Family_506F696E74;
using WorldStates = Atelia.DurableGraph.Generated.Family_576F726C64;
#endif

namespace BclScalarPackageConsumerProbe;

#if HISTORY_V1
[DurableType("Point", 1)]
public readonly partial record struct LegacyPoint(
    [field: DurableField(1)] Guid Id,
    [field: DurableField(2)] decimal Amount,
    [field: DurableField(3)] TimeSpan Duration,
    [field: DurableField(4)] int Count);
#else
// Old CLR declaration is intentionally absent. Its retained DTO still contains all three scalar types.
[DurableType("Point", 2)]
public readonly partial record struct CurrentPoint(
    [field: DurableField(1)] Guid Id,
    [field: DurableField(2)] decimal Amount,
    [field: DurableField(3)] TimeSpan Duration,
    [field: DurableField(4)] long Count);
#endif

[DurableType("Box", 1)]
public partial class Box<T> : DurableBase {
    [DurableField(1)] public T Value = default!;
}

#if HISTORY_V1
[DurableType("World", 1)]
#else
[DurableType("World", 2)]
#endif
public partial class World : DurableBase {
    internal static readonly Guid SampleId = new("00112233-4455-6677-8899-aabbccddeeff");
    [DurableField(1)] public Guid Id = SampleId;
    [DurableField(2)] public decimal Amount = 1.0m;
    [DurableField(3)] public TimeSpan Duration = TimeSpan.MinValue;
    [DurableField(4)] public Point Point;
    [DurableField(5)] public List<Point> Points = [];
    [DurableField(6)] public List<Point> Alias = [];
    [DurableField(7)] public Dictionary<decimal, Guid> Keys = [];
    [DurableField(8)] public List<decimal> Amounts = [];
    [DurableField(9)] public Guid?[] Ids = [null, Guid.Empty, SampleId];
    [DurableField(10)] public TimeSpan?[,] Durations = { { null, TimeSpan.MaxValue, TimeSpan.FromTicks(-1) } };
    [DurableField(11)] public List<decimal?> OptionalAmounts = [null, 1.0m, new decimal(0, 0, 0, true, 28)];
    [DurableField(12)] public Dictionary<Guid, TimeSpan> ById = new() { [SampleId] = TimeSpan.MaxValue };
    [DurableField(13)] public Dictionary<TimeSpan, decimal> ByDuration = new() { [TimeSpan.MinValue] = decimal.MinValue };
    [DurableField(14)] public Box<Guid> GuidBox = new() { Value = SampleId };
    [DurableField(15)] public Box<decimal> DecimalBox = new() { Value = decimal.MaxValue };
    [DurableField(16)] public Box<TimeSpan> DurationBox = new() { Value = TimeSpan.FromTicks(-1) };

    public static World Seed() {
        var world = new World { Point = new Point(SampleId, 1.0m, TimeSpan.MinValue, 10) };
        for (int i = 0; i < 32; i++) {
            world.Points.Add(new Point(SampleId, 1.0m, TimeSpan.FromTicks(i), 100 + i));
            world.Keys.Add(i + 1.0m, SampleId);
            world.Amounts.Add(i + 1.0m);
        }
        world.Alias = world.Points;
        return world;
    }
}

#if HISTORY_V2
[ValueUpgradeRuleSet]
internal sealed class PointRules { }

internal static class Upgrades {
    internal static int OwnerCalls;
    internal static readonly List<(ObjectId Id, ObjectStateKind Kind)> ValueCalls = [];

    [DurableValueUpgrade(typeof(PointRules), "Point", 1, 2)]
    internal static void PointV1ToV2(in PointStates.V1 old, out PointStates.V2 next, UpgradeContext context) {
        ValueCalls.Add((context.ObjectId, context.SourceObjectLayout.Kind));
        next = new(old.Segment0Field1, old.Segment0Field2, old.Segment0Field3, old.Segment0Field4 + 1000L);
    }

    [DurableUpgrade(typeof(World), 1)]
    [UpgradeDependency("point", typeof(PointRules), "World", 4, "World", 4)]
    internal static void WorldV1ToV2(in WorldStates.V1 old,
        out WorldStates.V2 next, UpgradeContext context) {
        OwnerCalls++;
        var convert = context.GetValueUpgrade<PointStates.V1, PointStates.V2>("point");
        next = new(old.Segment0Field1, old.Segment0Field2, old.Segment0Field3, convert(in old.Segment0Field4),
            old.Segment0Field5, old.Segment0Field6, old.Segment0Field7, old.Segment0Field8,
            old.Segment0Field9, old.Segment0Field10, old.Segment0Field11, old.Segment0Field12,
            old.Segment0Field13, old.Segment0Field14, old.Segment0Field15, old.Segment0Field16);
    }
}
#endif
