using Atelia.DurableGraph;
using Atelia.DurableGraph.Schema;
#if HISTORY_V1
using Point = ListPackageConsumerProbe.LegacyPoint;
#else
using Point = ListPackageConsumerProbe.CurrentPoint;
using PointStates = Atelia.DurableGraph.Generated.Family_506F696E74;
#endif

namespace ListPackageConsumerProbe;

#if HISTORY_V1
[DurableType("Point", 1)]
public partial struct LegacyPoint {
    [DurableField(1)] public int Value;
}
#else
// LegacyPoint does not exist in this build. Its retained DTO and reader come from history.
[DurableType("Point", 2)]
public partial struct CurrentPoint {
    [DurableField(1)] public long Value;
}
#endif

[DurableType("Pair", 1)]
public partial struct Pair<T, U> {
    [DurableField(1)] public T First;
    [DurableField(2)] public U Second;
}

[DurableType("Box", 1)]
public partial class Box<T> : IDurableObject {
    [DurableField(1)] public T Value = default!;
    [DurableField(2)] public List<T> Items = [];
}

[DurableType("World", 1)]
public partial class World : IDurableObject {
    [DurableField(1)] public List<Point> Points = [];
    [DurableField(2)] public List<Point> Alias = [];
    [DurableField(3)] public List<List<int>> Nested = [];
    [DurableField(4)] public List<Pair<int, string>[,]> Grids = [];
    [DurableField(5)] public List<int>[] Vectors = [];
    [DurableField(6)] public List<Pair<List<int>, string>> Values = [];
    [DurableField(7)] public Box<List<int>> Box = new();
    [DurableField(8)] public List<World> Cycle = [];

    public static World Seed() {
        List<Point> points = Enumerable.Range(100, 32).Select(value => new Point { Value = value }).ToList();
        List<int> inner = [3, 5, 7];
        var world = new World {
            Points = points, Alias = points, Nested = [inner, inner], Vectors = [inner],
            Grids = [new Pair<int, string>[1, 1] { { new() { First = 9, Second = "list graph" } } }],
            Values = [new() { First = inner, Second = "list graph" }],
            Box = new() { Value = inner, Items = [inner] },
        };
        world.Cycle = [world];
        return world;
    }
}

#if HISTORY_V2
[ValueUpgradeRuleSet]
internal sealed class ListRules { }

internal static class Upgrades {
    internal static readonly List<ObjectId> Calls = [];

    [DurableValueUpgrade(typeof(ListRules), "Point", 1, 2)]
    internal static void PointV1ToV2(in PointStates.V1 old, out PointStates.V2 next, UpgradeContext context) {
        if (context.SourceObjectLayout.Kind != ObjectStateKind.List || context.TargetObjectLayout.Kind != ObjectStateKind.List ||
            context.SourceObjectLayout.List!.ElementSlot.InlineSchema!.Version != 1 ||
            context.TargetObjectLayout.List!.ElementSlot.InlineSchema!.Version != 2 || context.ListCount != 32 || context.ArrayShape is not null) {
            throw new InvalidOperationException("The element provider must receive the shared list's exact owner facts.");
        }
        Calls.Add(context.ObjectId);
        next = new(old.Segment0Field1 + 1000L);
    }
}
#endif
