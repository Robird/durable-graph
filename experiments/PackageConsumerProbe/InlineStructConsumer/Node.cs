using Atelia.DurableGraph;

namespace InlineStructPackageConsumerProbe;

#if CHILD_V1
[DurableType("package.inline-node", 1)]
#elif CHILD_V2
[DurableType("package.inline-node", 2)]
#else
[DurableType("package.inline-node", 3)]
#endif
public sealed partial class Node : DurableBase {
#if CHILD_V1
    [DurableField(1)] private readonly int _value;
#else
    [DurableField(1)] private readonly long _value;
#endif
    [DurableField(2)] private readonly string _label;
    internal Node(int value, string label) { _value = value; _label = label; }
    internal long Value => _value;
    internal string Label => _label;
    internal static int UpgradeCalls = 0;
#if !CHILD_V1
    private static void UpgradeStateV1ToV2(in __DurableState.V1 old, out __DurableState.V2 next) {
        UpgradeCalls++;
        next = new(old.Segment0Field1 + 100L, old.Segment0Field2);
    }
#endif
#if CHILD_V3
    private static void UpgradeStateV2ToV3(in __DurableState.V2 old, out __DurableState.V3 next) {
        UpgradeCalls++;
        next = new(old.Segment0Field1 + 100L, old.Segment0Field2);
    }
#endif
}
