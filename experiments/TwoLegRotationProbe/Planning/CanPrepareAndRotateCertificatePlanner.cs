using Atelia.TwoLegRotationProbe.Model;

namespace Atelia.TwoLegRotationProbe.Planning;

/// <summary>
/// Proves one fixed, finite completion path on an exact scratch fork. It explores only
/// ascending single-object A-debt migration prefixes and never mutates the caller Store.
/// </summary>
internal static class CanPrepareAndRotateCertificatePlanner {
    public static CanPrepareAndRotateAttempt TryCreate(
        RbfFileStore store,
        ProbeRevisionCursor initialCursor,
        FeasibleCandidate<StayBRevisionPlan> initialStayB) {
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(initialCursor);
        ArgumentNullException.ThrowIfNull(initialStayB);

        ValidateInitialIdentity(initialStayB);
        RbfFileStore scratch = store.ForkForProbe();
        ProbeRevisionCursor continuationCursor =
            ExplicitProbeRevisionApplier.ApplyStayB(
                scratch,
                initialCursor,
                initialStayB);
        CanonicalTerminalSettlementAttempt continuation =
            CanonicalTerminalSettlementPlanner.TryCreateOnScratch(
                scratch,
                continuationCursor);
        if (continuation is
            CanonicalTerminalSettlementRejectedUnproven rejected) {
            return new CanPrepareAndRotateRejectedUnproven(rejected.Rejection);
        }

        CanonicalTerminalSettlementCertificate certificate =
            (continuation as CanonicalTerminalSettlementProven)?.Certificate
            ?? throw new InvalidDataException(
                "The terminal continuation proof has an unknown result kind.");
        return new CanPrepareAndRotateProven(
            new CanPrepareAndRotateCertificate(
                initialCursor,
                initialStayB,
                certificate.MaintenanceStayBSteps,
                certificate.FinalRotateC));
    }

    private static void ValidateInitialIdentity(
        FeasibleCandidate<StayBRevisionPlan> initialStayB) {
        StayBRevisionPlan plan = initialStayB.Plan ?? throw new ArgumentException(
            "The initial feasible Stay-B selection has no plan.",
            nameof(initialStayB));
        CandidateRawObservation observation = initialStayB.Observation ??
            throw new ArgumentException(
                "The initial feasible Stay-B selection has no observation.",
                nameof(initialStayB));
        if (observation.Target != CandidateTarget.StayB ||
            !ReferenceEquals(observation.Facts, plan.Facts) ||
            !ReferenceEquals(observation.Candidate, plan.Revision)) {
            throw new ArgumentException(
                "The initial feasible Stay-B selection does not retain exact facts and " +
                "candidate identity.",
                nameof(initialStayB));
        }
    }
}
