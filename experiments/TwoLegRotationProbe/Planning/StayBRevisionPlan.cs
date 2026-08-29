using Atelia.TwoLegRotationProbe.Rotation;

namespace Atelia.TwoLegRotationProbe.Planning;

/// <summary>
/// Pure Stay-B candidate tied to one normalized source snapshot and one explicit decision.
/// </summary>
internal sealed class StayBRevisionPlan {
    internal StayBRevisionPlan(
        NormalizedSaveFacts facts,
        StayBSaveDecision decision,
        PlannedRevisionV0 revision) {
        Facts = facts ?? throw new ArgumentNullException(nameof(facts));
        Decision = decision ?? throw new ArgumentNullException(nameof(decision));
        Revision = revision ?? throw new ArgumentNullException(nameof(revision));
    }

    public NormalizedSaveFacts Facts { get; }

    public StayBSaveDecision Decision { get; }

    public PlannedRevisionV0 Revision { get; }
}
