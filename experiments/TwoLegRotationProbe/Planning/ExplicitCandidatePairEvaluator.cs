using Atelia.TwoLegRotationProbe.Encoding;
using Atelia.TwoLegRotationProbe.Model;

namespace Atelia.TwoLegRotationProbe.Planning;

internal static class ExplicitCandidatePairEvaluator {
    public static ExplicitCandidatePairEvaluation Evaluate(
        RbfFileStore store,
        NormalizedSaveFacts facts,
        StayBSaveDecision stayBDecision,
        RotateCSaveDecision rotateCDecision) {
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(facts);
        ArgumentNullException.ThrowIfNull(stayBDecision);
        ArgumentNullException.ThrowIfNull(rotateCDecision);

        ExactCandidateAttempt<StayBRevisionPlan> stayBAttempt = AttemptStayB(
            store,
            facts,
            stayBDecision);
        ExactCandidateAttempt<RotateCRevisionPlan> rotateCAttempt = AttemptRotateC(
            store,
            facts,
            rotateCDecision);
        return new ExplicitCandidatePairEvaluation(
            facts,
            stayBDecision,
            rotateCDecision,
            stayBAttempt,
            rotateCAttempt);
    }

    private static ExactCandidateAttempt<StayBRevisionPlan> AttemptStayB(
        RbfFileStore store,
        NormalizedSaveFacts facts,
        StayBSaveDecision decision) {
        StayBRevisionPlan plan;
        try {
            plan = StayBRevisionPlanner.Create(store, facts, decision);
        } catch (RevisionCandidateCapacityException exception) {
            return new CapacityRejectedCandidate<StayBRevisionPlan>(
                exception.Rejection);
        }

        CandidateRawObservation observation = CandidateRawObservationBuilder.Create(
            store,
            facts,
            CandidateTarget.StayB,
            plan.Revision);
        return new FeasibleCandidate<StayBRevisionPlan>(plan, observation);
    }

    private static ExactCandidateAttempt<RotateCRevisionPlan> AttemptRotateC(
        RbfFileStore store,
        NormalizedSaveFacts facts,
        RotateCSaveDecision decision) {
        RotateCRevisionPlan plan;
        try {
            plan = RotateCRevisionPlanner.Create(
                store,
                facts,
                decision);
        } catch (RevisionCandidateCapacityException exception) {
            return new CapacityRejectedCandidate<RotateCRevisionPlan>(
                exception.Rejection);
        }

        CandidateRawObservation observation = CandidateRawObservationBuilder.Create(
            store,
            facts,
            CandidateTarget.RotateC,
            plan.Revision);
        return new FeasibleCandidate<RotateCRevisionPlan>(plan, observation);
    }
}
