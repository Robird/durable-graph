namespace SnapshotUpgradeShapeProbe;

internal struct SnapshotV1 {
    public string Field1;
}

internal struct SnapshotV2 {
    public string Field1;

    public bool Field2;
}

internal struct SnapshotV3 {
    public string Field1;

    public bool Field2;

    public int Field3;
}

internal static partial class Upgrades {
    public static partial void UpgradeV1ToV2(
        in SnapshotV1 oldValue,
        out SnapshotV2 newValue);

    public static partial void Upgrade(
        in SnapshotV1 oldValue,
        out SnapshotV2 newValue);

    public static partial void Upgrade(
        in SnapshotV1 oldValue,
        out SnapshotV3 newValue);
}

internal static partial class Upgrades {
    public static partial void UpgradeV1ToV2(
        in SnapshotV1 oldValue,
        out SnapshotV2 newValue) {
        newValue.Field1 = oldValue.Field1;
        newValue.Field2 = true;
    }

    public static partial void Upgrade(
        in SnapshotV1 oldValue,
        out SnapshotV2 newValue) {
        UpgradeV1ToV2(in oldValue, out newValue);
    }

    public static partial void Upgrade(
        in SnapshotV1 oldValue,
        out SnapshotV3 newValue) {
        newValue.Field1 = oldValue.Field1;
        newValue.Field2 = true;
        newValue.Field3 = 3;
    }
}

internal static class Program {
    private static int Main() {
        SnapshotV1 oldValue = new() {
            Field1 = "Ada",
        };

        Upgrades.UpgradeV1ToV2(in oldValue, out SnapshotV2 adjacent);

        SnapshotV3 jump;
        Upgrades.Upgrade(in oldValue, out jump);

        if (adjacent.Field1 != "Ada" ||
            !adjacent.Field2 ||
            jump.Field1 != "Ada" ||
            !jump.Field2 ||
            jump.Field3 != 3) {
            return 1;
        }

        Console.WriteLine("struct in/out upgrade probe succeeded");
        return 0;
    }
}
