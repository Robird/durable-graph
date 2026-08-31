namespace Atelia.TwoLegRotationProbe.Model;

public sealed class RbfFileStore {
    private readonly List<RbfFile> _files = [];

    public int FileCount => _files.Count;

    internal RbfFile CreateFile() {
        uint fileNumber = checked((uint)_files.Count + 1);
        RbfFile file = new(fileNumber);
        _files.Add(file);
        return file;
    }

    internal (RbfFile File, FrameTicket Ticket) CreateFileWithFirstFrame(
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

    internal RbfFile GetFile(uint fileNumber) {
        if (fileNumber == 0 || fileNumber > _files.Count) {
            throw new KeyNotFoundException($"RBF file {fileNumber} does not exist.");
        }

        return _files[checked((int)fileNumber - 1)];
    }

    internal Frame ReadFrame(AbsoluteFrameAddress address) =>
        GetFile(address.FileNumber).Read(address.FrameTicket);

    internal RbfFrameLayoutEstimate ReadLayout(AbsoluteFrameAddress address) =>
        GetFile(address.FileNumber).ReadLayout(address.FrameTicket);

    /// <summary>
    /// Creates a volatile in-memory scratch fork for probe planning. Existing Frame
    /// instances, tickets, stored layouts, tails, and file numbering are preserved while
    /// later appends affect only the fork. This is not a durable or crash-safe snapshot.
    /// </summary>
    internal RbfFileStore ForkForProbe() {
        RbfFileStore fork = new();
        foreach (RbfFile file in _files) {
            fork._files.Add(file.ForkForProbe());
        }

        return fork;
    }
}
