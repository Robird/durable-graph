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

    public AbsoluteFrameAddress Resolve(RelativeFrameTicket ticket) {
        uint targetFileNumber;
        if (ticket.IsPreviousFile) {
            targetFileNumber = PreviousFileNumber
                ?? throw new InvalidOperationException(
                    $"RBF file {CurrentFileNumber} has no previous file.");
        } else {
            targetFileNumber = CurrentFileNumber;
        }

        return new AbsoluteFrameAddress(targetFileNumber, ticket.FrameTicket);
    }

    public Frame ReadFrame(RbfFileStore store, RelativeFrameTicket ticket) {
        ArgumentNullException.ThrowIfNull(store);
        return store.ReadFrame(Resolve(ticket));
    }
}
