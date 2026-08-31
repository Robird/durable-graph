using Atelia.TwoLegRotationProbe.Arena;
using Atelia.TwoLegRotationProbe.Evaluation;
using Atelia.TwoLegRotationProbe.Model;
using Atelia.TwoLegRotationProbe.Planning;
using Atelia.TwoLegRotationProbe.Policies;
using Atelia.TwoLegRotationProbe.Simulation;
using Atelia.TwoLegRotationProbe.Workloads;

namespace Atelia.TwoLegRotationProbe.Tests;

public sealed partial class RotationPolicyComparisonTests {
    private const uint ThresholdBandHotObjectId = 1;

    [Fact]
    public void Adaptive_threshold_band_uses_strict_limit_for_hot_chain_Base_motive() {
        PolicySource source = CreateSource([
            new InitialObjectSeed(ThresholdBandHotObjectId, 10),
            new InitialObjectSeed(2, 1),
            new InitialObjectSeed(3, 1),
            new InitialObjectSeed(4, 1),
            new InitialObjectSeed(5, 1),
            new InitialObjectSeed(6, 1),
            new InitialObjectSeed(7, 1),
            new InitialObjectSeed(8, 1),
            new InitialObjectSeed(100, 1000),
        ]);
        SaveStep[] steps = [
            new SaveStep([new CreateObject(1001, 1)]),
            new SaveStep([new UpdateObject(ThresholdBandHotObjectId, 10, 5)]),
            new SaveStep([new UpdateObject(ThresholdBandHotObjectId, 10, 5)]),
            new SaveStep([new UpdateObject(ThresholdBandHotObjectId, 10, 5)]),
            new SaveStep([new UpdateObject(ThresholdBandHotObjectId, 10, 5)]),
            new SaveStep([new UpdateObject(ThresholdBandHotObjectId, 10, 5)]),
            new SaveStep([new UpdateObject(ThresholdBandHotObjectId, 10, 5)]),
            new SaveStep([
                new RemoveObject(2),
                new RemoveObject(3),
                new RemoveObject(4),
                new RemoveObject(5),
                new RemoveObject(6),
                new RemoveObject(7),
                new RemoveObject(1001),
            ]),
        ];

        ThresholdBandRun adaptive35 = RunThresholdBand(
            source,
            steps,
            new ReadAmplificationBaseBudgetPolicyParameters(3m, 0.05m));
        ThresholdBandRun adaptive44 = RunThresholdBand(
            source,
            steps,
            new ReadAmplificationBaseBudgetPolicyParameters(4m, 0.04m));

        StrategyTargetV1[] allStay = Enumerable
            .Repeat(StrategyTargetV1.StayB, steps.Length)
            .ToArray();
        Assert.Equal(allStay, adaptive35.Targets);
        Assert.Equal(allStay, adaptive44.Targets);
        Assert.Equal(
            Enumerable.Repeat(Array.Empty<uint>(), steps.Length),
            adaptive35.MigrationObjectIdsByStep);
        Assert.Equal(
            adaptive35.MigrationObjectIdsByStep,
            adaptive44.MigrationObjectIdsByStep);

        Assert.Equal(
            [
                StrategyUpdateWriteModeV1.Delta,
                StrategyUpdateWriteModeV1.Delta,
                StrategyUpdateWriteModeV1.Delta,
                StrategyUpdateWriteModeV1.Delta,
                StrategyUpdateWriteModeV1.Base,
                StrategyUpdateWriteModeV1.Delta,
            ],
            adaptive35.HotUpdateModes);
        Assert.Equal(
            Enumerable.Repeat(StrategyUpdateWriteModeV1.Delta, 6),
            adaptive44.HotUpdateModes);
        Assert.Equal([15L, 20L, 25L, 30L, 35L, 15L],
            adaptive35.HotProspectiveReadPayloadBytes);
        Assert.Equal([15L, 20L, 25L, 30L, 35L, 40L],
            adaptive44.HotProspectiveReadPayloadBytes);

        // Every Update has room for the 10-byte hot Base. The fifth Update diverges
        // only at 3.5 > 3 versus 3.5 <= 4; the sixth 4-limit decision is the
        // strict equality case 4 == 4 and remains Delta.
        Assert.All(adaptive35.AvailableUpdateBudgetBytes,
            remaining => Assert.True(remaining >= 10));
        Assert.All(adaptive44.AvailableUpdateBudgetBytes,
            remaining => Assert.True(remaining >= 10));
        Assert.Equal(
            Enumerable.Repeat(50L, 6),
            adaptive35.AvailableUpdateBudgetBytes);
        Assert.Equal(
            Enumerable.Repeat(40L, 6),
            adaptive44.AvailableUpdateBudgetBytes);
        Assert.Equal(35L, adaptive35.HotProspectiveReadPayloadBytes[4]);
        Assert.Equal(35L, adaptive44.HotProspectiveReadPayloadBytes[4]);
        Assert.Equal(40L, adaptive44.HotProspectiveReadPayloadBytes[5]);

        Assert.Equal(
            new RealizedReconstructionPayloadAmplificationSample(1, 15, 10),
            adaptive35.FinalHotAmplification);
        Assert.Equal(
            new RealizedReconstructionPayloadAmplificationSample(1, 40, 10),
            adaptive44.FinalHotAmplification);
        Assert.Equal(1.5m,
            adaptive35.FinalHotAmplification
                .EffectiveHeadToBasePayloadAmplification);
        Assert.Equal(4m,
            adaptive44.FinalHotAmplification
                .EffectiveHeadToBasePayloadAmplification);
        Assert.Equal(2, adaptive35.FinalHotReconstructionFrameCount);
        Assert.Equal(7, adaptive44.FinalHotReconstructionFrameCount);

    }

