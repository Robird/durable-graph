using Atelia.TwoLegRotationProbe.Baselines;
using Atelia.TwoLegRotationProbe.Benchmarking;
using Atelia.TwoLegRotationProbe.Evaluation;

namespace Atelia.TwoLegRotationProbe.Tests;

public sealed class BenchmarkV1ThresholdBandWorkloadTests {
    [Fact]
    public void Threshold_band_is_one_frozen_eight_step_workload_for_all_baselines() {
        BenchmarkV1BatchDefinition corpus = BenchmarkV1Corpus.Create(
            BenchmarkV1Baselines.All);
        BenchmarkV1CaseDefinition[] cases = FindCases(corpus);

        Assert.Equal(4, cases.Length);
        Assert.All(cases, benchmarkCase => {
            Assert.Equal(9, benchmarkCase.Trace.Steps.Count);
            Assert.Equal(8, benchmarkCase.ManifestCase.EvaluatedWorkloadStepCount);
            Assert.Equal(
                "read-amplification-threshold-band",
                benchmarkCase.Trace.ScenarioName);
        });
        Assert.All(
            cases.Skip(1),
            benchmarkCase => Assert.Same(cases[0].Trace, benchmarkCase.Trace));
        Assert.All(cases, benchmarkCase => Assert.Equal(
            "0d94e562a41af07ab793fd59f5d56d6730933d49e5f1f1ac3309cdeb28b2591d",
            benchmarkCase.ManifestCase.ResolvedTraceSha256));
    }

    [Fact]
    public void Threshold_band_admits_all_baselines_and_separates_adaptive_tradeoff() {
        BenchmarkV1BatchDefinition corpus = BenchmarkV1Corpus.Create(
            BenchmarkV1Baselines.All);
        BenchmarkV1BatchRun run = BenchmarkV1Runner.Run(corpus);
        AdmittedOutcomeReportV1 noMigration = AssertAdmitted(
            corpus,
            run.Report,
            BenchmarkV1Corpus.ThresholdBandNoMigrationCaseId,
            totalPhysicalWriteBytes: 1460,
            peakWorkloadCommitWriteBytes: 52,
            peakCommitWriteBytes: 1072,
            maxCurrentFileTailBytes: 1072,
            finalColdHeadReadBytes: 1064,
            publishedRevisionLengthBytes: 1064,
            currentFileTailBytes: 1072);
        AdmittedOutcomeReportV1 paced = AssertAdmitted(
            corpus,
            run.Report,
            BenchmarkV1Corpus.ThresholdBandPacedCaseId,
            totalPhysicalWriteBytes: 1512,
            peakWorkloadCommitWriteBytes: 60,
            peakCommitWriteBytes: 1056,
            maxCurrentFileTailBytes: 1056,
            finalColdHeadReadBytes: 1472,
            publishedRevisionLengthBytes: 1048,
            currentFileTailBytes: 1056);
        AdmittedOutcomeReportV1 adaptive35 = AssertAdmitted(
            corpus,
            run.Report,
            BenchmarkV1Corpus.ThresholdBandAdaptiveR3B5PercentCaseId,
            totalPhysicalWriteBytes: 1516,
            peakWorkloadCommitWriteBytes: 60,
            peakCommitWriteBytes: 1056,
            maxCurrentFileTailBytes: 1056,
            finalColdHeadReadBytes: 1212,
            publishedRevisionLengthBytes: 1048,
            currentFileTailBytes: 1056);
        AdmittedOutcomeReportV1 adaptive44 = AssertAdmitted(
            corpus,
            run.Report,
            BenchmarkV1Corpus.ThresholdBandAdaptiveR4B4PercentCaseId,
            totalPhysicalWriteBytes: 1512,
            peakWorkloadCommitWriteBytes: 60,
            peakCommitWriteBytes: 1056,
            maxCurrentFileTailBytes: 1056,
            finalColdHeadReadBytes: 1472,
            publishedRevisionLengthBytes: 1048,
            currentFileTailBytes: 1056);

        Assert.NotEqual(adaptive35.Metrics, adaptive44.Metrics);
        Assert.True(
            adaptive44.Metrics.TotalPhysicalWriteBytes <
                adaptive35.Metrics.TotalPhysicalWriteBytes);
        Assert.True(
            adaptive44.Metrics.TotalWorkloadColdReadBytes <
                adaptive35.Metrics.TotalWorkloadColdReadBytes);
        AdmittedEvaluatorRun adaptive35Endpoint = ExecuteAdmitted(
            corpus,
            BenchmarkV1Corpus.ThresholdBandAdaptiveR3B5PercentCaseId);
        AdmittedEvaluatorRun adaptive44Endpoint = ExecuteAdmitted(
            corpus,
            BenchmarkV1Corpus.ThresholdBandAdaptiveR4B4PercentCaseId);
        Assert.True(
            adaptive44Endpoint.Metrics.TerminalColdHeadReadBytes >
                adaptive35Endpoint.Metrics.TerminalColdHeadReadBytes);
        Assert.Equal(
            adaptive35.Metrics.PeakWorkloadCommitWriteBytes,
            adaptive44.Metrics.PeakWorkloadCommitWriteBytes);
        Assert.Equal(
            adaptive35.Metrics.MaxCurrentFileTailBytes,
            adaptive44.Metrics.MaxCurrentFileTailBytes);

        Assert.Equal(paced.Metrics, adaptive44.Metrics);

        // No-migration buys lower W/P/R with higher F. Adaptive (4,4%) strictly
        // dominates (3,5%) on canonical W/P/F/R here; the latter only lowers the
        // endpoint-only terminal cold-head read.
        Assert.True(
            noMigration.Metrics.TotalPhysicalWriteBytes <
                paced.Metrics.TotalPhysicalWriteBytes);
        Assert.True(
            noMigration.Metrics.PeakWorkloadCommitWriteBytes <
                paced.Metrics.PeakWorkloadCommitWriteBytes);
        Assert.True(
            noMigration.Metrics.MaxCurrentFileTailBytes >
                paced.Metrics.MaxCurrentFileTailBytes);
        Assert.True(
            noMigration.Metrics.TotalWorkloadColdReadBytes <
                paced.Metrics.TotalWorkloadColdReadBytes);
        Assert.True(
            noMigration.Metrics.TotalPhysicalWriteBytes <
                adaptive35.Metrics.TotalPhysicalWriteBytes);
        Assert.True(
            noMigration.Metrics.PeakWorkloadCommitWriteBytes <
                adaptive35.Metrics.PeakWorkloadCommitWriteBytes);
        Assert.True(
            noMigration.Metrics.MaxCurrentFileTailBytes >
                adaptive35.Metrics.MaxCurrentFileTailBytes);
        Assert.True(
            noMigration.Metrics.TotalWorkloadColdReadBytes <
                adaptive35.Metrics.TotalWorkloadColdReadBytes);
    }

