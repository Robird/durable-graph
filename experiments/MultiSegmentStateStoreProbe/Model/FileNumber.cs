namespace Atelia.MultiSegmentStateStoreProbe.Model;

internal readonly record struct FileNumber {
    private readonly uint _value;

    public FileNumber(uint value) {
        ArgumentOutOfRangeException.ThrowIfZero(value);
        _value = value;
    }

    public uint Value => _value != 0
        ? _value
        : throw new InvalidDataException("A FileNumber must be 1-based; the default value is invalid.");

    public FileNumber Next() => new(checked(Value + 1));

    public override string ToString() => Value.ToString(
        System.Globalization.CultureInfo.InvariantCulture);
}
