using Atelia.TwoLegRotationProbe.Arena;
using Atelia.TwoLegRotationProbe.Baselines;
using Atelia.TwoLegRotationProbe.Benchmarking;
using Atelia.TwoLegRotationProbe.Evaluation;

namespace Atelia.TwoLegRotationProbe.Tests;

public sealed class BenchmarkV1DebtShareDilutionWorkloadTests {
    [Fact]
    public void Debt_share_dilution_is_one_frozen_three_step_workload() {
        BenchmarkV1BatchDefinition corpus = BenchmarkV1Corpus.Create(
            BenchmarkV1Baselines.All);
        BenchmarkV1CaseDefinition[] cases = FindCases(corpus);

        Assert.Equal(4, cases.Length);
        Assert.All(cases, benchmarkCase => {
            Assert.Equal(4, benchmarkCase.Trace.Steps.Count);
            Assert.Equal(3, benchmarkCase.ManifestCase.EvaluatedWorkloadStepCount);
            Assert.Equal(
                "previous-debt-share-dilution-boundary",
                benchmarkCase.Trace.ScenarioName);
        });
        Assert.All(
            cases.Skip(1),
            benchmarkCase => Assert.Same(cases[0].Trace, benchmarkCase.Trace));
        Assert.All(cases, benchmarkCase => Assert.Equal(
            "b18b43f55f9233b14ef0d3c1787643e565350c98b7f33b9c9b0463e901c5b1e0",
            benchmarkCase.ManifestCase.ResolvedTraceSha256));

        AssertBoundaryView(AdvanceToBoundaryView(
            cases[0].Trace,
            BenchmarkV1Baselines.ReadAmplificationBaseBudgetR3B5Percent));
        AssertBoundaryView(AdvanceToBoundaryView(
            cases[0].Trace,
            BenchmarkV1Baselines.ReadAmplificationBaseBudgetR4B4Percent));
    }

    [Fact]
    public void Debt_share_dilution_admits_all_baselines_and_exposes_target_boundary() {
        BenchmarkV1BatchDefinition corpus = BenchmarkV1Corpus.Create(
            BenchmarkV1Baselines.All);
        BenchmarkV1BatchRun run = BenchmarkV1Runner.Run(corpus);

        AssertAdmittedAndTargets(
            corpus,
            run.Report,
            BenchmarkV1Corpus.DebtShareDilutionNoMigrationCaseId,
            [
                StrategyTargetV1.StayB,
                StrategyTargetV1.StayB,
                StrategyTargetV1.StayB,
            ],
            totalPhysicalWriteBytes: 3160,
            peakCommitWriteBytes: 1056,
            maxCurrentFileTailBytes: 2148,
            finalColdHeadReadBytes: 1048,
            previousFileNumber: 2,
            currentFileNumber: 3,
            publishedRevisionLengthBytes: 1048,
            currentFileTailBytes: 1056);
        AssertAdmittedAndTargets(
            corpus,
            run.Report,
            BenchmarkV1Corpus.DebtShareDilutionPacedCaseId,
            [
                StrategyTargetV1.StayB,
                StrategyTargetV1.StayB,
                StrategyTargetV1.RotateC,
            ],
            totalPhysicalWriteBytes: 4188,
            peakCommitWriteBytes: 1056,
            maxCurrentFileTailBytes: 2156,
            finalColdHeadReadBytes: 1048,
            previousFileNumber: 3,
            currentFileNumber: 4,
            publishedRevisionLengthBytes: 1048,
            currentFileTailBytes: 1056);
        AssertAdmittedAndTargets(
            corpus,
            run.Report,
            BenchmarkV1Corpus.DebtShareDilutionAdaptiveR3B5PercentCaseId,
            [
                StrategyTargetV1.StayB,
                StrategyTargetV1.RotateC,
                StrategyTargetV1.StayB,
            ],
            totalPhysicalWriteBytes: 2148,
            peakCommitWriteBytes: 1004,
            maxCurrentFileTailBytes: 1096,
            finalColdHeadReadBytes: 1124,
            previousFileNumber: 3,
            currentFileNumber: 4,
            publishedRevisionLengthBytes: 40,
            currentFileTailBytes: 48);
        AssertAdmittedAndTargets(
            corpus,
            run.Report,
            BenchmarkV1Corpus.DebtShareDilutionAdaptiveR4B4PercentCaseId,
            [
                StrategyTargetV1.StayB,
                StrategyTargetV1.StayB,
                StrategyTargetV1.RotateC,
            ],
            totalPhysicalWriteBytes: 2192,
            peakCommitWriteBytes: 1012,
            maxCurrentFileTailBytes: 1132,
            finalColdHeadReadBytes: 1088,
            previousFileNumber: 3,
            currentFileNumber: 4,
            publishedRevisionLengthBytes: 84,
            currentFileTailBytes: 92);
    }

    private static BenchmarkV1CaseDefinition[] FindCases(
        BenchmarkV1BatchDefinition corpus) => corpus.Cases
        .Where(benchmarkCase => benchmarkCase.ManifestCase.TraceDefinition.Id ==
            "previous-debt-share-dilution-boundary")
        .OrderBy(benchmarkCase =>
            benchmarkCase.ManifestCase.SelectionProfile.Id,
            StringComparer.Ordinal)
        .ToArray();

