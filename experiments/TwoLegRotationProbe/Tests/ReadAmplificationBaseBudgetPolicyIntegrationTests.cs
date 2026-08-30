using Atelia.TwoLegRotationProbe.Evaluation;
using Atelia.TwoLegRotationProbe.Model;
using Atelia.TwoLegRotationProbe.Planning;
using Atelia.TwoLegRotationProbe.Policies;
using Atelia.TwoLegRotationProbe.Simulation;
using Atelia.TwoLegRotationProbe.Workloads;

namespace Atelia.TwoLegRotationProbe.Tests;

public sealed partial class RotationPolicyComparisonTests {
    private static readonly CandidateTarget[] AdaptiveMatchedTargets = [
        CandidateTarget.StayB,
        CandidateTarget.StayB,
        CandidateTarget.StayB,
        CandidateTarget.StayB,
        CandidateTarget.RotateC,
    ];

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

        ReadAmplificationBaseBudgetPolicyProjection projection =
            ReadAmplificationBaseBudgetPolicyProjection.Create(facts);
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
        Assert.Equal(CandidateTarget.StayB, selection.Target);
        Assert.Equal(100, selection.PreferredBasePayloadBudgetBytes);
        Assert.Null(selection.StayProgressOverrideObjectId);
        Assert.Empty(selection.StayB.UnchangedMigrationObjectIds);
        Assert.Equal(
            [
                new UpdateWriteDecision(hotObjectId, UpdateWriteMode.Base),
                new UpdateWriteDecision(coldObjectId, UpdateWriteMode.Base),
            ],
            selection.StayB.UpdateDecisions);

