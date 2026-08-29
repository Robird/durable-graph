using System.Collections.ObjectModel;

namespace Atelia.TwoLegRotationProbe.Planning;

/// <summary>
/// Caller-explicit choices for the actions that remain optional in one Rotate-C Save.
/// A-dependent actions and full-OVD coverage are enforced by the planner.
/// </summary>
internal sealed class RotateCSaveDecision {
    private readonly ReadOnlyCollection<UpdateWriteDecision>
        _bContainedUpdateDecisions;
    private readonly ReadOnlyCollection<uint> _bContainedNoChangeBaseObjectIds;

    public RotateCSaveDecision(
        IEnumerable<UpdateWriteDecision> bContainedUpdateDecisions,
        IEnumerable<uint> bContainedNoChangeBaseObjectIds) {
        ArgumentNullException.ThrowIfNull(bContainedUpdateDecisions);
        ArgumentNullException.ThrowIfNull(bContainedNoChangeBaseObjectIds);

        UpdateWriteDecision[] canonicalUpdateDecisions = bContainedUpdateDecisions
            .OrderBy(static decision => decision.ObjectId)
            .ToArray();
        for (int index = 0; index < canonicalUpdateDecisions.Length; index++) {
            UpdateWriteDecision decision = canonicalUpdateDecisions[index];
            if (!Enum.IsDefined(decision.Mode)) {
                throw new ArgumentOutOfRangeException(
                    nameof(bContainedUpdateDecisions),
                    decision.Mode,
                    $"Object {decision.ObjectId} has an invalid Update write mode.");
            }

            if (index > 0 &&
                canonicalUpdateDecisions[index - 1].ObjectId == decision.ObjectId) {
                throw new ArgumentException(
                    $"B-contained Update decisions contain duplicate ObjectId " +
                    $"{decision.ObjectId}.",
                    nameof(bContainedUpdateDecisions));
            }
        }

        uint[] canonicalNoChangeBaseObjectIds = bContainedNoChangeBaseObjectIds
            .Order()
            .ToArray();
        for (int index = 1; index < canonicalNoChangeBaseObjectIds.Length; index++) {
            if (canonicalNoChangeBaseObjectIds[index - 1] ==
                canonicalNoChangeBaseObjectIds[index]) {
                throw new ArgumentException(
                    $"B-contained NoChange Base decisions contain duplicate ObjectId " +
                    $"{canonicalNoChangeBaseObjectIds[index]}.",
                    nameof(bContainedNoChangeBaseObjectIds));
            }
        }

        _bContainedUpdateDecisions = Array.AsReadOnly(canonicalUpdateDecisions);
        _bContainedNoChangeBaseObjectIds = Array.AsReadOnly(
            canonicalNoChangeBaseObjectIds);
    }

    public IReadOnlyList<UpdateWriteDecision> BContainedUpdateDecisions =>
        _bContainedUpdateDecisions;

    public IReadOnlyList<uint> BContainedNoChangeBaseObjectIds =>
        _bContainedNoChangeBaseObjectIds;
}
