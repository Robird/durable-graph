namespace SnapshotUpgradeShapeProbe;

internal struct SnapshotV1 {
}

internal struct SnapshotV2 {
}

internal struct SnapshotV3 {
}

internal static class Upgrades {
    public static SnapshotV2 Upgrade(SnapshotV1 oldValue) {
        return default;
    }

    public static SnapshotV3 Upgrade(SnapshotV1 oldValue) {
        return default;
    }
}

internal static class Program {
    private static void Main() {
    }
}
