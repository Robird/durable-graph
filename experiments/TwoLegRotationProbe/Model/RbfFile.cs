namespace Atelia.TwoLegRotationProbe.Model;

internal sealed class RbfFile {
    private readonly List<Frame> _frames = [];

    public RbfFile(uint fileNumber) {
        ArgumentOutOfRangeException.ThrowIfZero(fileNumber);
        FileNumber = fileNumber;
    }

    public uint FileNumber { get; }

    public int FrameCount => _frames.Count;

    public int Append(Frame frame) {
        ArgumentNullException.ThrowIfNull(frame);

        int frameNumber = _frames.Count;
        _frames.Add(frame);
        return frameNumber;
    }

    public Frame Read(int frameNumber) {
        if ((uint)frameNumber >= (uint)_frames.Count) {
            throw new ArgumentOutOfRangeException(nameof(frameNumber));
        }

        return _frames[frameNumber];
    }
}
