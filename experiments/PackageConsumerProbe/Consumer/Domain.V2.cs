using Atelia.DurableGraph;

namespace PackageConsumerProbe;

[DurableType("probe.package-character", 2)]
internal sealed partial class Character : DurableBase {
    [DurableField(1)]
    private int _score;

    [DurableField(2)]
    private bool _isActive;

    private static int UpgradeCalls { get; set; }

    public static string ExerciseGeneratedState() {
        __DurableState.V1 old = new(7);
        UpgradeStateV1ToV2(in old, out __DurableState.V2 first);
        UpgradeStateV1ToV2(in old, out __DurableState.V2 second);
        if (!ReferenceEquals(__DurableState.V1.Schema, GetSchema(1)) ||
            !ReferenceEquals(__DurableState.V2.Schema, Schema) ||
            first.Segment0Field1 != 7 || !first.Segment0Field2 ||
            !first.Equals(second)) {
            throw new InvalidOperationException("Generated State history or explicit upgrade shape is invalid.");
        }

        return $"{first.Segment0Field1}:{first.Segment0Field2}:{UpgradeCalls}";
    }

    private static void UpgradeStateV1ToV2(
        in __DurableState.V1 oldValue,
        out __DurableState.V2 newValue) {
        UpgradeCalls++;
        newValue = new __DurableState.V2(oldValue.Segment0Field1, true);
    }
}
