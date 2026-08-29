namespace Atelia.TwoLegRotationProbe.Model;

internal sealed class RbfFileStore {
    private readonly List<RbfFile> _files = [];

    public int FileCount => _files.Count;

    public RbfFile CreateFile() {
        uint fileNumber = checked((uint)_files.Count + 1);
        RbfFile file = new(fileNumber);
        _files.Add(file);
        return file;
    }

    public (RbfFile File, FrameTicket Ticket) CreateFileWithFirstFrame(
        Frame frame,
        RbfFrameLayoutEstimate expectedLayout) {
        ArgumentNullException.ThrowIfNull(frame);

        uint fileNumber = checked((uint)_files.Count + 1);
        RbfFile candidate = new(fileNumber);
        if (expectedLayout.FrameStartOffsetBytes != candidate.TailOffsetBytes) {
            throw new InvalidDataException(
                $"The first frame in file {fileNumber} must start at " +
                $"{candidate.TailOffsetBytes}, not {expectedLayout.FrameStartOffsetBytes}.");
        }

        FrameTicket ticket = candidate.Append(
            frame,
            expectedLayout.PayloadLengthBytes,
            expectedLayout.TailMetaLengthBytes);
        if (ticket != expectedLayout.Ticket ||
            candidate.ReadLayout(ticket) != expectedLayout) {
            throw new InvalidDataException(
                "The appended first frame does not match its precomputed RBF layout.");
        }

        _files.Add(candidate);
        return (candidate, ticket);
    }

    public RbfFile GetFile(uint fileNumber) {
        if (fileNumber == 0 || fileNumber > _files.Count) {
            throw new KeyNotFoundException($"RBF file {fileNumber} does not exist.");
        }

        return _files[checked((int)fileNumber - 1)];
    }

    public Frame ReadFrame(AbsoluteFrameAddress address) =>
        GetFile(address.FileNumber).Read(address.FrameTicket);

    public RbfFrameLayoutEstimate ReadLayout(AbsoluteFrameAddress address) =>
        GetFile(address.FileNumber).ReadLayout(address.FrameTicket);
}
