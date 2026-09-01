using Atelia.MultiSegmentStateStoreProbe.Encoding;
using Atelia.MultiSegmentStateStoreProbe.Model;

namespace Atelia.MultiSegmentStateStoreProbe.Tests;

public sealed class BackwardFrameReferenceWireCodecTests {
    [Theory]
    [InlineData(0u, 1ul)]
    [InlineData(1u, 127ul)]
    [InlineData(65_536u, 128ul)]
    [InlineData(uint.MaxValue - 1u, ulong.MaxValue)]
    public void Required_reference_round_trips(
        uint distance,
        ulong frameTicketCode) {
        BackwardFrameReference value = new(distance, frameTicketCode);
        Span<byte> destination = stackalloc byte[
            CanonicalUnsignedBase128.MaxUInt32Bytes +
            CanonicalUnsignedBase128.MaxUInt64Bytes];

        int written = BackwardFrameReferenceWireCodec.Write(destination, value);
        BackwardFrameReference roundTrip = BackwardFrameReferenceWireCodec.Read(
            destination[..written],
            out int consumed);

        Assert.Equal(written, consumed);
        Assert.Equal(value, roundTrip);
    }

    [Fact]
    public void Required_reference_rejects_zero_ticket_on_read() {
        byte[] encoded = [0x00, 0x00];

        Assert.Throws<InvalidDataException>(() =>
            BackwardFrameReferenceWireCodec.Read(encoded, out _));
    }
}
