namespace Atelia.MultiSegmentStateStoreProbe.Model;

internal readonly record struct FileNumber {
    public FileNumber(uint value) {
        ArgumentOutOfRangeException.ThrowIfZero(value);
        Value = value;
    }

    public uint Value { get; }

    public FileNumber Next() => new(checked(Value + 1));

    public override string ToString() => Value.ToString(
        System.Globalization.CultureInfo.InvariantCulture);
}
