using Atelia.TwoLegRotationProbe.Evaluation;
using Atelia.TwoLegRotationProbe.Model;
using Atelia.TwoLegRotationProbe.Planning;
using Atelia.TwoLegRotationProbe.Policies;
using Atelia.TwoLegRotationProbe.Workloads;

namespace Atelia.TwoLegRotationProbe.Tests;

public sealed partial class RotationPolicyComparisonTests {
    [Fact]
    public void Adaptive_base_fraction_lower_bound_naturally_crosses_target_and_reconverges_scope() {
        TargetBandRun adaptive35 = RunTargetBand(
            new ReadAmplificationBaseBudgetPolicyParameters(3m, 0.05m));
        TargetBandRun adaptive44 = RunTargetBand(
            new ReadAmplificationBaseBudgetPolicyParameters(4m, 0.04m));

        Assert.Equal(
            [CandidateTarget.RotateC, CandidateTarget.StayB],
            adaptive35.Steps.Select(static step => step.Target));
        Assert.Equal(
            [CandidateTarget.StayB, CandidateTarget.RotateC],
            adaptive44.Steps.Select(static step => step.Target));

        Assert.Equal(1000, adaptive35.Steps[0].GraphBasePayloadBytes);
        Assert.Equal(40, adaptive35.Steps[0].ADependentBasePayloadBytes);
        Assert.Equal(50, adaptive35.Steps[0].PreferredBaseBudgetBytes);
        Assert.Equal(1000, adaptive44.Steps[0].GraphBasePayloadBytes);
        Assert.Equal(40, adaptive44.Steps[0].ADependentBasePayloadBytes);
        Assert.Equal(40, adaptive44.Steps[0].PreferredBaseBudgetBytes);
        Assert.All(adaptive35.Steps,
            static step => Assert.Equal(ObjectVersionKind.Base, step.SelectedKind));
        Assert.All(adaptive44.Steps,
            static step => Assert.Equal(ObjectVersionKind.Base, step.SelectedKind));
        Assert.All(adaptive35.Steps,
            static step => Assert.Null(step.ProgressOverrideObjectId));
        Assert.All(adaptive44.Steps,
            static step => Assert.Null(step.ProgressOverrideObjectId));
        Assert.All(adaptive35.Steps,
            static step => Assert.Empty(step.StayMigrationObjectIds));
        Assert.All(adaptive44.Steps,
            static step => Assert.Empty(step.StayMigrationObjectIds));
        Assert.All(adaptive35.Steps,
            static step => Assert.Empty(step.RotateOptionalBaseObjectIds));
        Assert.All(adaptive44.Steps,
            static step => Assert.Empty(step.RotateOptionalBaseObjectIds));

        Assert.Equal([100U], adaptive35.Steps[0].ResultDebtObjectIds);
        Assert.Equal(new ScopeValue(2, 3), adaptive35.Steps[0].ResultScope);
        Assert.Empty(adaptive44.Steps[0].ResultDebtObjectIds);
        Assert.Equal(new ScopeValue(1, 2), adaptive44.Steps[0].ResultScope);
        Assert.Empty(adaptive35.Steps[1].ResultDebtObjectIds);
        Assert.Equal(new ScopeValue(2, 3), adaptive35.Steps[1].ResultScope);
        Assert.Equal([1U], adaptive44.Steps[1].ResultDebtObjectIds);
        Assert.Equal(new ScopeValue(2, 3), adaptive44.Steps[1].ResultScope);
        Assert.Equal(1000, adaptive35.Steps[1].GraphBasePayloadBytes);
        Assert.Equal(960, adaptive35.Steps[1].ADependentBasePayloadBytes);
        Assert.Equal(50, adaptive35.Steps[1].PreferredBaseBudgetBytes);
        Assert.Equal(1000, adaptive44.Steps[1].GraphBasePayloadBytes);
        Assert.Equal(0, adaptive44.Steps[1].ADependentBasePayloadBytes);
        Assert.Equal(40, adaptive44.Steps[1].PreferredBaseBudgetBytes);

        Assert.Equal(new ScopeValue(3, 4), adaptive35.FinalScope);
        Assert.Equal(new ScopeValue(3, 4), adaptive44.FinalScope);
        Assert.Equal([1U, 100U], adaptive35.FinalDebtObjectIds);
        Assert.Equal([100U], adaptive44.FinalDebtObjectIds);
        RealizedReconstructionPayloadAmplificationSample[] finalAmplification = [
            new(1, 40, 40),
            new(100, 960, 960),
        ];
        Assert.Equal(finalAmplification, adaptive35.FinalAmplificationSamples);
        Assert.Equal(finalAmplification, adaptive44.FinalAmplificationSamples);
        Assert.Equal(
            new FixedHorizonRawVector(3, 1144, 1004, 1096, 1124),
            adaptive35.Raw);
        Assert.Equal(
            new FixedHorizonRawVector(3, 1188, 1012, 1128, 1088),
            adaptive44.Raw);
    }

