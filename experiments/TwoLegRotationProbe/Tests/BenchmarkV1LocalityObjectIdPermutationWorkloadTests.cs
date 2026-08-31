using Atelia.TwoLegRotationProbe.Arena;
using Atelia.TwoLegRotationProbe.Baselines;
using Atelia.TwoLegRotationProbe.Benchmarking;
using Atelia.TwoLegRotationProbe.Evaluation;
using Atelia.TwoLegRotationProbe.Workloads;

namespace Atelia.TwoLegRotationProbe.Tests;

public sealed class BenchmarkV1LocalityObjectIdPermutationWorkloadTests {
    [Fact]
    public void Locality_pair_is_one_matched_ObjectId_permutation_family() {
        BenchmarkV1BatchDefinition corpus = BenchmarkV1Corpus.Create(
            BenchmarkV1Baselines.All);
        BenchmarkV1CaseDefinition[] lowCases = FindCases(
            corpus,
            "locality-next-update-low-id");
        BenchmarkV1CaseDefinition[] highCases = FindCases(
            corpus,
            "locality-next-update-high-id");

        Assert.Equal(4, lowCases.Length);
        Assert.Equal(4, highCases.Length);
        AssertMatchedTrace(
            lowCases,
            "locality-next-update-low-id",
            "4ab42dae6600bc32058838bd4e738fecdd41428f4d049e59135160a5445e4c42");
        AssertMatchedTrace(
            highCases,
            "locality-next-update-high-id",
            "fdc0630433f49dbf445e2805d4f567c1b175c67d7dfde0dd44cb8e2de0b738fd");
        Assert.Same(lowCases[0].Trace.Steps[0], highCases[0].Trace.Steps[0]);
        Assert.Same(lowCases[0].Trace.Steps[1], highCases[0].Trace.Steps[1]);

        UpdateObject lowUpdate = Assert.IsType<UpdateObject>(
            lowCases[0].Trace.Steps[2].Changes[0]);
        UpdateObject highUpdate = Assert.IsType<UpdateObject>(
            highCases[0].Trace.Steps[2].Changes[0]);
        Assert.Equal(10U, lowUpdate.ObjectId);
        Assert.Equal(20U, highUpdate.ObjectId);
        Assert.Equal(
            lowUpdate with { ObjectId = highUpdate.ObjectId },
            highUpdate);
        Assert.Equal(
            new RemoveObject(1001),
            Assert.IsType<RemoveObject>(lowCases[0].Trace.Steps[2].Changes[1]));
        Assert.Equal(
            new RemoveObject(1001),
            Assert.IsType<RemoveObject>(highCases[0].Trace.Steps[2].Changes[1]));
        IReadOnlyDictionary<uint, LogicalObjectState> lowFinal =
            WorkloadReplayer.Replay(lowCases[0].Trace);
        IReadOnlyDictionary<uint, LogicalObjectState> highFinal =
            WorkloadReplayer.Replay(highCases[0].Trace);
        Assert.Equal(lowFinal.Keys.Order(), highFinal.Keys.Order());
        Assert.Equal(lowFinal[10], highFinal[20]);
        Assert.Equal(lowFinal[20], highFinal[10]);
    }

    [Fact]
    public void Each_profile_sees_the_same_first_step_and_diverges_only_after_permutation() {
        BenchmarkV1BatchDefinition corpus = BenchmarkV1Corpus.Create(
            BenchmarkV1Baselines.All);

        foreach (StrategyBindingV1 strategy in BenchmarkV1Baselines.All) {
            BenchmarkV1CaseDefinition low = FindCase(
                corpus,
                "locality-next-update-low-id",
                strategy.Identity);
            BenchmarkV1CaseDefinition high = FindCase(
                corpus,
                "locality-next-update-high-id",
                strategy.Identity);
            CapturedSelections lowCaptured = CaptureSelections(low.Trace, strategy);
            CapturedSelections highCaptured = CaptureSelections(high.Trace, strategy);

            AssertEquivalentView(lowCaptured.Views[0], highCaptured.Views[0]);
            AssertEquivalentSelection(
                lowCaptured.Selections[0],
                highCaptured.Selections[0]);
            StrategyRunProductV1 lowProduct = BenchmarkV1Runner.ExecuteCase(low).Product;
            StrategyRunProductV1 highProduct = BenchmarkV1Runner.ExecuteCase(high).Product;
            Assert.Equal(
                lowProduct.WorkloadCommits[0],
                highProduct.WorkloadCommits[0]);
            Assert.Equal(
                [StrategyTargetV1.StayB, StrategyTargetV1.StayB],
                lowProduct.WorkloadCommits.Select(static receipt =>
                    receipt.SelectedTarget));
            Assert.Equal(
                [StrategyTargetV1.StayB, StrategyTargetV1.StayB],
                highProduct.WorkloadCommits.Select(static receipt =>
                    receipt.SelectedTarget));

            AssertSelectionTrajectory(strategy, lowCaptured, highCaptured);
        }
    }

