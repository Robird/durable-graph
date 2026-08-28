namespace Atelia.TwoLegRotationProbe.Model;

/// <summary>Probe 中用于从单个 RbfFile 读取 Frame 的票据。后续产品项目中对应的类型是 Atelia.Data.SizedPtr。</summary>
internal readonly record struct FrameTicket {
    public FrameTicket(long offsetBytes, int lengthBytes) {
        if (offsetBytes < RbfV040Layout.HeaderFenceBytes ||
            offsetBytes > RbfV040Layout.MaxNativeFrameStartOffsetBytes ||
            (offsetBytes & RbfV040Layout.AlignmentMask) != 0) {
            throw new ArgumentOutOfRangeException(
                nameof(offsetBytes),
                offsetBytes,
                $"Frame start must be 4B-aligned and in [{RbfV040Layout.HeaderFenceBytes}, {RbfV040Layout.MaxNativeFrameStartOffsetBytes}].");
        }

        if (lengthBytes < RbfV040Layout.MinFrameLengthBytes ||
            lengthBytes > RbfV040Layout.MaxFrameLengthBytes ||
            (lengthBytes & RbfV040Layout.AlignmentMask) != 0) {
            throw new ArgumentOutOfRangeException(
                nameof(lengthBytes),
                lengthBytes,
                $"Frame length must be 4B-aligned and in [{RbfV040Layout.MinFrameLengthBytes}, {RbfV040Layout.MaxFrameLengthBytes}].");
        }

        OffsetBytes = offsetBytes;
        LengthBytes = lengthBytes;
    }

    public long OffsetBytes { get; }

    public int LengthBytes { get; }

    public long EndOffsetExclusive => checked(OffsetBytes + LengthBytes);
}
