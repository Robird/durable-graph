namespace Atelia.TwoLegRotationProbe.Model;

internal sealed class RbfFile {
    private readonly List<Frame> _frames = [];

    public RbfFile(uint fileNumber) {
        ArgumentOutOfRangeException.ThrowIfZero(fileNumber);
        FileNumber = fileNumber;
    }

    public uint FileNumber { get; }

    public int FrameCount => _frames.Count;

    public FrameTicket Append(Frame frame) {
        ArgumentNullException.ThrowIfNull(frame);

        FrameTicket frameTicket = new(_frames.Count);
        _frames.Add(frame);
        return frameTicket;
    }

    public Frame Read(FrameTicket frameTicket) {
        if ((uint)frameTicket.Value >= (uint)_frames.Count) {
            throw new ArgumentOutOfRangeException(nameof(frameTicket));
        }

        return _frames[frameTicket.Value];
    }
}
