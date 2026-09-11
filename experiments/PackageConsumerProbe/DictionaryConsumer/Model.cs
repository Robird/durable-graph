using Atelia.DurableGraph;
#if HISTORY_V1
using Mode = DictionaryPackageConsumerProbe.LegacyMode;
using Point = DictionaryPackageConsumerProbe.LegacyPoint;
#else
using Mode = DictionaryPackageConsumerProbe.CurrentMode;
using Point = DictionaryPackageConsumerProbe.CurrentPoint;
using ModeStates = Atelia.DurableGraph.Generated.Family_4D6F6465;
using PointStates = Atelia.DurableGraph.Generated.Family_506F696E74;
#endif

namespace DictionaryPackageConsumerProbe;

#if HISTORY_V1
[DurableType("Mode", 1)]
public enum LegacyMode : int { Zero = 0, One = 1 }
[DurableType("Point", 1)]
public partial struct LegacyPoint {
    [DurableField(1)] public int Value;
    [DurableField(2)] public Node? Link;
}
#else
// Neither historical CLR name survives this compilation.
[DurableType("Mode", 2)]
public enum CurrentMode : long { Zero = 0, Current = 1001 }
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
    [DurableField(3)] public int Score;
}

[DurableType("Box", 1)]
public partial class Box<T> : IDurableObject {
    [DurableField(1)] public T Value = default!;
}

// Dictionary references preserve nominal constraints; their child upgrades do not advance World.
[DurableType("World", 1)]
public partial class World : IDurableObject {
    [DurableField(1)] public Dictionary<string, Point> Points = [];
    [DurableField(2)] public Dictionary<string, Point> Alias = [];
    [DurableField(3)] public Dictionary<Mode, Point> Modes = [];
    [DurableField(4)] public Dictionary<string, int> IgnoreCase = new(StringComparer.OrdinalIgnoreCase);
    [DurableField(5)] public Dictionary<string, Node> Identity = new(ReferenceEqualityComparer.Instance);
    [DurableField(6)] public string FirstKey = "";
    [DurableField(7)] public string IdentityFirst = "";
    [DurableField(8)] public string IdentitySecond = "";
    [DurableField(9)] public List<Dictionary<string, Point>> Nested = [];
    [DurableField(10)] public Dictionary<Mode, Point>[] Vector = [];
    [DurableField(11)] public Box<Dictionary<string, Point>> Box = new();

    public static World Seed() {
        var world = new World();
        var node = new Node { Owner = world, Score = 73 };
        node.Self = node;
        for (int i = 0; i < 32; i++) {
            string key = new($"key-{i:D3}".ToCharArray());
            world.Points.Add(key, new Point { Value = 100 + i, Link = node });
            world.Modes.Add((Mode)(-100 + i), new Point { Value = 200 + i, Link = node });
            if (i == 0) world.FirstKey = key;
        }
        world.Alias = world.Points;
        world.IgnoreCase.Add(new("MiXeD".ToCharArray()), 41);
        world.IgnoreCase.Add(new("SECOND".ToCharArray()), 51);
        world.IdentityFirst = new("same content".ToCharArray());
        world.IdentitySecond = new("same content".ToCharArray());
        world.Identity.Add(world.IdentityFirst, node);
        world.Identity.Add(world.IdentitySecond, node);
        world.Nested = [world.Points, world.Points];
        world.Vector = [world.Modes, world.Modes];
        world.Box.Value = world.Points;
        return world;
    }
}

#if HISTORY_V2
[ValueUpgradeRuleSet]
internal sealed class KeyRules { }
[ValueUpgradeRuleSet]
internal sealed class ValueRules { }

internal static class Upgrades {
    internal static readonly List<(ObjectId Id, int? Count, string Side)> Calls = [];

    [DurableValueUpgrade(typeof(KeyRules), "Mode", 1, 2)]
    internal static void ModeV1ToV2(in ModeStates.V1 old, out ModeStates.V2 next, UpgradeContext context) {
        Calls.Add((context.ObjectId, context.DictionaryCount, "key"));
        next = new(old.Segment0Field1 + 1000L);
    }

    [DurableValueUpgrade(typeof(ValueRules), "Point", 1, 2)]
    internal static void PointV1ToV2(in PointStates.V1 old, out PointStates.V2 next, UpgradeContext context) {
        Calls.Add((context.ObjectId, context.DictionaryCount, "value"));
        next = new(old.Segment0Field1 + 1000L, old.Segment0Field2);
    }
}
#endif
