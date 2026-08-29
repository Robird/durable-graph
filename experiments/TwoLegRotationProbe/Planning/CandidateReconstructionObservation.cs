using System.Collections.ObjectModel;
using Atelia.TwoLegRotationProbe.Model;
using Atelia.TwoLegRotationProbe.Simulation;

namespace Atelia.TwoLegRotationProbe.Planning;

/// <summary>
/// Exact current-reconstruction facts derived from one candidate plus the normalized
/// source paths. Historical lineage is intentionally excluded.
/// </summary>
internal sealed class CandidateReconstructionObservation {
    private readonly ReadOnlyCollection<AbsoluteFrameAddress> _uniqueFrameAddresses;
    private readonly ReadOnlyCollection<uint> _previousFileDependentObjectIds;

    internal CandidateReconstructionObservation(
        FileScope resultScope,
        IEnumerable<AbsoluteFrameAddress> uniqueFrameAddresses,
        IEnumerable<uint> previousFileDependentObjectIds,
        long previousFileDependentBasePayloadBytes,
        int previousFileUniqueFrameCount,
        long previousFileFrameBytes,
        PostSaveReconstructionMetrics metrics) {
        ArgumentNullException.ThrowIfNull(resultScope);
        ArgumentNullException.ThrowIfNull(uniqueFrameAddresses);
        ArgumentNullException.ThrowIfNull(previousFileDependentObjectIds);
        ArgumentOutOfRangeException.ThrowIfNegative(
            previousFileDependentBasePayloadBytes);
        ArgumentOutOfRangeException.ThrowIfNegative(previousFileUniqueFrameCount);
        ArgumentOutOfRangeException.ThrowIfNegative(previousFileFrameBytes);

        AbsoluteFrameAddress[] canonicalFrameAddresses = uniqueFrameAddresses
            .Distinct()
            .OrderBy(static address => address.FileNumber)
            .ThenBy(static address => address.FrameTicket.OffsetBytes)
            .ThenBy(static address => address.FrameTicket.LengthBytes)
            .ToArray();
        uint[] canonicalDependentObjectIds = previousFileDependentObjectIds
            .Distinct()
            .Order()
            .ToArray();

        if (canonicalFrameAddresses.Length != metrics.UniqueFrameCount ||
            canonicalDependentObjectIds.Length > metrics.LiveObjectCount) {
            throw new ArgumentException(
                "Candidate reconstruction collections disagree with their metrics.");
        }

        ResultScope = resultScope;
        _uniqueFrameAddresses = Array.AsReadOnly(canonicalFrameAddresses);
        _previousFileDependentObjectIds = Array.AsReadOnly(
            canonicalDependentObjectIds);
        PreviousFileDependentBasePayloadBytes =
            previousFileDependentBasePayloadBytes;
        PreviousFileUniqueFrameCount = previousFileUniqueFrameCount;
        PreviousFileFrameBytes = previousFileFrameBytes;
        Metrics = metrics;
    }

    public FileScope ResultScope { get; }

    public IReadOnlyList<AbsoluteFrameAddress> UniqueFrameAddresses =>
        _uniqueFrameAddresses;

    public IReadOnlyList<uint> PreviousFileDependentObjectIds =>
        _previousFileDependentObjectIds;

    public long PreviousFileDependentBasePayloadBytes { get; }

    public int PreviousFileUniqueFrameCount { get; }

    public long PreviousFileFrameBytes { get; }

    public PostSaveReconstructionMetrics Metrics { get; }
}
