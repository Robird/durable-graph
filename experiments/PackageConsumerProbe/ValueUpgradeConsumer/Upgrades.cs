using Atelia.DurableGraph;
using BoxStates = Atelia.DurableGraph.Generated.Family_426F78;
using WorldStates = Atelia.DurableGraph.Generated.Family_576F726C64;
using PairStates = Atelia.DurableGraph.Generated.Family_50616972;
using PointStates = Atelia.DurableGraph.Generated.Family_506F696E74;
using LegacyStates = Atelia.DurableGraph.Generated.Family_4C6567616379506F696E74;
#if !HISTORY_V1
using WorldV1 = Atelia.DurableGraph.Generated.Family_576F726C64.V1<Atelia.DurableGraph.Generated.Family_50616972.V1<Atelia.DurableGraph.Generated.Family_4C6567616379506F696E74.V1>>;
using WorldV2 = Atelia.DurableGraph.Generated.Family_576F726C64.V2<Atelia.DurableGraph.Generated.Family_50616972.V2<Atelia.DurableGraph.Generated.Family_4C6567616379506F696E74.V2>>;
#endif

namespace ValueUpgradePackageConsumerProbe;

internal static class UpgradeTrace {
    internal static readonly List<(uint ObjectId, DurableSchema Source, DurableSchema Target, string Provider)> Calls = [];
    internal static void Record(UpgradeContext context, string provider) =>
        Calls.Add((context.ObjectId, context.SourceObjectSchema, context.TargetObjectSchema, provider));
}

#if !HISTORY_V1
[ValueUpgradeRuleSet(AllowKeepExact = true)]
internal sealed class Coordinates { }

internal static class Upgrades {
    [DurableUpgrade(typeof(Box<>), 1)]
    [UpgradeDependency("value", typeof(Coordinates), "Box", 1, "Box", 1)]
    internal static void BoxV1ToV2<A, B>(in BoxStates.V1<A> old, out BoxStates.V2<B> next, UpgradeContext context)
        where A : unmanaged where B : unmanaged {
        UpgradeTrace.Record(context, "box-value");
        var convert = context.GetValueUpgrade<A, B>("value");
        next = new(convert(in old.Segment0Field1), 100);
    }

    [DurableValueUpgrade(typeof(Coordinates), "Pair", 1, 2)]
    [UpgradeDependency("value", typeof(Coordinates), "Pair", 1, "Pair", 1)]
    internal static void PairV1ToV2<A, B>(in PairStates.V1<A> old, out PairStates.V2<B> next, UpgradeContext context)
        where A : unmanaged where B : unmanaged {
        UpgradeTrace.Record(context, "pair-value");
        // This same local key belongs to Pair's scope, not Box's enclosing scope.
        var convert = context.GetValueUpgrade<A, B>("value");
        next = new(convert(in old.Segment0Field1), convert(in old.Segment0Field2));
    }

#if !OMIT_POINT_RULE
    [DurableValueUpgrade(typeof(Coordinates), "Point", 1, 2)]
    internal static void PointV1ToV2(in PointStates.V1 old, out PointStates.V2 next, UpgradeContext context) {
        UpgradeTrace.Record(context, "point-leaf");
        next = new(old.Segment0Field1 + 1000L);
    }
#endif

    [DurableValueUpgrade(typeof(Coordinates), "LegacyPoint", 1, 2)]
    internal static void LegacyV1ToV2(in LegacyStates.V1 old, out LegacyStates.V2 next, UpgradeContext context) {
        UpgradeTrace.Record(context, "legacy-leaf");
        next = new(old.Segment0Field1 + 1000L);
    }

    [DurableUpgrade(typeof(World), 1)]
    [UpgradeDependency("legacy", typeof(Coordinates), "World", 6, "World", 6)]
    internal static void WorldV1ToV2(in WorldV1 old, out WorldV2 next, UpgradeContext context) {
        UpgradeTrace.Record(context, "world-value");
        var convert = context.GetValueUpgrade<PairStates.V1<LegacyStates.V1>, PairStates.V2<LegacyStates.V2>>("legacy");
        next = new(old.Segment0Field1, old.Segment0Field2, old.Segment0Field3, old.Segment0Field4,
            old.Segment0Field5, convert(in old.Segment0Field6));
    }

#if HISTORY_V3
    [DurableUpgrade(typeof(Box<>), 2)]
    [UpgradeDependency("value", typeof(Coordinates), "Box", 1, "Box", 1)]
    internal static void BoxV2ToV3<TState>(in BoxStates.V2<TState> old, out BoxStates.V3<TState> next, UpgradeContext context)
        where TState : unmanaged {
        UpgradeTrace.Record(context, "box-third-version");
        var keep = context.GetValueUpgrade<TState, TState>("value");
        next = new(keep(in old.Segment0Field1), old.Segment0Field2, 300);
    }

    [DurableUpgrade(typeof(World), 2)]
    internal static void WorldV2ToV3(in WorldV2 old, out WorldStates.V3 next, UpgradeContext context) {
        UpgradeTrace.Record(context, "world-delete-inline");
        next = new(old.Segment0Field1, old.Segment0Field2, old.Segment0Field3, old.Segment0Field4,
            old.Segment0Field5, old.Segment0Field6.Segment0Field1.Segment0Field1 + old.Segment0Field6.Segment0Field2.Segment0Field1);
    }
#endif
}
#endif
