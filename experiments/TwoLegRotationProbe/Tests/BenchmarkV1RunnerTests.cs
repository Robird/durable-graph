using Atelia.TwoLegRotationProbe.Benchmarking;
using Atelia.TwoLegRotationProbe.Encoding;
using Atelia.TwoLegRotationProbe.Evaluation;
using Atelia.TwoLegRotationProbe.Model;
using Atelia.TwoLegRotationProbe.Planning;
using Atelia.TwoLegRotationProbe.Policies;
using Atelia.TwoLegRotationProbe.Simulation;
using Atelia.TwoLegRotationProbe.Workloads;

namespace Atelia.TwoLegRotationProbe.Tests;

public sealed class BenchmarkV1RunnerTests {
    [Fact]
    public void Frozen_corpus_defines_two_workloads_with_four_selection_profiles_each() {
        BenchmarkV1BatchDefinition definition = BenchmarkV1Corpus.Create();

        Assert.Equal(2, definition.Manifest.Schema.Version);
        Assert.Equal(3, definition.Manifest.ManifestRevision);
        Assert.Equal(BenchmarkV1ProtocolIdentities.Evaluator, definition.Manifest.Evaluator);
        Assert.Equal(
            BenchmarkV1ProtocolIdentities.TerminalSettlement,
            definition.Manifest.TerminalSettlement);
        Assert.Equal(
            BenchmarkV1ProtocolIdentities.ReadSchedule,
            definition.Manifest.ReadSchedule);
        Assert.Equal(BenchmarkV1ProtocolIdentities.Metrics, definition.Manifest.Metrics);
        Assert.Equal(
            BenchmarkV1ProtocolIdentities.FrameLayout,
            definition.Manifest.FrameLayout);
        Assert.Equal(
            BenchmarkV1ProtocolIdentities.RevisionGrammar,
            definition.Manifest.RevisionGrammar);
        Assert.Equal(8, definition.Cases.Count);
        Assert.Equal(
            [
                BenchmarkV1Corpus.DebtZeroThenRotateNoMigrationCaseId,
                BenchmarkV1Corpus.DebtZeroThenRotatePacedCaseId,
                BenchmarkV1Corpus.DebtZeroThenRotateAdaptiveR3B5PercentCaseId,
                BenchmarkV1Corpus.DebtZeroThenRotateAdaptiveR4B4PercentCaseId,
                BenchmarkV1Corpus.MixedSmallNoMigrationCaseId,
                BenchmarkV1Corpus.MixedSmallPacedCaseId,
                BenchmarkV1Corpus.MixedSmallAdaptiveR3B5PercentCaseId,
                BenchmarkV1Corpus.MixedSmallAdaptiveR4B4PercentCaseId,
            ],
            definition.Cases.Select(static benchmarkCase =>
                benchmarkCase.ManifestCase.CaseId));
        BenchmarkV1CaseDefinition debtNoMigration = FindCaseDefinition(
            definition,
            BenchmarkV1Corpus.DebtZeroThenRotateNoMigrationCaseId);
        BenchmarkV1CaseDefinition debtPaced = FindCaseDefinition(
            definition,
            BenchmarkV1Corpus.DebtZeroThenRotatePacedCaseId);
        BenchmarkV1CaseDefinition debtAdaptive35 = FindCaseDefinition(
            definition,
            BenchmarkV1Corpus.DebtZeroThenRotateAdaptiveR3B5PercentCaseId);
        BenchmarkV1CaseDefinition debtAdaptive44 = FindCaseDefinition(
            definition,
            BenchmarkV1Corpus.DebtZeroThenRotateAdaptiveR4B4PercentCaseId);
        BenchmarkV1CaseDefinition mixedNoMigration = FindCaseDefinition(
            definition,
            BenchmarkV1Corpus.MixedSmallNoMigrationCaseId);
        BenchmarkV1CaseDefinition mixedPaced = FindCaseDefinition(
            definition,
            BenchmarkV1Corpus.MixedSmallPacedCaseId);
        BenchmarkV1CaseDefinition mixedAdaptive35 = FindCaseDefinition(
            definition,
            BenchmarkV1Corpus.MixedSmallAdaptiveR3B5PercentCaseId);
        BenchmarkV1CaseDefinition mixedAdaptive44 = FindCaseDefinition(
            definition,
            BenchmarkV1Corpus.MixedSmallAdaptiveR4B4PercentCaseId);
        AssertMatchedWorkload(
            debtNoMigration,
            debtPaced,
            debtAdaptive35,
            debtAdaptive44);
        AssertMatchedWorkload(
            mixedNoMigration,
            mixedPaced,
            mixedAdaptive35,
            mixedAdaptive44);
        BenchmarkComponentIdentityV1[] expectedProfiles = [
            BenchmarkV1SelectionProfiles.DebtZeroThenRotateDeltaNoMigration.Identity,
            BenchmarkV1SelectionProfiles
                .DebtZeroThenRotateDeltaPacedOneDebtByObjectId.Identity,
            BenchmarkV1SelectionProfiles.ReadAmplificationBaseBudgetR3B5Percent.Identity,
            BenchmarkV1SelectionProfiles.ReadAmplificationBaseBudgetR4B4Percent.Identity,
        ];
        Assert.Equal(
            expectedProfiles.OrderBy(static profile => profile.Id),
            definition.Cases
                .Select(static benchmarkCase =>
                    benchmarkCase.ManifestCase.SelectionProfile)
                .Distinct()
                .OrderBy(static profile => profile.Id));
        Assert.NotEqual(
            debtNoMigration.ManifestCase.ResolvedTraceSha256,
            mixedNoMigration.ManifestCase.ResolvedTraceSha256);
    }

