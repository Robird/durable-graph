namespace Atelia.TwoLegRotationProbe.Model;

internal sealed class RbfFile {
    private readonly Dictionary<FrameTicket, StoredFrame> _frames = [];

    public RbfFile(uint fileNumber) {
        ArgumentOutOfRangeException.ThrowIfZero(fileNumber);
        FileNumber = fileNumber;
    }

    public uint FileNumber { get; }

    public int FrameCount => _frames.Count;

    public long TailOffsetBytes { get; private set; } = RbfV040Layout.InitialTailOffsetBytes;

    public FrameTicket Append(
        Frame frame,
        int payloadLengthBytes = 0,
        int tailMetaLengthBytes = 0) {
        ArgumentNullException.ThrowIfNull(frame);

        RbfFrameLayoutEstimate layout = RbfV040Layout.Estimate(
            TailOffsetBytes,
            payloadLengthBytes,
            tailMetaLengthBytes);
        _frames.Add(layout.Ticket, new StoredFrame(frame, layout));
        TailOffsetBytes = layout.TailOffsetAfterBytes;
        return layout.Ticket;
    }

    public Frame Read(FrameTicket frameTicket) {
        if (!_frames.TryGetValue(frameTicket, out StoredFrame? stored)) {
            throw new KeyNotFoundException($"RBF frame {frameTicket} does not exist in file {FileNumber}.");
        }

        return stored.Frame;
    }

    public RbfFrameLayoutEstimate ReadLayout(FrameTicket frameTicket) {
        if (!_frames.TryGetValue(frameTicket, out StoredFrame? stored)) {
            throw new KeyNotFoundException($"RBF frame {frameTicket} does not exist in file {FileNumber}.");
        }

        return stored.Layout;
    }

    /// <summary>
    /// Creates a volatile in-memory scratch fork for probe planning. The fork preserves
    /// exact tickets, layouts, tail, and immutable Frame references. It is not a durable,
    /// crash-consistent, or concurrent snapshot contract.
    /// </summary>
    public RbfFile ForkForProbe() {
        RbfFile fork = new(FileNumber);
        foreach ((FrameTicket ticket, StoredFrame stored) in _frames) {
            fork._frames.Add(ticket, stored);
        }

        fork.TailOffsetBytes = TailOffsetBytes;
        return fork;
    }

    private sealed record StoredFrame(Frame Frame, RbfFrameLayoutEstimate Layout);
}