    [Fact]
    public void Locality_pair_admits_all_baselines_with_strategy_local_outcomes() {
        BenchmarkV1BatchDefinition corpus = BenchmarkV1Corpus.Create(
            BenchmarkV1Baselines.All);
        BenchmarkReportV1 report = BenchmarkV1Runner.Run(corpus).Report;

        AdmittedOutcomeReportV1 noMigrationLow = AssertAdmitted(
            report,
            BenchmarkV1Corpus.LocalityLowIdNoMigrationCaseId,
            totalPhysicalWriteBytes: 448,
            peakCommitWriteBytes: 256,
            maxCurrentFileTailBytes: 256,
            finalColdHeadReadBytes: 248);
        AdmittedOutcomeReportV1 noMigrationHigh = AssertAdmitted(
            report,
            BenchmarkV1Corpus.LocalityHighIdNoMigrationCaseId,
            totalPhysicalWriteBytes: 448,
            peakCommitWriteBytes: 256,
            maxCurrentFileTailBytes: 256,
            finalColdHeadReadBytes: 248);
        Assert.Equal(noMigrationLow.Metrics, noMigrationHigh.Metrics);

        AssertAdmitted(
            report,
            BenchmarkV1Corpus.LocalityLowIdPacedCaseId,
            totalPhysicalWriteBytes: 456,
            peakCommitWriteBytes: 256,
            maxCurrentFileTailBytes: 448,
            finalColdHeadReadBytes: 440);
        AssertAdmitted(
            report,
            BenchmarkV1Corpus.LocalityHighIdPacedCaseId,
            totalPhysicalWriteBytes: 452,
            peakCommitWriteBytes: 152,
            maxCurrentFileTailBytes: 340,
            finalColdHeadReadBytes: 292);
        AdmittedOutcomeReportV1 adaptive35Low = AssertAdmitted(
            report,
            BenchmarkV1Corpus.LocalityLowIdAdaptiveR3B5PercentCaseId,
            totalPhysicalWriteBytes: 452,
            peakCommitWriteBytes: 252,
            maxCurrentFileTailBytes: 444,
            finalColdHeadReadBytes: 288);
        AdmittedOutcomeReportV1 adaptive35High = AssertAdmitted(
            report,
            BenchmarkV1Corpus.LocalityHighIdAdaptiveR3B5PercentCaseId,
            totalPhysicalWriteBytes: 348,
            peakCommitWriteBytes: 152,
            maxCurrentFileTailBytes: 340,
            finalColdHeadReadBytes: 332);
        AdmittedOutcomeReportV1 adaptive44Low = AssertAdmitted(
            report,
            BenchmarkV1Corpus.LocalityLowIdAdaptiveR4B4PercentCaseId,
            totalPhysicalWriteBytes: 452,
            peakCommitWriteBytes: 252,
            maxCurrentFileTailBytes: 444,
            finalColdHeadReadBytes: 288);
        AdmittedOutcomeReportV1 adaptive44High = AssertAdmitted(
            report,
            BenchmarkV1Corpus.LocalityHighIdAdaptiveR4B4PercentCaseId,
            totalPhysicalWriteBytes: 348,
            peakCommitWriteBytes: 152,
            maxCurrentFileTailBytes: 340,
            finalColdHeadReadBytes: 332);

        Assert.Equal(adaptive35Low.Metrics, adaptive44Low.Metrics);
        Assert.Equal(adaptive35High.Metrics, adaptive44High.Metrics);
    }

