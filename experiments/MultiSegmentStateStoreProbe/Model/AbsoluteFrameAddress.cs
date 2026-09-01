namespace Atelia.MultiSegmentStateStoreProbe.Model;

/// <summary>
/// Probe authority representation. <see cref="FrameTicketCode"/> is an opaque,
/// non-zero stand-in for the future canonical SizedPtr value.
/// </summary>
internal readonly record struct AbsoluteFrameAddress {
    public AbsoluteFrameAddress(FileNumber fileNumber, ulong frameTicketCode) {
        ArgumentOutOfRangeException.ThrowIfZero(frameTicketCode);
        FileNumber = fileNumber;
        FrameTicketCode = frameTicketCode;
    }

    public FileNumber FileNumber { get; }

    public ulong FrameTicketCode { get; }
}
