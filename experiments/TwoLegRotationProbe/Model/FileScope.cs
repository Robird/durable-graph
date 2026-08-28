namespace Atelia.TwoLegRotationProbe.Model;

internal sealed class FileScope {
    public FileScope(uint currentFileNumber) {
        ArgumentOutOfRangeException.ThrowIfZero(currentFileNumber);
        CurrentFileNumber = currentFileNumber;
    }

    public readonly uint CurrentFileNumber;

    public uint? PreviousFileNumber => CurrentFileNumber > 1
        ? CurrentFileNumber - 1
        : null;

    public Frame ReadFrame(RbfFileStore store, RelativeFrameTicket ticket) {
        ArgumentNullException.ThrowIfNull(store);

        uint targetFileNumber;
        if (ticket.IsPreviousFile) {
            targetFileNumber = PreviousFileNumber
                ?? throw new InvalidOperationException(
                    $"RBF file {CurrentFileNumber} has no previous file.");
        } else {
            targetFileNumber = CurrentFileNumber;
        }

        return store.GetFile(targetFileNumber).Read(ticket.FrameTicket);
    }
}
