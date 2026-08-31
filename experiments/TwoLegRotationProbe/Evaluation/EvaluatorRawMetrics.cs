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
        long totalPhysicalWriteBytes,
        long peakCommitWriteBytes,
        long maxCurrentFileTailBytes,
        IEnumerable<WorkloadColdReadSample> workloadColdReadSamples,
        FinalColdHeadReadObservation terminalColdHeadRead) {
        ArgumentOutOfRangeException.ThrowIfNegative(realizedCommitCount);
        ArgumentOutOfRangeException.ThrowIfNegative(totalPhysicalWriteBytes);
        ArgumentOutOfRangeException.ThrowIfNegative(peakCommitWriteBytes);
        if (peakCommitWriteBytes > totalPhysicalWriteBytes) {
            throw new ArgumentException(
                "Peak Commit write bytes cannot exceed total physical write bytes.",
                nameof(peakCommitWriteBytes));
        }

        if (realizedCommitCount == 0 && peakCommitWriteBytes != 0) {
            throw new ArgumentException(
                "A run without realized Commits cannot have a nonzero write peak.",
                nameof(peakCommitWriteBytes));
        }

        if (maxCurrentFileTailBytes < RbfV040Layout.InitialTailOffsetBytes) {
            throw new ArgumentOutOfRangeException(
                nameof(maxCurrentFileTailBytes),
                maxCurrentFileTailBytes,
                "The maximum Current-file tail must include an initialized RBF file.");
        }

        RealizedCommitCount = realizedCommitCount;
        TotalPhysicalWriteBytes = totalPhysicalWriteBytes;
        PeakCommitWriteBytes = peakCommitWriteBytes;
        MaxCurrentFileTailBytes = maxCurrentFileTailBytes;
        ArgumentNullException.ThrowIfNull(workloadColdReadSamples);
        WorkloadColdReadSample[] samples = workloadColdReadSamples.ToArray();
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

    public long TotalPhysicalWriteBytes { get; }

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
