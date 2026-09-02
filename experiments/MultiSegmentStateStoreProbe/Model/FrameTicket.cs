namespace Atelia.MultiSegmentStateStoreProbe.Model;

using Atelia.MultiSegmentStateStoreProbe.Storage;

/// <summary>
/// Probe-local byte-range identity for one complete Frame. This is deliberately not the
/// product <c>SizedPtr</c> wire representation.
/// </summary>
internal readonly record struct FrameTicket {
    public FrameTicket(long offsetBytes, int lengthBytes) {
        if (offsetBytes < ProvisionalFrameEnvelopeEstimator.InitialTailOffsetBytes ||
            offsetBytes > ProvisionalFrameEnvelopeEstimator.MaxFrameStartOffsetBytes ||
            (offsetBytes & ProvisionalFrameEnvelopeEstimator.AlignmentMask) != 0) {
            throw new ArgumentOutOfRangeException(nameof(offsetBytes));
        }

        if (lengthBytes < ProvisionalFrameEnvelopeEstimator.FrameFixedBytes ||
            lengthBytes > ProvisionalFrameEnvelopeEstimator.MaxFrameLengthBytes ||
            (lengthBytes & ProvisionalFrameEnvelopeEstimator.AlignmentMask) != 0) {
            throw new ArgumentOutOfRangeException(nameof(lengthBytes));
        }

        _ = checked(offsetBytes + lengthBytes);
        OffsetBytes = offsetBytes;
        LengthBytes = lengthBytes;
    }

    public long OffsetBytes { get; }

    public int LengthBytes { get; }

    public long EndOffsetExclusive => checked(OffsetBytes + LengthBytes);

    public bool IsValid =>
        OffsetBytes >= ProvisionalFrameEnvelopeEstimator.InitialTailOffsetBytes &&
        OffsetBytes <= ProvisionalFrameEnvelopeEstimator.MaxFrameStartOffsetBytes &&
        (OffsetBytes & ProvisionalFrameEnvelopeEstimator.AlignmentMask) == 0 &&
        LengthBytes >= ProvisionalFrameEnvelopeEstimator.FrameFixedBytes &&
        LengthBytes <= ProvisionalFrameEnvelopeEstimator.MaxFrameLengthBytes &&
        (LengthBytes & ProvisionalFrameEnvelopeEstimator.AlignmentMask) == 0;
}
