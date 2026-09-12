using Atelia.DurableGraph;
using Atelia.DurableGraph.Runtime;
using Atelia.DurableGraph.Schema;
#if HISTORY_V1
using Point = NullablePackageConsumerProbe.LegacyPoint;
#else
using Point = NullablePackageConsumerProbe.CurrentPoint;
using PointStates = Atelia.DurableGraph.Generated.Family_506F696E74;
using WorldStates = Atelia.DurableGraph.Generated.Family_576F726C64;
#endif

namespace NullablePackageConsumerProbe;

#if HISTORY_V1
[DurableType("Point", 1)]
public partial struct LegacyPoint {
    [DurableField(1)] public int Value;
    [DurableField(2)] public Node? Link;
}
#else
// The old domain declaration is absent; history retains its exact state and body.
[DurableType("Point", 2)]
public partial struct CurrentPoint {
    [DurableField(1)] public long Value;
    [DurableField(2)] public Node? Link;
}
#endif

[DurableType("Node", 1)]
public partial class Node : IDurableObject {
    [DurableField(1)] public World? Owner;
    [DurableField(2)] public Node? Self;
    [DurableField(3)] public string Label = "nullable node";
}

#if HISTORY_V1
[DurableType("World", 1)]
#else
[DurableType("World", 2)]
#endif
public partial class World : IDurableObject {
    [DurableField(1)] public Point? Optional;
    [DurableField(2)] public List<Point?> Points = [];
    [DurableField(3)] public List<Point?> Alias = [];
    [DurableField(4)] public Point?[] Vector = [];
    [DurableField(5)] public Point?[,,,] Grid = new Point?[0, 0, 0, 0];
    [DurableField(6)] public int? Number;

    public static World Seed() {
        var world = new World { Number = -17 };
        var node = new Node { Owner = world };
        node.Self = node;
        world.Optional = new Point { Value = 41, Link = node };
        world.Points = Enumerable.Range(100, 32)
            .Select(value => value == 107 ? (Point?)null : new Point { Value = value, Link = node }).ToList();
        world.Alias = world.Points;
        world.Vector = [new Point { Value = 61, Link = node }, null, new Point { Value = 71, Link = node }];
        world.Grid = new Point?[1, 1, 1, 2];
        world.Grid[0, 0, 0, 1] = new Point { Value = 81, Link = node };
        return world;
    }
}

#if HISTORY_V2
[ValueUpgradeRuleSet(AllowNullableLifting = true)]
internal sealed class NullableRules { }

internal static class Upgrades {
    internal static readonly List<(ObjectId Id, ObjectStateKind Kind)> Calls = [];
    internal static int OwnerCalls;

    [DurableValueUpgrade(typeof(NullableRules), "Point", 1, 2)]
    internal static void PointV1ToV2(in PointStates.V1 old, out PointStates.V2 next, UpgradeContext context) {
        Calls.Add((context.ObjectId, context.SourceObjectLayout.Kind));
        next = new(old.Segment0Field1 + 1000L, old.Segment0Field2);
    }

    [DurableUpgrade(typeof(World), 1)]
    [UpgradeDependency("optional", typeof(NullableRules), "World", 1, "World", 1)]
    internal static void WorldV1ToV2(in WorldStates.V1<NullableState<PointStates.V1>, NullableState<int>> old,
        out WorldStates.V2<NullableState<PointStates.V2>, NullableState<int>> next, UpgradeContext context) {
        OwnerCalls++;
        var convert = context.GetValueUpgrade<NullableState<PointStates.V1>, NullableState<PointStates.V2>>("optional");
        next = new(convert(in old.Segment0Field1), old.Segment0Field2, old.Segment0Field3,
            old.Segment0Field4, old.Segment0Field5, old.Segment0Field6);
    }
}
#endif
