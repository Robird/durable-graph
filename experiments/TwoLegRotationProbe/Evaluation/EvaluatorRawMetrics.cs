using System.Collections.ObjectModel;
using Atelia.TwoLegRotationProbe.Model;

namespace Atelia.TwoLegRotationProbe.Evaluation;

/// <summary>
/// Raw, unweighted metrics for one caller-declared evaluation horizon. This value
/// does not decide admissibility, closed-horizon settlement, ranking, or scoring.
/// </summary>
internal sealed class EvaluatorRawMetrics {
    internal EvaluatorRawMetrics(
        int realizedCommitCount,
        long workloadPhysicalWriteBytes,
        long terminalSettlementPhysicalWriteBytes,
        long totalWorkloadDeltaReferencePayloadBytes,
        long totalWorkloadBaseReferencePayloadBytes,
        long peakWorkloadCommitWriteBytes,
        long maxCurrentFileTailBytes,
        IEnumerable<WorkloadColdReadSample> workloadColdReadSamples,
        FinalColdHeadReadObservation terminalColdHeadRead) {
        ArgumentOutOfRangeException.ThrowIfNegative(realizedCommitCount);
        ArgumentOutOfRangeException.ThrowIfNegative(workloadPhysicalWriteBytes);
        ArgumentOutOfRangeException.ThrowIfNegative(
            terminalSettlementPhysicalWriteBytes);
        ArgumentOutOfRangeException.ThrowIfNegative(
            totalWorkloadDeltaReferencePayloadBytes);
        ArgumentOutOfRangeException.ThrowIfNegative(
            totalWorkloadBaseReferencePayloadBytes);
        ArgumentOutOfRangeException.ThrowIfNegative(peakWorkloadCommitWriteBytes);
        long totalPhysicalWriteBytes = checked(
            workloadPhysicalWriteBytes + terminalSettlementPhysicalWriteBytes);
        long peakCommitWriteBytes = Math.Max(
            peakWorkloadCommitWriteBytes,
            terminalSettlementPhysicalWriteBytes);
        if (peakWorkloadCommitWriteBytes > workloadPhysicalWriteBytes) {
            throw new ArgumentException(
                "Peak workload Commit write bytes cannot exceed workload physical write bytes.",
                nameof(peakWorkloadCommitWriteBytes));
        }

        if (realizedCommitCount == 0 && peakCommitWriteBytes != 0) {
            throw new ArgumentException(
                "A run without realized Commits cannot have a nonzero write peak.",
                nameof(realizedCommitCount));
        }

        if (maxCurrentFileTailBytes < RbfV040Layout.InitialTailOffsetBytes) {
            throw new ArgumentOutOfRangeException(
                nameof(maxCurrentFileTailBytes),
                maxCurrentFileTailBytes,
                "The maximum Current-file tail must include an initialized RBF file.");
        }

        RealizedCommitCount = realizedCommitCount;
        WorkloadPhysicalWriteBytes = workloadPhysicalWriteBytes;
        TerminalSettlementPhysicalWriteBytes = terminalSettlementPhysicalWriteBytes;
        TotalPhysicalWriteBytes = totalPhysicalWriteBytes;
        TotalWorkloadDeltaReferencePayloadBytes =
            totalWorkloadDeltaReferencePayloadBytes;
        TotalWorkloadBaseReferencePayloadBytes =
            totalWorkloadBaseReferencePayloadBytes;
        PeakWorkloadCommitWriteBytes = peakWorkloadCommitWriteBytes;
        PeakCommitWriteBytes = peakCommitWriteBytes;
        MaxCurrentFileTailBytes = maxCurrentFileTailBytes;
        ArgumentNullException.ThrowIfNull(workloadColdReadSamples);
        WorkloadColdReadSample[] samples = workloadColdReadSamples.ToArray();
        if (samples.Length > realizedCommitCount) {
            throw new ArgumentException(
                "Workload samples cannot exceed realized Commits.",
                nameof(workloadColdReadSamples));
        }

        if (samples.Length == 0 &&
            (workloadPhysicalWriteBytes != 0 ||
                peakWorkloadCommitWriteBytes != 0 ||
                totalWorkloadDeltaReferencePayloadBytes != 0 ||
                totalWorkloadBaseReferencePayloadBytes != 0)) {
            throw new ArgumentException(
                "Metrics without workload samples cannot contain workload write accounting.",
                nameof(workloadColdReadSamples));
        }

        if (samples.Length > 0 && peakWorkloadCommitWriteBytes == 0) {
            throw new ArgumentException(
                "Metrics with workload samples must have a positive workload write peak.",
                nameof(peakWorkloadCommitWriteBytes));
        }

        for (int index = 0; index < samples.Length; index++) {
            WorkloadColdReadSample sample = samples[index] ??
                throw new ArgumentException(
                    "Workload cold-read samples cannot contain null.",
                    nameof(workloadColdReadSamples));
            if (sample.WorkloadSaveOrdinal != index) {
                throw new ArgumentException(
                    "Workload cold-read samples must have contiguous zero-based ordinals.",
                    nameof(workloadColdReadSamples));
            }
        }

        _workloadColdReadSamples = Array.AsReadOnly(samples);
        long totalWorkloadColdReadBytes = 0;
        long totalWorkloadLogicalBasePayloadBytes = 0;
        foreach (WorkloadColdReadSample sample in samples) {
            totalWorkloadColdReadBytes = checked(
                totalWorkloadColdReadBytes + sample.ColdRead.UniqueFrameBytes);
            totalWorkloadLogicalBasePayloadBytes = checked(
                totalWorkloadLogicalBasePayloadBytes +
                sample.PostLiveGraphBasePayloadBytes);
        }

        TotalWorkloadColdReadBytes = totalWorkloadColdReadBytes;
        TotalWorkloadLogicalBasePayloadBytes = totalWorkloadLogicalBasePayloadBytes;
        TerminalColdHeadRead = terminalColdHeadRead ??
            throw new ArgumentNullException(nameof(terminalColdHeadRead));
    }

