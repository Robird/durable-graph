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
        ProvisionalFrameLayout layout,
        RevisionFrame? revisionFrame = null) {
        Plan = plan;
        FileNumber = fileNumber;
        _relativeReferences = Array.AsReadOnly(relativeReferences.ToArray());
        _encodedReferenceBytes = (byte[])encodedReferenceBytes.Clone();
        Layout = layout;
        Address = new AbsoluteFrameAddress(fileNumber, layout.Ticket);
        RevisionFrame = revisionFrame;
    }

    public OriginFreeFramePlan Plan { get; }

    public FileNumber FileNumber { get; }

    public IReadOnlyList<RelativeFrameTicket> RelativeReferences =>
        _relativeReferences;

    public ReadOnlyMemory<byte> EncodedReferenceBytes => _encodedReferenceBytes;

    public ProvisionalFrameLayout Layout { get; }

    public AbsoluteFrameAddress Address { get; }

    public RevisionFrame? RevisionFrame { get; }

    public long SyntheticObjectPayloadBytes => Plan.SyntheticPayloadBytes;

    public int SemanticMetadataPayloadBytes => Plan.SemanticMetadataPayloadBytes;

    public int EncodedReferenceLengthBytes => _encodedReferenceBytes.Length;
}
