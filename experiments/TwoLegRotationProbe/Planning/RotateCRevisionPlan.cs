using Atelia.TwoLegRotationProbe.Rotation;

namespace Atelia.TwoLegRotationProbe.Planning;

/// <summary>
/// Pure Rotate-C candidate tied to one normalized source snapshot and one explicit decision.
/// </summary>
internal sealed class RotateCRevisionPlan {
    internal RotateCRevisionPlan(
        NormalizedSaveFacts facts,
        RotateCSaveDecision decision,
        PlannedRevisionV0 revision) {
        Facts = facts ?? throw new ArgumentNullException(nameof(facts));
        Decision = decision ?? throw new ArgumentNullException(nameof(decision));
        Revision = revision ?? throw new ArgumentNullException(nameof(revision));
    }

    public NormalizedSaveFacts Facts { get; }

    public RotateCSaveDecision Decision { get; }

    public PlannedRevisionV0 Revision { get; }
}