    private static void AssertMatchedTrace(
        IReadOnlyList<BenchmarkV1CaseDefinition> cases,
        string scenarioName,
        string expectedHash) {
        Assert.All(cases, benchmarkCase => {
            Assert.Same(cases[0].Trace, benchmarkCase.Trace);
            Assert.Equal(3, benchmarkCase.Trace.Steps.Count);
            Assert.Equal(2, benchmarkCase.ManifestCase.EvaluatedWorkloadStepCount);
            Assert.Equal(scenarioName, benchmarkCase.Trace.ScenarioName);
            Assert.Equal(
                expectedHash,
                benchmarkCase.ManifestCase.ResolvedTraceSha256);
        });
    }

    private static BenchmarkV1CaseDefinition[] FindCases(
        BenchmarkV1BatchDefinition corpus,
        string traceId) => corpus.Cases
        .Where(benchmarkCase =>
            benchmarkCase.ManifestCase.TraceDefinition.Id == traceId)
        .OrderBy(benchmarkCase =>
            benchmarkCase.ManifestCase.SelectionProfile.Id,
            StringComparer.Ordinal)
        .ToArray();

    private static BenchmarkV1CaseDefinition FindCase(
        BenchmarkV1BatchDefinition corpus,
        string traceId,
        BenchmarkComponentIdentityV1 strategyIdentity) => corpus.Cases.Single(
            benchmarkCase =>
                benchmarkCase.ManifestCase.TraceDefinition.Id == traceId &&
                benchmarkCase.ManifestCase.SelectionProfile == strategyIdentity);

    private static CapturedSelections CaptureSelections(
        WorkloadTrace trace,
        StrategyBindingV1 strategy) {
        BenchmarkV1BootstrappedSource source = BenchmarkV1SourceBootstrap.Create(trace);
        StrategyRunContextV1 context = new(
            source.Store,
            source.Cursor,
            trace,
            firstWorkloadTraceStepIndex: 1,
            totalWorkloadStepCount: 2);
        List<StrategyStepViewV1> views = [];
        List<StrategySelectionV1> selections = [];
        while (context.CurrentStep is StrategyStepViewV1 view) {
            StrategySelectionV1 selection = BenchmarkV1Baselines.Select(
                strategy.Identity,
                view);
            views.Add(view);
            selections.Add(selection);
            Assert.Equal(
                StrategyCommitStatusV1.AppliedStayB,
                context.Commit(selection));
        }

        return new CapturedSelections(views.ToArray(), selections.ToArray());
    }

    private static void AssertEquivalentView(
        StrategyStepViewV1 expected,
        StrategyStepViewV1 actual) {
        Assert.Equal(
            expected.PostLiveGraphBasePayloadBytes,
            actual.PostLiveGraphBasePayloadBytes);
        Assert.Equal(
            expected.ADependentEvacuationBasePayloadBytes,
            actual.ADependentEvacuationBasePayloadBytes);
        Assert.Equal(expected.HasParentPreviousDebt, actual.HasParentPreviousDebt);
        Assert.Equal(
            expected.Objects.Select(ProjectFact),
            actual.Objects.Select(ProjectFact));
    }

    private static ObjectFactValue ProjectFact(StrategyObjectFactV1 fact) => new(
        fact.ObjectId,
        fact.Kind,
        fact.SourceIsPreviousDependent,
        fact.SourceBasePayloadBytes,
        fact.SourceHeadReconstructionPayloadBytes,
        fact.ResultBasePayloadBytes,
        fact.DeltaPayloadBytes);

    private static void AssertEquivalentSelection(
        StrategySelectionV1 expected,
        StrategySelectionV1 actual) {
        Assert.Equal(expected.Target, actual.Target);
        Assert.Equal(
            expected.Stay.UpdateDecisions,
            actual.Stay.UpdateDecisions);
        Assert.Equal(
            expected.Stay.UnchangedMigrationObjectIds,
            actual.Stay.UnchangedMigrationObjectIds);
        Assert.Equal(
            expected.Rotate.BContainedUpdateDecisions,
            actual.Rotate.BContainedUpdateDecisions);
        Assert.Equal(
            expected.Rotate.BContainedNoChangeBaseObjectIds,
            actual.Rotate.BContainedNoChangeBaseObjectIds);
    }

