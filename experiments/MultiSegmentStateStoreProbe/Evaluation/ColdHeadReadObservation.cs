using System.Collections.ObjectModel;
using Atelia.MultiSegmentStateStoreProbe.Model;

namespace Atelia.MultiSegmentStateStoreProbe.Evaluation;

internal sealed class ColdHeadReadObservation {
    private readonly ReadOnlyCollection<AbsoluteFrameAddress> _ovdFrameAddresses;
    private readonly ReadOnlyCollection<AbsoluteFrameAddress> _objectFrameAddresses;
    private readonly ReadOnlyCollection<AbsoluteFrameAddress> _uniqueFrameAddresses;

    internal ColdHeadReadObservation(
        IEnumerable<AbsoluteFrameAddress> ovdFrameAddresses,
        IEnumerable<AbsoluteFrameAddress> objectFrameAddresses,
        long postLiveBasePayloadBytes,
        Func<AbsoluteFrameAddress, long> getFrameBytes) {
        ArgumentNullException.ThrowIfNull(ovdFrameAddresses);
        ArgumentNullException.ThrowIfNull(objectFrameAddresses);
        ArgumentNullException.ThrowIfNull(getFrameBytes);
        ArgumentOutOfRangeException.ThrowIfNegative(postLiveBasePayloadBytes);

        AbsoluteFrameAddress[] ovd = Canonicalize(ovdFrameAddresses);
        AbsoluteFrameAddress[] objects = Canonicalize(objectFrameAddresses);
        AbsoluteFrameAddress[] unique = Canonicalize(ovd.Concat(objects));
        _ovdFrameAddresses = Array.AsReadOnly(ovd);
        _objectFrameAddresses = Array.AsReadOnly(objects);
        _uniqueFrameAddresses = Array.AsReadOnly(unique);
        OvdFrameBytes = Sum(ovd, getFrameBytes);
        ObjectFrameBytes = Sum(objects, getFrameBytes);
        UniqueFrameBytes = Sum(unique, getFrameBytes);
        PostLiveBasePayloadBytes = postLiveBasePayloadBytes;
    }

    public IReadOnlyList<AbsoluteFrameAddress> OvdFrameAddresses =>
        _ovdFrameAddresses;

    public IReadOnlyList<AbsoluteFrameAddress> ObjectFrameAddresses =>
        _objectFrameAddresses;

    public IReadOnlyList<AbsoluteFrameAddress> UniqueFrameAddresses =>
        _uniqueFrameAddresses;

    public long OvdFrameBytes { get; }

    public long ObjectFrameBytes { get; }

    /// <summary>R contribution for this empty-cache current-state load.</summary>
    public long UniqueFrameBytes { get; }

    /// <summary>L contribution sampled at the same accepted head.</summary>
    public long PostLiveBasePayloadBytes { get; }

    private static AbsoluteFrameAddress[] Canonicalize(
        IEnumerable<AbsoluteFrameAddress> addresses) => addresses
        .Distinct()
        .OrderBy(static address => address.FileNumber.Value)
        .ThenBy(static address => address.FrameTicket.OffsetBytes)
        .ThenBy(static address => address.FrameTicket.LengthBytes)
        .ToArray();

    private static long Sum(
        IEnumerable<AbsoluteFrameAddress> addresses,
        Func<AbsoluteFrameAddress, long> getFrameBytes) {
        long total = 0;
        foreach (AbsoluteFrameAddress address in addresses) {
            long bytes = getFrameBytes(address);
            ArgumentOutOfRangeException.ThrowIfNegative(bytes);
            total = checked(total + bytes);
        }

        return total;
    }
}
