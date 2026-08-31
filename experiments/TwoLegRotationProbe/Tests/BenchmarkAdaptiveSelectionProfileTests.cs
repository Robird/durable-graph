using Atelia.TwoLegRotationProbe.Arena;
using Atelia.TwoLegRotationProbe.Baselines;
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
        StrategyStepViewV1 view = StrategyStepViewV1.Create(facts);
        ReadAmplificationBaseBudgetPolicyProjection projection =
            ReadAmplificationBaseBudgetPolicyProjection.Create(view);
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

        ReadAmplificationBaseBudgetPolicySelection directR3B5 =
            ReadAmplificationBaseBudgetPolicy.Select(
                projection,
                new ReadAmplificationBaseBudgetPolicyParameters(3m, 0.05m));
        ReadAmplificationBaseBudgetPolicySelection directR4B4 =
            ReadAmplificationBaseBudgetPolicy.Select(
                projection,
                new ReadAmplificationBaseBudgetPolicyParameters(4m, 0.04m));
        StrategySelectionV1 profileR3B5 = BenchmarkV1Baselines.Select(
            BenchmarkV1Baselines.ReadAmplificationBaseBudgetR3B5Percent.Identity,
            view);
        StrategySelectionV1 profileR4B4 = BenchmarkV1Baselines.Select(
            BenchmarkV1Baselines.ReadAmplificationBaseBudgetR4B4Percent.Identity,
            view);

        Assert.Equal(125, directR3B5.PreferredBasePayloadBudgetBytes);
        Assert.Equal(100, directR4B4.PreferredBasePayloadBudgetBytes);
        AssertBenchmarkSelectionEquals(directR3B5, profileR3B5);
        AssertBenchmarkSelectionEquals(directR4B4, profileR4B4);

        Assert.Equal(StrategyTargetV1.RotateC, profileR3B5.Target);
        Assert.Equal(profileR3B5.Target, profileR4B4.Target);
        Assert.Equal(
            StrategyUpdateWriteModeV1.Base,
            FindHotUpdate(profileR3B5.Stay.UpdateDecisions).Mode);
        Assert.Equal(
            StrategyUpdateWriteModeV1.Delta,
            FindHotUpdate(profileR4B4.Stay.UpdateDecisions).Mode);
        Assert.Equal(
            StrategyUpdateWriteModeV1.Base,
            FindHotUpdate(profileR3B5.Rotate.BContainedUpdateDecisions).Mode);
        Assert.Equal(
            StrategyUpdateWriteModeV1.Delta,
            FindHotUpdate(profileR4B4.Rotate.BContainedUpdateDecisions).Mode);
    }

    private static void AssertBenchmarkSelectionEquals(
        ReadAmplificationBaseBudgetPolicySelection expected,
        StrategySelectionV1 actual) {
        Assert.Equal(expected.Target, actual.Target);
        Assert.Equal(
            expected.StayB.UpdateDecisions,
            actual.Stay.UpdateDecisions);
        Assert.Equal(
            expected.StayB.UnchangedMigrationObjectIds,
            actual.Stay.UnchangedMigrationObjectIds);
        Assert.Equal(
            expected.RotateC.BContainedUpdateDecisions,
            actual.Rotate.BContainedUpdateDecisions);
        Assert.Equal(
            expected.RotateC.BContainedNoChangeBaseObjectIds,
            actual.Rotate.BContainedNoChangeBaseObjectIds);
    }

    private static StrategyUpdateWriteDecisionV1 FindHotUpdate(
        IReadOnlyList<StrategyUpdateWriteDecisionV1> decisions) => decisions.Single(
            static decision =>
                decision.ObjectId == ProfileBindingHotObjectId);
}
