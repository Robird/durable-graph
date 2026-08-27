using Atelia.DurableGraph;

namespace PackageConsumerProbe;

[DurableType("probe.package-character", 2)]
internal sealed partial class Character : DurableBase {
    [DurableField(1)]
    private string _displayName = string.Empty;

    [DurableField(2)]
    private bool _isActive;

    public static string ExerciseGeneratedSnapshots() {
        __DurableSnapshotV1 oldValue = new() {
            Field1 = "V1",
        };
        UpgradeV1ToV2(in oldValue, out __DurableSnapshotV2 newValue);
        return $"{newValue.Field1}:{newValue.Field2}";
    }

    private static void UpgradeV1ToV2(
        in __DurableSnapshotV1 oldValue,
        out __DurableSnapshotV2 newValue) {
        newValue = new __DurableSnapshotV2 {
            Field1 = oldValue.Field1,
            Field2 = true,
        };
    }
}
