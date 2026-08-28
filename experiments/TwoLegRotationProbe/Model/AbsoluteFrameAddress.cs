namespace Atelia.TwoLegRotationProbe.Model;

internal readonly record struct AbsoluteFrameAddress {
    public AbsoluteFrameAddress(uint fileNumber, FrameTicket frameTicket) {
        ArgumentOutOfRangeException.ThrowIfZero(fileNumber);
        FileNumber = fileNumber;
        FrameTicket = frameTicket;
    }

    public uint FileNumber { get; }

    public FrameTicket FrameTicket { get; }
}
