namespace Atelia.MultiSegmentStateStoreProbe.Model;

/// <summary>
/// Probe runtime authority representation. The containing Store supplies the meaning of
/// <see cref="FileNumber"/>; <see cref="FrameTicket"/> identifies one complete Frame.
/// </summary>
internal readonly record struct AbsoluteFrameAddress {
    public AbsoluteFrameAddress(FileNumber fileNumber, FrameTicket frameTicket) {
        if (fileNumber.Value == 0) {
            throw new ArgumentOutOfRangeException(nameof(fileNumber));
        }

        if (!frameTicket.IsValid) {
            throw new ArgumentOutOfRangeException(nameof(frameTicket));
        }

        FileNumber = fileNumber;
        FrameTicket = frameTicket;
    }

    public FileNumber FileNumber { get; }

    public FrameTicket FrameTicket { get; }
}
