using Atelia.TwoLegRotationProbe.Arena;
using Atelia.TwoLegRotationProbe.Evaluation;
using Atelia.TwoLegRotationProbe.Model;
using Atelia.TwoLegRotationProbe.Planning;
using Atelia.TwoLegRotationProbe.Policies;
using Atelia.TwoLegRotationProbe.Simulation;
using Atelia.TwoLegRotationProbe.Workloads;

namespace Atelia.TwoLegRotationProbe.Tests;

public sealed partial class RotationPolicyComparisonTests {
    private static readonly StrategyTargetV1[] AdaptiveMatchedTargets = [
        StrategyTargetV1.StayB,
        StrategyTargetV1.StayB,
        StrategyTargetV1.StayB,
        StrategyTargetV1.StayB,
        StrategyTargetV1.RotateC,
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
        Assert.Null(selection.StayProgressOverrideObjectId);
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
        ReadAmplificationBaseBudgetPolicyParameters adaptive35Parameters =
            new(3m, 0.05m);
        ReadAmplificationBaseBudgetPolicyParameters adaptive44Parameters =
            new(4m, 0.04m);

        AdaptiveMatchedRun control = RunAdaptiveMatchedTreatment(
            source,
            steps,
            AdaptiveMatchedTreatment.DeltaNoMigration,
            adaptive35Parameters);
        AdaptiveMatchedRun paced = RunAdaptiveMatchedTreatment(
            source,
            steps,
            AdaptiveMatchedTreatment.DeltaPacedOneDebt,
            adaptive35Parameters);
        AdaptiveMatchedRun adaptive35 = RunAdaptiveMatchedTreatment(
            source,
            steps,
            AdaptiveMatchedTreatment.Adaptive,
            adaptive35Parameters);
        AdaptiveMatchedRun adaptive44 = RunAdaptiveMatchedTreatment(
            source,
            steps,
            AdaptiveMatchedTreatment.Adaptive,
            adaptive44Parameters);

        Assert.Equal(AdaptiveMatchedTargets, control.Targets);
        Assert.Equal(AdaptiveMatchedTargets, paced.Targets);
        Assert.Equal(AdaptiveMatchedTargets, adaptive35.Targets);
        Assert.Equal(adaptive35.Targets, adaptive44.Targets);
        Assert.Equal(
            [
                new uint[] { 10, 20, 30, 40 },
                new uint[] { 10, 30, 40 },
                new uint[] { 10, 40 },
                new uint[] { 10 },
                Array.Empty<uint>(),
            ],
            adaptive35.SourceDebtByStep);
        Assert.Equal(adaptive35.SourceDebtByStep, adaptive44.SourceDebtByStep);
        Assert.Equal(
            [20U, 30U, 40U, 10U, null],
            adaptive35.ProgressOverrideObjectIds);
        Assert.Equal(
            adaptive35.ProgressOverrideObjectIds,
            adaptive44.ProgressOverrideObjectIds);
        Assert.Equal(
            [new uint[] { 20 }, new uint[] { 30 }, new uint[] { 40 }, [], []],
            adaptive35.MigrationObjectIdsByStep);
        Assert.Equal(
            adaptive35.MigrationObjectIdsByStep,
            adaptive44.MigrationObjectIdsByStep);
        Assert.Equal(
            [
                new StrategyUpdateWriteDecisionV1(
                    10,
                    StrategyUpdateWriteModeV1.Delta),
                new StrategyUpdateWriteDecisionV1(
                    10,
                    StrategyUpdateWriteModeV1.Delta),
                new StrategyUpdateWriteDecisionV1(
                    10,
                    StrategyUpdateWriteModeV1.Delta),
                new StrategyUpdateWriteDecisionV1(
                    10,
                    StrategyUpdateWriteModeV1.Base),
            ],
            adaptive35.UpdateDecisionsByStep.Take(4).Select(Assert.Single));
        Assert.Empty(adaptive35.UpdateDecisionsByStep[4]);
        Assert.Equal(
            adaptive35.UpdateDecisionsByStep,
            adaptive44.UpdateDecisionsByStep);
        AssertRealizedHotObjectHeadToBasePayloads(
            control,
            [100L, 150L, 200L, 250L, 300L, 100L]);
        AssertRealizedHotObjectHeadToBasePayloads(
            paced,
            [100L, 150L, 200L, 250L, 300L, 100L]);
        AssertRealizedHotObjectHeadToBasePayloads(
            adaptive35,
            [100L, 150L, 200L, 250L, 100L, 100L]);
        AssertRealizedHotObjectHeadToBasePayloads(
            adaptive44,
            [100L, 150L, 200L, 250L, 100L, 100L]);
        AssertRealizedHeadToBasePayloadCheckpointsEqual(
            adaptive35,
            adaptive44);

        Assert.Equal([10U, 20U, 30U, 40U, 1001U], control.FinalDebtObjectIds);
        Assert.Equal([10U, 1001U], paced.FinalDebtObjectIds);
        Assert.Equal([1001U], adaptive35.FinalDebtObjectIds);
        Assert.Equal(adaptive35.FinalDebtObjectIds, adaptive44.FinalDebtObjectIds);
        AssertExactState(control.FinalState, paced.FinalState);
        AssertExactState(control.FinalState, adaptive35.FinalState);
        AssertExactState(adaptive35.FinalState, adaptive44.FinalState);

        Assert.Equal(
            new FixedHorizonRawVector(6, 924, 476, 476, 524),
            control.Raw);
        Assert.Equal(
            new FixedHorizonRawVector(6, 1248, 372, 748, 524),
            paced.Raw);
        Assert.Equal(
            new FixedHorizonRawVector(6, 1296, 472, 796, 524),
            adaptive35.Raw);
        Assert.Equal(
            new FixedHorizonRawVector(6, 1296, 472, 796, 524),
            adaptive44.Raw);

        // These are raw fixed-horizon facts, not a scalar score or winner.
        Assert.Equal(372,
            adaptive35.Raw.TotalPhysicalWriteBytes -
                control.Raw.TotalPhysicalWriteBytes);
        Assert.Equal(-4,
            adaptive35.Raw.PeakCommitWriteBytes - control.Raw.PeakCommitWriteBytes);
        Assert.Equal(320,
            adaptive35.Raw.MaxCurrentFileTailBytes -
                control.Raw.MaxCurrentFileTailBytes);
        Assert.Equal(0,
            adaptive35.Raw.FinalColdHeadReadBytes -
                control.Raw.FinalColdHeadReadBytes);

        // This fixture is intentionally parameter-insensitive. On the fourth
        // Update, (H + D) / B is exactly 3, so neither strict threshold selects
        // Base; both variants write Base because the progress override selects 10.
        Assert.Equal(48,
            adaptive35.Raw.TotalPhysicalWriteBytes - paced.Raw.TotalPhysicalWriteBytes);
        Assert.Equal(100,
            adaptive35.Raw.PeakCommitWriteBytes - paced.Raw.PeakCommitWriteBytes);
        Assert.Equal(48,
            adaptive35.Raw.MaxCurrentFileTailBytes - paced.Raw.MaxCurrentFileTailBytes);
        Assert.Equal(0,
            adaptive35.Raw.FinalColdHeadReadBytes - paced.Raw.FinalColdHeadReadBytes);
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
        List<StrategyTargetV1> targets = [];
        List<uint[]> sourceDebtByStep = [];
        List<uint?> progressOverrideObjectIds = [];
        List<uint[]> migrationObjectIdsByStep = [];
        List<StrategyUpdateWriteDecisionV1[]> updateDecisionsByStep = [];
        List<RealizedReconstructionPayloadAmplificationDiagnostic>
            realizedHeadToBasePayloadAmplificationCheckpoints = [
                CaptureRealizedHeadToBasePayloadAmplification(session),
            ];

        for (int index = 0; index < steps.Count; index++) {
            SaveStep step = steps[index];
            NormalizedSaveFacts facts = SaveStepNormalizer.Normalize(
                session.Store,
                session.Cursor.FileScope.CurrentFileNumber,
                session.Cursor.PublishedRevisionAddress,
                step);
            sourceDebtByStep.Add(GetSourcePreviousDebtObjectIds(facts));

            StrategyTargetV1 target = AdaptiveMatchedTargets[index];
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
                    StrategyStepViewV1 view = StrategyStepViewV1.Create(facts);
                    ReadAmplificationBaseBudgetPolicyProjection projection =
                        ReadAmplificationBaseBudgetPolicyProjection.Create(view);
                    ReadAmplificationBaseBudgetPolicySelection selection =
                        ReadAmplificationBaseBudgetPolicy.Select(
                            projection,
                            parameters);
                    Assert.Same(view, projection.View);
                    Assert.Same(projection, selection.Projection);
                    target = selection.Target;
                    SaveDecisionPair projectedSelection =
                        ProjectStrategySelection(selection.Selection);
                    stayB = projectedSelection.StayB;
                    rotateC = projectedSelection.RotateC;
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
                StrategyRunContextV1.ProjectTarget(target));
            switch (attempt) {
                case AppliedStayBPolicyStep appliedStay:
                    Assert.Equal(StrategyTargetV1.StayB, target);
                    Assert.Same(pair, appliedStay.Evaluation);
                    Assert.Same(appliedStay.Selected,
                        appliedStay.CompletionCertificate.InitialStayB);
                    migrationObjectIdsByStep.Add([
                        .. appliedStay.Selected.Plan.Decision
                            .UnchangedMigrationObjectIds,
                    ]);
                    updateDecisionsByStep.Add([
                        .. appliedStay.Selected.Plan.Decision.UpdateDecisions
                            .Select(ProjectPlanningUpdateDecision),
                    ]);
                    break;
                case AppliedRotateCPolicyStep appliedRotate:
                    Assert.Equal(StrategyTargetV1.RotateC, target);
                    Assert.Same(pair, appliedRotate.Evaluation);
                    migrationObjectIdsByStep.Add([]);
                    updateDecisionsByStep.Add([
                        .. appliedRotate.Selected.Plan.Decision
                            .BContainedUpdateDecisions
                            .Select(ProjectPlanningUpdateDecision),
                    ]);
                    break;
                default:
                    throw new InvalidOperationException(
                        $"Matched policy action was not applied: {attempt.GetType().Name}.");
            }

            ApplyExpectedState(expectedState, step);
            AssertExactState(expectedState, facts.PostLiveStates);
            AssertRuntimeStateAndClosure(session.Store, session.Cursor, expectedState);
            realizedHeadToBasePayloadAmplificationCheckpoints.Add(
                CaptureRealizedHeadToBasePayloadAmplification(session));
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
        realizedHeadToBasePayloadAmplificationCheckpoints.Add(
            CaptureRealizedHeadToBasePayloadAmplification(session));
        return new AdaptiveMatchedRun(
            ProjectFixedHorizonRaw(admitted),
            targets,
            sourceDebtByStep,
            progressOverrideObjectIds,
            migrationObjectIdsByStep,
            updateDecisionsByStep,
            realizedHeadToBasePayloadAmplificationCheckpoints,
            GetFixedHorizonPreviousDebtObjectIds(
                session.Store,
                admitted.FinalCursor),
            finalState);
    }

