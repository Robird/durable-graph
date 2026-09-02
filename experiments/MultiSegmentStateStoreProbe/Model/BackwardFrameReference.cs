namespace Atelia.MultiSegmentStateStoreProbe.Model;

/// <summary>
/// Required wire-level reference to a Frame in the containing file or any older file.
/// Optional None and OVD BindSelf use separate field grammars.
/// </summary>
internal readonly record struct BackwardFrameReference {
    public BackwardFrameReference(uint backwardFileDistance, ulong frameTicketCode) {
        ArgumentOutOfRangeException.ThrowIfZero(frameTicketCode);
        BackwardFileDistance = backwardFileDistance;
        FrameTicketCode = frameTicketCode;
    }

    public uint BackwardFileDistance { get; }

    public ulong FrameTicketCode { get; }

    internal void ValidateRequired() {
        if (FrameTicketCode == 0) {
            throw new InvalidDataException(
                "A required BackwardFrameReference must have a non-zero FrameTicketCode.");
        }
    }
}
