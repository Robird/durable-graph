using Atelia.TwoLegRotationProbe.Arena;
using Atelia.TwoLegRotationProbe.Evaluation;
using Atelia.TwoLegRotationProbe.Model;
using Atelia.TwoLegRotationProbe.Planning;
using Atelia.TwoLegRotationProbe.Policies;
using Atelia.TwoLegRotationProbe.Simulation;
using Atelia.TwoLegRotationProbe.Workloads;

namespace Atelia.TwoLegRotationProbe.Tests;

public sealed partial class RotationPolicyComparisonTests {
    [Fact]
    public void Above_threshold_Base_flows_through_exact_Stay_plan_and_apply() {
        const uint hotObjectId = 10;
        const uint coldObjectId = 20;
        const uint sentinelObjectId = 900;
        PolicySource source = CreateSource([
            new InitialObjectSeed(hotObjectId, 100),
            new InitialObjectSeed(coldObjectId, 100),
        ]);
        ProbeRevisionCursor cursor = CreateCursor(source);
        Dictionary<uint, LogicalObjectState> expectedState = new(
            source.InitialExpectedState);

        ApplyExactStaySetup(
            source.Store,
            ref cursor,
            expectedState,
            new SaveStep([new CreateObject(sentinelObjectId, 1)]),
            updateDecisions: [],
            migrationObjectIds: [hotObjectId]);
        ApplyExactStaySetup(
            source.Store,
            ref cursor,
            expectedState,
            new SaveStep([
                new RemoveObject(sentinelObjectId),
                new UpdateObject(hotObjectId, 100, 51),
            ]),
            [new UpdateWriteDecision(hotObjectId, UpdateWriteMode.Delta)],
            migrationObjectIds: []);
        ApplyExactStaySetup(
            source.Store,
            ref cursor,
            expectedState,
            new SaveStep([new UpdateObject(hotObjectId, 100, 50)]),
            [new UpdateWriteDecision(hotObjectId, UpdateWriteMode.Delta)],
            migrationObjectIds: []);
        ApplyExactStaySetup(
            source.Store,
            ref cursor,
            expectedState,
            new SaveStep([new UpdateObject(hotObjectId, 100, 50)]),
            [new UpdateWriteDecision(hotObjectId, UpdateWriteMode.Delta)],
            migrationObjectIds: []);

        SaveStep decisiveStep = new([
            new UpdateObject(hotObjectId, 100, 50),
            new UpdateObject(coldObjectId, 100, 100),
        ]);
        NormalizedSaveFacts facts = SaveStepNormalizer.Normalize(
            source.Store,
            cursor.FileScope.CurrentFileNumber,
            cursor.PublishedRevisionAddress,
            decisiveStep);
        Assert.Equal(251, facts.ParentLive[hotObjectId]
            .HeadReconstructionObjectPayloadBytes);
        Assert.Equal(100, facts.ParentLive[coldObjectId]
            .HeadReconstructionObjectPayloadBytes);

        StrategyStepViewV1 view = StrategyStepViewV1.Create(facts);
        ReadAmplificationBaseBudgetPolicyProjection projection =
            ReadAmplificationBaseBudgetPolicyProjection.Create(view);
        Assert.Equal(200, projection.PostLiveGraphBasePayloadBytes);
        Assert.Equal(100, projection.ADependentEvacuationBasePayloadBytes);
        ReadAmplificationBaseBudgetPolicyObjectFact hot = projection
            .PostLiveObjects
            .Single(fact => fact.ObjectId == hotObjectId);
        ReadAmplificationBaseBudgetPolicyObjectFact cold = projection
            .PostLiveObjects
            .Single(fact => fact.ObjectId == coldObjectId);
        Assert.False(hot.IsADependent);
        Assert.Equal(301, hot.ReadAmplificationNumeratorBytes);
        Assert.Equal(50, hot.DeltaPayloadBytes);
        Assert.True(hot.PostSaveBasePayloadBytes > hot.DeltaPayloadBytes);
        Assert.True(cold.IsADependent);
        Assert.Equal(200, cold.ReadAmplificationNumeratorBytes);
        Assert.Equal(cold.PostSaveBasePayloadBytes, cold.DeltaPayloadBytes);

        ReadAmplificationBaseBudgetPolicySelection selection =
            ReadAmplificationBaseBudgetPolicy.Select(
                projection,
                new ReadAmplificationBaseBudgetPolicyParameters(3m, 0.5m));
        Assert.Equal(StrategyTargetV1.StayB, selection.Target);
        Assert.Equal(100, selection.PreferredBasePayloadBudgetBytes);
        Assert.Empty(selection.StayB.UnchangedMigrationObjectIds);
        Assert.Equal(
            [
                new StrategyUpdateWriteDecisionV1(
                    hotObjectId,
                    StrategyUpdateWriteModeV1.Base),
                new StrategyUpdateWriteDecisionV1(
                    coldObjectId,
                    StrategyUpdateWriteModeV1.Base),
            ],
            selection.StayB.UpdateDecisions);

        SaveDecisionPair projectedSelection = ProjectStrategySelection(
            selection.Selection);
        ExplicitCandidatePairEvaluation pair =
            ExplicitCandidatePairEvaluator.Evaluate(
                source.Store,
                facts,
                projectedSelection.StayB,
                projectedSelection.RotateC);
        FeasibleCandidate<StayBRevisionPlan> exact =
            Assert.IsType<FeasibleCandidate<StayBRevisionPlan>>(pair.StayBAttempt);
        Assert.Equal(
            ObjectVersionKind.Base,
            exact.Observation.Candidate.Frame.ObjectVersions[hotObjectId].Kind);
        Assert.Equal(
            ObjectVersionKind.Base,
            exact.Observation.Candidate.Frame.ObjectVersions[coldObjectId].Kind);

        AppliedStayBPolicyStep applied = Assert.IsType<AppliedStayBPolicyStep>(
            ExplicitRotationPolicyStepHarness.TryApplySelected(
                source.Store,
                cursor,
                pair,
                StrategyRunContextV1.ProjectTarget(selection.Target)));
        Assert.Same(exact, applied.Selected);
        cursor = applied.ResultCursor;
        ApplyExpectedState(expectedState, decisiveStep);
        AssertRuntimeStateAndClosure(source.Store, cursor, expectedState);

        NormalizedSaveFacts result = SaveStepNormalizer.NormalizeMaintenanceOnly(
            source.Store,
            cursor.FileScope.CurrentFileNumber,
            cursor.PublishedRevisionAddress);
        Assert.Equal(100, result.ParentLive[hotObjectId]
            .HeadReconstructionObjectPayloadBytes);
        Assert.Equal(100, result.ParentLive[coldObjectId]
            .HeadReconstructionObjectPayloadBytes);
        Assert.Equal(
            cursor.FileScope.CurrentFileNumber,
            result.ParentLive[hotObjectId].BaseAddress.FileNumber);
        Assert.Equal(
            cursor.FileScope.CurrentFileNumber,
            result.ParentLive[coldObjectId].BaseAddress.FileNumber);
        Assert.Empty(GetSourcePreviousDebtObjectIds(result));
    }

