namespace SnapshotUpgradeShapeProbe;

internal struct SnapshotV1 {
}

internal struct SnapshotV2 {
}

internal struct SnapshotV3 {
}

internal static class Upgrades {
    public static void Upgrade(in SnapshotV1 oldValue, out SnapshotV2 newValue) {
        newValue = default;
    }

    public static void Upgrade(in SnapshotV1 oldValue, out SnapshotV3 newValue) {
        newValue = default;
    }
}

internal static class Program {
    private static void Main() {
        SnapshotV1 oldValue = default;
        Upgrades.Upgrade(in oldValue, out var newValue);
    }
}
