using Atelia.TwoLegRotationProbe.Arena;
using Atelia.TwoLegRotationProbe.Baselines;
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
    public void Corpus_defines_eighteen_workloads_with_two_active_profiles_each() {
        BenchmarkV1BatchDefinition definition = BenchmarkV1Corpus.Create(
            BenchmarkV1Baselines.All);

        Assert.Equal(2, definition.Manifest.Schema.Version);
        Assert.Equal(18, definition.Manifest.ManifestRevision);
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
        Assert.Equal(36, definition.Cases.Count);
        Assert.Equal(18, definition.Cases
            .Select(static benchmarkCase =>
                benchmarkCase.ManifestCase.TraceDefinition.Id)
            .Distinct(StringComparer.Ordinal)
            .Count());
        Assert.Equal(
            [
                BenchmarkV1Baselines.ReadAmplificationBaseBudgetR3B5Percent.Identity,
                BenchmarkV1Baselines.ReadAmplificationBaseBudgetR4B4Percent.Identity,
            ],
            definition.Cases
                .Select(static benchmarkCase =>
                    benchmarkCase.ManifestCase.SelectionProfile)
                .Distinct()
                .OrderBy(static identity => identity.Id, StringComparer.Ordinal));
        Assert.All(
            definition.Cases,
            static benchmarkCase => Assert.Equal(
                2,
                benchmarkCase.ManifestCase.SelectionProfile.Version));

        foreach (IGrouping<string, BenchmarkV1CaseDefinition> group in
            definition.Cases.GroupBy(
                static benchmarkCase =>
                    benchmarkCase.ManifestCase.TraceDefinition.Id,
                StringComparer.Ordinal)) {
            BenchmarkV1CaseDefinition[] cases = group.ToArray();
            Assert.Equal(2, cases.Length);
            Assert.Same(cases[0].Trace, cases[1].Trace);
            Assert.Equal(
                cases[0].ManifestCase.ResolvedTraceSha256,
                cases[1].ManifestCase.ResolvedTraceSha256);
        }
    }

    [Fact]
    public void Frozen_corpus_admits_both_profiles_and_preserves_shared_references() {
        BenchmarkV1BatchRun run = BenchmarkV1Runner.Run(
            BenchmarkV1Corpus.Create(BenchmarkV1Baselines.All));

        Assert.Equal(36, run.Report.Cases.Count);
        foreach (IGrouping<string, BenchmarkCaseReportV1> group in
            run.Report.Cases.GroupBy(
                static item => item.ResolvedTraceSha256,
                StringComparer.Ordinal)) {
            EvaluatorMetricsReportV1[] metrics = group
                .Select(static item => Assert.IsType<AdmittedOutcomeReportV1>(
                    item.Outcome).Metrics)
                .ToArray();
            Assert.Equal(2, metrics.Length);
            Assert.Single(metrics
                .Select(static item =>
                    item.TotalWorkloadDeltaReferencePayloadBytes)
                .Distinct());
            Assert.Single(metrics
                .Select(static item =>
                    item.TotalWorkloadBaseReferencePayloadBytes)
                .Distinct());
            Assert.All(metrics, static item => {
                Assert.InRange(
                    item.PeakWorkloadCommitWriteBytes,
                    0,
                    item.WorkloadPhysicalWriteBytes);
            });
        }

        AssertActiveHundred(
            run.Report,
            BenchmarkV1Corpus.ActiveHundredMixedAdaptiveR3B5PercentCaseId,
            workloadWriteBytes: 116424,
            peakWorkloadWriteBytes: 2128,
            maxCurrentFileTailBytes: 58460,
            totalColdReadBytes: 3501092);
        AssertActiveHundred(
            run.Report,
            BenchmarkV1Corpus.ActiveHundredMixedAdaptiveR4B4PercentCaseId,
            workloadWriteBytes: 114756,
            peakWorkloadWriteBytes: 2104,
            maxCurrentFileTailBytes: 91380,
            totalColdReadBytes: 3754140);
    }

    [Fact]
    public void Active_hundred_with_cold_debt_stays_for_the_whole_workload_horizon() {
        BenchmarkV1BatchDefinition corpus = BenchmarkV1Corpus.Create(
            BenchmarkV1Baselines.All);

        foreach (string caseId in new[] {
            BenchmarkV1Corpus.ActiveHundredMixedColdDebtAdaptiveR3B5PercentCaseId,
            BenchmarkV1Corpus.ActiveHundredMixedColdDebtAdaptiveR4B4PercentCaseId,
        }) {
            BenchmarkV1CaseExecution execution = BenchmarkV1Runner.ExecuteCase(
                FindCaseDefinition(corpus, caseId));
            StrategyRunProductV1 product = execution.Product;
            _ = Assert.IsType<AdmittedEvaluatorRun>(execution.Outcome);
            Assert.Equal(64, product.WorkloadCommits.Count);
            Assert.All(
                product.WorkloadCommits,
                static receipt => Assert.Equal(
                    StrategyTargetV1.StayB,
                    receipt.SelectedTarget));

            StrategyRevisionCheckpointV1 lastWorkload =
                product.WorkloadCommits[^1].Result;
            AbsoluteFrameAddress lastWorkloadRevision = new(
                lastWorkload.PublishedRevisionFileNumber,
                new FrameTicket(
                    lastWorkload.PublishedRevisionOffsetBytes,
                    lastWorkload.PublishedRevisionLengthBytes));
            for (int index = 0;
                index < BenchmarkV1Corpus.ActiveHundredColdDebtObjectCount;
                index++) {
                uint objectId = checked(
                    BenchmarkV1Corpus.ActiveHundredColdDebtObjectIdStart +
                    (uint)index);
                ObjectVersionDictionaryLookupInspection lookup =
                    ObjectVersionDictionaryReader.LookupLive(
                        product.Store,
                        lastWorkloadRevision,
                        objectId);
                Assert.Equal(
                    lastWorkload.PreviousFileNumber,
                    lookup.ResolvedObjectVersionAddress?.FileNumber);
            }
        }
    }

    [Fact]
    public void Oversized_cold_nochange_does_not_escape_the_workload_Base_budget() {
        BenchmarkV1BatchRun run = BenchmarkV1Runner.Run(
            BenchmarkV1Corpus.Create(BenchmarkV1Baselines.All));

        foreach (string caseId in new[] {
            BenchmarkV1Corpus
                .OversizedColdNoChangeTinyClockAdaptiveR3B5PercentCaseId,
            BenchmarkV1Corpus
                .OversizedColdNoChangeTinyClockAdaptiveR4B4PercentCaseId,
        }) {
            AdmittedOutcomeReportV1 admitted =
                Assert.IsType<AdmittedOutcomeReportV1>(
                    FindCase(run.Report, caseId).Outcome);

            // The only foreground payload after bootstrap is a 1-byte clock.
            // A workload peak at or above the cold object's 10,000-byte Base
            // proves that an unmotivated NoChange migration escaped the budget.
            Assert.InRange(
                admitted.Metrics.PeakWorkloadCommitWriteBytes,
                0,
                9_999);
        }
    }

    [Fact]
    public void Rebuilt_corpus_manifest_and_report_are_byte_identical() {
        BenchmarkV1BatchRun first = BenchmarkV1Runner.Run(
            BenchmarkV1Corpus.Create(BenchmarkV1Baselines.All));
        BenchmarkV1BatchRun second = BenchmarkV1Runner.Run(
            BenchmarkV1Corpus.Create(BenchmarkV1Baselines.All.Reverse()));

        Assert.Equal(
            BenchmarkV1Json.WriteManifest(first.Definition.Manifest),
            BenchmarkV1Json.WriteManifest(second.Definition.Manifest));
        Assert.Equal(
            BenchmarkV1Json.WriteReport(first.Report),
            BenchmarkV1Json.WriteReport(second.Report));
    }

    [Fact]
    public void Bootstrap_places_step_zero_Bases_in_A_and_excludes_them_from_metrics() {
        BenchmarkV1CaseDefinition definition = FindCaseDefinition(
            BenchmarkV1Corpus.Create(BenchmarkV1Baselines.All),
            BenchmarkV1Corpus.DebtZeroThenRotateAdaptiveR3B5PercentCaseId);
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

        long bootstrapBytes = TotalTailBytes(source.Store);
        EvaluatorV1Session session = new(
            source.Store,
            source.Cursor,
            totalWorkloadStepCount: 0);
        AdmittedEvaluatorRun admitted = Assert.IsType<AdmittedEvaluatorRun>(
            session.Complete());
        long measuredGrowth = TotalTailBytes(session.Store) - bootstrapBytes;

        Assert.True(bootstrapBytes > 0);
        Assert.Equal(measuredGrowth, admitted.Metrics.TotalPhysicalWriteBytes);
        Assert.Equal(measuredGrowth, admitted.Metrics.PeakCommitWriteBytes);
        Assert.NotEqual(
            TotalTailBytes(session.Store),
            admitted.Metrics.TotalPhysicalWriteBytes);
    }

    [Fact]
    public void Adaptive_registry_profiles_equal_their_direct_policy_selections() {
        BenchmarkV1CaseDefinition definition = FindCaseDefinition(
            BenchmarkV1Corpus.Create(BenchmarkV1Baselines.All),
            BenchmarkV1Corpus.DebtZeroThenRotateAdaptiveR3B5PercentCaseId);
        BenchmarkV1BootstrappedSource source = BenchmarkV1SourceBootstrap.Create(
            definition.Trace);
        NormalizedSaveFacts facts = SaveStepNormalizer.Normalize(
            source.Store,
            source.Cursor.FileScope.CurrentFileNumber,
            source.Cursor.PublishedRevisionAddress,
            definition.Trace.Steps[1]);

        AssertAdaptiveProfileEqualsDirectPolicy(
            BenchmarkV1Baselines.ReadAmplificationBaseBudgetR3B5Percent.Identity,
            new ReadAmplificationBaseBudgetPolicyParameters(3m, 0.05m),
            facts);
        AssertAdaptiveProfileEqualsDirectPolicy(
            BenchmarkV1Baselines.ReadAmplificationBaseBudgetR4B4Percent.Identity,
            new ReadAmplificationBaseBudgetPolicyParameters(4m, 0.04m),
            facts);
    }

    [Fact]
    public void Identity_only_case_is_manifest_only_and_runner_fails_closed() {
        WorkloadTrace trace = BenchmarkV1Corpus.Create(
            BenchmarkV1Baselines.All).Cases[0].Trace;
        BenchmarkComponentIdentityV1 traceDefinition = new(trace.ScenarioName, 1);
        BenchmarkV1CaseDefinition unknown = new(
            "unknown-selection-profile",
            traceDefinition,
            trace,
            new BenchmarkComponentIdentityV1("unknown-selection-profile", 1));
        BenchmarkV1CaseDefinition wrongVersion = new(
            "wrong-version-selection-profile",
            traceDefinition,
            trace,
            new BenchmarkComponentIdentityV1(
                BenchmarkV1Baselines
                    .ReadAmplificationBaseBudgetR3B5Percent.Identity.Id,
                BenchmarkV1Baselines
                    .ReadAmplificationBaseBudgetR3B5Percent.Identity.Version + 1));

        Assert.Null(unknown.Strategy);
        Assert.Null(wrongVersion.Strategy);
        Assert.Throws<InvalidDataException>(() =>
            BenchmarkV1Runner.ExecuteCase(unknown));
        Assert.Throws<InvalidDataException>(() =>
            BenchmarkV1Runner.ExecuteCase(wrongVersion));
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
    public void Selected_capacity_rejection_stops_without_fallback_or_metrics() {
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
        StrategyBindingV1 alwaysStay = new(
            new BenchmarkComponentIdentityV1("always-stay-capacity-probe", 1),
            "always-stay-capacity-probe",
            static () => RunAlwaysStay);
        BenchmarkV1CaseDefinition benchmarkCase = new(
            "selected-capacity-case",
            new BenchmarkComponentIdentityV1("selected-capacity", 1),
            trace,
            alwaysStay);

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
    public void Bootstrap_only_trace_has_zero_workload_metrics_and_still_settles() {
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
            BenchmarkV1Baselines.ReadAmplificationBaseBudgetR3B5Percent);
        BenchmarkV1CaseExecution execution = BenchmarkV1Runner.ExecuteCase(
            benchmarkCase);
        AdmittedEvaluatorRun rawAdmitted = Assert.IsType<AdmittedEvaluatorRun>(
            execution.Outcome);

        BenchmarkV1BatchRun run = BenchmarkV1Runner.Run(Batch(benchmarkCase));

        Assert.Equal(0, rawAdmitted.Metrics.WorkloadPhysicalWriteBytes);
        Assert.True(rawAdmitted.Metrics.TerminalSettlementPhysicalWriteBytes > 0);
        Assert.Equal(
            rawAdmitted.Metrics.TerminalSettlementPhysicalWriteBytes,
            rawAdmitted.Metrics.TotalPhysicalWriteBytes);
        Assert.Equal(0, benchmarkCase.ManifestCase.EvaluatedWorkloadStepCount);
        AdmittedOutcomeReportV1 admitted = Assert.IsType<AdmittedOutcomeReportV1>(
            Assert.Single(run.Report.Cases).Outcome);
        Assert.Equal(0, admitted.Metrics.PeakWorkloadCommitWriteBytes);
        Assert.Equal(0, admitted.Metrics.WorkloadPhysicalWriteBytes);
        string reportJson = System.Text.Encoding.UTF8.GetString(
            BenchmarkV1Json.WriteReport(run.Report));
        Assert.DoesNotContain(
            "\"totalPhysicalWriteBytes\"",
            reportJson,
            StringComparison.Ordinal);
        Assert.DoesNotContain(
            "\"terminalSettlementPhysicalWriteBytes\"",
            reportJson,
            StringComparison.Ordinal);
    }

    [Fact]
    public void Admitted_sample_count_is_checked_before_report_projection() {
        BenchmarkV1CaseDefinition definition = FindCaseDefinition(
            BenchmarkV1Corpus.Create(BenchmarkV1Baselines.All),
            BenchmarkV1Corpus.MixedSmallAdaptiveR3B5PercentCaseId);
        AdmittedEvaluatorRun admitted = Assert.IsType<AdmittedEvaluatorRun>(
            BenchmarkV1Runner.ExecuteCase(definition).Outcome);
        EvaluatorRawMetrics missingSamples = new(
            admitted.Metrics.RealizedCommitCount,
            workloadPhysicalWriteBytes: 0,
            terminalSettlementPhysicalWriteBytes:
                admitted.Metrics.TerminalSettlementPhysicalWriteBytes,
            totalWorkloadDeltaReferencePayloadBytes: 0,
            totalWorkloadBaseReferencePayloadBytes: 0,
            peakWorkloadCommitWriteBytes: 0,
            admitted.Metrics.MaxCurrentFileTailBytes,
            workloadColdReadSamples: [],
            admitted.Metrics.TerminalColdHeadRead);
        AdmittedEvaluatorRun inconsistent = new(
            admitted.Position,
            admitted.FinalCursor,
            missingSamples,
            admitted.Settlement);

        Assert.Throws<InvalidDataException>(() =>
            BenchmarkOutcomeReportV1.Project(inconsistent));
    }

    [Fact]
    public void Batch_rejects_protocol_identities_that_do_not_match_the_runner() {
        BenchmarkV1CaseDefinition benchmarkCase = BenchmarkV1Corpus.Create(
            BenchmarkV1Baselines.All).Cases[0];

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
        Assert.Throws<ArgumentException>(() => new BenchmarkV1BatchDefinition(
            BenchmarkV1Corpus.ManifestId,
            BenchmarkV1Corpus.ManifestRevision,
            BenchmarkV1ProtocolIdentities.Evaluator,
            BenchmarkV1ProtocolIdentities.TerminalSettlement,
            BenchmarkV1ProtocolIdentities.ReadSchedule,
            new BenchmarkComponentIdentityV1("raw-wpfr", 4),
            BenchmarkV1ProtocolIdentities.FrameLayout,
            BenchmarkV1ProtocolIdentities.RevisionGrammar,
            [benchmarkCase]));
    }

    private static StrategyRunProductV1 RunAlwaysStay(
        StrategyRunContextV1 context) {
        while (context.CurrentStep is StrategyStepViewV1 view) {
            StrategySelectionV1 selection = new(
                StrategyTargetV1.StayB,
                new StrategyStayDecisionV1(
                    view.Objects
                        .Where(static fact =>
                            fact.Kind == StrategyObjectKindV1.Update)
                        .Select(static fact =>
                            new StrategyUpdateWriteDecisionV1(
                                fact.ObjectId,
                                StrategyUpdateWriteModeV1.Delta)),
                    []),
                new StrategyRotateDecisionV1([], []));
            StrategyCommitStatusV1 status = context.Commit(selection);
            if (status != StrategyCommitStatusV1.AppliedStayB) {
                break;
            }
        }

        return context.Complete();
    }

    private static void AssertActiveHundred(
        BenchmarkReportV1 report,
        string caseId,
        long workloadWriteBytes,
        long peakWorkloadWriteBytes,
        long maxCurrentFileTailBytes,
        long totalColdReadBytes) {
        AdmittedOutcomeReportV1 admitted = Assert.IsType<AdmittedOutcomeReportV1>(
            FindCase(report, caseId).Outcome);
        EvaluatorMetricsReportV1 metrics = admitted.Metrics;
        Assert.Equal(workloadWriteBytes, metrics.WorkloadPhysicalWriteBytes);
        Assert.Equal(
            67206,
            metrics.TotalWorkloadDeltaReferencePayloadBytes);
        Assert.Equal(
            165606,
            metrics.TotalWorkloadBaseReferencePayloadBytes);
        Assert.Equal(peakWorkloadWriteBytes, metrics.PeakWorkloadCommitWriteBytes);
        Assert.Equal(maxCurrentFileTailBytes, metrics.MaxCurrentFileTailBytes);
        Assert.Equal(totalColdReadBytes, metrics.TotalWorkloadColdReadBytes);
        Assert.Equal(273804, metrics.TotalWorkloadLogicalBasePayloadBytes);
    }

    private static void AssertAdaptiveProfileEqualsDirectPolicy(
        BenchmarkComponentIdentityV1 profile,
        ReadAmplificationBaseBudgetPolicyParameters parameters,
        NormalizedSaveFacts facts) {
        StrategyStepViewV1 view = StrategyStepViewV1.Create(facts);
        StrategySelectionV1 registry = BenchmarkV1Baselines.Select(profile, view);
        ReadAmplificationBaseBudgetPolicySelection direct =
            ReadAmplificationBaseBudgetPolicy.Select(
                ReadAmplificationBaseBudgetPolicyProjection.Create(view),
                parameters);

        Assert.Equal(direct.Target, registry.Target);
        Assert.Equal(direct.StayB.UpdateDecisions, registry.Stay.UpdateDecisions);
        Assert.Equal(
            direct.StayB.UnchangedMigrationObjectIds,
            registry.Stay.UnchangedMigrationObjectIds);
        Assert.Equal(
            direct.RotateC.BContainedUpdateDecisions,
            registry.Rotate.BContainedUpdateDecisions);
        Assert.Equal(
            direct.RotateC.BContainedNoChangeBaseObjectIds,
            registry.Rotate.BContainedNoChangeBaseObjectIds);
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

    private static long TotalTailBytes(RbfFileStore store) => Enumerable
        .Range(1, store.FileCount)
        .Sum(index => store.GetFile((uint)index).TailOffsetBytes);
}
