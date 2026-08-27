using Atelia.DurableGraph;

namespace PackageConsumerProbe;

[DurableType("probe.package-character", 1)]
internal sealed partial class Character : DurableBase {
    [DurableField(1)]
    private string _displayName = string.Empty;

    public static string ExerciseGeneratedSnapshots() {
        __DurableSnapshotV1 snapshot = new() {
            Field1 = "V1",
        };

        return snapshot.Field1;
    }
}
