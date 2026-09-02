using System.Collections.ObjectModel;

namespace Atelia.MultiSegmentStateStoreProbe.Workloads;

internal sealed class SaveStep {
    private readonly ReadOnlyCollection<WorkloadChange> _changes;

    public SaveStep(IEnumerable<WorkloadChange> changes) {
        ArgumentNullException.ThrowIfNull(changes);

        WorkloadChange[] canonical = changes
            .Select(static change => change ?? throw new ArgumentException(
                "A Save step cannot contain a null change.",
                nameof(changes)))
            .OrderBy(static change => change.ObjectId)
            .ToArray();
        if (canonical.Length == 0) {
            throw new ArgumentException("A Save step must contain at least one change.", nameof(changes));
        }

        for (int index = 1; index < canonical.Length; index++) {
            if (canonical[index - 1].ObjectId == canonical[index].ObjectId) {
                throw new ArgumentException(
                    $"Object {canonical[index].ObjectId} occurs more than once in one Save step.",
                    nameof(changes));
            }
        }

        _changes = Array.AsReadOnly(canonical);
    }

    public IReadOnlyList<WorkloadChange> Changes => _changes;
}
