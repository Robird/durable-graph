using Atelia.DurableGraph;

namespace InlineStructPackageConsumerProbe;

#if HISTORY_V1
[DurableType("package.inline-owner", 1)]
#elif HISTORY_V2
[DurableType("package.inline-owner", 2)]
#else
[DurableType("package.inline-owner", 3)]
#endif
public abstract partial class Owner : DurableBase {
#if HISTORY_V3
    [DurableField(1)] private readonly long _summary = -1;
    internal long Summary => _summary;
#else
    [DurableField(1)] private Links _links;
    internal Links Links => _links;
    protected void SetLinks(Links links) => _links = links;
#endif
    [Transient] private readonly int _transient = 77;
    internal int TransientValue => _transient;
#if !HISTORY_V1
    private static void UpgradeStateV1ToV2(in __DurableState.V1 old, out __DurableState.V2 next) {
        // No Point/Links CLR name or helper is used. This remains compiled after their deletion.
        next = new(new(
            new(old.Segment0Field1.Segment0Field1.Segment0Field1 + 1000L,
                old.Segment0Field1.Segment0Field1.Segment0Field2, old.Segment0Field1.Segment0Field1.Segment0Field3),
            new(old.Segment0Field1.Segment0Field2.Segment0Field1 + 1000L,
                old.Segment0Field1.Segment0Field2.Segment0Field2, old.Segment0Field1.Segment0Field2.Segment0Field3),
            old.Segment0Field1.Segment0Field3));
    }
#endif
#if HISTORY_V3
    private static void UpgradeStateV2ToV3(in __DurableState.V2 old, out __DurableState.V3 next) =>
        next = new(old.Segment0Field1.Segment0Field1.Segment0Field1 + old.Segment0Field1.Segment0Field2.Segment0Field1);
#endif
}

#if HISTORY_V1
[DurableType("package.inline-world", 1)]
#elif HISTORY_V2
[DurableType("package.inline-world", 2)]
#else
[DurableType("package.inline-world", 3)]
#endif
public sealed partial class World : Owner {
    [DurableField(1)] private readonly int _score;
    internal int Score => _score;
    internal static int UpgradeCalls = 0;
#if HISTORY_V1
    internal World(Node node, string rightLabel) {
        _score = 5;
        SetLinks(new(new(11, node.Label, node), new(21, rightLabel, node), this));
    }
    internal void ChangeLeft(int value) => SetLinks(new(new(value, Links.Left.Label, Links.Left.Node), Links.Right, this));
#elif HISTORY_V2
    private World(int unused) { _score = unused; }
#else
    private World(int unused) { _score = unused; }
#endif
#if !HISTORY_V1
    private static void UpgradeStateV1ToV2(in __DurableState.V1 old, out __DurableState.V2 next) {
        UpgradeCalls++;
        // The leaf owns the complete upgrade, including its inherited inline layout.
        next = new(new(
            new(old.Segment0Field1.Segment0Field1.Segment0Field1 + 1000L,
                old.Segment0Field1.Segment0Field1.Segment0Field2, old.Segment0Field1.Segment0Field1.Segment0Field3),
            new(old.Segment0Field1.Segment0Field2.Segment0Field1 + 1000L,
                old.Segment0Field1.Segment0Field2.Segment0Field2, old.Segment0Field1.Segment0Field2.Segment0Field3),
            old.Segment0Field1.Segment0Field3), old.Segment1Field1 + 100);
    }
#endif
#if HISTORY_V3
    private static void UpgradeStateV2ToV3(in __DurableState.V2 old, out __DurableState.V3 next) {
        UpgradeCalls++;
        next = new(old.Segment0Field1.Segment0Field1.Segment0Field1 + old.Segment0Field1.Segment0Field2.Segment0Field1,
            old.Segment1Field1 + 1000);
    }
#endif
}
