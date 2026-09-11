using Atelia.DurableGraph;
#if HISTORY_V2
using PointStates = Atelia.DurableGraph.Generated.Family_506F696E74;
#endif

namespace ArrayPackageConsumerProbe;

#if HISTORY_V1
[DurableType("Point", 1)]
#else
[DurableType("Point", 2)]
#endif
public partial struct Point {
#if HISTORY_V1
    [DurableField(1)] public int Value;
#else
    [DurableField(1)] public long Value;
#endif
}

[DurableType("Pair", 1)]
public partial struct Pair<T, U> {
    [DurableField(1)] public T First;
    [DurableField(2)] public U Second;
}

[DurableType("Box", 1)]
public partial class Box<T> : IDurableObject {
    [DurableField(1)] public T Value = default!;
    [DurableField(2)] public T[] Items = [];
}

[DurableType("World", 1)]
public partial class World : IDurableObject {
    [DurableField(1)] public Point[] Points = [];
    [DurableField(2)] public Point[] Alias = [];
    [DurableField(3)] public int[][] Jagged = [];
    [DurableField(4)] public Pair<int, string>[,] Grid = new Pair<int, string>[0, 0];
    [DurableField(5)] public int[,,] Cube = new int[0, 0, 0];
    [DurableField(6)] public int[,,,] Hyper = new int[0, 0, 0, 0];
    [DurableField(7)] public Box<int[]> Box = new();
    [DurableField(8)] public World[] Cycle = [];

    public static World Seed() {
        var points = Enumerable.Range(100, 32).Select(value => new Point { Value = value }).ToArray();
        int[] inner = [3, 5, 7];
        var world = new World {
            Points = points, Alias = points, Jagged = [inner, inner],
            Grid = new Pair<int, string>[1, 1] { { new() { First = 9, Second = "array graph" } } },
            Cube = new int[1, 2, 1] { { { 11 }, { 13 } } },
            Hyper = new int[1, 1, 1, 2] { { { { 17, 19 } } } },
            Box = new() { Value = inner, Items = [inner] },
        };
        world.Cycle = [world];
        return world;
    }
}

#if HISTORY_V2
[ValueUpgradeRuleSet]
internal sealed class ArrayRules { }

internal static class Upgrades {
    internal static readonly List<ObjectId> Calls = [];

    [DurableValueUpgrade(typeof(ArrayRules), "Point", 1, 2)]
    internal static void PointV1ToV2(in PointStates.V1 old, out PointStates.V2 next, UpgradeContext context) {
        if (context.SourceObjectLayout.Kind != ObjectStateKind.Array || context.TargetObjectLayout.Kind != ObjectStateKind.Array ||
            context.SourceObjectLayout.Array!.ElementSlot.InlineSchema!.Version != 1 ||
            context.TargetObjectLayout.Array!.ElementSlot.InlineSchema!.Version != 2 ||
            context.ArrayShape!.Rank != 1 || context.ArrayShape.Count != 32) {
            throw new InvalidOperationException("The element provider must receive the shared array's exact owner facts.");
        }
        Calls.Add(context.ObjectId);
        next = new(old.Segment0Field1 + 1000L);
    }
}
#endif
