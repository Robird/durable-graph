namespace Atelia.TwoLegRotationProbe.Model;

internal readonly record struct RbfFrameLayoutEstimate {
    internal RbfFrameLayoutEstimate(
        long frameStartOffsetBytes,
        int payloadLengthBytes,
        int tailMetaLengthBytes,
        int paddingLengthBytes,
        int frameLengthBytes,
        int appendLengthBytes,
        long tailOffsetAfterBytes) {
        FrameStartOffsetBytes = frameStartOffsetBytes;
        PayloadLengthBytes = payloadLengthBytes;
        TailMetaLengthBytes = tailMetaLengthBytes;
        PaddingLengthBytes = paddingLengthBytes;
        FrameLengthBytes = frameLengthBytes;
        AppendLengthBytes = appendLengthBytes;
        TailOffsetAfterBytes = tailOffsetAfterBytes;
    }

    public long FrameStartOffsetBytes { get; }

    public int PayloadLengthBytes { get; }

    public int TailMetaLengthBytes { get; }

    public int PaddingLengthBytes { get; }

    public int FrameLengthBytes { get; }

    public int AppendLengthBytes { get; }

    public long TailOffsetAfterBytes { get; }

    public FrameTicket Ticket => new(FrameStartOffsetBytes, FrameLengthBytes);
}