    private static BenchmarkV1CaseDefinition[] FindCases(
        BenchmarkV1BatchDefinition corpus) => corpus.Cases
        .Where(benchmarkCase => benchmarkCase.ManifestCase.TraceDefinition.Id ==
            "read-amplification-threshold-band")
        .OrderBy(benchmarkCase =>
            benchmarkCase.ManifestCase.SelectionProfile.Id,
            StringComparer.Ordinal)
        .ToArray();

    private static AdmittedOutcomeReportV1 AssertAdmitted(
        BenchmarkV1BatchDefinition corpus,
        BenchmarkReportV1 report,
        string caseId,
        long totalPhysicalWriteBytes,
        long peakWorkloadCommitWriteBytes,
        long peakCommitWriteBytes,
        long maxCurrentFileTailBytes,
        long finalColdHeadReadBytes,
        int publishedRevisionLengthBytes,
        long currentFileTailBytes) {
        BenchmarkCaseReportV1 benchmarkCase = report.Cases.Single(
            candidate => candidate.CaseId == caseId);
        AdmittedOutcomeReportV1 admitted = Assert.IsType<AdmittedOutcomeReportV1>(
            benchmarkCase.Outcome);

        Assert.Equal(EvaluatorRunPhase.TerminalSettlement, admitted.Position.Phase);
        Assert.Equal(8, admitted.Position.CompletedWorkloadStepCount);
        Assert.Equal(8, admitted.Position.TotalWorkloadStepCount);
        Assert.Equal(
            totalPhysicalWriteBytes,
            admitted.Metrics.TotalPhysicalWriteBytes);
        Assert.Equal(
            peakWorkloadCommitWriteBytes,
            admitted.Metrics.PeakWorkloadCommitWriteBytes);
        Assert.Equal(
            maxCurrentFileTailBytes,
            admitted.Metrics.MaxCurrentFileTailBytes);

        AdmittedEvaluatorRun endpoint = ExecuteAdmitted(corpus, caseId);
        Assert.Equal(9, endpoint.Metrics.RealizedCommitCount);
        Assert.Equal(peakCommitWriteBytes, endpoint.Metrics.PeakCommitWriteBytes);
        Assert.Equal(
            finalColdHeadReadBytes,
            endpoint.Metrics.TerminalColdHeadReadBytes);
        Assert.Equal(2U, endpoint.FinalCursor.FileScope.PreviousFileNumber);
        Assert.Equal(3U, endpoint.FinalCursor.FileScope.CurrentFileNumber);
        Assert.Equal(3U, endpoint.FinalCursor.PublishedRevisionAddress.FileNumber);
        Assert.Equal(
            4,
            endpoint.FinalCursor.PublishedRevisionAddress.FrameTicket.OffsetBytes);
        Assert.Equal(
            publishedRevisionLengthBytes,
            endpoint.FinalCursor.PublishedRevisionAddress.FrameTicket.LengthBytes);
        Assert.Equal(
            currentFileTailBytes,
            endpoint.FinalCursor.CurrentFileTailOffsetBytes);
        Assert.Empty(endpoint.Settlement.MigratedObjectIds);
        Assert.Equal(0, endpoint.Settlement.MaintenanceRevisionCount);
        Assert.Equal(1, endpoint.Settlement.RealizedRevisionCount);
        return admitted;
    }

    private static AdmittedEvaluatorRun ExecuteAdmitted(
        BenchmarkV1BatchDefinition corpus,
        string caseId) {
        BenchmarkV1CaseDefinition definition = corpus.Cases.Single(
            benchmarkCase => benchmarkCase.ManifestCase.CaseId == caseId);
        return Assert.IsType<AdmittedEvaluatorRun>(
            BenchmarkV1Runner.ExecuteCase(definition).Outcome);
    }
}
