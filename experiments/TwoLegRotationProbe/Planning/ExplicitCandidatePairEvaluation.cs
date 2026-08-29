namespace Atelia.TwoLegRotationProbe.Planning;

/// <summary>
/// Independent exact attempts for two caller-explicit actions over the same normalized Save.
/// This value deliberately contains no winner, score, or fallback.
/// </summary>
internal sealed class ExplicitCandidatePairEvaluation {
    internal ExplicitCandidatePairEvaluation(
        NormalizedSaveFacts facts,
        StayBSaveDecision stayBDecision,
        RotateCSaveDecision rotateCDecision,
        ExactCandidateAttempt<StayBRevisionPlan> stayBAttempt,
        ExactCandidateAttempt<RotateCRevisionPlan> rotateCAttempt) {
        Facts = facts ?? throw new ArgumentNullException(nameof(facts));
        StayBDecision = stayBDecision ??
            throw new ArgumentNullException(nameof(stayBDecision));
        RotateCDecision = rotateCDecision ??
            throw new ArgumentNullException(nameof(rotateCDecision));
        StayBAttempt = stayBAttempt ??
            throw new ArgumentNullException(nameof(stayBAttempt));
        RotateCAttempt = rotateCAttempt ??
            throw new ArgumentNullException(nameof(rotateCAttempt));

        ValidateStayBIdentity();
        ValidateRotateCIdentity();
    }

    public NormalizedSaveFacts Facts { get; }

    public StayBSaveDecision StayBDecision { get; }

    public RotateCSaveDecision RotateCDecision { get; }

    public ExactCandidateAttempt<StayBRevisionPlan> StayBAttempt { get; }

    public ExactCandidateAttempt<RotateCRevisionPlan> RotateCAttempt { get; }

    private void ValidateStayBIdentity() {
        if (StayBAttempt is not FeasibleCandidate<StayBRevisionPlan> feasible) {
            return;
        }

        if (feasible.Plan is null || feasible.Observation is null ||
            !ReferenceEquals(feasible.Plan.Facts, Facts) ||
            !ReferenceEquals(feasible.Plan.Decision, StayBDecision) ||
            !ReferenceEquals(feasible.Plan.Revision, feasible.Observation.Candidate) ||
            feasible.Observation.Target != CandidateTarget.StayB) {
            throw new ArgumentException(
                "Feasible Stay-B attempt does not retain the pair's exact input and candidate identity.",
                nameof(StayBAttempt));
        }
    }

    private void ValidateRotateCIdentity() {
        if (RotateCAttempt is not FeasibleCandidate<RotateCRevisionPlan> feasible) {
            return;
        }

        if (feasible.Plan is null || feasible.Observation is null ||
            !ReferenceEquals(feasible.Plan.Facts, Facts) ||
            !ReferenceEquals(feasible.Plan.Decision, RotateCDecision) ||
            !ReferenceEquals(feasible.Plan.Revision, feasible.Observation.Candidate) ||
            feasible.Observation.Target != CandidateTarget.RotateC) {
            throw new ArgumentException(
                "Feasible Rotate-C attempt does not retain the pair's exact input and candidate identity.",
                nameof(RotateCAttempt));
        }
    }
}
