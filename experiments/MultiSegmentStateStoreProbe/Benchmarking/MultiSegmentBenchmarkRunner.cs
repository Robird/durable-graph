using System.Collections.ObjectModel;
using System.Globalization;
using Atelia.MultiSegmentStateStoreProbe.Evaluation;
using Atelia.MultiSegmentStateStoreProbe.Model;
using Atelia.MultiSegmentStateStoreProbe.Normalization;
using Atelia.MultiSegmentStateStoreProbe.Planning;
using Atelia.MultiSegmentStateStoreProbe.Policies;
using Atelia.MultiSegmentStateStoreProbe.Storage;
using Atelia.MultiSegmentStateStoreProbe.Workloads;

namespace Atelia.MultiSegmentStateStoreProbe.Benchmarking;

internal enum BenchmarkPolicyKind {
    AllDelta,
    AllBase,
    Adaptive,
}

internal abstract record BenchmarkRunOutcome(string PolicyId);

internal sealed record AdmittedBenchmarkRun(
    string PolicyId,
    EvaluatorRawMetrics Metrics,
    int SegmentCount) : BenchmarkRunOutcome(PolicyId);

internal sealed record RejectedBenchmarkRun(
    string PolicyId,
    int SaveOrdinal,
    RejectedRevisionCommit Rejection) : BenchmarkRunOutcome(PolicyId);

internal sealed class BenchmarkBatchReport {
    private readonly ReadOnlyCollection<BenchmarkRunOutcome> _outcomes;

    public BenchmarkBatchReport(IEnumerable<BenchmarkRunOutcome> outcomes) {
        _outcomes = Array.AsReadOnly(outcomes.ToArray());
    }

    public IReadOnlyList<BenchmarkRunOutcome> Outcomes => _outcomes;

    public string FormatRaw() => string.Join(
        Environment.NewLine,
        _outcomes.Select(FormatOutcome));

    private static string FormatOutcome(BenchmarkRunOutcome outcome) => outcome switch {
        AdmittedBenchmarkRun admitted => string.Create(
            CultureInfo.InvariantCulture,
            $"{admitted.PolicyId}: W={admitted.Metrics.WorkloadPhysicalWriteBytes} " +
            $"P={admitted.Metrics.PeakWorkloadSaveWriteBytes} " +
            $"F={admitted.Metrics.MaxSegmentTailBytes} " +
            $"{FormatReadAmplification(admitted.Metrics)} " +
            $"DeltaRef={admitted.Metrics.TotalWorkloadDeltaReferencePayloadBytes} " +
            $"BaseRef={admitted.Metrics.TotalWorkloadBaseReferencePayloadBytes} " +
            $"Segments={admitted.SegmentCount}"),
        RejectedBenchmarkRun rejected =>
            $"{rejected.PolicyId}: REJECTED Save={rejected.SaveOrdinal} " +
            $"Kind={rejected.Rejection.Kind}",
        _ => throw new InvalidDataException("Unsupported benchmark outcome."),
    };

    private static string FormatReadAmplification(EvaluatorRawMetrics metrics) =>
        metrics.IsAggregateReadAmplificationDefined
            ? $"R/L={metrics.TotalWorkloadColdReadBytes}/" +
                $"{metrics.TotalWorkloadLogicalBasePayloadBytes}"
            : $"R/L=undefined R={metrics.TotalWorkloadColdReadBytes} L=0";
}

internal static class MultiSegmentBenchmarkRunner {
    public const long DefaultRolloverThresholdBytes = 512;

    public static BenchmarkBatchReport RunCanonicalBatch() {
        WorkloadTrace trace = MultiSegmentBenchmarkCorpus.CreateDeterministicMixedTrace();
        return new BenchmarkBatchReport(new[] {
            Run(trace, BenchmarkPolicyKind.AllDelta),
            Run(trace, BenchmarkPolicyKind.AllBase),
            Run(trace, BenchmarkPolicyKind.Adaptive),
        });
    }

