using Atelia.TwoLegRotationProbe.Benchmarking;
using Atelia.TwoLegRotationProbe.Planning;
using Atelia.TwoLegRotationProbe.Policies;
using Atelia.TwoLegRotationProbe.Workloads;

namespace Atelia.TwoLegRotationProbe.Tests;

public sealed partial class RotationPolicyComparisonTests {
    private const uint ProfileBindingHotObjectId = 10;
    private const uint ProfileBindingBallastObjectId = 20;
    private const uint ProfileBindingSentinelObjectId = 1001;

    [Fact]
    public void Adaptive_benchmark_profiles_bind_their_declared_parameters_at_a_divergent_checkpoint() {
        PolicySource source = CreateSource([
            new InitialObjectSeed(ProfileBindingHotObjectId, 100),
            new InitialObjectSeed(ProfileBindingBallastObjectId, 2400),
        ]);
        ProbeRevisionCursor cursor = CreateCursor(source);
        Dictionary<uint, LogicalObjectState> expectedState = new(
            source.InitialExpectedState);

        ApplyExactStaySetup(
            source.Store,
            ref cursor,
            expectedState,
            new SaveStep([
                new CreateObject(ProfileBindingSentinelObjectId, 1),
            ]),
            updateDecisions: [],
            migrationObjectIds: [
                ProfileBindingHotObjectId,
                ProfileBindingBallastObjectId,
            ]);
        ApplyExactStaySetup(
            source.Store,
            ref cursor,
            expectedState,
            new SaveStep([
                new RemoveObject(ProfileBindingSentinelObjectId),
                new UpdateObject(
                    ProfileBindingHotObjectId,
                    ResultBasePayloadBytes: 100,
                    DeltaPayloadBytes: 50),
            ]),
            [new UpdateWriteDecision(
                ProfileBindingHotObjectId,
                UpdateWriteMode.Delta)],
            migrationObjectIds: []);
        for (int index = 0; index < 3; index++) {
            ApplyExactStaySetup(
                source.Store,
                ref cursor,
                expectedState,
                new SaveStep([
                    new UpdateObject(
                        ProfileBindingHotObjectId,
                        ResultBasePayloadBytes: 100,
                        DeltaPayloadBytes: 50),
                ]),
                [new UpdateWriteDecision(
                    ProfileBindingHotObjectId,
                    UpdateWriteMode.Delta)],
                migrationObjectIds: []);
        }

        NormalizedSaveFacts facts = SaveStepNormalizer.Normalize(
            source.Store,
            cursor.FileScope.CurrentFileNumber,
            cursor.PublishedRevisionAddress,
            new SaveStep([
                new UpdateObject(
                    ProfileBindingHotObjectId,
                    ResultBasePayloadBytes: 100,
                    DeltaPayloadBytes: 50),
            ]));
        ReadAmplificationBaseBudgetPolicyProjection projection =
            ReadAmplificationBaseBudgetPolicyProjection.Create(facts);
        ReadAmplificationBaseBudgetPolicyObjectFact hot = projection
            .PostLiveObjects
            .Single(static fact =>
                fact.ObjectId == ProfileBindingHotObjectId);

        Assert.False(hot.IsADependent);
        Assert.Equal(300, hot.SourceHeadReconstructionObjectPayloadBytes);
        Assert.Equal(350, hot.ReadAmplificationNumeratorBytes);
        Assert.Equal(100, hot.PostSaveBasePayloadBytes);
        Assert.Equal(50, hot.DeltaPayloadBytes);
        Assert.Equal(2500, projection.PostLiveGraphBasePayloadBytes);
        Assert.Equal(0, projection.ADependentEvacuationBasePayloadBytes);

        AssertAdaptiveProfileParameters(
            BenchmarkV1SelectionProfiles.ReadAmplificationBaseBudgetR3B5Percent,
            expectedReadAmplificationLimit: 3m,
            expectedBaseBudgetFraction: 0.05m);
        AssertAdaptiveProfileParameters(
            BenchmarkV1SelectionProfiles.ReadAmplificationBaseBudgetR4B4Percent,
            expectedReadAmplificationLimit: 4m,
            expectedBaseBudgetFraction: 0.04m);

        ReadAmplificationBaseBudgetPolicySelection directR3B5 =
            ReadAmplificationBaseBudgetPolicy.Select(
                projection,
                new ReadAmplificationBaseBudgetPolicyParameters(3m, 0.05m));
        ReadAmplificationBaseBudgetPolicySelection directR4B4 =
            ReadAmplificationBaseBudgetPolicy.Select(
                projection,
                new ReadAmplificationBaseBudgetPolicyParameters(4m, 0.04m));
        BenchmarkV1StepSelection profileR3B5 =
            BenchmarkV1SelectionProfileSelector.Select(
                BenchmarkV1SelectionProfiles
                    .ReadAmplificationBaseBudgetR3B5Percent.Identity,
                facts);
        BenchmarkV1StepSelection profileR4B4 =
            BenchmarkV1SelectionProfileSelector.Select(
                BenchmarkV1SelectionProfiles
                    .ReadAmplificationBaseBudgetR4B4Percent.Identity,
                facts);

        Assert.Equal(125, directR3B5.PreferredBasePayloadBudgetBytes);
        Assert.Equal(100, directR4B4.PreferredBasePayloadBudgetBytes);
        AssertBenchmarkSelectionEquals(directR3B5, profileR3B5);
        AssertBenchmarkSelectionEquals(directR4B4, profileR4B4);

        Assert.Equal(CandidateTarget.RotateC, profileR3B5.Target);
        Assert.Equal(profileR3B5.Target, profileR4B4.Target);
        Assert.Equal(
            UpdateWriteMode.Base,
            FindHotUpdate(profileR3B5.StayB.UpdateDecisions).Mode);
        Assert.Equal(
            UpdateWriteMode.Delta,
            FindHotUpdate(profileR4B4.StayB.UpdateDecisions).Mode);
        Assert.Equal(
            UpdateWriteMode.Base,
            FindHotUpdate(profileR3B5.RotateC.BContainedUpdateDecisions).Mode);
        Assert.Equal(
            UpdateWriteMode.Delta,
            FindHotUpdate(profileR4B4.RotateC.BContainedUpdateDecisions).Mode);
    }