    private readonly ReadOnlyCollection<WorkloadColdReadSample>
        _workloadColdReadSamples;

    public int RealizedCommitCount { get; }

    /// <summary>
    /// Physical bytes appended by successful outer workload Saves. This is the sole
    /// write total projected into the canonical strategy-comparison report.
    /// </summary>
    public long WorkloadPhysicalWriteBytes { get; }

    /// <summary>
    /// Physical bytes appended by the canonical terminal settlement Commit. This is
    /// a synthetic cutoff-liability and accounting diagnostic, not a strategy write
    /// comparator and not part of the canonical benchmark report.
    /// </summary>
    public long TerminalSettlementPhysicalWriteBytes { get; }

    /// <summary>
    /// Sum of workload and terminal-settlement physical write bytes. This derived
    /// closed-horizon value preserves Store-tail conservation for internal validation;
    /// it must not be used to rank strategies and is not projected into the canonical
    /// benchmark report.
    /// </summary>
    public long TotalPhysicalWriteBytes { get; }

    /// <summary>
    /// Workload-only payload reference in which Inserts contribute their Base payload
    /// and Updates contribute their Delta payload. Remove, NoChange, bootstrap, terminal
    /// settlement, and rejected Saves contribute zero.
    /// </summary>
    public long TotalWorkloadDeltaReferencePayloadBytes { get; }

    /// <summary>
    /// Workload-only payload reference in which Inserts and Updates contribute their
    /// resulting Base payload. Remove, NoChange, bootstrap, terminal settlement, and
    /// rejected Saves contribute zero.
    /// </summary>
    public long TotalWorkloadBaseReferencePayloadBytes { get; }

    /// <summary>
    /// Largest physical append burst among successful outer workload Commits.
    /// Bootstrap, terminal settlement, and rejected Saves are excluded.
    /// </summary>
    public long PeakWorkloadCommitWriteBytes { get; }

    /// <summary>
    /// Largest physical append burst in the closed horizon. Terminal settlement is
    /// one synthetic outer Commit, so this is exactly the maximum of the workload
    /// peak and <see cref="TerminalSettlementPhysicalWriteBytes"/>. It is retained
    /// only for internal closure-accounting diagnostics; canonical comparison uses
    /// <see cref="PeakWorkloadCommitWriteBytes"/>.
    /// </summary>
    public long PeakCommitWriteBytes { get; }

    public long MaxCurrentFileTailBytes { get; }

    public IReadOnlyList<WorkloadColdReadSample> WorkloadColdReadSamples =>
        _workloadColdReadSamples;

    public int WorkloadColdReadSampleCount => _workloadColdReadSamples.Count;

    /// <summary>
    /// R: sum of empty-cache current-state reconstruction bytes after every successful
    /// outer workload Save. Bootstrap and terminal settlement are excluded.
    /// </summary>
    public long TotalWorkloadColdReadBytes { get; }

    /// <summary>
    /// Sum of post-Save live-graph Base payload bytes over the same workload samples.
    /// Together with <see cref="TotalWorkloadColdReadBytes"/> it is the exact integer
    /// numerator/denominator pair for aggregate read amplification.
    /// </summary>
    public long TotalWorkloadLogicalBasePayloadBytes { get; }

    public long TerminalColdHeadReadBytes => TerminalColdHeadRead.UniqueFrameBytes;

    public FinalColdHeadReadObservation TerminalColdHeadRead { get; }
}

internal sealed record WorkloadColdReadSample {
    public WorkloadColdReadSample(
        int workloadSaveOrdinal,
        FinalColdHeadReadObservation coldRead,
        long postLiveGraphBasePayloadBytes) {
        ArgumentOutOfRangeException.ThrowIfNegative(workloadSaveOrdinal);
        ArgumentOutOfRangeException.ThrowIfNegative(postLiveGraphBasePayloadBytes);
        WorkloadSaveOrdinal = workloadSaveOrdinal;
        ColdRead = coldRead ?? throw new ArgumentNullException(nameof(coldRead));
        PostLiveGraphBasePayloadBytes = postLiveGraphBasePayloadBytes;
    }

    public int WorkloadSaveOrdinal { get; }

    public FinalColdHeadReadObservation ColdRead { get; }

    public long PostLiveGraphBasePayloadBytes { get; }
}
