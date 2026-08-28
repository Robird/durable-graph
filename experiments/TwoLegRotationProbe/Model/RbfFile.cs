namespace Atelia.TwoLegRotationProbe.Model;

internal sealed class RbfFile {
    private readonly List<Frame> _frames = [];

    public RbfFile(uint fileNumber) {
        ArgumentOutOfRangeException.ThrowIfZero(fileNumber);
        FileNumber = fileNumber;
    }

    public uint FileNumber { get; }

    public int FrameCount => _frames.Count;

    public FrameId Append(Frame frame) {
        ArgumentNullException.ThrowIfNull(frame);

        FrameId frameId = new(_frames.Count);
        _frames.Add(frame);
        return frameId;
    }

    public Frame Read(FrameId frameId) {
        if ((uint)frameId.Value >= (uint)_frames.Count) {
            throw new ArgumentOutOfRangeException(nameof(frameId));
        }

        return _frames[frameId.Value];
    }
}