    [Fact]
    public void Adaptive_base_fraction_lower_bound_selector_is_read_limit_inert_under_Base_dominance() {
        TargetBandFixture fixture = CreateTargetBandFixture();
        SaveStep firstStep = CreateTargetBandSteps()[0];
        NormalizedSaveFacts facts = SaveStepNormalizer.Normalize(
            fixture.Source.Store,
            fixture.Cursor.FileScope.CurrentFileNumber,
            fixture.Cursor.PublishedRevisionAddress,
            firstStep);
        ReadAmplificationBaseBudgetPolicyProjection projection =
            ReadAmplificationBaseBudgetPolicyProjection.Create(facts);

        ReadAmplificationBaseBudgetPolicySelection read3Fraction4 = Select(
            projection,
            readLimit: 3m,
            baseFraction: 0.04m);
        ReadAmplificationBaseBudgetPolicySelection read4Fraction4 = Select(
            projection,
            readLimit: 4m,
            baseFraction: 0.04m);
        ReadAmplificationBaseBudgetPolicySelection read3Fraction5 = Select(
            projection,
            readLimit: 3m,
            baseFraction: 0.05m);
        ReadAmplificationBaseBudgetPolicySelection read4Fraction5 = Select(
            projection,
            readLimit: 4m,
            baseFraction: 0.05m);

        ReadAmplificationBaseBudgetPolicyObjectFact update = Assert.Single(
            projection.PostLiveObjects,
            static fact =>
                fact.Kind == ReadAmplificationBaseBudgetPolicyObjectKind.Update);
        Assert.Equal(1U, update.ObjectId);
        Assert.Equal(80, update.ReadAmplificationNumeratorBytes);
        Assert.Equal(40, update.PostSaveBasePayloadBytes);
        Assert.Equal(40, update.DeltaPayloadBytes);
        Assert.Equal(CandidateTarget.StayB, read3Fraction4.Target);
        Assert.Equal(read3Fraction4.Target, read4Fraction4.Target);
        Assert.Equal(CandidateTarget.RotateC, read3Fraction5.Target);
        Assert.Equal(read3Fraction5.Target, read4Fraction5.Target);

        foreach (ReadAmplificationBaseBudgetPolicySelection selection in new[] {
            read3Fraction4,
            read4Fraction4,
            read3Fraction5,
            read4Fraction5,
        }) {
            Assert.Null(selection.StayProgressOverrideObjectId);
            Assert.Empty(selection.StayB.UnchangedMigrationObjectIds);
            Assert.Equal(
                new UpdateWriteDecision(1, UpdateWriteMode.Base),
                Assert.Single(selection.StayB.UpdateDecisions));
            Assert.Empty(selection.RotateC.BContainedUpdateDecisions);
            Assert.Empty(selection.RotateC.BContainedNoChangeBaseObjectIds);
        }
    }

