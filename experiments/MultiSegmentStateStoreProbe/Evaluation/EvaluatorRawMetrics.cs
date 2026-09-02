using System.Collections.ObjectModel;

namespace Atelia.MultiSegmentStateStoreProbe.Evaluation;

internal sealed record WorkloadColdReadSample(
    int SaveOrdinal,
    ColdHeadReadObservation ColdRead);

internal sealed class EvaluatorRawMetrics {
    private readonly ReadOnlyCollection<WorkloadColdReadSample> _coldReadSamples;

    internal EvaluatorRawMetrics(
        int admittedSaveCount,
        long workloadPhysicalWriteBytes,
        long peakWorkloadSaveWriteBytes,
        long maxSegmentTailBytes,
        long totalWorkloadDeltaReferencePayloadBytes,
        long totalWorkloadBaseReferencePayloadBytes,
        IEnumerable<WorkloadColdReadSample> coldReadSamples) {
        ArgumentOutOfRangeException.ThrowIfNegative(admittedSaveCount);
        ArgumentOutOfRangeException.ThrowIfNegative(workloadPhysicalWriteBytes);
        ArgumentOutOfRangeException.ThrowIfNegative(peakWorkloadSaveWriteBytes);
        ArgumentOutOfRangeException.ThrowIfNegative(maxSegmentTailBytes);
        ArgumentOutOfRangeException.ThrowIfNegative(
            totalWorkloadDeltaReferencePayloadBytes);
        ArgumentOutOfRangeException.ThrowIfNegative(
            totalWorkloadBaseReferencePayloadBytes);
        if (peakWorkloadSaveWriteBytes > workloadPhysicalWriteBytes) {
            throw new ArgumentException("P cannot exceed W.", nameof(peakWorkloadSaveWriteBytes));
        }

        WorkloadColdReadSample[] samples = coldReadSamples.ToArray();
        if (samples.Length != admittedSaveCount) {
            throw new ArgumentException(
                "Every admitted workload Save requires exactly one cold-read sample.",
                nameof(coldReadSamples));
        }

        long totalRead = 0;
        long totalLogical = 0;
        for (int index = 0; index < samples.Length; index++) {
            if (samples[index].SaveOrdinal != index) {
                throw new ArgumentException(
                    "Cold-read samples require contiguous zero-based ordinals.",
                    nameof(coldReadSamples));
            }

            totalRead = checked(totalRead + samples[index].ColdRead.UniqueFrameBytes);
            totalLogical = checked(
                totalLogical + samples[index].ColdRead.PostLiveBasePayloadBytes);
        }

        AdmittedSaveCount = admittedSaveCount;
        WorkloadPhysicalWriteBytes = workloadPhysicalWriteBytes;
        PeakWorkloadSaveWriteBytes = peakWorkloadSaveWriteBytes;
        MaxSegmentTailBytes = maxSegmentTailBytes;
        TotalWorkloadColdReadBytes = totalRead;
        TotalWorkloadLogicalBasePayloadBytes = totalLogical;
        TotalWorkloadDeltaReferencePayloadBytes =
            totalWorkloadDeltaReferencePayloadBytes;
        TotalWorkloadBaseReferencePayloadBytes =
            totalWorkloadBaseReferencePayloadBytes;
        _coldReadSamples = Array.AsReadOnly(samples);
    }

    public int AdmittedSaveCount { get; }

    public long WorkloadPhysicalWriteBytes { get; }

    public long PeakWorkloadSaveWriteBytes { get; }

    public long MaxSegmentTailBytes { get; }

    public long TotalWorkloadColdReadBytes { get; }

    public long TotalWorkloadLogicalBasePayloadBytes { get; }

    /// <summary>
    /// R/L is defined only when L is nonzero. The evaluator retains the exact integer
    /// numerator and denominator and never substitutes a rounded ratio.
    /// </summary>
    public bool IsAggregateReadAmplificationDefined =>
        TotalWorkloadLogicalBasePayloadBytes != 0;

    public long TotalWorkloadDeltaReferencePayloadBytes { get; }

    public long TotalWorkloadBaseReferencePayloadBytes { get; }

    public IReadOnlyList<WorkloadColdReadSample> ColdReadSamples =>
        _coldReadSamples;
}
