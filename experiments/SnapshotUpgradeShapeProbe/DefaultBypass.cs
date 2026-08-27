#pragma warning disable CS0649 // Probe intentionally observes default fields.

namespace SnapshotUpgradeShapeProbe;

internal struct SnapshotV1 {
}

internal struct SnapshotV2 {
    public string Field1;

    public bool Field2;
}

internal static class Upgrades {
    public static void UpgradeV1ToV2(
        in SnapshotV1 oldValue,
        out SnapshotV2 newValue) {
        newValue = default;
    }
}

internal static class Program {
    private static int Main() {
        SnapshotV1 oldValue = default;
        Upgrades.UpgradeV1ToV2(in oldValue, out SnapshotV2 newValue);

        if (newValue.Field1 is not null || newValue.Field2) {
            return 1;
        }

        Console.WriteLine("default bypasses field-by-field assignment checking");
        return 0;
    }
}
