namespace Atelia.TwoLegRotationProbe.Model;

/// <summary>
/// TwoLegRotationProbe 使用的 RBF v0.40 外壳尺寸契约。
/// 这里只建模 Frame 外壳、Payload、TailMeta、Padding 和后置 Fence，不建模 DurableGraph 自身的 Payload 编码。
/// </summary>
internal static class RbfV040Layout {
    public const int AlignmentBytes = 4;
    public const int AlignmentMask = AlignmentBytes - 1;
    public const int HeaderFenceBytes = 4;
    public const long InitialTailOffsetBytes = HeaderFenceBytes;
    public const int FrameFixedBytes = 24;
    public const int TrailingFenceBytes = 4;
    public const int MinFrameLengthBytes = FrameFixedBytes;
    public const int MaxFrameLengthBytes = 268_435_452;
    public const int MaxPayloadAndTailMetaLengthBytes = 268_435_428;
    public const int MaxTailMetaLengthBytes = 65_535;
    public const long MaxNativeFrameStartOffsetBytes = 1_099_511_627_772;
    public const long MaxDurableGraphRelativeFrameStartOffsetBytes = 549_755_813_884;

    public static RbfFrameLayoutEstimate Estimate(
        long frameStartOffsetBytes,
        int payloadLengthBytes,
        int tailMetaLengthBytes) {
        ValidateFrameStart(frameStartOffsetBytes);
        ArgumentOutOfRangeException.ThrowIfNegative(payloadLengthBytes);

        if ((uint)tailMetaLengthBytes > MaxTailMetaLengthBytes) {
            throw new ArgumentOutOfRangeException(
                nameof(tailMetaLengthBytes),
                tailMetaLengthBytes,
                $"TailMeta length must be in [0, {MaxTailMetaLengthBytes}].");
        }

        long payloadAndTailMetaBytes = checked((long)payloadLengthBytes + tailMetaLengthBytes);
        if (payloadAndTailMetaBytes > MaxPayloadAndTailMetaLengthBytes) {
            throw new ArgumentOutOfRangeException(
                nameof(payloadLengthBytes),
                payloadLengthBytes,
                $"Payload and TailMeta total exceeds {MaxPayloadAndTailMetaLengthBytes} bytes.");
        }

        int paddingBytes = (int)((-payloadAndTailMetaBytes) & AlignmentMask);
        int frameLengthBytes = checked((int)(
            FrameFixedBytes + payloadAndTailMetaBytes + paddingBytes));
        int appendLengthBytes = checked(frameLengthBytes + TrailingFenceBytes);
        long tailOffsetAfterBytes = checked(frameStartOffsetBytes + appendLengthBytes);

        return new RbfFrameLayoutEstimate(
            frameStartOffsetBytes,
            payloadLengthBytes,
            tailMetaLengthBytes,
            paddingBytes,
            frameLengthBytes,
            appendLengthBytes,
            tailOffsetAfterBytes);
    }

    public static bool IsDurableGraphRelativeStartRepresentable(FrameTicket ticket) =>
        IsValidFrameStart(ticket.OffsetBytes) &&
        ticket.LengthBytes >= MinFrameLengthBytes &&
        ticket.LengthBytes <= MaxFrameLengthBytes &&
        (ticket.LengthBytes & AlignmentMask) == 0 &&
        ticket.OffsetBytes <= MaxDurableGraphRelativeFrameStartOffsetBytes;

    private static void ValidateFrameStart(long frameStartOffsetBytes) {
        if (!IsValidFrameStart(frameStartOffsetBytes)) {
            throw new ArgumentOutOfRangeException(
                nameof(frameStartOffsetBytes),
                frameStartOffsetBytes,
                $"Frame start must be 4B-aligned and in [{HeaderFenceBytes}, {MaxNativeFrameStartOffsetBytes}].");
        }
    }

    private static bool IsValidFrameStart(long frameStartOffsetBytes) =>
        frameStartOffsetBytes >= HeaderFenceBytes &&
        frameStartOffsetBytes <= MaxNativeFrameStartOffsetBytes &&
        (frameStartOffsetBytes & AlignmentMask) == 0;
}
