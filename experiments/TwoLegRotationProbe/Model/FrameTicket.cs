namespace Atelia.TwoLegRotationProbe.Model;

/// <summary>Probe 中用于从单个 RbfFile 读取 Frame 的票据。后续产品项目中对应的类型是Atelia.Data.SizedPtr。</summary>
internal readonly record struct FrameTicket {
    public FrameTicket(int value) {
        ArgumentOutOfRangeException.ThrowIfNegative(value);
        Value = value;
    }

    public int Value { get; }
}
