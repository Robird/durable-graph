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
        FinalColdHeadReadObservation finalColdHeadRead) {
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
        FinalColdHeadRead = finalColdHeadRead ??
            throw new ArgumentNullException(nameof(finalColdHeadRead));
    }

    public int RealizedCommitCount { get; }

    public long TotalPhysicalWriteBytes { get; }

    public long PeakCommitWriteBytes { get; }

    public long MaxCurrentFileTailBytes { get; }

    public long FinalColdHeadReadBytes => FinalColdHeadRead.UniqueFrameBytes;

    public FinalColdHeadReadObservation FinalColdHeadRead { get; }
}
