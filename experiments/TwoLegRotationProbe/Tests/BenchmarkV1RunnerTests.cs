using Atelia.TwoLegRotationProbe.Benchmarking;
using Atelia.TwoLegRotationProbe.Encoding;
using Atelia.TwoLegRotationProbe.Evaluation;
using Atelia.TwoLegRotationProbe.Model;
using Atelia.TwoLegRotationProbe.Planning;
using Atelia.TwoLegRotationProbe.Simulation;
using Atelia.TwoLegRotationProbe.Workloads;

namespace Atelia.TwoLegRotationProbe.Tests;

public sealed class BenchmarkV1RunnerTests {
    [Fact]
    public void Frozen_corpus_runs_both_cases_to_admitted_closed_horizons() {
        BenchmarkV1BatchDefinition definition = BenchmarkV1Corpus.Create();

        BenchmarkV1BatchRun run = BenchmarkV1Runner.Run(definition);

        Assert.Same(definition, run.Definition);
        AdmittedOutcomeReportV1 named = Assert.IsType<AdmittedOutcomeReportV1>(
            FindCase(run.Report, BenchmarkV1Corpus.DebtZeroThenRotatePacedCaseId)
                .Outcome);
        Assert.Equal(5, named.Metrics.RealizedCommitCount);
        Assert.Equal(3U, named.FinalCursor.PreviousFileNumber);
        Assert.Equal(4U, named.FinalCursor.CurrentFileNumber);
        Assert.Empty(named.Settlement.MigratedObjectIds);

        AdmittedOutcomeReportV1 generated = Assert.IsType<AdmittedOutcomeReportV1>(
            FindCase(run.Report, BenchmarkV1Corpus.MixedSmallNoMigrationCaseId)
                .Outcome);
        Assert.Equal(3, generated.Metrics.RealizedCommitCount);
        Assert.Equal(2U, generated.FinalCursor.PreviousFileNumber);
        Assert.Equal(3U, generated.FinalCursor.CurrentFileNumber);
        Assert.Empty(generated.Settlement.MigratedObjectIds);
        Assert.NotEqual(
            FindCase(run.Report, BenchmarkV1Corpus.DebtZeroThenRotatePacedCaseId)
                .ResolvedTraceSha256,
            FindCase(run.Report, BenchmarkV1Corpus.MixedSmallNoMigrationCaseId)
                .ResolvedTraceSha256);
    }

    [Fact]
    public void Rebuilt_corpus_manifest_and_report_are_byte_identical() {
        BenchmarkV1BatchRun first = BenchmarkV1Runner.Run(
            BenchmarkV1Corpus.Create());
        BenchmarkV1BatchRun second = BenchmarkV1Runner.Run(
            BenchmarkV1Corpus.Create());

        Assert.Equal(
            BenchmarkV1Json.WriteManifest(first.Definition.Manifest),
            BenchmarkV1Json.WriteManifest(second.Definition.Manifest));
        Assert.Equal(
            BenchmarkV1Json.WriteReport(first.Report),
            BenchmarkV1Json.WriteReport(second.Report));
        Assert.Equal(
            "438326877cedd8f7925c913d4c446fe02f8d50f4bf11c8b1e3227283b74d914c",
            first.Report.ManifestSha256);
        Assert.Equal(
            "01e6f2bca39fd3ed5225a2dc243fb85aa78f0d023a1586322389b4f0868ab842",
            BenchmarkV1Json.ComputeSha256(
                BenchmarkV1Json.WriteReport(first.Report)));
        Assert.Equal(
            "cd064c19d4cd94f0a25536481fa0c901ca77e7187b0ff9350b95a2b804d286e2",
            FindCase(
                first.Report,
                BenchmarkV1Corpus.DebtZeroThenRotatePacedCaseId)
                .ResolvedTraceSha256);
        Assert.Equal(
            "5bd76d8da2021ed92de43e3d34d08e47cfd6b8b2a79831a2b81cfc41e4d219fc",
            FindCase(
                first.Report,
                BenchmarkV1Corpus.MixedSmallNoMigrationCaseId)
                .ResolvedTraceSha256);
    }

