using Atelia.TwoLegRotationProbe.Encoding;
using Atelia.TwoLegRotationProbe.Model;
using Atelia.TwoLegRotationProbe.Planning;

namespace Atelia.TwoLegRotationProbe.Policies;

/// <summary>
/// Stateless probe-only orchestration for applying one caller-selected member of an
/// exact candidate pair. It owns neither policy choice nor a durable publication head.
/// </summary>
internal static class ExplicitRotationPolicyStepHarness {
    public static RotationPolicyStepAttempt TryApplySelected(
        RbfFileStore store,
        ProbeRevisionCursor cursor,
        ExplicitCandidatePairEvaluation evaluation,
        CandidateTarget selectedTarget) {
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(cursor);
        ArgumentNullException.ThrowIfNull(evaluation);

        return selectedTarget switch {
            CandidateTarget.StayB => TryApplyStayB(store, cursor, evaluation),
            CandidateTarget.RotateC => ApplyRotateC(store, cursor, evaluation),
            _ => throw new ArgumentOutOfRangeException(
                nameof(selectedTarget),
                selectedTarget,
                "Unknown explicit candidate target."),
        };
    }

    private static RotationPolicyStepAttempt TryApplyStayB(
        RbfFileStore store,
        ProbeRevisionCursor cursor,
        ExplicitCandidatePairEvaluation evaluation) {
        if (evaluation.StayBAttempt is
            CapacityRejectedCandidate<StayBRevisionPlan> rejected) {
            return new SelectedPolicyCandidateCapacityRejected(
                evaluation,
                CandidateTarget.StayB,
                rejected.Rejection);
        }

        FeasibleCandidate<StayBRevisionPlan> selected =
            evaluation.StayBAttempt as FeasibleCandidate<StayBRevisionPlan>
            ?? throw new InvalidDataException(
                "The exact Stay-B attempt has an unknown result kind.");
        CanPrepareAndRotateAttempt completion =
            CanPrepareAndRotateCertificatePlanner.TryCreate(
                store,
                cursor,
                selected);
        if (completion is CanPrepareAndRotateRejectedUnproven unproven) {
            return new StayBPolicyCompletionRejectedUnproven(
                evaluation,
                selected,
                unproven.Rejection);
        }

        CanPrepareAndRotateCertificate certificate =
            (completion as CanPrepareAndRotateProven)?.Certificate
            ?? throw new InvalidDataException(
                "The completion proof has an unknown result kind.");
        if (!ReferenceEquals(certificate.InitialStayB, selected)) {
            throw new InvalidDataException(
                "The completion certificate does not retain the pair's exact Stay-B candidate.");
        }

        ProbeRevisionCursor result = ExplicitProbeRevisionApplier.ApplyStayB(
            store,
            cursor,
            certificate.InitialStayB);
        return new AppliedStayBPolicyStep(
            evaluation,
            selected,
            certificate,
            result);
    }

    private static RotationPolicyStepAttempt ApplyRotateC(
        RbfFileStore store,
        ProbeRevisionCursor cursor,
        ExplicitCandidatePairEvaluation evaluation) {
        if (evaluation.RotateCAttempt is
            CapacityRejectedCandidate<RotateCRevisionPlan> rejected) {
            return new SelectedPolicyCandidateCapacityRejected(
                evaluation,
                CandidateTarget.RotateC,
                rejected.Rejection);
        }

        FeasibleCandidate<RotateCRevisionPlan> selected =
            evaluation.RotateCAttempt as FeasibleCandidate<RotateCRevisionPlan>
            ?? throw new InvalidDataException(
                "The exact Rotate-C attempt has an unknown result kind.");
        ProbeRevisionCursor result = ExplicitProbeRevisionApplier.ApplyRotateC(
            store,
            cursor,
            selected);
        return new AppliedRotateCPolicyStep(
            evaluation,
            selected,
            result);
    }
}

internal abstract record RotationPolicyStepAttempt(
    ExplicitCandidatePairEvaluation Evaluation);

internal sealed record AppliedStayBPolicyStep(
    ExplicitCandidatePairEvaluation Evaluation,
    FeasibleCandidate<StayBRevisionPlan> Selected,
    CanPrepareAndRotateCertificate CompletionCertificate,
    ProbeRevisionCursor ResultCursor) : RotationPolicyStepAttempt(Evaluation);

internal sealed record AppliedRotateCPolicyStep(
    ExplicitCandidatePairEvaluation Evaluation,
    FeasibleCandidate<RotateCRevisionPlan> Selected,
    ProbeRevisionCursor ResultCursor) : RotationPolicyStepAttempt(Evaluation);

internal sealed record SelectedPolicyCandidateCapacityRejected(
    ExplicitCandidatePairEvaluation Evaluation,
    CandidateTarget SelectedTarget,
    RevisionCandidateCapacityRejection Rejection) :
    RotationPolicyStepAttempt(Evaluation);

internal sealed record StayBPolicyCompletionRejectedUnproven(
    ExplicitCandidatePairEvaluation Evaluation,
    FeasibleCandidate<StayBRevisionPlan> Selected,
    CanPrepareAndRotateRejection Rejection) : RotationPolicyStepAttempt(Evaluation);
