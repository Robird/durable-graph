using System.Buffers;
using Atelia.DurableGraph.Serialization;

namespace Atelia.DurableGraph.Serialization.Tests;

public sealed class BclScalarPayloadTests {
    [Fact]
    public void Guid_has_independent_big_endian_golden_and_preserves_arbitrary_bits() {
        Guid value = new("00112233-4455-6677-8899-aabbccddeeff");
        byte[] golden = [0x00, 0x11, 0x22, 0x33, 0x44, 0x55, 0x66, 0x77, 0x88, 0x99, 0xaa, 0xbb, 0xcc, 0xdd, 0xee, 0xff];
        ArrayBufferWriter<byte> buffer = new();
        BinaryPayloadWriter writer = new(buffer);
        writer.WriteGuid(value);
        Assert.Equal(golden, buffer.WrittenSpan.ToArray());
        BinaryPayloadReader goldenReader = new(golden);
        Assert.Equal(value, goldenReader.ReadGuid());
        goldenReader.EnsureFullyConsumed();

        AssertUnmanaged(value);
        foreach (Guid candidate in new[] { Guid.Empty, new Guid("ffffffff-ffff-ffff-ffff-ffffffffffff"), new Guid("01234567-89ab-cdef-0123-456789abcdef") }) {
            buffer.Clear();
            writer.WriteGuid(candidate);
            Assert.Equal(16, buffer.WrittenCount);
            BinaryPayloadReader reader = new(buffer.WrittenSpan);
            Assert.Equal(candidate, reader.ReadGuid());
            reader.EnsureFullyConsumed();
        }
    }

    [Fact]
    public void Decimal_has_independent_four_word_little_endian_golden() {
        decimal value = new(0x12345678, unchecked((int)0x90abcdef), 0x11223344, true, 28);
        byte[] golden = [0x78, 0x56, 0x34, 0x12, 0xef, 0xcd, 0xab, 0x90, 0x44, 0x33, 0x22, 0x11, 0, 0, 0x1c, 0x80];
        ArrayBufferWriter<byte> buffer = new();
        BinaryPayloadWriter writer = new(buffer);
        writer.WriteDecimal(value);
        writer.WriteDecimal(1.0m);
        Assert.Equal(golden, buffer.WrittenSpan[..16].ToArray());
        Assert.Equal(new byte[] { 10, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 1, 0 }, buffer.WrittenSpan[16..].ToArray());

        BinaryPayloadReader reader = new(golden);
        Assert.Equal(new[] { 0x12345678, unchecked((int)0x90abcdef), 0x11223344, unchecked((int)0x801c0000) }, decimal.GetBits(reader.ReadDecimal()));
        reader.EnsureFullyConsumed();
        AssertUnmanaged(value);
    }

    [Fact]
    public void Decimal_round_trip_preserves_every_scale_and_both_zero_signs() {
        ArrayBufferWriter<byte> buffer = new();
        BinaryPayloadWriter writer = new(buffer);
        foreach (bool negative in new[] { false, true }) {
            for (byte scale = 0; scale <= 28; scale++) {
                foreach (int coefficient in new[] { 0, 1, -1 }) {
                    decimal value = new(coefficient, coefficient, coefficient, negative, scale);
                    buffer.Clear();
                    writer.WriteDecimal(value);
                    BinaryPayloadReader reader = new(buffer.WrittenSpan);
                    decimal restored = reader.ReadDecimal();
                    Assert.Equal(decimal.GetBits(value), decimal.GetBits(restored));
                    Assert.True(ScalarStateEquality.DecimalEquals(in value, in restored));
                    reader.EnsureFullyConsumed();
                }
            }
        }
    }

    [Fact]
    public void Decimal_persistent_equality_distinguishes_scale_sign_and_each_coefficient_word() {
        decimal one = 1m, onePointZero = 1.0m, onePointZeroZero = 1.00m;
        decimal zero = 0m, negativeZero = new(0, 0, 0, true, 0), scaledZero = new(0, 0, 0, false, 28);
        Assert.Equal(one, onePointZero);
        Assert.Equal(onePointZero, onePointZeroZero);
        Assert.Equal(zero, negativeZero);
        Assert.Equal(zero, scaledZero);
        Assert.False(ScalarStateEquality.DecimalEquals(in one, in onePointZero));
        Assert.False(ScalarStateEquality.DecimalEquals(in onePointZero, in onePointZeroZero));
        Assert.False(ScalarStateEquality.DecimalEquals(in zero, in negativeZero));
        Assert.False(ScalarStateEquality.DecimalEquals(in zero, in scaledZero));
        foreach (decimal value in new[] { new decimal(1, 0, 0, false, 0), new decimal(0, 1, 0, false, 0), new decimal(0, 0, 1, false, 0) }) {
            Assert.False(ScalarStateEquality.DecimalEquals(in zero, in value));
            Assert.True(ScalarStateEquality.DecimalEquals(in value, in value));
        }
    }