    [Fact]
    public void Bootstrap_places_step_zero_Bases_in_A_and_excludes_them_from_W_and_P() {
        BenchmarkV1CaseDefinition definition = BenchmarkV1Corpus.Create().Cases
            .Single(benchmarkCase => benchmarkCase.ManifestCase.CaseId ==
                BenchmarkV1Corpus.DebtZeroThenRotatePacedCaseId);
        BenchmarkV1BootstrappedSource source = BenchmarkV1SourceBootstrap.Create(
            definition.Trace);

        ObjectVersionDictionaryMaterializationInspection materialized =
            ObjectVersionDictionaryReader.MaterializeLive(
                source.Store,
                source.Cursor.PublishedRevisionAddress);
        Assert.Equal([10U, 20U, 30U], materialized.Bindings.Keys.Order());
        Assert.All(
            materialized.Bindings.Values,
            address => Assert.Equal(1U, address.FileNumber));
        Assert.All(
            materialized.Bindings,
            pair => Assert.Equal(
                1U,
                PhysicalStateOracle.InspectObjectReconstruction(
                    source.Store,
                    pair.Key,
                    pair.Value).BaseAddress.FileNumber));

        long bootstrapBytes = TotalTailBytes(source.Store);
        EvaluatorV1Session session = new(
            source.Store,
            source.Cursor,
            totalWorkloadStepCount: 0);
        AdmittedEvaluatorRun admitted = Assert.IsType<AdmittedEvaluatorRun>(
            session.Complete());
        long horizonGrowth = TotalTailBytes(session.Store) - bootstrapBytes;

        Assert.True(bootstrapBytes > 0);
        Assert.Equal(bootstrapBytes, TotalTailBytes(source.Store));
        Assert.Equal(horizonGrowth, admitted.Metrics.TotalPhysicalWriteBytes);
        Assert.Equal(horizonGrowth, admitted.Metrics.PeakCommitWriteBytes);
        Assert.NotEqual(
            TotalTailBytes(session.Store),
            admitted.Metrics.TotalPhysicalWriteBytes);
    }

    [Fact]
    public void Pacing_and_control_read_the_same_facts_and_differ_only_by_smallest_debt_migration() {
        BenchmarkV1CaseDefinition definition = BenchmarkV1Corpus.Create().Cases
            .Single(benchmarkCase => benchmarkCase.ManifestCase.CaseId ==
                BenchmarkV1Corpus.DebtZeroThenRotatePacedCaseId);
        BenchmarkV1BootstrappedSource source = BenchmarkV1SourceBootstrap.Create(
            definition.Trace);
        NormalizedSaveFacts facts = SaveStepNormalizer.Normalize(
            source.Store,
            source.Cursor.FileScope.CurrentFileNumber,
            source.Cursor.PublishedRevisionAddress,
            definition.Trace.Steps[1]);

        BenchmarkV1StepSelection control = BenchmarkV1TreatmentSelector.Select(
            BenchmarkV1TreatmentIdentities.DebtZeroThenRotate,
            BenchmarkV1TreatmentIdentities.DeltaNoMigration,
            facts);
        BenchmarkV1StepSelection paced = BenchmarkV1TreatmentSelector.Select(
            BenchmarkV1TreatmentIdentities.DebtZeroThenRotate,
            BenchmarkV1TreatmentIdentities.DeltaPacedOneDebtByObjectId,
            facts);

        Assert.Equal(CandidateTarget.StayB, control.Target);
        Assert.Equal(control.Target, paced.Target);
        Assert.Equal(
            control.StayB.UpdateDecisions,
            paced.StayB.UpdateDecisions);
        Assert.Equal(
            control.RotateC.BContainedUpdateDecisions,
            paced.RotateC.BContainedUpdateDecisions);
        Assert.Empty(control.StayB.UnchangedMigrationObjectIds);
        Assert.Equal([10U], paced.StayB.UnchangedMigrationObjectIds);
        Assert.Empty(control.RotateC.BContainedNoChangeBaseObjectIds);
        Assert.Empty(paced.RotateC.BContainedNoChangeBaseObjectIds);
    }

