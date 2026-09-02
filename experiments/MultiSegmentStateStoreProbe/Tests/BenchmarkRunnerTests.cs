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

        Assert.Equal(string.Join(Environment.NewLine, new[] {
            "all-delta: W=2444 P=424 F=424 R/L=11004/2418 " +
                "DeltaRef=1139 BaseRef=1796 Segments=8",
            "all-base: W=2812 P=424 F=424 R/L=6672/2418 " +
                "DeltaRef=1139 BaseRef=1796 Segments=8",
            "adaptive-r3-b5pct: W=2436 P=424 F=424 R/L=10988/2418 " +
                "DeltaRef=1139 BaseRef=1796 Segments=8",
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
