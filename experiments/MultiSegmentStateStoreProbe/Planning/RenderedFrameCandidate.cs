using System.Collections.ObjectModel;
using Atelia.MultiSegmentStateStoreProbe.Model;
using Atelia.MultiSegmentStateStoreProbe.Storage;

namespace Atelia.MultiSegmentStateStoreProbe.Planning;

internal sealed class RenderedFrameCandidate {
    private readonly ReadOnlyCollection<RelativeFrameTicket> _relativeReferences;
    private readonly byte[] _encodedReferenceBytes;

    internal RenderedFrameCandidate(
        OriginFreeFramePlan plan,
        FileNumber fileNumber,
        IEnumerable<RelativeFrameTicket> relativeReferences,
        byte[] encodedReferenceBytes,
        ProvisionalFrameLayout layout) {
        Plan = plan;
        FileNumber = fileNumber;
        _relativeReferences = Array.AsReadOnly(relativeReferences.ToArray());
        _encodedReferenceBytes = (byte[])encodedReferenceBytes.Clone();
        Layout = layout;
        Address = new AbsoluteFrameAddress(fileNumber, layout.Ticket);
    }

    public OriginFreeFramePlan Plan { get; }

    public FileNumber FileNumber { get; }

    public IReadOnlyList<RelativeFrameTicket> RelativeReferences =>
        _relativeReferences;

    public ReadOnlyMemory<byte> EncodedReferenceBytes => _encodedReferenceBytes;

    public ProvisionalFrameLayout Layout { get; }

    public AbsoluteFrameAddress Address { get; }
}
