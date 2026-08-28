namespace Atelia.TwoLegRotationProbe.Model;

internal sealed class FileScope {
    public FileScope(uint currentFileNumber) {
        ArgumentOutOfRangeException.ThrowIfZero(currentFileNumber);
        CurrentFileNumber = currentFileNumber;
    }

    public uint CurrentFileNumber;

    public uint? PreviousFileNumber => CurrentFileNumber > 1
        ? CurrentFileNumber - 1
        : null;
}
