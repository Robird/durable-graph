namespace SnapshotUpgradeShapeProbe;

internal struct Snapshot {
    public string Field1;
}

internal static class Program {
    private static unsafe void Main() {
        Snapshot* snapshots = stackalloc Snapshot[1];
        snapshots[0].Field1 = "Ada";
    }
}
