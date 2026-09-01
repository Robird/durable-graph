using Atelia.MultiSegmentStateStoreProbe.Encoding;

namespace Atelia.MultiSegmentStateStoreProbe.Tests;

public sealed class CanonicalUnsignedBase128Tests {
    [Theory]
    [InlineData(0u, 1)]
    [InlineData(127u, 1)]
    [InlineData(128u, 2)]
    [InlineData(65_535u, 3)]
    [InlineData(65_536u, 3)]
    [InlineData(uint.MaxValue, 5)]
    public void UInt32_round_trips_canonically(uint value, int expectedWidth) {
        Span<byte> destination = stackalloc byte[
            CanonicalUnsignedBase128.MaxUInt32Bytes];

        int written = CanonicalUnsignedBase128.WriteUInt32(destination, value);
        uint roundTrip = CanonicalUnsignedBase128.ReadUInt32(
            destination[..written],
            out int consumed);

        Assert.Equal(expectedWidth, written);
        Assert.Equal(written, consumed);
        Assert.Equal(value, roundTrip);
    }

    [Fact]
    public void Overlong_zero_is_rejected() {
        byte[] overlong = [0x80, 0x00];

        Assert.Throws<InvalidDataException>(() =>
            CanonicalUnsignedBase128.ReadUInt32(overlong, out _));
    }

    [Fact]
    public void UInt32_overflow_is_rejected() {
        byte[] overflow = [0xff, 0xff, 0xff, 0xff, 0x10];

        Assert.Throws<InvalidDataException>(() =>
            CanonicalUnsignedBase128.ReadUInt32(overflow, out _));
    }

    [Fact]
    public void Truncated_value_is_rejected() {
        byte[] truncated = [0x80];

        Assert.Throws<EndOfStreamException>(() =>
            CanonicalUnsignedBase128.ReadUInt32(truncated, out _));
    }
}
