using Atelia.MultiSegmentStateStoreProbe.Model;
using Atelia.MultiSegmentStateStoreProbe.Storage;

namespace Atelia.MultiSegmentStateStoreProbe.Tests;

public sealed class ProvisionalFrameEnvelopeTests {
    [Fact]
    public void Empty_frame_has_one_exact_byte_range_ticket() {
        ProvisionalFrameLayout layout = ProvisionalFrameEnvelopeEstimator.Estimate(
            ProvisionalFrameEnvelopeEstimator.InitialTailOffsetBytes,
            payloadLengthBytes: 0,
            tailMetadataLengthBytes: 0);

        Assert.Equal(new FrameTicket(4, 24), layout.Ticket);
        Assert.Equal(28, layout.AppendLengthBytes);
        Assert.Equal(32, layout.TailOffsetAfterBytes);
    }

    [Fact]
    public void Payload_and_metadata_use_one_aligned_envelope_estimate() {
        ProvisionalFrameLayout layout = ProvisionalFrameEnvelopeEstimator.Estimate(
            frameStartOffsetBytes: 4,
            payloadLengthBytes: 10,
            tailMetadataLengthBytes: 5);

        Assert.Equal(1, layout.PaddingLengthBytes);
        Assert.Equal(40, layout.FrameLengthBytes);
        Assert.Equal(44, layout.AppendLengthBytes);
        Assert.Equal(48, layout.TailOffsetAfterBytes);
    }

    [Fact]
    public void Hard_envelope_overflow_has_a_typed_rejection() {
        FrameCapacityException exception = Assert.Throws<FrameCapacityException>(() =>
            ProvisionalFrameEnvelopeEstimator.Estimate(
                frameStartOffsetBytes: 4,
                payloadLengthBytes:
                    ProvisionalFrameEnvelopeEstimator.MaxPayloadAndMetadataLengthBytes,
                tailMetadataLengthBytes: 1));

        Assert.Equal(
            new FrameCapacityRejection(
                FrameCapacityLimit.PayloadAndMetadataLength,
                ProvisionalFrameEnvelopeEstimator.MaxPayloadAndMetadataLengthBytes + 1L,
                ProvisionalFrameEnvelopeEstimator.MaxPayloadAndMetadataLengthBytes),
            exception.Rejection);
    }

    [Fact]
    public void Long_arithmetic_overflow_is_saturated_into_a_typed_rejection() {
        FrameCapacityException exception = Assert.Throws<FrameCapacityException>(() =>
            ProvisionalFrameEnvelopeEstimator.Estimate(
                frameStartOffsetBytes: 4,
                payloadLengthBytes: long.MaxValue,
                tailMetadataLengthBytes: 1));

        Assert.Equal(
            new FrameCapacityRejection(
                FrameCapacityLimit.PayloadAndMetadataLength,
                long.MaxValue,
                ProvisionalFrameEnvelopeEstimator.MaxPayloadAndMetadataLengthBytes),
            exception.Rejection);
    }

    [Theory]
    [InlineData(3L, 24)]
    [InlineData(5L, 24)]
    [InlineData(4L, 23)]
    [InlineData(4L, 25)]
    public void Strong_frame_ticket_rejects_out_of_envelope_or_unaligned_ranges(
        long offsetBytes,
        int lengthBytes) {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new FrameTicket(offsetBytes, lengthBytes));
    }
}