    private static RealizedReconstructionPayloadAmplificationDiagnostic
        CaptureRealizedHeadToBasePayloadAmplification(EvaluatorV1Session session) {
        NormalizedSaveFacts facts = SaveStepNormalizer.NormalizeMaintenanceOnly(
            session.Store,
            session.Cursor.FileScope.CurrentFileNumber,
            session.Cursor.PublishedRevisionAddress);
        return RealizedReconstructionPayloadAmplificationDiagnostic.Capture(facts);
    }

    private static void AssertRealizedHotObjectHeadToBasePayloads(
        AdaptiveMatchedRun run,
        IReadOnlyList<long> expectedInitialAndWorkloadHeadPayloadBytes) {
        RealizedReconstructionPayloadAmplificationSample[] hotSamples = run
            .RealizedHeadToBasePayloadAmplificationCheckpoints
            .Select(checkpoint => checkpoint.Samples.Single(
                static sample => sample.ObjectId == 10))
            .ToArray();

        Assert.Equal(
            expectedInitialAndWorkloadHeadPayloadBytes.Count + 1,
            hotSamples.Length);
        Assert.Equal(
            expectedInitialAndWorkloadHeadPayloadBytes,
            hotSamples
                .Take(expectedInitialAndWorkloadHeadPayloadBytes.Count)
                .Select(static sample =>
                    sample.HeadReconstructionObjectPayloadBytes));
        Assert.All(
            hotSamples,
            static sample => Assert.Equal(100, sample.BasePayloadBytes));

        RealizedReconstructionPayloadAmplificationSample final = hotSamples[^1];
        Assert.Equal(100, final.HeadReconstructionObjectPayloadBytes);
        Assert.Equal(100, final.BasePayloadBytes);
    }

