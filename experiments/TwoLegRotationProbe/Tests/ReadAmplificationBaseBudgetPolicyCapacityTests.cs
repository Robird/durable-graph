using Atelia.TwoLegRotationProbe.Encoding;
using Atelia.TwoLegRotationProbe.Planning;
using Atelia.TwoLegRotationProbe.Policies;
using Atelia.TwoLegRotationProbe.Workloads;

namespace Atelia.TwoLegRotationProbe.Tests;

public sealed partial class GroupedForegroundBurstCapacityCouplingTests {
    [Fact]
    public void Adaptive_policy_selected_Rotate_capacity_rejection_does_not_fallback() {
        BurstFixture source = CreateSource();
        StoreSnapshot beforeEvaluation = CaptureStore(source.Store);

        NormalizedSaveFacts facts = SaveStepNormalizer.Normalize(
            source.Store,
            source.Current.FileNumber,
            source.PublishedRevisionAddress,
            new SaveStep([
                new UpdateObject(
                    FirstUpdateObjectId,
                    UpdateBasePayloadBytes,
                    DeltaPayloadBytes),
                new UpdateObject(
                    SecondUpdateObjectId,
                    UpdateBasePayloadBytes,
                    DeltaPayloadBytes),
                new UpdateObject(
                    ThirdUpdateObjectId,
                    UpdateBasePayloadBytes,
                    DeltaPayloadBytes),
            ]));
        ReadAmplificationBaseBudgetPolicyProjection projection =
            ReadAmplificationBaseBudgetPolicyProjection.Create(facts);
        ReadAmplificationBaseBudgetPolicySelection selection =
            ReadAmplificationBaseBudgetPolicy.Select(
                projection,
                new ReadAmplificationBaseBudgetPolicyParameters(
                    readAmplificationLimit: 1m,
                    baseBudgetFraction: 1m));

        Assert.Same(facts, projection.Facts);
        Assert.Equal(CandidateTarget.RotateC, selection.Target);
        Assert.Equal(
            [FirstUpdateObjectId, SecondUpdateObjectId, ThirdUpdateObjectId],
            selection.RotateC.BContainedUpdateDecisions.Select(
                static decision => decision.ObjectId));
        Assert.All(
            selection.RotateC.BContainedUpdateDecisions,
            static decision => Assert.Equal(
                UpdateWriteMode.Base,
                decision.Mode));
        Assert.Empty(selection.RotateC.BContainedNoChangeBaseObjectIds);
        Assert.Contains(
            projection.PostLiveObjects,
            fact => fact.ObjectId == DebtObjectId && fact.IsADependent);

        ExplicitCandidatePairEvaluation evaluation =
            ExplicitCandidatePairEvaluator.Evaluate(
                source.Store,
                facts,
                selection.StayB,
                selection.RotateC);
        CapacityRejectedCandidate<RotateCRevisionPlan> rejectedRotate =
            Assert.IsType<CapacityRejectedCandidate<RotateCRevisionPlan>>(
                evaluation.RotateCAttempt);
        Assert.Equal(
            RevisionCandidateCapacityLimit.PayloadAndTailMetaLength,
            rejectedRotate.Rejection.Limit);
        Assert.True(
            rejectedRotate.Rejection.AttemptedValue >
            rejectedRotate.Rejection.MaximumValue);
        AssertStoreSnapshot(beforeEvaluation, CaptureStore(source.Store));

        SelectedPolicyCandidateCapacityRejected outcome =
            Assert.IsType<SelectedPolicyCandidateCapacityRejected>(
                ExplicitRotationPolicyStepHarness.TryApplySelected(
                    source.Store,
                    source.Cursor,
                    evaluation,
                    selection.Target));

        Assert.Same(evaluation, outcome.Evaluation);
        Assert.Equal(selection.Target, outcome.SelectedTarget);
        Assert.Equal(rejectedRotate.Rejection, outcome.Rejection);
        AssertStoreSnapshot(beforeEvaluation, CaptureStore(source.Store));
    }
}
