namespace Atelia.MultiSegmentStateStoreProbe.Storage;

/// <summary>
/// Sole size authority for the probe-local Frame envelope. Constants intentionally model
/// the useful RBF v0.40 constraints without freezing a product wire format.
/// </summary>
internal static class ProvisionalFrameEnvelopeEstimator {
    public const int AlignmentBytes = 4;
    public const int AlignmentMask = AlignmentBytes - 1;
    public const long InitialTailOffsetBytes = 4;
    public const int FrameFixedBytes = 24;
    public const int TrailingFenceBytes = 4;
    public const int MaxTailMetadataLengthBytes = 65_535;
    public const int MaxPayloadAndMetadataLengthBytes = 268_435_428;
    public const int MaxFrameLengthBytes = 268_435_452;
    public const long MaxFrameStartOffsetBytes = 1_099_511_627_772;

    public static ProvisionalFrameLayout Estimate(
        long frameStartOffsetBytes,
        long payloadLengthBytes,
        int tailMetadataLengthBytes) {
        if (frameStartOffsetBytes < InitialTailOffsetBytes ||
            frameStartOffsetBytes > MaxFrameStartOffsetBytes ||
            (frameStartOffsetBytes & AlignmentMask) != 0) {
            throw new FrameCapacityException(new FrameCapacityRejection(
                FrameCapacityLimit.FrameStart,
                frameStartOffsetBytes,
                MaxFrameStartOffsetBytes));
        }

        ArgumentOutOfRangeException.ThrowIfNegative(payloadLengthBytes);
        if ((uint)tailMetadataLengthBytes > MaxTailMetadataLengthBytes) {
            throw new FrameCapacityException(new FrameCapacityRejection(
                FrameCapacityLimit.TailMetadataLength,
                tailMetadataLengthBytes,
                MaxTailMetadataLengthBytes));
        }

        long payloadAndMetadataLength = payloadLengthBytes >
            long.MaxValue - tailMetadataLengthBytes
            ? long.MaxValue
            : payloadLengthBytes + tailMetadataLengthBytes;
        if (payloadAndMetadataLength > MaxPayloadAndMetadataLengthBytes) {
            throw new FrameCapacityException(new FrameCapacityRejection(
                FrameCapacityLimit.PayloadAndMetadataLength,
                payloadAndMetadataLength,
                MaxPayloadAndMetadataLengthBytes));
        }

        int paddingLengthBytes = (int)(
            (-payloadAndMetadataLength) & AlignmentMask);
        int frameLengthBytes = checked((int)(
            FrameFixedBytes + payloadAndMetadataLength + paddingLengthBytes));
        int appendLengthBytes = checked(frameLengthBytes + TrailingFenceBytes);
        long tailOffsetAfterBytes = checked(
            frameStartOffsetBytes + appendLengthBytes);
        return new ProvisionalFrameLayout(
            frameStartOffsetBytes,
            checked((int)payloadLengthBytes),
            tailMetadataLengthBytes,
            paddingLengthBytes,
            frameLengthBytes,
            appendLengthBytes,
            tailOffsetAfterBytes);
    }
}
