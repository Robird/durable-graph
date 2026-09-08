using Atelia.DurableGraph;
using BoxStates = Atelia.DurableGraph.Generated.Family_47656E65726963426F78;
using WorldStates = Atelia.DurableGraph.Generated.Family_47656E65726963576F726C64;
using PointStates = Atelia.DurableGraph.Generated.Family_47656E65726963506F696E74;
#if !HISTORY_V1
using WorldV1 = Atelia.DurableGraph.Generated.Family_47656E65726963576F726C64.V1<Atelia.DurableGraph.Generated.Family_47656E6572696350616972.V1<Atelia.DurableGraph.Generated.Family_47656E657269634C6567616379506F696E74.V1>>;
using WorldV2 = Atelia.DurableGraph.Generated.Family_47656E65726963576F726C64.V2<Atelia.DurableGraph.Generated.Family_47656E6572696350616972.V2<Atelia.DurableGraph.Generated.Family_47656E657269634C6567616379506F696E74.V2>>;
#endif

namespace GenericPackageConsumerProbe;

internal static class UpgradeTrace {
    internal static readonly List<(ObjectId ObjectId, DurableSchema Source, DurableSchema Target, string Provider)> Calls = [];
    internal static void Record(UpgradeContext context, string provider) =>
        Calls.Add((context.ObjectId, context.SourceObjectSchema, context.TargetObjectSchema, provider));
}

#if !HISTORY_V1
internal static class Upgrades {
    [DurableUpgrade(typeof(Box<>), 1)]
    internal static void BoxV1ToV2<TState>(in BoxStates.V1<TState> old, out BoxStates.V2<TState> next, UpgradeContext context)
        where TState : unmanaged {
        UpgradeTrace.Record(context, "box-pass-through");
        next = new(old.Segment0Field1, 100);
    }

#if !OMIT_CLOSED_UPGRADE
    [DurableUpgrade(typeof(Box<Point>), 1)]
    internal static void PointBoxV1ToV2(in BoxStates.V1<PointStates.V1> old,
        out BoxStates.V2<PointStates.V2> next, UpgradeContext context) {
        UpgradeTrace.Record(context, "point-closed");
        next = new(new(old.Segment0Field1.Segment0Field1 + 1000L), 200);
    }
#endif

    [DurableUpgrade(typeof(World), 1)]
    internal static void WorldV1ToV2(in WorldV1 old, out WorldV2 next, UpgradeContext context) {
        UpgradeTrace.Record(context, "world-inline");
        // The retained history types supply these constructors after LegacyPoint.cs is excluded.
        next = new(old.Segment0Field1, old.Segment0Field2, old.Segment0Field3, old.Segment0Field4,
            new(new(old.Segment0Field5.Segment0Field1.Segment0Field1 + 1000L),
                new(old.Segment0Field5.Segment0Field2.Segment0Field1 + 1000L)));
    }

#if HISTORY_V3
    [DurableUpgrade(typeof(Box<>), 2)]
    internal static void BoxV2ToV3<TState>(in BoxStates.V2<TState> old, out BoxStates.V3<TState> next, UpgradeContext context)
        where TState : unmanaged {
        UpgradeTrace.Record(context, "box-third-version");
        next = new(old.Segment0Field1, old.Segment0Field2, 300);
    }

    [DurableUpgrade(typeof(World), 2)]
    internal static void WorldV2ToV3(in WorldV2 old, out WorldStates.V3 next, UpgradeContext context) {
        UpgradeTrace.Record(context, "world-delete-inline");
        next = new(old.Segment0Field1, old.Segment0Field2, old.Segment0Field3, old.Segment0Field4,
            old.Segment0Field5.Segment0Field1.Segment0Field1 + old.Segment0Field5.Segment0Field2.Segment0Field1);
    }
#endif
}
#endif