    private static StrategyStepViewV1 AdvanceToBoundaryView(
        Atelia.TwoLegRotationProbe.Workloads.WorkloadTrace trace,
        StrategyBindingV1 strategy) {
        BenchmarkV1BootstrappedSource source = BenchmarkV1SourceBootstrap.Create(trace);
        StrategyRunContextV1 context = new(
            source.Store,
            source.Cursor,
            trace,
            firstWorkloadTraceStepIndex: 1,
            totalWorkloadStepCount: 3);
        StrategyStepViewV1 first = Assert.IsType<StrategyStepViewV1>(
            context.CurrentStep);
        StrategySelectionV1 selection = BenchmarkV1Baselines.Select(
            strategy.Identity,
            first);
        Assert.Equal(StrategyTargetV1.StayB, selection.Target);
        Assert.Equal(
            [new StrategyUpdateWriteDecisionV1(
                100,
                StrategyUpdateWriteModeV1.Base)],
            selection.Stay.UpdateDecisions);
        Assert.Empty(selection.Stay.UnchangedMigrationObjectIds);
        Assert.Equal(
            StrategyCommitStatusV1.AppliedStayB,
            context.Commit(selection));
        return Assert.IsType<StrategyStepViewV1>(context.CurrentStep);
    }

    private static void AssertBoundaryView(StrategyStepViewV1 view) {
        Assert.Equal(1000, view.PostLiveGraphBasePayloadBytes);
        Assert.Equal(40, view.ADependentEvacuationBasePayloadBytes);
        Assert.True(view.HasParentPreviousDebt);
        StrategyObjectFactV1 update = Assert.Single(
            view.Objects,
            static fact => fact.Kind == StrategyObjectKindV1.Update);
        Assert.Equal(1U, update.ObjectId);
        Assert.True(update.SourceIsPreviousDependent);
        Assert.Equal(40, update.ResultBasePayloadBytes);
        Assert.Equal(40, update.DeltaPayloadBytes);
        StrategyObjectFactV1 noChange = Assert.Single(
            view.Objects,
            static fact => fact.Kind == StrategyObjectKindV1.NoChange);
        Assert.Equal(100U, noChange.ObjectId);
        Assert.False(noChange.SourceIsPreviousDependent);
        Assert.Equal(960, noChange.ResultBasePayloadBytes);
        Assert.DoesNotContain(
            view.Objects,
            static fact => fact.Kind == StrategyObjectKindV1.Insert);
    }

    private static void AssertAdmittedAndTargets(
        BenchmarkV1BatchDefinition corpus,
        BenchmarkReportV1 report,
        string caseId,
        IReadOnlyList<StrategyTargetV1> expectedTargets,
        long totalPhysicalWriteBytes,
        long peakCommitWriteBytes,
        long maxCurrentFileTailBytes,
        long finalColdHeadReadBytes,
        uint previousFileNumber,
        uint currentFileNumber,
        int publishedRevisionLengthBytes,
        long currentFileTailBytes) {
        BenchmarkV1CaseDefinition definition = corpus.Cases.Single(
            benchmarkCase => benchmarkCase.ManifestCase.CaseId == caseId);
        BenchmarkV1CaseExecution execution = BenchmarkV1Runner.ExecuteCase(definition);
        Assert.Equal(StrategyRunTerminationV1.Admitted, execution.Product.Termination);
        Assert.Equal(
            [0, 1, 2],
            execution.Product.WorkloadCommits.Select(static receipt =>
                receipt.WorkloadStepOrdinal));
        Assert.Equal(
            expectedTargets,
            execution.Product.WorkloadCommits.Select(static receipt =>
                receipt.SelectedTarget));

        BenchmarkCaseReportV1 benchmarkCase = report.Cases.Single(
            candidate => candidate.CaseId == caseId);
        AdmittedOutcomeReportV1 admitted = Assert.IsType<AdmittedOutcomeReportV1>(
            benchmarkCase.Outcome);
        Assert.Equal(EvaluatorRunPhase.TerminalSettlement, admitted.Position.Phase);
        Assert.Equal(3, admitted.Position.CompletedWorkloadStepCount);
        Assert.Equal(3, admitted.Position.TotalWorkloadStepCount);
        Assert.Equal(4, admitted.Metrics.RealizedCommitCount);
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
        Assert.Equal(
            currentFileNumber,
            admitted.FinalCursor.PublishedRevision.FileNumber);
        Assert.Equal(4, admitted.FinalCursor.PublishedRevision.OffsetBytes);
        Assert.Equal(
            publishedRevisionLengthBytes,
            admitted.FinalCursor.PublishedRevision.LengthBytes);
        Assert.Equal(currentFileTailBytes, admitted.FinalCursor.CurrentFileTailBytes);
        Assert.Empty(admitted.Settlement.MigratedObjectIds);
        Assert.Equal(0, admitted.Settlement.MaintenanceRevisionCount);
        Assert.Equal(1, admitted.Settlement.RealizedRevisionCount);
    }
}