    public static BenchmarkRunOutcome Run(
        WorkloadTrace trace,
        BenchmarkPolicyKind policy,
        long rolloverThresholdBytes = DefaultRolloverThresholdBytes,
        ReadAmplificationBaseBudgetPolicyParameters? adaptiveParameters = null) {
        ArgumentNullException.ThrowIfNull(trace);
        if (!Enum.IsDefined(policy)) {
            throw new ArgumentOutOfRangeException(nameof(policy));
        }

        ReadAmplificationBaseBudgetPolicyParameters parameters = adaptiveParameters ??
            new ReadAmplificationBaseBudgetPolicyParameters(3m, 0.05m);
        string policyId = policy switch {
            BenchmarkPolicyKind.AllDelta => "all-delta",
            BenchmarkPolicyKind.AllBase => "all-base",
            BenchmarkPolicyKind.Adaptive => string.Create(
                CultureInfo.InvariantCulture,
                $"adaptive-r{parameters.ReadAmplificationLimit:0.################}-" +
                $"b{parameters.BaseBudgetFraction * 100m:0.################}pct"),
            _ => throw new ArgumentOutOfRangeException(nameof(policy)),
        };
        InMemorySegmentStore store = new();
        RevisionCommitSession session = new(store, rolloverThresholdBytes);
        EvaluatorRawMetricAccumulator evaluator = new(store);
        for (int saveOrdinal = 0; saveOrdinal < trace.Steps.Count; saveOrdinal++) {
            SaveStep step = trace.Steps[saveOrdinal];
            NormalizedSaveFacts facts;
            RevisionSaveSelection selection;
            try {
                facts = RevisionSaveNormalizer.Normalize(
                    store,
                    session.PublishedHead,
                    session.UsedObjectIds,
                    step);
                selection = policy switch {
                    BenchmarkPolicyKind.AllDelta => FixedSelection(
                        facts,
                        UpdateWriteMode.Delta),
                    BenchmarkPolicyKind.AllBase => FixedSelection(
                        facts,
                        UpdateWriteMode.Base),
                    BenchmarkPolicyKind.Adaptive =>
                        ReadAmplificationPolicyAdapter.Select(
                            store,
                            facts,
                            parameters,
                            ObjectVersionDictionaryKind.Base).RevisionSelection,
                    _ => throw new ArgumentOutOfRangeException(nameof(policy)),
                };
            } catch (Exception exception) when (
                exception is ArgumentException or InvalidDataException or OverflowException) {
                evaluator.ValidateRejectedSave(store);
                return new RejectedBenchmarkRun(
                    policyId,
                    saveOrdinal,
                    new RejectedRevisionCommit(
                        RevisionCommitRejectionKind.InvalidInput,
                        exception.Message));
            }

            RevisionCommitAttempt attempt = session.Commit(
                step,
                session.PublishedHead,
                selection);
            if (attempt is RejectedRevisionCommit rejected) {
                evaluator.ValidateRejectedSave(store);
                return new RejectedBenchmarkRun(policyId, saveOrdinal, rejected);
            }

            PublishedRevisionCommit published = attempt as PublishedRevisionCommit
                ?? throw new InvalidDataException(
                    "The benchmark runner received a non-published Commit outcome.");
            evaluator.RecordAcceptedSave(store, facts, published.PublishedHead);
        }

        return new AdmittedBenchmarkRun(
            policyId,
            evaluator.Complete(store),
            store.SegmentCount);
    }

    internal static RevisionSaveSelection FixedSelection(
        NormalizedSaveFacts facts,
        UpdateWriteMode updateMode) => new(
        ObjectVersionDictionaryKind.Base,
        facts.Updates.Select(update => new UpdateWriteSelection(
            update.ObjectId,
            updateMode)),
        facts.NoChanges.Select(static noChange => new NoChangeWriteSelection(
            noChange.ObjectId,
            NoChangeWriteMode.Inherit)));
}