    private static void AssertAdaptiveProfileParameters(
        BenchmarkV1SelectionProfileDefinition profile,
        decimal expectedReadAmplificationLimit,
        decimal expectedBaseBudgetFraction) {
        Assert.Equal(
            BenchmarkV1SelectionProfileKind.ReadAmplificationBaseBudget,
            profile.Kind);
        ReadAmplificationBaseBudgetPolicyParameters parameters = Assert.IsType<
            ReadAmplificationBaseBudgetPolicyParameters>(
                profile.AdaptiveParameters);
        Assert.Equal(
            expectedReadAmplificationLimit,
            parameters.ReadAmplificationLimit);
        Assert.Equal(expectedBaseBudgetFraction, parameters.BaseBudgetFraction);
    }

    private static void AssertBenchmarkSelectionEquals(
        ReadAmplificationBaseBudgetPolicySelection expected,
        BenchmarkV1StepSelection actual) {
        Assert.Equal(expected.Target, actual.Target);
        Assert.Equal(
            expected.StayB.UpdateDecisions,
            actual.StayB.UpdateDecisions);
        Assert.Equal(
            expected.StayB.UnchangedMigrationObjectIds,
            actual.StayB.UnchangedMigrationObjectIds);
        Assert.Equal(
            expected.RotateC.BContainedUpdateDecisions,
            actual.RotateC.BContainedUpdateDecisions);
        Assert.Equal(
            expected.RotateC.BContainedNoChangeBaseObjectIds,
            actual.RotateC.BContainedNoChangeBaseObjectIds);
    }

    private static UpdateWriteDecision FindHotUpdate(
        IReadOnlyList<UpdateWriteDecision> decisions) => decisions.Single(
            static decision =>
                decision.ObjectId == ProfileBindingHotObjectId);
}