    private static void ApplyExactStaySetup(
        RbfFileStore store,
        ref ProbeRevisionCursor cursor,
        Dictionary<uint, LogicalObjectState> expectedState,
        SaveStep step,
        IEnumerable<UpdateWriteDecision> updateDecisions,
        IEnumerable<uint> migrationObjectIds) {
        NormalizedSaveFacts facts = SaveStepNormalizer.Normalize(
            store,
            cursor.FileScope.CurrentFileNumber,
            cursor.PublishedRevisionAddress,
            step);
        SaveDecisionPair decisions = CreateDecisions(
            facts,
            updateDecisions,
            migrationObjectIds);
        ExplicitCandidatePairEvaluation pair =
            ExplicitCandidatePairEvaluator.Evaluate(
                store,
                facts,
                decisions.StayB,
                decisions.RotateC);
        AppliedStayBPolicyStep applied = Assert.IsType<AppliedStayBPolicyStep>(
            ExplicitRotationPolicyStepHarness.TryApplySelected(
                store,
                cursor,
                pair,
                CandidateTarget.StayB));
        Assert.IsType<FeasibleCandidate<StayBRevisionPlan>>(pair.StayBAttempt);
        Assert.Same(pair, applied.Evaluation);
        Assert.Same(applied.Selected, applied.CompletionCertificate.InitialStayB);
        cursor = applied.ResultCursor;

        ApplyExpectedState(expectedState, step);
        AssertExactState(expectedState, facts.PostLiveStates);
        AssertRuntimeStateAndClosure(store, cursor, expectedState);
    }

    private static SaveDecisionPair ProjectStrategySelection(
        StrategySelectionV1 selection) => new(
        StrategyRunContextV1.ProjectStay(selection.Stay),
        StrategyRunContextV1.ProjectRotate(selection.Rotate));

    private static FixedHorizonRawVector ProjectFixedHorizonRaw(
        AdmittedEvaluatorRun admitted) => new(
            admitted.Metrics.RealizedCommitCount,
            admitted.Metrics.TotalPhysicalWriteBytes,
            admitted.Metrics.PeakCommitWriteBytes,
            admitted.Metrics.MaxCurrentFileTailBytes,
            admitted.Metrics.TerminalColdHeadReadBytes);

    private static void AssertFixedHorizonScope(
        ProbeRevisionCursor cursor,
        uint previousFileNumber,
        uint currentFileNumber) {
        Assert.Equal(previousFileNumber, cursor.FileScope.PreviousFileNumber);
        Assert.Equal(currentFileNumber, cursor.FileScope.CurrentFileNumber);
        Assert.Equal(currentFileNumber, cursor.PublishedRevisionAddress.FileNumber);
    }

    private static void AssertDirectFixedHorizonSettlement(
        TerminalSettlementObservation settlement) {
        Assert.Empty(settlement.MigratedObjectIds);
        Assert.Equal(0, settlement.MaintenanceRevisionCount);
        Assert.Equal(1, settlement.RealizedRevisionCount);
    }

    private static uint[] GetFixedHorizonPreviousDebtObjectIds(
        RbfFileStore store,
        ProbeRevisionCursor cursor) {
        uint previousFileNumber = cursor.FileScope.PreviousFileNumber
            ?? throw new InvalidDataException("The witness requires a two-file scope.");
        return SaveStepNormalizer.NormalizeMaintenanceOnly(
                store,
                cursor.FileScope.CurrentFileNumber,
                cursor.PublishedRevisionAddress)
            .NoChanges
            .Where(fact => fact.Source.BaseAddress.FileNumber == previousFileNumber)
            .Select(static fact => fact.ObjectId)
            .Order()
            .ToArray();
    }

    private static long FixedHorizonTotalTailBytes(RbfFileStore store) => Enumerable
        .Range(1, store.FileCount)
        .Sum(index => store.GetFile((uint)index).TailOffsetBytes);

    private readonly record struct FixedHorizonRawVector(
        int RealizedCommitCount,
        long TotalPhysicalWriteBytes,
        long PeakCommitWriteBytes,
        long MaxCurrentFileTailBytes,
        long FinalColdHeadReadBytes);
}