    [Fact]
    public void Frozen_corpus_runs_all_profiles_to_exact_admitted_raw_outcomes() {
        BenchmarkV1BatchDefinition definition = BenchmarkV1Corpus.Create();

        BenchmarkV1BatchRun run = BenchmarkV1Runner.Run(definition);

        Assert.Same(definition, run.Definition);
        AdmittedOutcomeReportV1 debtNoMigration = AssertAdmitted(
            run.Report,
            BenchmarkV1Corpus.DebtZeroThenRotateNoMigrationCaseId,
            realizedCommitCount: 5,
            totalPhysicalWriteBytes: 856,
            peakCommitWriteBytes: 680,
            maxCurrentFileTailBytes: 680,
            finalColdHeadReadBytes: 832,
            previousFileNumber: 2,
            currentFileNumber: 3);
        AdmittedOutcomeReportV1 debtPaced = AssertAdmitted(
            run.Report,
            BenchmarkV1Corpus.DebtZeroThenRotatePacedCaseId,
            realizedCommitCount: 5,
            totalPhysicalWriteBytes: 1536,
            peakCommitWriteBytes: 696,
            maxCurrentFileTailBytes: 804,
            finalColdHeadReadBytes: 756,
            previousFileNumber: 3,
            currentFileNumber: 4);
        AdmittedOutcomeReportV1 debtAdaptive35 = AssertAdmitted(
            run.Report,
            BenchmarkV1Corpus.DebtZeroThenRotateAdaptiveR3B5PercentCaseId,
            realizedCommitCount: 5,
            totalPhysicalWriteBytes: 1536,
            peakCommitWriteBytes: 696,
            maxCurrentFileTailBytes: 804,
            finalColdHeadReadBytes: 756,
            previousFileNumber: 3,
            currentFileNumber: 4);
        AdmittedOutcomeReportV1 debtAdaptive44 = AssertAdmitted(
            run.Report,
            BenchmarkV1Corpus.DebtZeroThenRotateAdaptiveR4B4PercentCaseId,
            realizedCommitCount: 5,
            totalPhysicalWriteBytes: 1536,
            peakCommitWriteBytes: 696,
            maxCurrentFileTailBytes: 804,
            finalColdHeadReadBytes: 756,
            previousFileNumber: 3,
            currentFileNumber: 4);
        AdmittedOutcomeReportV1 mixedNoMigration = AssertAdmitted(
            run.Report,
            BenchmarkV1Corpus.MixedSmallNoMigrationCaseId,
            realizedCommitCount: 3,
            totalPhysicalWriteBytes: 368,
            peakCommitWriteBytes: 164,
            maxCurrentFileTailBytes: 344,
            finalColdHeadReadBytes: 352,
            previousFileNumber: 2,
            currentFileNumber: 3);
        AdmittedOutcomeReportV1 mixedPaced = AssertAdmitted(
            run.Report,
            BenchmarkV1Corpus.MixedSmallPacedCaseId,
            realizedCommitCount: 3,
            totalPhysicalWriteBytes: 368,
            peakCommitWriteBytes: 164,
            maxCurrentFileTailBytes: 344,
            finalColdHeadReadBytes: 352,
            previousFileNumber: 2,
            currentFileNumber: 3);
        AdmittedOutcomeReportV1 mixedAdaptive35 = AssertAdmitted(
            run.Report,
            BenchmarkV1Corpus.MixedSmallAdaptiveR3B5PercentCaseId,
            realizedCommitCount: 3,
            totalPhysicalWriteBytes: 440,
            peakCommitWriteBytes: 164,
            maxCurrentFileTailBytes: 184,
            finalColdHeadReadBytes: 280,
            previousFileNumber: 3,
            currentFileNumber: 4);
        AdmittedOutcomeReportV1 mixedAdaptive44 = AssertAdmitted(
            run.Report,
            BenchmarkV1Corpus.MixedSmallAdaptiveR4B4PercentCaseId,
            realizedCommitCount: 3,
            totalPhysicalWriteBytes: 440,
            peakCommitWriteBytes: 164,
            maxCurrentFileTailBytes: 184,
            finalColdHeadReadBytes: 280,
            previousFileNumber: 3,
            currentFileNumber: 4);

        Assert.NotEqual(debtNoMigration.Metrics, debtPaced.Metrics);
        Assert.Equal(debtPaced.Metrics, debtAdaptive35.Metrics);
        Assert.Equal(debtAdaptive35.Metrics, debtAdaptive44.Metrics);
        Assert.Equal(mixedNoMigration.Metrics, mixedPaced.Metrics);
        Assert.Equal(mixedNoMigration.FinalCursor, mixedPaced.FinalCursor);
        Assert.Equal(
            mixedNoMigration.Settlement.MigratedObjectIds,
            mixedPaced.Settlement.MigratedObjectIds);
        Assert.Equal(mixedAdaptive35.Metrics, mixedAdaptive44.Metrics);
        Assert.Equal(mixedAdaptive35.FinalCursor, mixedAdaptive44.FinalCursor);
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
            "d5a436d29c6416930430d7a22ae082e857b741bc7f7f41410c03732964b998a6",
            first.Report.ManifestSha256);
        Assert.Equal(
            "7b45ca1b4a1dc8473a5d9933fd1ca9ee15db3eda0732f044dbd01c0c59bb7f31",
            BenchmarkV1Json.ComputeSha256(
                BenchmarkV1Json.WriteReport(first.Report)));
        Assert.Equal(
            "cd064c19d4cd94f0a25536481fa0c901ca77e7187b0ff9350b95a2b804d286e2",
            FindCase(
                first.Report,
                BenchmarkV1Corpus.DebtZeroThenRotateNoMigrationCaseId)
                .ResolvedTraceSha256);
        Assert.Equal(
            "cd064c19d4cd94f0a25536481fa0c901ca77e7187b0ff9350b95a2b804d286e2",
            FindCase(
                first.Report,
                BenchmarkV1Corpus.DebtZeroThenRotatePacedCaseId)
                .ResolvedTraceSha256);
        Assert.Equal(
            "cd064c19d4cd94f0a25536481fa0c901ca77e7187b0ff9350b95a2b804d286e2",
            FindCase(
                first.Report,
                BenchmarkV1Corpus.DebtZeroThenRotateAdaptiveR3B5PercentCaseId)
                .ResolvedTraceSha256);
        Assert.Equal(
            "cd064c19d4cd94f0a25536481fa0c901ca77e7187b0ff9350b95a2b804d286e2",
            FindCase(
                first.Report,
                BenchmarkV1Corpus.DebtZeroThenRotateAdaptiveR4B4PercentCaseId)
                .ResolvedTraceSha256);
        Assert.Equal(
            "5bd76d8da2021ed92de43e3d34d08e47cfd6b8b2a79831a2b81cfc41e4d219fc",
            FindCase(
                first.Report,
                BenchmarkV1Corpus.MixedSmallNoMigrationCaseId)
                .ResolvedTraceSha256);
        Assert.Equal(
            "5bd76d8da2021ed92de43e3d34d08e47cfd6b8b2a79831a2b81cfc41e4d219fc",
            FindCase(
                first.Report,
                BenchmarkV1Corpus.MixedSmallPacedCaseId)
                .ResolvedTraceSha256);
        Assert.Equal(
            "5bd76d8da2021ed92de43e3d34d08e47cfd6b8b2a79831a2b81cfc41e4d219fc",
            FindCase(
                first.Report,
                BenchmarkV1Corpus.MixedSmallAdaptiveR3B5PercentCaseId)
                .ResolvedTraceSha256);
        Assert.Equal(
            "5bd76d8da2021ed92de43e3d34d08e47cfd6b8b2a79831a2b81cfc41e4d219fc",
            FindCase(
                first.Report,
                BenchmarkV1Corpus.MixedSmallAdaptiveR4B4PercentCaseId)
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

        BenchmarkV1StepSelection control =
            BenchmarkV1SelectionProfileSelector.Select(
            BenchmarkV1SelectionProfiles.DebtZeroThenRotateDeltaNoMigration.Identity,
            facts);
        BenchmarkV1StepSelection paced =
            BenchmarkV1SelectionProfileSelector.Select(
                BenchmarkV1SelectionProfiles
                .DebtZeroThenRotateDeltaPacedOneDebtByObjectId.Identity,
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
    public void Adaptive_registry_profiles_equal_their_direct_pure_policy_selections() {
        BenchmarkV1CaseDefinition definition = BenchmarkV1Corpus.Create().Cases
            .Single(benchmarkCase => benchmarkCase.ManifestCase.CaseId ==
                BenchmarkV1Corpus.DebtZeroThenRotateAdaptiveR3B5PercentCaseId);
        BenchmarkV1BootstrappedSource source = BenchmarkV1SourceBootstrap.Create(
            definition.Trace);
        NormalizedSaveFacts facts = SaveStepNormalizer.Normalize(
            source.Store,
            source.Cursor.FileScope.CurrentFileNumber,
            source.Cursor.PublishedRevisionAddress,
            definition.Trace.Steps[1]);

        AssertAdaptiveProfileEqualsDirectPolicy(
            BenchmarkV1SelectionProfiles.ReadAmplificationBaseBudgetR3B5Percent.Identity,
            new ReadAmplificationBaseBudgetPolicyParameters(3m, 0.05m),
            facts);
        AssertAdaptiveProfileEqualsDirectPolicy(
            BenchmarkV1SelectionProfiles.ReadAmplificationBaseBudgetR4B4Percent.Identity,
            new ReadAmplificationBaseBudgetPolicyParameters(4m, 0.04m),
            facts);
    }

    [Fact]
    public void Case_definition_rejects_unknown_or_wrong_version_selection_profiles() {
        WorkloadTrace trace = BenchmarkV1Corpus.Create().Cases[0].Trace;
        BenchmarkComponentIdentityV1 traceDefinition = new(
            trace.ScenarioName,
            1);

        Assert.Throws<ArgumentException>(() => new BenchmarkV1CaseDefinition(
            "unknown-selection-profile",
            traceDefinition,
            trace,
            new BenchmarkComponentIdentityV1("unknown-selection-profile", 1)));
        Assert.Throws<ArgumentException>(() => new BenchmarkV1CaseDefinition(
            "wrong-version-selection-profile",
            traceDefinition,
            trace,
            new BenchmarkComponentIdentityV1(
                BenchmarkV1SelectionProfiles
                    .ReadAmplificationBaseBudgetR3B5Percent.Identity.Id,
                2)));
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
            BenchmarkV1SelectionProfiles.DebtZeroThenRotateDeltaNoMigration.Identity);

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
            BenchmarkV1SelectionProfiles.DebtZeroThenRotateDeltaNoMigration.Identity);
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

    private static BenchmarkV1CaseDefinition FindCaseDefinition(
        BenchmarkV1BatchDefinition definition,
        string caseId) => definition.Cases.Single(benchmarkCase =>
            benchmarkCase.ManifestCase.CaseId == caseId);

    private static void AssertMatchedWorkload(
        params BenchmarkV1CaseDefinition[] cases) {
        Assert.Equal(4, cases.Length);
        BenchmarkV1CaseDefinition first = cases[0];
        BenchmarkCaseManifestV1 firstManifest = first.ManifestCase;
        Assert.Equal(4, cases
            .Select(static benchmarkCase =>
                benchmarkCase.ManifestCase.SelectionProfile)
            .Distinct()
            .Count());

        foreach (BenchmarkV1CaseDefinition benchmarkCase in cases.Skip(1)) {
            BenchmarkCaseManifestV1 manifest = benchmarkCase.ManifestCase;
            Assert.NotEqual(firstManifest.CaseId, manifest.CaseId);
            Assert.Same(first.Trace, benchmarkCase.Trace);
            Assert.Equal(firstManifest.SourceFixture, manifest.SourceFixture);
            Assert.Equal(firstManifest.TraceDefinition, manifest.TraceDefinition);
            Assert.Equal(firstManifest.Generator, manifest.Generator);
            Assert.Equal(firstManifest.Seed, manifest.Seed);
            Assert.Equal(
                firstManifest.ResolvedTraceSha256,
                manifest.ResolvedTraceSha256);
            Assert.Equal(
                firstManifest.BootstrapStepCount,
                manifest.BootstrapStepCount);
            Assert.Equal(firstManifest.TraceStepCount, manifest.TraceStepCount);
            Assert.Equal(
                firstManifest.EvaluatedWorkloadStepCount,
                manifest.EvaluatedWorkloadStepCount);
        }
    }

    private static void AssertAdaptiveProfileEqualsDirectPolicy(
        BenchmarkComponentIdentityV1 profile,
        ReadAmplificationBaseBudgetPolicyParameters parameters,
        NormalizedSaveFacts facts) {
        BenchmarkV1StepSelection registry =
            BenchmarkV1SelectionProfileSelector.Select(profile, facts);
        ReadAmplificationBaseBudgetPolicySelection direct =
            ReadAmplificationBaseBudgetPolicy.Select(
                ReadAmplificationBaseBudgetPolicyProjection.Create(facts),
                parameters);

        Assert.Equal(direct.Target, registry.Target);
        Assert.Equal(direct.StayB.UpdateDecisions, registry.StayB.UpdateDecisions);
        Assert.Equal(
            direct.StayB.UnchangedMigrationObjectIds,
            registry.StayB.UnchangedMigrationObjectIds);
        Assert.Equal(
            direct.RotateC.BContainedUpdateDecisions,
            registry.RotateC.BContainedUpdateDecisions);
        Assert.Equal(
            direct.RotateC.BContainedNoChangeBaseObjectIds,
            registry.RotateC.BContainedNoChangeBaseObjectIds);
    }

    private static AdmittedOutcomeReportV1 AssertAdmitted(
        BenchmarkReportV1 report,
        string caseId,
        int realizedCommitCount,
        long totalPhysicalWriteBytes,
        long peakCommitWriteBytes,
        long maxCurrentFileTailBytes,
        long finalColdHeadReadBytes,
        uint previousFileNumber,
        uint currentFileNumber) {
        BenchmarkCaseReportV1 benchmarkCase = FindCase(report, caseId);
        AdmittedOutcomeReportV1 admitted = Assert.IsType<AdmittedOutcomeReportV1>(
            benchmarkCase.Outcome);
        Assert.Equal(realizedCommitCount, admitted.Metrics.RealizedCommitCount);
        Assert.Equal(
            totalPhysicalWriteBytes,
            admitted.Metrics.TotalPhysicalWriteBytes);
        Assert.Equal(peakCommitWriteBytes, admitted.Metrics.PeakCommitWriteBytes);
        Assert.Equal(
            maxCurrentFileTailBytes,
            admitted.Metrics.MaxCurrentFileTailBytes);
        Assert.Equal(
            finalColdHeadReadBytes,
            admitted.Metrics.FinalColdHeadReadBytes);
        Assert.Equal(previousFileNumber, admitted.FinalCursor.PreviousFileNumber);
        Assert.Equal(currentFileNumber, admitted.FinalCursor.CurrentFileNumber);
        Assert.Empty(admitted.Settlement.MigratedObjectIds);
        return admitted;
    }

    private static long TotalTailBytes(
        Atelia.TwoLegRotationProbe.Model.RbfFileStore store) => Enumerable
        .Range(1, store.FileCount)
        .Sum(index => store.GetFile((uint)index).TailOffsetBytes);
}
