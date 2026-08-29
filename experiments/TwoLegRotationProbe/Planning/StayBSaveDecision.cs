using System.Collections.ObjectModel;

namespace Atelia.TwoLegRotationProbe.Planning;

/// <summary>
/// Caller-explicit write choices for one Stay-B Save. This is an action value,
/// not a heuristic or policy result.
/// </summary>
internal sealed class StayBSaveDecision {
    private readonly ReadOnlyCollection<UpdateWriteDecision> _updateDecisions;
    private readonly ReadOnlyCollection<uint> _unchangedMigrationObjectIds;

    public StayBSaveDecision(
        IEnumerable<UpdateWriteDecision> updateDecisions,
        IEnumerable<uint> unchangedMigrationObjectIds) {
        ArgumentNullException.ThrowIfNull(updateDecisions);
        ArgumentNullException.ThrowIfNull(unchangedMigrationObjectIds);

        UpdateWriteDecision[] canonicalUpdateDecisions = updateDecisions
            .OrderBy(static decision => decision.ObjectId)
            .ToArray();
        for (int index = 0; index < canonicalUpdateDecisions.Length; index++) {
            UpdateWriteDecision decision = canonicalUpdateDecisions[index];
            if (!Enum.IsDefined(decision.Mode)) {
                throw new ArgumentOutOfRangeException(
                    nameof(updateDecisions),
                    decision.Mode,
                    $"Object {decision.ObjectId} has an invalid Update write mode.");
            }

            if (index > 0 &&
                canonicalUpdateDecisions[index - 1].ObjectId == decision.ObjectId) {
                throw new ArgumentException(
                    $"Update write decisions contain duplicate ObjectId {decision.ObjectId}.",
                    nameof(updateDecisions));
            }
        }

        uint[] canonicalMigrationObjectIds = unchangedMigrationObjectIds
            .Order()
            .ToArray();
        for (int index = 1; index < canonicalMigrationObjectIds.Length; index++) {
            if (canonicalMigrationObjectIds[index - 1] ==
                canonicalMigrationObjectIds[index]) {
                throw new ArgumentException(
                    $"Unchanged migration decisions contain duplicate ObjectId " +
                    $"{canonicalMigrationObjectIds[index]}.",
                    nameof(unchangedMigrationObjectIds));
            }
        }

        _updateDecisions = Array.AsReadOnly(canonicalUpdateDecisions);
        _unchangedMigrationObjectIds = Array.AsReadOnly(
            canonicalMigrationObjectIds);
    }

    public IReadOnlyList<UpdateWriteDecision> UpdateDecisions => _updateDecisions;

    public IReadOnlyList<uint> UnchangedMigrationObjectIds =>
        _unchangedMigrationObjectIds;
}
