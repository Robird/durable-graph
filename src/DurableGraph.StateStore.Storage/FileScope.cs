namespace Atelia.DurableGraph.StateStore.Storage;

/// <summary>
/// Converts between absolute file numbers and backward distances relative to one
/// containing file.
/// </summary>
public sealed class FileScope {
    public FileScope(uint currentFileNumber) {
        ArgumentOutOfRangeException.ThrowIfZero(currentFileNumber);
        CurrentFileNumber = currentFileNumber;
    }

    public uint CurrentFileNumber { get; }

    public uint ToAbsoluteFileNumber(uint backwardFileDistance) {
        if (backwardFileDistance >= CurrentFileNumber) {
            throw new InvalidDataException(
                $"Backward file distance {backwardFileDistance} underflows " +
                $"1-based current file {CurrentFileNumber}.");
        }

        return CurrentFileNumber - backwardFileDistance;
    }

    public uint ToBackwardFileDistance(uint absoluteFileNumber) {
        ArgumentOutOfRangeException.ThrowIfZero(absoluteFileNumber);
        if (absoluteFileNumber > CurrentFileNumber) {
            throw new InvalidDataException(
                $"Future file {absoluteFileNumber} cannot be referenced from " +
                $"current file {CurrentFileNumber}.");
        }

        return CurrentFileNumber - absoluteFileNumber;
    }
}
