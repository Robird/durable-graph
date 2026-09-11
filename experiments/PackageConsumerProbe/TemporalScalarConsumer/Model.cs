using Atelia.DurableGraph;
#if HISTORY_V1
using Point = TemporalScalarPackageConsumerProbe.LegacyPoint;
#else
using Point = TemporalScalarPackageConsumerProbe.CurrentPoint;
using PointStates = Atelia.DurableGraph.Generated.Family_506F696E74;
using WorldStates = Atelia.DurableGraph.Generated.Family_576F726C64;
#endif

namespace TemporalScalarPackageConsumerProbe;

#if HISTORY_V1
[DurableType("Point", 1)]
public readonly partial record struct LegacyPoint(
    [field: DurableField(1)] DateOnly Date,
    [field: DurableField(2)] DateTimeOffset Timestamp,
    [field: DurableField(3)] TimeOnly Time,
    [field: DurableField(4)] int Count);
#else
// Old CLR declaration is intentionally absent. Its retained DTO still contains all three scalar types.
[DurableType("Point", 2)]
public readonly partial record struct CurrentPoint(
    [field: DurableField(1)] DateOnly Date,
    [field: DurableField(2)] DateTimeOffset Timestamp,
    [field: DurableField(3)] TimeOnly Time,
    [field: DurableField(4)] long Count);
#endif

[DurableType("Box", 1)]
public partial class Box<T> : IDurableObject {
    [DurableField(1)] public T Value = default!;
}

#if HISTORY_V1
[DurableType("World", 1)]
#else
[DurableType("World", 2)]
#endif
public partial class World : IDurableObject {
    internal static readonly DateOnly SampleDate = new(2024, 2, 29);
    internal static DateTimeOffset Moment(int offsetHours) => new DateTimeOffset(2026, 9, 10, 0, 0, 0, TimeSpan.Zero).ToOffset(TimeSpan.FromHours(offsetHours));
    [DurableField(1)] public DateOnly Date = SampleDate;
    [DurableField(2)] public DateTimeOffset Timestamp = Moment(0);
    [DurableField(3)] public TimeOnly Time = TimeOnly.MinValue;
    [DurableField(4)] public Point Point;
    [DurableField(5)] public List<Point> Points = [];
    [DurableField(6)] public List<Point> Alias = [];
    [DurableField(7)] public Dictionary<DateTimeOffset, DateOnly> Keys = [];
    [DurableField(8)] public List<DateTimeOffset> Timestamps = [];
    [DurableField(9)] public DateOnly?[] Dates = [null, DateOnly.MinValue, SampleDate];
    [DurableField(10)] public TimeOnly?[,] Times = { { null, TimeOnly.MaxValue, new TimeOnly(TimeOnly.MaxValue.Ticks - 1) } };
    [DurableField(11)] public List<DateTimeOffset?> OptionalTimestamps = [null, Moment(0), DateTimeOffset.MinValue];
    [DurableField(12)] public Dictionary<DateOnly, TimeOnly> ByDate = new() { [SampleDate] = TimeOnly.MaxValue };
    [DurableField(13)] public Dictionary<TimeOnly, DateTimeOffset> ByTime = new() { [TimeOnly.MinValue] = DateTimeOffset.MinValue };
    [DurableField(14)] public Box<DateOnly> DateOnlyBox = new() { Value = SampleDate };
    [DurableField(15)] public Box<DateTimeOffset> TimestampBox = new() { Value = DateTimeOffset.MaxValue };
    [DurableField(16)] public Box<TimeOnly> TimeBox = new() { Value = new TimeOnly(TimeOnly.MaxValue.Ticks - 1) };

    public static World Seed() {
        var world = new World { Point = new Point(SampleDate, Moment(0), TimeOnly.MinValue, 10) };
        for (int i = 0; i < 32; i++) {
            world.Points.Add(new Point(SampleDate, Moment(0), new TimeOnly(i), 100 + i));
            world.Keys.Add(Moment(0).AddDays(i), SampleDate);
            world.Timestamps.Add(Moment(0).AddDays(i));
            world.OptionalTimestamps.Add(Moment(0).AddDays(i));
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
