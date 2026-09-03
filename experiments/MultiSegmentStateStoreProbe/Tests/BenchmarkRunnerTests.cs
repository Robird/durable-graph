using Atelia.MultiSegmentStateStoreProbe.Benchmarking;
using Atelia.MultiSegmentStateStoreProbe.Policies;

namespace Atelia.MultiSegmentStateStoreProbe.Tests;

public sealed class BenchmarkRunnerTests {
    [Fact]
    public void Frozen_corpus_produces_three_deterministic_admitted_raw_reports() {
        BenchmarkBatchReport first = MultiSegmentBenchmarkRunner.RunCanonicalBatch();
        BenchmarkBatchReport second = MultiSegmentBenchmarkRunner.RunCanonicalBatch();

        Assert.Equal(first.FormatRaw(), second.FormatRaw());
        AdmittedBenchmarkRun[] admitted = first.Outcomes
            .Select(Assert.IsType<AdmittedBenchmarkRun>)
            .ToArray();
        Assert.Equal(["all-delta", "all-base", "adaptive-r3-b5pct"],
            admitted.Select(static run => run.PolicyId));
        Assert.Single(admitted.Select(static run =>
            run.Metrics.TotalWorkloadDeltaReferencePayloadBytes).Distinct());
        Assert.Single(admitted.Select(static run =>
            run.Metrics.TotalWorkloadBaseReferencePayloadBytes).Distinct());
        Assert.All(admitted, static run => Assert.True(
            run.Metrics.MaxSegmentTailBytes >
            MultiSegmentBenchmarkRunner.DefaultRolloverThresholdBytes));

        Assert.Equal(string.Join(Environment.NewLine, new[] {
            "all-delta: W=2440 P=424 F=692 R/L=11052/2418 " +
                "DeltaRef=1139 BaseRef=1796 Segments=4",
            "all-base: W=2808 P=424 F=744 R/L=6692/2418 " +
                "DeltaRef=1139 BaseRef=1796 Segments=4",
            "adaptive-r3-b5pct: W=2436 P=424 F=692 R/L=11044/2418 " +
                "DeltaRef=1139 BaseRef=1796 Segments=4",
        }), first.FormatRaw());
    }

    [Fact]
    public void Adaptive_report_identity_reflects_custom_parameters() {
        AdmittedBenchmarkRun run = Assert.IsType<AdmittedBenchmarkRun>(
            MultiSegmentBenchmarkRunner.Run(
                MultiSegmentBenchmarkCorpus.CreateDeterministicMixedTrace(),
                BenchmarkPolicyKind.Adaptive,
                adaptiveParameters:
                    new ReadAmplificationBaseBudgetPolicyParameters(4m, 0.04m)));

        Assert.Equal("adaptive-r4-b4pct", run.PolicyId);
    }
}
