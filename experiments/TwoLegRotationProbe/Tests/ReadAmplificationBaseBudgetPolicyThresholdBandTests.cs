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
    public void Adaptive_threshold_band_trades_write_bytes_for_realized_hot_chain_amplification() {
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
            new uint?[] { 1, 2, 3, 4, 5, 6, 7, 8 },
            adaptive35.ProgressOverrideObjectIds);
        Assert.Equal(
            adaptive35.ProgressOverrideObjectIds,
            adaptive44.ProgressOverrideObjectIds);
        Assert.Equal(
            new uint[][] { [1], [2], [3], [4], [5], [6], [7], [8] },
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

        // Every Update has room for the 10-byte hot Base after the separate
        // one-byte progress migration. The fifth Update therefore diverges
        // only at 3.5 > 3 versus 3.5 <= 4; the sixth 4-limit decision is the
        // strict equality case 4 == 4 and remains Delta.
        Assert.All(adaptive35.UpdateBudgetBytesAfterProgress,
            remaining => Assert.True(remaining >= 10));
        Assert.All(adaptive44.UpdateBudgetBytesAfterProgress,
            remaining => Assert.True(remaining >= 10));
        Assert.Equal(
            Enumerable.Repeat(49L, 6),
            adaptive35.UpdateBudgetBytesAfterProgress);
        Assert.Equal(
            Enumerable.Repeat(39L, 6),
            adaptive44.UpdateBudgetBytesAfterProgress);
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

        Assert.Equal(
            new FixedHorizonRawVector(9, 1516, 1056, 1056, 1212),
            adaptive35.Raw);
        Assert.Equal(
            new FixedHorizonRawVector(9, 1512, 1056, 1056, 1472),
            adaptive44.Raw);
        Assert.True(
            adaptive44.Raw.TotalPhysicalWriteBytes <
                adaptive35.Raw.TotalPhysicalWriteBytes);
        Assert.True(
            adaptive44.Raw.FinalColdHeadReadBytes >
                adaptive35.Raw.FinalColdHeadReadBytes);
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
        List<uint?> progressOverrideObjectIds = [];
        List<uint[]> migrationObjectIdsByStep = [];
        List<StrategyUpdateWriteModeV1> hotUpdateModes = [];
        List<long> hotProspectiveReadPayloadBytes = [];
        List<long> updateBudgetBytesAfterProgress = [];

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
            progressOverrideObjectIds.Add(
                selection.StayProgressOverrideObjectId);
            if (step.Changes.OfType<UpdateObject>().SingleOrDefault() is { } update) {
                ReadAmplificationBaseBudgetPolicyObjectFact hot = projection
                    .PostLiveObjects
                    .Single(fact => fact.ObjectId == ThresholdBandHotObjectId);
                Assert.Equal(ThresholdBandHotObjectId, update.ObjectId);
                Assert.False(hot.IsADependent);
                Assert.Equal(10, hot.PostSaveBasePayloadBytes);
                Assert.Equal(5, hot.DeltaPayloadBytes);
                hotProspectiveReadPayloadBytes.Add(
                    hot.ReadAmplificationNumeratorBytes!.Value);
                hotUpdateModes.Add(selection.StayB.UpdateDecisions
                    .Single(decision =>
                        decision.ObjectId == ThresholdBandHotObjectId)
                    .Mode);
                uint progressId = Assert.IsType<uint>(
                    selection.StayProgressOverrideObjectId);
                int progressBytes = projection.PostLiveObjects
                    .Single(fact => fact.ObjectId == progressId)
                    .PostSaveBasePayloadBytes;
                Assert.Equal(1, progressBytes);
                updateBudgetBytesAfterProgress.Add(
                    selection.PreferredBasePayloadBudgetBytes - progressBytes);
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

        NormalizedSaveFacts finalFacts = SaveStepNormalizer.NormalizeMaintenanceOnly(
            session.Store,
            admitted.FinalCursor.FileScope.CurrentFileNumber,
            admitted.FinalCursor.PublishedRevisionAddress);
        RealizedReconstructionPayloadAmplificationSample finalHot =
            RealizedReconstructionPayloadAmplificationDiagnostic
                .Capture(finalFacts)
                .Samples
                .Single(sample => sample.ObjectId == ThresholdBandHotObjectId);
        AbsoluteFrameAddress hotHead = ObjectVersionDictionaryReader
            .MaterializeLive(
                session.Store,
                admitted.FinalCursor.PublishedRevisionAddress)
            .Bindings[ThresholdBandHotObjectId];
        ObjectReconstructionInspection hotReconstruction =
            PhysicalStateOracle.InspectObjectReconstruction(
                session.Store,
                ThresholdBandHotObjectId,
                hotHead);

        return new ThresholdBandRun(
            ProjectFixedHorizonRaw(admitted),
            targets,
            progressOverrideObjectIds,
            migrationObjectIdsByStep,
            hotUpdateModes,
            hotProspectiveReadPayloadBytes,
            updateBudgetBytesAfterProgress,
            finalHot,
            hotReconstruction.ReconstructionFrameAddresses.Count);
    }

    private sealed record ThresholdBandRun(
        FixedHorizonRawVector Raw,
        IReadOnlyList<StrategyTargetV1> Targets,
        IReadOnlyList<uint?> ProgressOverrideObjectIds,
        IReadOnlyList<uint[]> MigrationObjectIdsByStep,
        IReadOnlyList<StrategyUpdateWriteModeV1> HotUpdateModes,
        IReadOnlyList<long> HotProspectiveReadPayloadBytes,
        IReadOnlyList<long> UpdateBudgetBytesAfterProgress,
        RealizedReconstructionPayloadAmplificationSample FinalHotAmplification,
        int FinalHotReconstructionFrameCount);
}