    private static ThresholdBandRun RunThresholdBand(
        PolicySource source,
        IReadOnlyList<SaveStep> steps,
        ReadAmplificationBaseBudgetPolicyParameters parameters) {
        EvaluatorV1Session session = new(
            source.Store,
            CreateCursor(source),
            totalWorkloadStepCount: steps.Count);
        Dictionary<uint, LogicalObjectState> expectedState = new(
            source.InitialExpectedState);
        List<StrategyTargetV1> targets = [];
        List<uint[]> migrationObjectIdsByStep = [];
        List<StrategyUpdateWriteModeV1> hotUpdateModes = [];
        List<long> hotProspectiveReadPayloadBytes = [];
        List<long> availableUpdateBudgetBytes = [];

        foreach (SaveStep step in steps) {
            NormalizedSaveFacts facts = SaveStepNormalizer.Normalize(
                session.Store,
                session.Cursor.FileScope.CurrentFileNumber,
                session.Cursor.PublishedRevisionAddress,
                step);
            ReadAmplificationBaseBudgetPolicyProjection projection =
                ReadAmplificationBaseBudgetPolicyProjection.Create(
                    StrategyStepViewV1.Create(facts));
            ReadAmplificationBaseBudgetPolicySelection selection =
                ReadAmplificationBaseBudgetPolicy.Select(projection, parameters);

            targets.Add(selection.Target);
            if (step.Changes.OfType<UpdateObject>().SingleOrDefault() is { } update) {
                ReadAmplificationBaseBudgetPolicyObjectFact hot = projection
                    .PostLiveObjects
                    .Single(fact => fact.ObjectId == ThresholdBandHotObjectId);
                Assert.Equal(ThresholdBandHotObjectId, update.ObjectId);
                Assert.Equal(10, hot.PostSaveBasePayloadBytes);
                Assert.Equal(5, hot.DeltaPayloadBytes);
                hotProspectiveReadPayloadBytes.Add(
                    hot.ReadAmplificationNumeratorBytes!.Value);
                hotUpdateModes.Add(selection.StayB.UpdateDecisions
                    .Single(decision =>
                        decision.ObjectId == ThresholdBandHotObjectId)
                    .Mode);
                availableUpdateBudgetBytes.Add(
                    selection.PreferredBasePayloadBudgetBytes);
            }

            ExplicitCandidatePairEvaluation pair =
                ExplicitCandidatePairEvaluator.Evaluate(
                    session.Store,
                    facts,
                    StrategyRunContextV1.ProjectStay(selection.StayB),
                    StrategyRunContextV1.ProjectRotate(selection.RotateC));
            AppliedStayBPolicyStep applied = Assert.IsType<AppliedStayBPolicyStep>(
                session.ApplySelectedWorkloadCommit(
                    pair,
                    StrategyRunContextV1.ProjectTarget(selection.Target)));
            Assert.Same(pair, applied.Evaluation);
            migrationObjectIdsByStep.Add([
                .. applied.Selected.Plan.Decision.UnchangedMigrationObjectIds,
            ]);

            ApplyExpectedState(expectedState, step);
            AssertExactState(expectedState, facts.PostLiveStates);
            AssertRuntimeStateAndClosure(
                session.Store,
                session.Cursor,
                expectedState);
        }

        NormalizedSaveFacts workloadEndFacts =
            SaveStepNormalizer.NormalizeMaintenanceOnly(
                session.Store,
                session.Cursor.FileScope.CurrentFileNumber,
                session.Cursor.PublishedRevisionAddress);
        RealizedReconstructionPayloadAmplificationSample workloadEndHot =
            RealizedReconstructionPayloadAmplificationDiagnostic
                .Capture(workloadEndFacts)
                .Samples
                .Single(sample => sample.ObjectId == ThresholdBandHotObjectId);
        AbsoluteFrameAddress hotHead = ObjectVersionDictionaryReader
            .MaterializeLive(
                session.Store,
                session.Cursor.PublishedRevisionAddress)
            .Bindings[ThresholdBandHotObjectId];
        ObjectReconstructionInspection hotReconstruction =
            PhysicalStateOracle.InspectObjectReconstruction(
                session.Store,
                ThresholdBandHotObjectId,
                hotHead);

        AdmittedEvaluatorRun admitted = Assert.IsType<AdmittedEvaluatorRun>(
            session.Complete());
        Assert.Equal(steps.Count + 1, admitted.Metrics.RealizedCommitCount);
        Assert.Equal(3, session.Store.FileCount);
        AssertFixedHorizonScope(admitted.FinalCursor, 2, 3);
        AssertDirectFixedHorizonSettlement(admitted.Settlement);
        AssertRuntimeStateAndClosure(
            session.Store,
            admitted.FinalCursor,
            expectedState);

        return new ThresholdBandRun(
            targets,
            migrationObjectIdsByStep,
            hotUpdateModes,
            hotProspectiveReadPayloadBytes,
            availableUpdateBudgetBytes,
            workloadEndHot,
            hotReconstruction.ReconstructionFrameAddresses.Count);
    }

    private sealed record ThresholdBandRun(
        IReadOnlyList<StrategyTargetV1> Targets,
        IReadOnlyList<uint[]> MigrationObjectIdsByStep,
        IReadOnlyList<StrategyUpdateWriteModeV1> HotUpdateModes,
        IReadOnlyList<long> HotProspectiveReadPayloadBytes,
        IReadOnlyList<long> AvailableUpdateBudgetBytes,
        RealizedReconstructionPayloadAmplificationSample FinalHotAmplification,
        int FinalHotReconstructionFrameCount);
}