    private static TargetBandRun RunTargetBand(
        ReadAmplificationBaseBudgetPolicyParameters parameters) {
        TargetBandFixture fixture = CreateTargetBandFixture();
        SaveStep[] steps = CreateTargetBandSteps();
        long horizonSourceTailBytes = FixedHorizonTotalTailBytes(
            fixture.Source.Store);
        Dictionary<uint, LogicalObjectState> expectedState = new(
            fixture.ExpectedState);
        EvaluatorV1Session session = new(
            fixture.Source.Store,
            fixture.Cursor,
            totalWorkloadStepCount: steps.Length);
        List<TargetBandStep> observations = [];

        foreach (SaveStep step in steps) {
            NormalizedSaveFacts facts = SaveStepNormalizer.Normalize(
                session.Store,
                session.Cursor.FileScope.CurrentFileNumber,
                session.Cursor.PublishedRevisionAddress,
                step);
            ReadAmplificationBaseBudgetPolicyProjection projection =
                ReadAmplificationBaseBudgetPolicyProjection.Create(facts);
            ReadAmplificationBaseBudgetPolicySelection selection =
                ReadAmplificationBaseBudgetPolicy.Select(projection, parameters);
            ExplicitCandidatePairEvaluation pair =
                ExplicitCandidatePairEvaluator.Evaluate(
                    session.Store,
                    facts,
                    selection.StayB,
                    selection.RotateC);
            FeasibleCandidate<StayBRevisionPlan> stay =
                Assert.IsType<FeasibleCandidate<StayBRevisionPlan>>(
                    pair.StayBAttempt);
            FeasibleCandidate<RotateCRevisionPlan> rotate =
                Assert.IsType<FeasibleCandidate<RotateCRevisionPlan>>(
                    pair.RotateCAttempt);
            uint updatedObjectId = Assert.Single(step.Changes.OfType<UpdateObject>())
                .ObjectId;
            Assert.Equal(
                ObjectVersionKind.Base,
                stay.Observation.Candidate.Frame.ObjectVersions[updatedObjectId]
                    .Kind);
            Assert.Equal(
                ObjectVersionKind.Base,
                rotate.Observation.Candidate.Frame.ObjectVersions[updatedObjectId]
                    .Kind);

            RotationPolicyStepAttempt attempt = session.ApplySelectedWorkloadCommit(
                pair,
                selection.Target);
            CandidateTarget appliedTarget = attempt switch {
                AppliedStayBPolicyStep => CandidateTarget.StayB,
                AppliedRotateCPolicyStep => CandidateTarget.RotateC,
                _ => throw new InvalidOperationException(
                    $"Target-band policy action was not applied: {attempt.GetType().Name}."),
            };
            Assert.Equal(selection.Target, appliedTarget);
            ObjectVersionKind selectedKind = attempt switch {
                AppliedStayBPolicyStep appliedStay => appliedStay.Selected
                    .Observation.Candidate.Frame.ObjectVersions[updatedObjectId].Kind,
                AppliedRotateCPolicyStep appliedRotate => appliedRotate.Selected
                    .Observation.Candidate.Frame.ObjectVersions[updatedObjectId].Kind,
                _ => throw new InvalidOperationException(
                    $"Target-band policy action was not applied: {attempt.GetType().Name}."),
            };

            ApplyExpectedState(expectedState, step);
            AssertExactState(expectedState, facts.PostLiveStates);
            AssertRuntimeStateAndClosure(
                session.Store,
                session.Cursor,
                expectedState);
            observations.Add(new TargetBandStep(
                projection.PostLiveGraphBasePayloadBytes,
                projection.ADependentEvacuationBasePayloadBytes,
                selection.PreferredBasePayloadBudgetBytes,
                selection.Target,
                selection.StayProgressOverrideObjectId,
                selection.StayB.UnchangedMigrationObjectIds.ToArray(),
                selection.RotateC.BContainedNoChangeBaseObjectIds.ToArray(),
                selectedKind,
                ScopeValue.From(session.Cursor.FileScope),
                GetFixedHorizonPreviousDebtObjectIds(
                    session.Store,
                    session.Cursor)));
        }

        AdmittedEvaluatorRun admitted = Assert.IsType<AdmittedEvaluatorRun>(
            session.Complete());
        Assert.Equal(steps.Length + 1, admitted.Metrics.RealizedCommitCount);
        Assert.Equal(4, session.Store.FileCount);
        AssertFixedHorizonScope(admitted.FinalCursor, 3, 4);
        AssertDirectFixedHorizonSettlement(admitted.Settlement);
        AssertRuntimeStateAndClosure(
            session.Store,
            admitted.FinalCursor,
            expectedState);
        Assert.Equal(
            admitted.Metrics.TotalPhysicalWriteBytes,
            checked(FixedHorizonTotalTailBytes(session.Store) -
                horizonSourceTailBytes));
        NormalizedSaveFacts finalFacts = SaveStepNormalizer.NormalizeMaintenanceOnly(
            session.Store,
            admitted.FinalCursor.FileScope.CurrentFileNumber,
            admitted.FinalCursor.PublishedRevisionAddress);

        return new TargetBandRun(
            ProjectFixedHorizonRaw(admitted),
            observations,
            ScopeValue.From(admitted.FinalCursor.FileScope),
            GetFixedHorizonPreviousDebtObjectIds(
                session.Store,
                admitted.FinalCursor),
            RealizedReconstructionPayloadAmplificationDiagnostic
                .Capture(finalFacts)
                .Samples);
    }

