using Atelia.DurableGraph;

namespace EventHistoryPackageConsumerProbe;

#if HISTORY_V1
[DurableType("Alice", 1)]
#else
[DurableType("Alice", 2)]
#endif
public sealed partial class Alice : DurableBase {
#if HISTORY_V1
    [DurableField(1)] public int Score;
#else
    [DurableField(1)] public long Score;
#endif
    [DurableField(2)] public Alice? Self;
    [DurableField(3)] public List<string> Labels = [];
    [DurableField(4)] public long CreatedAtTicks = 638_931_456_000_000_000;
    internal static int UpgradeCalls = 0;
}

[DurableType("Bob", 1)]
public sealed partial class Bob : DurableBase {
    [DurableField(1)] public int Score;
}

[DurableType("Observed", 1)]
public sealed partial class Observed : DurableBase {
    [DurableField(1)] public Alice Target = null!;
    [DurableField(2)] public Alice Alias = null!;
    public Observed(Alice target) { Target = Alias = target; }
}

#if HISTORY_V1
[DurableType("World", 1)]
#else
[DurableType("World", 2)]
#endif
public sealed partial class World : DurableBase {
    [DurableField(1)] public Alice Alice = null!;
    [DurableField(2)] public Bob Bob = null!;
#if HISTORY_V2
    [DurableField(3)] public int Generation;
#endif
    [Transient] public int Cache = 42;
    internal static int UpgradeCalls = 0;
}

#if HISTORY_V2
internal static class Upgrades {
    [DurableUpgrade(typeof(Alice), 1)]
    internal static void AliceV1ToV2(in Atelia.DurableGraph.Generated.Family_416C696365.V1 old,
        out Atelia.DurableGraph.Generated.Family_416C696365.V2 next, UpgradeContext context) {
        Alice.UpgradeCalls++;
        next = new(old.Segment0Field1 + 1000L, old.Segment0Field2, old.Segment0Field3, old.Segment0Field4);
    }

    [DurableUpgrade(typeof(World), 1)]
    internal static void WorldV1ToV2(in Atelia.DurableGraph.Generated.Family_576F726C64.V1 old,
        out Atelia.DurableGraph.Generated.Family_576F726C64.V2 next, UpgradeContext context) {
        World.UpgradeCalls++;
        next = new(old.Segment0Field1, old.Segment0Field2, 2);
    }
}
#endif
