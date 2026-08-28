namespace Atelia.TwoLegRotationProbe.Model;

internal readonly record struct FrameId {
    public FrameId(int value) {
        ArgumentOutOfRangeException.ThrowIfNegative(value);
        Value = value;
    }

    public int Value { get; }
}
