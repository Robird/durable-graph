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
            run.Report,
            BenchmarkV1Corpus.ThresholdBandNoMigrationCaseId,
            totalPhysicalWriteBytes: 1460,
            peakCommitWriteBytes: 1072,
            maxCurrentFileTailBytes: 1072,
            finalColdHeadReadBytes: 1064,
            publishedRevisionLengthBytes: 1064,
            currentFileTailBytes: 1072);
        AdmittedOutcomeReportV1 paced = AssertAdmitted(
            run.Report,
            BenchmarkV1Corpus.ThresholdBandPacedCaseId,
            totalPhysicalWriteBytes: 1512,
            peakCommitWriteBytes: 1056,
            maxCurrentFileTailBytes: 1056,
            finalColdHeadReadBytes: 1472,
            publishedRevisionLengthBytes: 1048,
            currentFileTailBytes: 1056);
        AdmittedOutcomeReportV1 adaptive35 = AssertAdmitted(
            run.Report,
            BenchmarkV1Corpus.ThresholdBandAdaptiveR3B5PercentCaseId,
            totalPhysicalWriteBytes: 1516,
            peakCommitWriteBytes: 1056,
            maxCurrentFileTailBytes: 1056,
            finalColdHeadReadBytes: 1212,
            publishedRevisionLengthBytes: 1048,
            currentFileTailBytes: 1056);
        AdmittedOutcomeReportV1 adaptive44 = AssertAdmitted(
            run.Report,
            BenchmarkV1Corpus.ThresholdBandAdaptiveR4B4PercentCaseId,
            totalPhysicalWriteBytes: 1512,
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
            adaptive44.Metrics.FinalColdHeadReadBytes >
                adaptive35.Metrics.FinalColdHeadReadBytes);
        Assert.Equal(
            adaptive35.Metrics.PeakCommitWriteBytes,
            adaptive44.Metrics.PeakCommitWriteBytes);
        Assert.Equal(
            adaptive35.Metrics.MaxCurrentFileTailBytes,
            adaptive44.Metrics.MaxCurrentFileTailBytes);

        Assert.Equal(paced.Metrics, adaptive44.Metrics);

        // The three unique vectors are pairwise incomparable in this workload:
        // no-migration buys lower W/R with higher P/F, while the two Adaptive
        // limits exchange four write bytes for 260 final cold-read bytes.
        Assert.True(
            noMigration.Metrics.TotalPhysicalWriteBytes <
                paced.Metrics.TotalPhysicalWriteBytes);
        Assert.True(
            noMigration.Metrics.PeakCommitWriteBytes >
                paced.Metrics.PeakCommitWriteBytes);
        Assert.True(
            noMigration.Metrics.MaxCurrentFileTailBytes >
                paced.Metrics.MaxCurrentFileTailBytes);
        Assert.True(
            noMigration.Metrics.FinalColdHeadReadBytes <
                paced.Metrics.FinalColdHeadReadBytes);
        Assert.True(
            noMigration.Metrics.TotalPhysicalWriteBytes <
                adaptive35.Metrics.TotalPhysicalWriteBytes);
        Assert.True(
            noMigration.Metrics.PeakCommitWriteBytes >
                adaptive35.Metrics.PeakCommitWriteBytes);
        Assert.True(
            noMigration.Metrics.MaxCurrentFileTailBytes >
                adaptive35.Metrics.MaxCurrentFileTailBytes);
        Assert.True(
            noMigration.Metrics.FinalColdHeadReadBytes <
                adaptive35.Metrics.FinalColdHeadReadBytes);
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
        BenchmarkReportV1 report,
        string caseId,
        long totalPhysicalWriteBytes,
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
        Assert.Equal(9, admitted.Metrics.RealizedCommitCount);
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
        Assert.Equal(2U, admitted.FinalCursor.PreviousFileNumber);
        Assert.Equal(3U, admitted.FinalCursor.CurrentFileNumber);
        Assert.Equal(3U, admitted.FinalCursor.PublishedRevision.FileNumber);
        Assert.Equal(4, admitted.FinalCursor.PublishedRevision.OffsetBytes);
        Assert.Equal(
            publishedRevisionLengthBytes,
            admitted.FinalCursor.PublishedRevision.LengthBytes);
        Assert.Equal(currentFileTailBytes, admitted.FinalCursor.CurrentFileTailBytes);
        Assert.Empty(admitted.Settlement.MigratedObjectIds);
        Assert.Equal(0, admitted.Settlement.MaintenanceRevisionCount);
        Assert.Equal(1, admitted.Settlement.RealizedRevisionCount);
        return admitted;
    }
}
