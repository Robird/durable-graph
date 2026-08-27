#pragma warning disable CS0649 // Probe intentionally leaves fields unassigned.

namespace SnapshotUpgradeShapeProbe;

internal struct SnapshotV1 {
    public string Field1;
}

internal struct SnapshotV2 {
    public string Field1;
}

internal static class Upgrades {
    public static bool TryUpgradeV1ToV2(
        in SnapshotV1 oldValue,
        out SnapshotV2 newValue) {
        if (oldValue.Field1.Length == 0) {
            return false;
        }

        newValue.Field1 = oldValue.Field1;
        return true;
    }
}

internal static class Program {
    private static void Main() {
    }
}
