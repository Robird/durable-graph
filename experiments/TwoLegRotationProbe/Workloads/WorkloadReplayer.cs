namespace Atelia.TwoLegRotationProbe.Workloads;

internal static class WorkloadReplayer {
    public static IReadOnlyDictionary<uint, LogicalObjectState> Replay(WorkloadTrace trace) {
        ArgumentNullException.ThrowIfNull(trace);

        WorkloadReplayCursor cursor = new();
        foreach (SaveStep step in trace.Steps) {
            cursor.Apply(step);
        }

        return cursor.Snapshot();
    }
}
