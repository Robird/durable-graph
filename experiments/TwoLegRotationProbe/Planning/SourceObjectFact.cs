using System.Collections.ObjectModel;
using Atelia.TwoLegRotationProbe.Model;
using Atelia.TwoLegRotationProbe.Workloads;

namespace Atelia.TwoLegRotationProbe.Planning;

/// <summary>
/// Immutable planning view of one live object in the source PublishedRevision.
/// Its addresses come only from the materialized runtime OVD and reconstruction oracle.
/// </summary>
internal sealed class SourceObjectFact {
    private readonly ReadOnlyCollection<AbsoluteFrameAddress> _reconstructionFrameAddresses;

    internal SourceObjectFact(
        uint objectId,
        LogicalObjectState state,
        AbsoluteFrameAddress headAddress,
        AbsoluteFrameAddress baseAddress,
        long headReconstructionObjectPayloadBytes,
        IEnumerable<AbsoluteFrameAddress> reconstructionFrameAddresses) {
        ArgumentOutOfRangeException.ThrowIfNegative(headReconstructionObjectPayloadBytes);
        ArgumentNullException.ThrowIfNull(reconstructionFrameAddresses);

        AbsoluteFrameAddress[] frozenReconstructionFrameAddresses =
            reconstructionFrameAddresses.ToArray();
        if (frozenReconstructionFrameAddresses.Length == 0 ||
            frozenReconstructionFrameAddresses[0] != headAddress ||
            frozenReconstructionFrameAddresses[^1] != baseAddress) {
            throw new ArgumentException(
                "A source object reconstruction path must run from its head to its terminating Base.",
                nameof(reconstructionFrameAddresses));
        }

        ObjectId = objectId;
        State = state;
        HeadAddress = headAddress;
        BaseAddress = baseAddress;
        HeadReconstructionObjectPayloadBytes = headReconstructionObjectPayloadBytes;
        _reconstructionFrameAddresses = Array.AsReadOnly(
            frozenReconstructionFrameAddresses);
    }

    public uint ObjectId { get; }

    public LogicalObjectState State { get; }

    public AbsoluteFrameAddress HeadAddress { get; }

    public AbsoluteFrameAddress BaseAddress { get; }

    public long HeadReconstructionObjectPayloadBytes { get; }

    public IReadOnlyList<AbsoluteFrameAddress> ReconstructionFrameAddresses =>
        _reconstructionFrameAddresses;
}