    private static void AssertRealizedHeadToBasePayloadCheckpointsEqual(
        AdaptiveMatchedRun expected,
        AdaptiveMatchedRun actual) {
        Assert.Equal(
            expected.RealizedHeadToBasePayloadAmplificationCheckpoints.Count,
            actual.RealizedHeadToBasePayloadAmplificationCheckpoints.Count);
        for (int index = 0;
            index < expected.RealizedHeadToBasePayloadAmplificationCheckpoints.Count;
            index++) {
            Assert.Equal(
                expected.RealizedHeadToBasePayloadAmplificationCheckpoints[index]
                    .Samples,
                actual.RealizedHeadToBasePayloadAmplificationCheckpoints[index]
                    .Samples);
        }
    }

    private enum AdaptiveMatchedTreatment {
        DeltaNoMigration,
        DeltaPacedOneDebt,
        Adaptive,
    }

    private static SaveDecisionPair ProjectStrategySelection(
        StrategySelectionV1 selection) => new(
        StrategyRunContextV1.ProjectStay(selection.Stay),
        StrategyRunContextV1.ProjectRotate(selection.Rotate));

    private static StrategyUpdateWriteDecisionV1 ProjectPlanningUpdateDecision(
        UpdateWriteDecision decision) => new(
        decision.ObjectId,
        decision.Mode switch {
            UpdateWriteMode.Base => StrategyUpdateWriteModeV1.Base,
            UpdateWriteMode.Delta => StrategyUpdateWriteModeV1.Delta,
            _ => throw new ArgumentOutOfRangeException(nameof(decision)),
        });

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

    private sealed record AdaptiveMatchedRun(
        FixedHorizonRawVector Raw,
        IReadOnlyList<StrategyTargetV1> Targets,
        IReadOnlyList<uint[]> SourceDebtByStep,
        IReadOnlyList<uint?> ProgressOverrideObjectIds,
        IReadOnlyList<uint[]> MigrationObjectIdsByStep,
        IReadOnlyList<StrategyUpdateWriteDecisionV1[]> UpdateDecisionsByStep,
        IReadOnlyList<RealizedReconstructionPayloadAmplificationDiagnostic>
            RealizedHeadToBasePayloadAmplificationCheckpoints,
        uint[] FinalDebtObjectIds,
        IReadOnlyDictionary<uint, LogicalObjectState> FinalState);
}