    private static void AssertSelectionTrajectory(
        StrategyBindingV1 strategy,
        CapturedSelections low,
        CapturedSelections high) {
        Assert.Equal(2, low.Selections.Count);
        Assert.Equal(2, high.Selections.Count);
        Assert.All(low.Selections, selection =>
            Assert.Equal(StrategyTargetV1.StayB, selection.Target));
        Assert.All(high.Selections, selection =>
            Assert.Equal(StrategyTargetV1.StayB, selection.Target));

        bool noMigration = strategy.Identity ==
            BenchmarkV1Baselines.DebtZeroThenRotateDeltaNoMigration.Identity;
        bool paced = strategy.Identity == BenchmarkV1Baselines
            .DebtZeroThenRotateDeltaPacedOneDebtByObjectId.Identity;
        StrategyUpdateWriteModeV1 expectedUpdateMode = noMigration || paced
            ? StrategyUpdateWriteModeV1.Delta
            : StrategyUpdateWriteModeV1.Base;
        uint[] expectedFirstMigrations = noMigration ? [] : [10];
        uint[] expectedLowSecondMigrations = noMigration ? [] : [20];

        Assert.Empty(low.Selections[0].Stay.UpdateDecisions);
        Assert.Empty(high.Selections[0].Stay.UpdateDecisions);
        Assert.Equal(
            expectedFirstMigrations,
            low.Selections[0].Stay.UnchangedMigrationObjectIds);
        Assert.Equal(
            expectedFirstMigrations,
            high.Selections[0].Stay.UnchangedMigrationObjectIds);
        Assert.Equal(
            [new StrategyUpdateWriteDecisionV1(10, expectedUpdateMode)],
            low.Selections[1].Stay.UpdateDecisions);
        Assert.Equal(
            [new StrategyUpdateWriteDecisionV1(20, expectedUpdateMode)],
            high.Selections[1].Stay.UpdateDecisions);
        Assert.Equal(
            expectedLowSecondMigrations,
            low.Selections[1].Stay.UnchangedMigrationObjectIds);
        Assert.Empty(high.Selections[1].Stay.UnchangedMigrationObjectIds);
    }

    private static AdmittedOutcomeReportV1 AssertAdmitted(
        BenchmarkReportV1 report,
        string caseId,
        long totalPhysicalWriteBytes,
        long peakCommitWriteBytes,
        long maxCurrentFileTailBytes,
        long finalColdHeadReadBytes) {
        BenchmarkCaseReportV1 benchmarkCase = report.Cases.Single(
            candidate => candidate.CaseId == caseId);
        AdmittedOutcomeReportV1 admitted = Assert.IsType<AdmittedOutcomeReportV1>(
            benchmarkCase.Outcome);
        Assert.Equal(EvaluatorRunPhase.TerminalSettlement, admitted.Position.Phase);
        Assert.Equal(2, admitted.Position.CompletedWorkloadStepCount);
        Assert.Equal(2, admitted.Position.TotalWorkloadStepCount);
        Assert.Equal(3, admitted.Metrics.RealizedCommitCount);
        Assert.Equal(
            totalPhysicalWriteBytes,
            admitted.Metrics.TotalPhysicalWriteBytes);
        Assert.Equal(peakCommitWriteBytes, admitted.Metrics.PeakCommitWriteBytes);
        Assert.Equal(
            maxCurrentFileTailBytes,
            admitted.Metrics.MaxCurrentFileTailBytes);
        Assert.Equal(
            finalColdHeadReadBytes,
            admitted.Metrics.TerminalColdHeadReadBytes);
        Assert.Equal(2U, admitted.FinalCursor.PreviousFileNumber);
        Assert.Equal(3U, admitted.FinalCursor.CurrentFileNumber);
        Assert.Empty(admitted.Settlement.MigratedObjectIds);
        Assert.Equal(0, admitted.Settlement.MaintenanceRevisionCount);
        Assert.Equal(1, admitted.Settlement.RealizedRevisionCount);
        return admitted;
    }

    private sealed record CapturedSelections(
        IReadOnlyList<StrategyStepViewV1> Views,
        IReadOnlyList<StrategySelectionV1> Selections);

    private readonly record struct ObjectFactValue(
        uint ObjectId,
        StrategyObjectKindV1 Kind,
        bool? SourceIsPreviousDependent,
        int? SourceBasePayloadBytes,
        long? SourceHeadReconstructionPayloadBytes,
        int? ResultBasePayloadBytes,
        int? DeltaPayloadBytes);
}