    [Fact]
    public void Non_Create_bootstrap_fails_closed_before_an_evaluator_outcome_exists() {
        WorkloadTrace invalid = new(
            "invalid-bootstrap",
            "handwritten",
            generatorVersion: 1,
            seed: 0,
            [new SaveStep([new UpdateObject(1, 10, 1)])]);

        InvalidDataException exception = Assert.Throws<InvalidDataException>(
            () => BenchmarkV1SourceBootstrap.Create(invalid));

        Assert.Contains("step 0", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Selected_capacity_rejection_stops_the_case_without_fallback_or_partial_metrics() {
        WorkloadTrace trace = new(
            "selected-capacity",
            "handwritten",
            generatorVersion: 1,
            seed: 0,
            [
                new SaveStep([new CreateObject(1, 10)]),
                new SaveStep([new CreateObject(
                    2,
                    RbfV040Layout.MaxPayloadAndTailMetaLengthBytes)]),
                new SaveStep([new CreateObject(3, 1)]),
            ]);
        BenchmarkV1CaseDefinition benchmarkCase = new(
            "selected-capacity-case",
            new BenchmarkComponentIdentityV1("selected-capacity", 1),
            trace,
            BenchmarkV1TreatmentIdentities.DebtZeroThenRotate,
            BenchmarkV1TreatmentIdentities.DeltaNoMigration);

        BenchmarkV1BatchRun run = BenchmarkV1Runner.Run(Batch(benchmarkCase));

        CapacityRejectedOutcomeReportV1 rejected =
            Assert.IsType<CapacityRejectedOutcomeReportV1>(
                Assert.Single(run.Report.Cases).Outcome);
        Assert.Equal(CandidateTarget.StayB, rejected.SelectedTarget);
        Assert.Equal(EvaluatorRunPhase.Workload, rejected.Position.Phase);
        Assert.Equal(0, rejected.Position.CompletedWorkloadStepCount);
        Assert.Equal(2, rejected.Position.TotalWorkloadStepCount);
        Assert.Equal(
            RevisionCandidateCapacityLimit.PayloadAndTailMetaLength,
            rejected.Rejection.Limit);
    }

    [Fact]
    public void One_step_trace_has_zero_evaluated_workload_and_still_settles() {
        WorkloadTrace trace = new(
            "bootstrap-only",
            "handwritten",
            generatorVersion: 1,
            seed: 0,
            [new SaveStep([new CreateObject(1, 10)])]);
        BenchmarkV1CaseDefinition benchmarkCase = new(
            "bootstrap-only-case",
            new BenchmarkComponentIdentityV1("bootstrap-only", 1),
            trace,
            BenchmarkV1TreatmentIdentities.DebtZeroThenRotate,
            BenchmarkV1TreatmentIdentities.DeltaNoMigration);
        BenchmarkV1BatchRun run = BenchmarkV1Runner.Run(Batch(benchmarkCase));

        Assert.Equal(0, benchmarkCase.ManifestCase.EvaluatedWorkloadStepCount);
        AdmittedOutcomeReportV1 admitted = Assert.IsType<AdmittedOutcomeReportV1>(
            Assert.Single(run.Report.Cases).Outcome);
        Assert.Equal(1, admitted.Metrics.RealizedCommitCount);
        Assert.Equal(2U, admitted.FinalCursor.PreviousFileNumber);
        Assert.Equal(3U, admitted.FinalCursor.CurrentFileNumber);
    }

    [Fact]
    public void Batch_rejects_protocol_identity_that_does_not_match_the_runner() {
        BenchmarkV1CaseDefinition benchmarkCase = BenchmarkV1Corpus.Create().Cases[0];

        Assert.Throws<ArgumentException>(() => new BenchmarkV1BatchDefinition(
            BenchmarkV1Corpus.ManifestId,
            BenchmarkV1Corpus.ManifestRevision,
            new BenchmarkComponentIdentityV1("not-the-evaluator", 1),
            BenchmarkV1ProtocolIdentities.TerminalSettlement,
            BenchmarkV1ProtocolIdentities.ReadSchedule,
            BenchmarkV1ProtocolIdentities.Metrics,
            BenchmarkV1ProtocolIdentities.FrameLayout,
            BenchmarkV1ProtocolIdentities.RevisionGrammar,
            [benchmarkCase]));
    }

    private static BenchmarkV1BatchDefinition Batch(
        params BenchmarkV1CaseDefinition[] cases) => new(
        BenchmarkV1Corpus.ManifestId,
        BenchmarkV1Corpus.ManifestRevision,
        BenchmarkV1ProtocolIdentities.Evaluator,
        BenchmarkV1ProtocolIdentities.TerminalSettlement,
        BenchmarkV1ProtocolIdentities.ReadSchedule,
        BenchmarkV1ProtocolIdentities.Metrics,
        BenchmarkV1ProtocolIdentities.FrameLayout,
        BenchmarkV1ProtocolIdentities.RevisionGrammar,
        cases);

    private static BenchmarkCaseReportV1 FindCase(
        BenchmarkReportV1 report,
        string caseId) => report.Cases.Single(benchmarkCase =>
            benchmarkCase.CaseId == caseId);

    private static long TotalTailBytes(
        Atelia.TwoLegRotationProbe.Model.RbfFileStore store) => Enumerable
        .Range(1, store.FileCount)
        .Sum(index => store.GetFile((uint)index).TailOffsetBytes);
}
