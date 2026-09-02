using Atelia.MultiSegmentStateStoreProbe.Model;

namespace Atelia.MultiSegmentStateStoreProbe.Storage;

internal readonly record struct ProvisionalFrameLayout(
    long FrameStartOffsetBytes,
    int PayloadLengthBytes,
    int TailMetadataLengthBytes,
    int PaddingLengthBytes,
    int FrameLengthBytes,
    int AppendLengthBytes,
    long TailOffsetAfterBytes) {
    public FrameTicket Ticket => new(FrameStartOffsetBytes, FrameLengthBytes);
}
