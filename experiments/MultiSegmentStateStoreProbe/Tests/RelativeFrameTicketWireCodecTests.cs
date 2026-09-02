using Atelia.MultiSegmentStateStoreProbe.Encoding;
using Atelia.MultiSegmentStateStoreProbe.Model;

namespace Atelia.MultiSegmentStateStoreProbe.Tests;

public sealed class RelativeFrameTicketWireCodecTests {
    [Theory]
    [InlineData(0u, 4L, 24)]
    [InlineData(1u, 128L, 128)]
    [InlineData(65_536u, 128L, 16_384)]
    [InlineData(uint.MaxValue, 1_099_511_627_772L, 268_435_452)]
    public void Required_reference_round_trips(
        uint distance,
        long offsetBytes,
        int lengthBytes) {
        RelativeFrameTicket value = new(
            distance,
            new FrameTicket(offsetBytes, lengthBytes));
        Span<byte> destination = stackalloc byte[
            RelativeFrameTicketWireCodec.MaxEncodedBytes];

        int written = RelativeFrameTicketWireCodec.Write(destination, value);
        RelativeFrameTicket roundTrip = RelativeFrameTicketWireCodec.Read(
            destination[..written],
            out int consumed);

        Assert.Equal(RelativeFrameTicketWireCodec.GetEncodedLength(value), written);
        Assert.Equal(written, consumed);
        Assert.Equal(value, roundTrip);
    }

    [Theory]
    [InlineData(new byte[] { 0x00, 0x00, 0x18 })]
    [InlineData(new byte[] { 0x00, 0x04, 0x00 })]
    public void Required_reference_rejects_zero_ticket_components(byte[] encoded) {
        Assert.Throws<InvalidDataException>(() =>
            RelativeFrameTicketWireCodec.Read(encoded, out _));
    }

    [Fact]
    public void Default_required_reference_is_rejected_before_write() {
        byte[] destination = new byte[RelativeFrameTicketWireCodec.MaxEncodedBytes];

        Assert.Throws<InvalidDataException>(() =>
            RelativeFrameTicketWireCodec.Write(destination, default));
    }
}