        ExplicitCandidatePairEvaluation pair =
            ExplicitCandidatePairEvaluator.Evaluate(
                source.Store,
                facts,
                selection.StayB,
                selection.RotateC);
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
                selection.Target));
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

    [Fact]
    public void Adaptive_policy_naturally_matches_control_cadence_and_retires_debt() {
        PolicySource source = CreateSource([
            new InitialObjectSeed(10, 100),
            new InitialObjectSeed(20, 100),
            new InitialObjectSeed(30, 100),
            new InitialObjectSeed(40, 100),
        ]);
        SaveStep[] steps = [
            new SaveStep([new UpdateObject(10, 100, 50)]),
            new SaveStep([new UpdateObject(10, 100, 50)]),
            new SaveStep([new UpdateObject(10, 100, 50)]),
            new SaveStep([new UpdateObject(10, 100, 50)]),
            new SaveStep([new CreateObject(1001, 1)]),
        ];
        ReadAmplificationBaseBudgetPolicyParameters parameters = new(3m, 0.05m);

        AdaptiveMatchedRun control = RunAdaptiveMatchedTreatment(
            source,
            steps,
            AdaptiveMatchedTreatment.DeltaNoMigration,
            parameters);
        AdaptiveMatchedRun paced = RunAdaptiveMatchedTreatment(
            source,
            steps,
            AdaptiveMatchedTreatment.DeltaPacedOneDebt,
            parameters);
        AdaptiveMatchedRun adaptive = RunAdaptiveMatchedTreatment(
            source,
            steps,
            AdaptiveMatchedTreatment.Adaptive,
            parameters);

        Assert.Equal(AdaptiveMatchedTargets, control.Targets);
        Assert.Equal(AdaptiveMatchedTargets, paced.Targets);
        Assert.Equal(AdaptiveMatchedTargets, adaptive.Targets);
        Assert.Equal(
            [
                new uint[] { 10, 20, 30, 40 },
                new uint[] { 10, 30, 40 },
                new uint[] { 10, 40 },
                new uint[] { 10 },
                Array.Empty<uint>(),
            ],
            adaptive.SourceDebtByStep);
        Assert.Equal(
            [20U, 30U, 40U, 10U, null],
            adaptive.ProgressOverrideObjectIds);
        Assert.Equal(
            [new uint[] { 20 }, new uint[] { 30 }, new uint[] { 40 }, [], []],
            adaptive.MigrationObjectIdsByStep);
        Assert.Equal(
            [
                new UpdateWriteDecision(10, UpdateWriteMode.Delta),
                new UpdateWriteDecision(10, UpdateWriteMode.Delta),
                new UpdateWriteDecision(10, UpdateWriteMode.Delta),
                new UpdateWriteDecision(10, UpdateWriteMode.Base),
            ],
            adaptive.UpdateDecisionsByStep.Take(4).Select(Assert.Single));
        Assert.Empty(adaptive.UpdateDecisionsByStep[4]);
        Assert.Equal(
            [100L, 150L, 200L, 250L, 300L],
            control.HotObjectSourceReconstructionPayloadBytesByStep);
        Assert.Equal(
            control.HotObjectSourceReconstructionPayloadBytesByStep,
            paced.HotObjectSourceReconstructionPayloadBytesByStep);
        Assert.Equal(
            [100L, 150L, 200L, 250L, 100L],
            adaptive.HotObjectSourceReconstructionPayloadBytesByStep);

        Assert.Equal([10U, 20U, 30U, 40U, 1001U], control.FinalDebtObjectIds);
        Assert.Equal([10U, 1001U], paced.FinalDebtObjectIds);
        Assert.Equal([1001U], adaptive.FinalDebtObjectIds);
        AssertExactState(control.FinalState, paced.FinalState);
        AssertExactState(control.FinalState, adaptive.FinalState);

        Assert.Equal(
            new FixedHorizonRawVector(6, 924, 476, 476, 524),
            control.Raw);
        Assert.Equal(
            new FixedHorizonRawVector(6, 1248, 372, 748, 524),
            paced.Raw);
        Assert.Equal(
            new FixedHorizonRawVector(6, 1296, 472, 796, 524),
            adaptive.Raw);

        // These are raw fixed-horizon facts, not a scalar score or winner.
        Assert.Equal(372,
            adaptive.Raw.TotalPhysicalWriteBytes -
                control.Raw.TotalPhysicalWriteBytes);
        Assert.Equal(-4,
            adaptive.Raw.PeakCommitWriteBytes - control.Raw.PeakCommitWriteBytes);
        Assert.Equal(320,
            adaptive.Raw.MaxCurrentFileTailBytes -
                control.Raw.MaxCurrentFileTailBytes);
        Assert.Equal(0,
            adaptive.Raw.FinalColdHeadReadBytes -
                control.Raw.FinalColdHeadReadBytes);
        Assert.Equal(48,
            adaptive.Raw.TotalPhysicalWriteBytes - paced.Raw.TotalPhysicalWriteBytes);
        Assert.Equal(100,
            adaptive.Raw.PeakCommitWriteBytes - paced.Raw.PeakCommitWriteBytes);
        Assert.Equal(48,
            adaptive.Raw.MaxCurrentFileTailBytes - paced.Raw.MaxCurrentFileTailBytes);
        Assert.Equal(0,
            adaptive.Raw.FinalColdHeadReadBytes - paced.Raw.FinalColdHeadReadBytes);
    }

    private static AdaptiveMatchedRun RunAdaptiveMatchedTreatment(
        PolicySource source,
        IReadOnlyList<SaveStep> steps,
        AdaptiveMatchedTreatment treatment,
        ReadAmplificationBaseBudgetPolicyParameters parameters) {
        long sourceTailBytes = FixedHorizonTotalTailBytes(source.Store);
        Dictionary<uint, LogicalObjectState> expectedState = new(
            source.InitialExpectedState);
        EvaluatorV1Session session = new(
            source.Store,
            CreateCursor(source),
            totalWorkloadStepCount: steps.Count);
        List<CandidateTarget> targets = [];
        List<uint[]> sourceDebtByStep = [];
        List<uint?> progressOverrideObjectIds = [];
        List<uint[]> migrationObjectIdsByStep = [];
        List<UpdateWriteDecision[]> updateDecisionsByStep = [];
        List<long> hotObjectSourceReconstructionPayloadBytesByStep = [];

        for (int index = 0; index < steps.Count; index++) {
            SaveStep step = steps[index];
            NormalizedSaveFacts facts = SaveStepNormalizer.Normalize(
                session.Store,
                session.Cursor.FileScope.CurrentFileNumber,
                session.Cursor.PublishedRevisionAddress,
                step);
            sourceDebtByStep.Add(GetSourcePreviousDebtObjectIds(facts));
            hotObjectSourceReconstructionPayloadBytesByStep.Add(
                facts.ParentLive[10].HeadReconstructionObjectPayloadBytes);

            CandidateTarget target = AdaptiveMatchedTargets[index];
            StayBSaveDecision stayB;
            RotateCSaveDecision rotateC;
            uint? progressOverrideObjectId = null;
            switch (treatment) {
                case AdaptiveMatchedTreatment.DeltaNoMigration:
                    SaveDecisionPair control = SelectDeltaNoMigrationDecisions(facts);
                    stayB = control.StayB;
                    rotateC = control.RotateC;
                    break;
                case AdaptiveMatchedTreatment.DeltaPacedOneDebt:
                    SaveDecisionPair paced = SelectDeltaPacedOneDebtDecisions(facts);
                    stayB = paced.StayB;
                    rotateC = paced.RotateC;
                    break;
                case AdaptiveMatchedTreatment.Adaptive:
                    ReadAmplificationBaseBudgetPolicyProjection projection =
                        ReadAmplificationBaseBudgetPolicyProjection.Create(facts);
                    ReadAmplificationBaseBudgetPolicySelection selection =
                        ReadAmplificationBaseBudgetPolicy.Select(
                            projection,
                            parameters);
                    Assert.Same(facts, projection.Facts);
                    Assert.Same(projection, selection.Projection);
                    target = selection.Target;
                    stayB = selection.StayB;
                    rotateC = selection.RotateC;
                    progressOverrideObjectId =
                        selection.StayProgressOverrideObjectId;
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(treatment));
            }

            targets.Add(target);
            progressOverrideObjectIds.Add(progressOverrideObjectId);
            ExplicitCandidatePairEvaluation pair =
                ExplicitCandidatePairEvaluator.Evaluate(
                    session.Store,
                    facts,
                    stayB,
                    rotateC);
            RotationPolicyStepAttempt attempt = session.ApplySelectedWorkloadCommit(
                pair,
                target);
            switch (attempt) {
                case AppliedStayBPolicyStep appliedStay:
                    Assert.Equal(CandidateTarget.StayB, target);
                    Assert.Same(pair, appliedStay.Evaluation);
                    Assert.Same(appliedStay.Selected,
                        appliedStay.CompletionCertificate.InitialStayB);
                    migrationObjectIdsByStep.Add([
                        .. appliedStay.Selected.Plan.Decision
                            .UnchangedMigrationObjectIds,
                    ]);
                    updateDecisionsByStep.Add([
                        .. appliedStay.Selected.Plan.Decision.UpdateDecisions,
                    ]);
                    break;
                case AppliedRotateCPolicyStep appliedRotate:
                    Assert.Equal(CandidateTarget.RotateC, target);
                    Assert.Same(pair, appliedRotate.Evaluation);
                    migrationObjectIdsByStep.Add([]);
                    updateDecisionsByStep.Add([
                        .. appliedRotate.Selected.Plan.Decision
                            .BContainedUpdateDecisions,
                    ]);
                    break;
                default:
                    throw new InvalidOperationException(
                        $"Matched policy action was not applied: {attempt.GetType().Name}.");
            }

            ApplyExpectedState(expectedState, step);
            AssertExactState(expectedState, facts.PostLiveStates);
            AssertRuntimeStateAndClosure(session.Store, session.Cursor, expectedState);
        }

        AdmittedEvaluatorRun admitted = Assert.IsType<AdmittedEvaluatorRun>(
            session.Complete());
        Assert.Equal(steps.Count + 1, admitted.Metrics.RealizedCommitCount);
        Assert.Equal(4, session.Store.FileCount);
        AssertFixedHorizonScope(admitted.FinalCursor, 3, 4);
        AssertDirectFixedHorizonSettlement(admitted.Settlement);
        Assert.Equal(
            admitted.Metrics.TotalPhysicalWriteBytes,
            checked(FixedHorizonTotalTailBytes(session.Store) - sourceTailBytes));
        AssertRuntimeStateAndClosure(session.Store, admitted.FinalCursor, expectedState);

        IReadOnlyDictionary<uint, LogicalObjectState> finalState =
            PhysicalStateOracle.Materialize(
                session.Store,
                ObjectVersionDictionaryReader.MaterializeLive(
                    session.Store,
                    admitted.FinalCursor.PublishedRevisionAddress).Bindings);
        return new AdaptiveMatchedRun(
            ProjectFixedHorizonRaw(admitted),
            targets,
            sourceDebtByStep,
            progressOverrideObjectIds,
            migrationObjectIdsByStep,
            updateDecisionsByStep,
            hotObjectSourceReconstructionPayloadBytesByStep,
            GetFixedHorizonPreviousDebtObjectIds(
                session.Store,
                admitted.FinalCursor),
            finalState);
    }

    private enum AdaptiveMatchedTreatment {
        DeltaNoMigration,
        DeltaPacedOneDebt,
        Adaptive,
    }

    private sealed record AdaptiveMatchedRun(
        FixedHorizonRawVector Raw,
        IReadOnlyList<CandidateTarget> Targets,
        IReadOnlyList<uint[]> SourceDebtByStep,
        IReadOnlyList<uint?> ProgressOverrideObjectIds,
        IReadOnlyList<uint[]> MigrationObjectIdsByStep,
        IReadOnlyList<UpdateWriteDecision[]> UpdateDecisionsByStep,
        IReadOnlyList<long> HotObjectSourceReconstructionPayloadBytesByStep,
        uint[] FinalDebtObjectIds,
        IReadOnlyDictionary<uint, LogicalObjectState> FinalState);
}
