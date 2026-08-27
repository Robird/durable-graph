namespace SnapshotUpgradeShapeProbe;

internal struct SnapshotV1 {
    public string Field1;
}

internal struct SnapshotV2 {
    public string Field1;
}

internal static partial class Upgrades {
    public static partial void UpgradeV1ToV2(
        in SnapshotV1 oldValue,
        out SnapshotV2 newValue);
}

internal static class Program {
    private static void Main() {
    }
}
