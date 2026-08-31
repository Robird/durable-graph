using System.Collections.ObjectModel;
using Atelia.TwoLegRotationProbe.Model;

namespace Atelia.TwoLegRotationProbe.Evaluation;

/// <summary>
/// Full-Frame read accounting for one cold load of a PublishedRevision. The
/// dictionary and object sets are retained separately for diagnosis; the evaluator's
/// R metric is their de-duplicated union.
/// </summary>
internal sealed class FinalColdHeadReadObservation {
    private readonly ReadOnlyCollection<AbsoluteFrameAddress> _dictionaryFrameAddresses;
    private readonly ReadOnlyCollection<AbsoluteFrameAddress>
        _objectReconstructionFrameAddresses;
    private readonly ReadOnlyCollection<AbsoluteFrameAddress> _uniqueFrameAddresses;

    internal FinalColdHeadReadObservation(
        IEnumerable<AbsoluteFrameAddress> dictionaryFrameAddresses,
        IEnumerable<AbsoluteFrameAddress> objectReconstructionFrameAddresses,
        long postLiveBasePayloadBytes,
        Func<AbsoluteFrameAddress, long> getFrameBytes) {
        ArgumentNullException.ThrowIfNull(dictionaryFrameAddresses);
        ArgumentNullException.ThrowIfNull(objectReconstructionFrameAddresses);
        ArgumentNullException.ThrowIfNull(getFrameBytes);
        ArgumentOutOfRangeException.ThrowIfNegative(postLiveBasePayloadBytes);

        AbsoluteFrameAddress[] dictionaryFrames = Canonicalize(
            dictionaryFrameAddresses);
        AbsoluteFrameAddress[] objectFrames = Canonicalize(
            objectReconstructionFrameAddresses);
        AbsoluteFrameAddress[] uniqueFrames = Canonicalize(
            dictionaryFrames.Concat(objectFrames));

        _dictionaryFrameAddresses = Array.AsReadOnly(dictionaryFrames);
        _objectReconstructionFrameAddresses = Array.AsReadOnly(objectFrames);
        _uniqueFrameAddresses = Array.AsReadOnly(uniqueFrames);
        DictionaryFrameBytes = SumFrameBytes(dictionaryFrames, getFrameBytes);
        ObjectReconstructionFrameBytes = SumFrameBytes(objectFrames, getFrameBytes);
        UniqueFrameBytes = SumFrameBytes(uniqueFrames, getFrameBytes);
        PostLiveBasePayloadBytes = postLiveBasePayloadBytes;
    }

    public IReadOnlyList<AbsoluteFrameAddress> DictionaryFrameAddresses =>
        _dictionaryFrameAddresses;

    public IReadOnlyList<AbsoluteFrameAddress> ObjectReconstructionFrameAddresses =>
        _objectReconstructionFrameAddresses;

    public IReadOnlyList<AbsoluteFrameAddress> UniqueFrameAddresses =>
        _uniqueFrameAddresses;

    public long DictionaryFrameBytes { get; }

    public long ObjectReconstructionFrameBytes { get; }

    public long UniqueFrameBytes { get; }

    public long PostLiveBasePayloadBytes { get; }

    private static AbsoluteFrameAddress[] Canonicalize(
        IEnumerable<AbsoluteFrameAddress> addresses) => addresses
        .Distinct()
        .OrderBy(static address => address.FileNumber)
        .ThenBy(static address => address.FrameTicket.OffsetBytes)
        .ThenBy(static address => address.FrameTicket.LengthBytes)
        .ToArray();

    private static long SumFrameBytes(
        IEnumerable<AbsoluteFrameAddress> addresses,
        Func<AbsoluteFrameAddress, long> getFrameBytes) {
        long total = 0;
        foreach (AbsoluteFrameAddress address in addresses) {
            long frameBytes = getFrameBytes(address);
            ArgumentOutOfRangeException.ThrowIfNegative(frameBytes);
            total = checked(total + frameBytes);
        }

        return total;
    }
}