    [Theory]
    [InlineData(0x00000001u)]
    [InlineData(0x00008000u)]
    [InlineData(0x01000000u)]
    [InlineData(0x40000000u)]
    [InlineData(0x001d0000u)]
    [InlineData(0x00ff0000u)]
    [InlineData(0x801d0000u)]
    public void Decimal_rejects_invalid_flags_without_advancing(uint flags) {
        byte[] bytes = new byte[16];
        bytes[12] = (byte)flags;
        bytes[13] = (byte)(flags >> 8);
        bytes[14] = (byte)(flags >> 16);
        bytes[15] = (byte)(flags >> 24);
        Assert.IsType<InvalidDataException>(ReadFailure(bytes, ScalarKind.Decimal));
    }

    [Fact]
    public void Fixed_scalars_reject_every_truncation_without_advancing() {
        for (int length = 0; length < 16; length++) {
            Assert.IsType<EndOfStreamException>(ReadFailure(new byte[length], ScalarKind.Guid));
            Assert.IsType<EndOfStreamException>(ReadFailure(new byte[length], ScalarKind.Decimal));
        }
    }

    [Theory]
    [InlineData(0L, new byte[] { 0 })]
    [InlineData(1L, new byte[] { 2 })]
    [InlineData(-1L, new byte[] { 1 })]
    [InlineData(long.MinValue, new byte[] { 0xff, 0xff, 0xff, 0xff, 0xff, 0xff, 0xff, 0xff, 0xff, 1 })]
    [InlineData(long.MaxValue, new byte[] { 0xfe, 0xff, 0xff, 0xff, 0xff, 0xff, 0xff, 0xff, 0xff, 1 })]
    public void TimeSpan_uses_signed_ticks_with_independent_golden(long ticks, byte[] golden) {
        TimeSpan value = new(ticks);
        AssertUnmanaged(value);
        ArrayBufferWriter<byte> buffer = new();
        BinaryPayloadWriter writer = new(buffer);
        writer.WriteTimeSpan(value);
        Assert.Equal(golden, buffer.WrittenSpan.ToArray());
        BinaryPayloadReader reader = new(golden);
        Assert.Equal(ticks, reader.ReadTimeSpan().Ticks);
        reader.EnsureFullyConsumed();
    }

    [Theory]
    [InlineData(new byte[] { 0x80, 0 })]
    [InlineData(new byte[] { 0x81, 0 })]
    [InlineData(new byte[] { 0xff, 0xff, 0xff, 0xff, 0xff, 0xff, 0xff, 0xff, 0xff, 2 })]
    [InlineData(new byte[] { 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80 })]
    public void TimeSpan_rejects_noncanonical_overflow_and_overwidth_without_advancing(byte[] bytes) =>
        Assert.IsType<InvalidDataException>(ReadFailure(bytes, ScalarKind.TimeSpan));

    [Fact]
    public void TimeSpan_rejects_truncated_ticks_without_advancing() {
        Assert.IsType<EndOfStreamException>(ReadFailure([], ScalarKind.TimeSpan));
        Assert.IsType<EndOfStreamException>(ReadFailure([0x80], ScalarKind.TimeSpan));
    }

    [Fact]
    public void Nested_scalar_readers_leave_following_values_and_full_body_rejects_trailing_bytes() {
        Guid guid = new("00112233-4455-6677-8899-aabbccddeeff");
        ArrayBufferWriter<byte> buffer = new();
        BinaryPayloadWriter writer = new(buffer);
        writer.WriteGuid(guid);
        writer.WriteDecimal(1.00m);
        writer.WriteTimeSpan(TimeSpan.MinValue);
        writer.WriteByte(0xab);
        BinaryPayloadReader reader = new(buffer.WrittenSpan);
        Assert.Equal(guid, reader.ReadGuid());
        Assert.Equal(16, reader.ConsumedCount);
        Assert.Equal(new[] { 100, 0, 0, 0x20000 }, decimal.GetBits(reader.ReadDecimal()));
        Assert.Equal(32, reader.ConsumedCount);
        Assert.Equal(TimeSpan.MinValue, reader.ReadTimeSpan());
        Assert.Equal(1, reader.RemainingCount);
        Exception? exception = null;
        try {
            reader.EnsureFullyConsumed();
        } catch (Exception current) {
            exception = current;
        }
        Assert.IsType<InvalidDataException>(exception);
        Assert.Equal(0xab, reader.ReadByte());
        reader.EnsureFullyConsumed();
    }

    private static void AssertUnmanaged<T>(T value) where T : unmanaged { }

    private enum ScalarKind { Guid, Decimal, TimeSpan }

    private static Exception? ReadFailure(byte[] bytes, ScalarKind kind) {
        BinaryPayloadReader reader = new(bytes);
        Exception? exception = null;
        try {
            switch (kind) {
                case ScalarKind.Guid: reader.ReadGuid(); break;
                case ScalarKind.Decimal: reader.ReadDecimal(); break;
                case ScalarKind.TimeSpan: reader.ReadTimeSpan(); break;
            }
        } catch (Exception current) {
            exception = current;
        }
        Assert.Equal(0, reader.ConsumedCount);
        Assert.Equal(bytes.Length, reader.RemainingCount);
        return exception;
    }
}