    private static TargetBandFixture CreateTargetBandFixture() {
        PolicySource source = CreateSource([new InitialObjectSeed(1, 40)]);
        ProbeRevisionCursor cursor = CreateCursor(source);
        Dictionary<uint, LogicalObjectState> expectedState = new(
            source.InitialExpectedState);
        ApplyExactStaySetup(
            source.Store,
            ref cursor,
            expectedState,
            new SaveStep([new CreateObject(100, 960)]),
            updateDecisions: [],
            migrationObjectIds: []);
        AssertFixedHorizonScope(cursor, 1, 2);
        Assert.Equal(
            [1U],
            GetFixedHorizonPreviousDebtObjectIds(source.Store, cursor));
        return new TargetBandFixture(source, cursor, expectedState);
    }

    private static SaveStep[] CreateTargetBandSteps() => [
        new SaveStep([new UpdateObject(1, 40, 40)]),
        new SaveStep([new UpdateObject(100, 960, 960)]),
    ];

    private static ReadAmplificationBaseBudgetPolicySelection Select(
        ReadAmplificationBaseBudgetPolicyProjection projection,
        decimal readLimit,
        decimal baseFraction) => ReadAmplificationBaseBudgetPolicy.Select(
            projection,
            new ReadAmplificationBaseBudgetPolicyParameters(
                readLimit,
                baseFraction));

    private sealed record TargetBandFixture(
        PolicySource Source,
        ProbeRevisionCursor Cursor,
        IReadOnlyDictionary<uint, LogicalObjectState> ExpectedState);

    private sealed record TargetBandStep(
        long GraphBasePayloadBytes,
        long ADependentBasePayloadBytes,
        long PreferredBaseBudgetBytes,
        CandidateTarget Target,
        uint? ProgressOverrideObjectId,
        IReadOnlyList<uint> StayMigrationObjectIds,
        IReadOnlyList<uint> RotateOptionalBaseObjectIds,
        ObjectVersionKind SelectedKind,
        ScopeValue ResultScope,
        uint[] ResultDebtObjectIds);

    private sealed record TargetBandRun(
        FixedHorizonRawVector Raw,
        IReadOnlyList<TargetBandStep> Steps,
        ScopeValue FinalScope,
        uint[] FinalDebtObjectIds,
        IReadOnlyList<RealizedReconstructionPayloadAmplificationSample>
            FinalAmplificationSamples);
}
