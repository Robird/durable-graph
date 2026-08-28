using System.Collections.ObjectModel;

namespace Atelia.TwoLegRotationProbe.Workloads;

internal sealed class SaveStep {
    private readonly ReadOnlyCollection<WorkloadChange> _changes;

    public SaveStep(IEnumerable<WorkloadChange> changes) {
        ArgumentNullException.ThrowIfNull(changes);

        WorkloadChange[] canonicalChanges = changes
            .Select(static change => change ?? throw new ArgumentException(
                "A save step cannot contain a null change.",
                nameof(changes)))
            .OrderBy(static change => change.ObjectId)
            .ToArray();

        if (canonicalChanges.Length == 0) {
            throw new ArgumentException("A save step must contain at least one change.", nameof(changes));
        }

        for (int index = 1; index < canonicalChanges.Length; index++) {
            if (canonicalChanges[index - 1].ObjectId == canonicalChanges[index].ObjectId) {
                throw new ArgumentException(
                    $"Object {canonicalChanges[index].ObjectId} occurs more than once in one save step.",
                    nameof(changes));
            }
        }

        _changes = Array.AsReadOnly(canonicalChanges);
    }

    public IReadOnlyList<WorkloadChange> Changes => _changes;
}
