namespace Atelia.MultiSegmentStateStoreProbe.Model;

/// <summary>
/// Required origin-scoped reference to a Frame in the containing file or any older file.
/// Optional None and future OVD BindSelf values use separate field grammars.
/// </summary>
internal readonly record struct RelativeFrameTicket {
    public RelativeFrameTicket(
        uint backwardFileDistance,
        FrameTicket frameTicket) {
        if (!frameTicket.IsValid) {
            throw new ArgumentOutOfRangeException(nameof(frameTicket));
        }

        BackwardFileDistance = backwardFileDistance;
        FrameTicket = frameTicket;
    }

    public uint BackwardFileDistance { get; }

    public FrameTicket FrameTicket { get; }

    internal void ValidateRequired() {
        if (!FrameTicket.IsValid) {
            throw new InvalidDataException(
                "A required relative Frame ticket must contain a valid byte range.");
        }
    }
}
