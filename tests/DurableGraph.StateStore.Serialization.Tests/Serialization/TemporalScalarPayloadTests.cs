using System.Buffers;
using Atelia.DurableGraph.StateStore.Serialization;

namespace Atelia.DurableGraph.StateStore.Serialization.Tests;

public sealed class TemporalScalarPayloadTests {
    [Theory]
    [InlineData(0, new byte[] { 0 })]
    [InlineData(738944, new byte[] { 0x80, 0x8d, 0x2d })]
    [InlineData(3652058, new byte[] { 0xda, 0xf3, 0xde, 1 })]
    public void DateOnly_has_independent_day_number_golden(int dayNumber, byte[] golden) {
        DateOnly value = DateOnly.FromDayNumber(dayNumber);
        AssertUnmanaged(value);
        if (dayNumber == 738944) {
            Assert.Equal(new DateOnly(2024, 2, 29), value);
        }
        ArrayBufferWriter<byte> buffer = new();
        BinaryPayloadWriter writer = new(buffer);
        writer.WriteDateOnly(value);
        Assert.Equal(golden, buffer.WrittenSpan.ToArray());
        BinaryPayloadReader reader = new(golden);
        Assert.Equal(value, reader.ReadDateOnly());
        reader.EnsureFullyConsumed();
    }

    [Theory]
    [InlineData(0L, new byte[] { 0 })]
    [InlineData(1L, new byte[] { 1 })]
    [InlineData(863999999999L, new byte[] { 0xff, 0xff, 0xa6, 0xd3, 0x92, 0x19 })]
    public void TimeOnly_has_independent_unsigned_ticks_golden(long ticks, byte[] golden) {
        TimeOnly value = new(ticks);
        AssertUnmanaged(value);
        ArrayBufferWriter<byte> buffer = new();
        BinaryPayloadWriter writer = new(buffer);
        writer.WriteTimeOnly(value);
        Assert.Equal(golden, buffer.WrittenSpan.ToArray());
        BinaryPayloadReader reader = new(golden);
        Assert.Equal(value, reader.ReadTimeOnly());
        reader.EnsureFullyConsumed();
    }

    [Theory]
    [InlineData(0L, 0, new byte[] { 0, 0 })]
    [InlineData(600000000L, 1, new byte[] { 0x80, 0x8c, 0x8d, 0x9e, 2, 2 })]
    [InlineData(600000000L, -1, new byte[] { 0x80, 0x8c, 0x8d, 0x9e, 2, 1 })]
    [InlineData(504000000000L, 840, new byte[] { 0x80, 0xe0, 0xf6, 0xc5, 0xd5, 0x0e, 0x90, 0x0d })]
    [InlineData(504000000000L, -840, new byte[] { 0x80, 0xe0, 0xf6, 0xc5, 0xd5, 0x0e, 0x8f, 0x0d })]
    [InlineData(3155378975999999999L, 0, new byte[] { 0xff, 0xff, 0xdc, 0xa1, 0xdf, 0x8e, 0x8a, 0xe5, 0x2b, 0 })]
    public void DateTimeOffset_has_independent_clock_then_minute_offset_golden(long ticks, int minutes, byte[] golden) {
        DateTimeOffset value = new(ticks, TimeSpan.FromMinutes(minutes));
        AssertUnmanaged(value);
        ArrayBufferWriter<byte> buffer = new();
        BinaryPayloadWriter writer = new(buffer);
        writer.WriteDateTimeOffset(value);
        Assert.Equal(golden, buffer.WrittenSpan.ToArray());
        BinaryPayloadReader reader = new(golden);
        Assert.True(value.EqualsExact(reader.ReadDateTimeOffset()));
        reader.EnsureFullyConsumed();
    }

    [Fact]
    public void DateTimeOffset_preserves_legal_clock_and_utc_extrema_at_every_offset() {
        ArrayBufferWriter<byte> buffer = new();
        BinaryPayloadWriter writer = new(buffer);
        for (int minutes = -840; minutes <= 840; minutes++) {
            TimeSpan offset = TimeSpan.FromMinutes(minutes);
            long minimumClock = Math.Max(0, offset.Ticks);
            long maximumClock = DateTime.MaxValue.Ticks + Math.Min(0, offset.Ticks);
            foreach (long ticks in new[] { minimumClock, maximumClock }) {
                DateTimeOffset value = new(ticks, offset);
                buffer.Clear();
                writer.WriteDateTimeOffset(value);
                Assert.InRange(buffer.WrittenCount, 2, 11);
                BinaryPayloadReader reader = new(buffer.WrittenSpan);
                DateTimeOffset restored = reader.ReadDateTimeOffset();
                Assert.True(value.EqualsExact(restored));
                Assert.Equal(ticks, restored.Ticks);
                Assert.Equal(offset, restored.Offset);
                reader.EnsureFullyConsumed();
            }
        }
    }

    [Fact]
    public void Same_instant_across_days_keeps_distinct_clock_and_offset_bytes() {
        DateTimeOffset first = new(2026, 9, 10, 1, 0, 0, TimeSpan.FromHours(14));
        DateTimeOffset second = first.ToOffset(TimeSpan.FromHours(-14));
        Assert.Equal(first, second);
        Assert.NotEqual(first.Day, second.Day);
        Assert.False(first.EqualsExact(second));
        ArrayBufferWriter<byte> firstBuffer = new(), secondBuffer = new();
        BinaryPayloadWriter firstWriter = new(firstBuffer), secondWriter = new(secondBuffer);
        firstWriter.WriteDateTimeOffset(first);
        secondWriter.WriteDateTimeOffset(second);
        Assert.False(firstBuffer.WrittenSpan.SequenceEqual(secondBuffer.WrittenSpan));
        BinaryPayloadReader firstReader = new(firstBuffer.WrittenSpan), secondReader = new(secondBuffer.WrittenSpan);
        Assert.True(first.EqualsExact(firstReader.ReadDateTimeOffset()));
        Assert.True(second.EqualsExact(secondReader.ReadDateTimeOffset()));
    }

    [Theory]
    [InlineData(3652059u)]
    [InlineData(uint.MaxValue)]
    public void DateOnly_rejects_calendar_overflow_without_advancing(uint dayNumber) {
        ArrayBufferWriter<byte> buffer = new();
        BinaryPayloadWriter writer = new(buffer);
        writer.WriteUInt32(dayNumber);
        Assert.IsType<InvalidDataException>(ReadFailure(buffer.WrittenSpan.ToArray(), ScalarKind.DateOnly));
    }

    [Theory]
    [InlineData(864000000000UL)]
    [InlineData(9223372036854775808UL)]
    [InlineData(ulong.MaxValue)]
    public void TimeOnly_rejects_day_and_integer_overflow_without_advancing(ulong ticks) {
        ArrayBufferWriter<byte> buffer = new();
        BinaryPayloadWriter writer = new(buffer);
        writer.WriteUInt64(ticks);
        Assert.IsType<InvalidDataException>(ReadFailure(buffer.WrittenSpan.ToArray(), ScalarKind.TimeOnly));
    }

    [Theory]
    [InlineData(864000000000UL, 841)]
    [InlineData(864000000000UL, -841)]
    [InlineData(864000000000UL, int.MinValue)]
    [InlineData(864000000000UL, int.MaxValue)]
    [InlineData(0UL, 840)]
    [InlineData(3155378975999999999UL, -840)]
    [InlineData(3155378976000000000UL, 0)]
    [InlineData(9223372036854775808UL, 0)]
    [InlineData(ulong.MaxValue, 0)]
    public void DateTimeOffset_rejects_offset_clock_and_utc_overflow_without_advancing(ulong clockTicks, int minutes) {
        ArrayBufferWriter<byte> buffer = new();
        BinaryPayloadWriter writer = new(buffer);
        writer.WriteUInt64(clockTicks);
        writer.WriteInt32(minutes);
        Assert.IsType<InvalidDataException>(ReadFailure(buffer.WrittenSpan.ToArray(), ScalarKind.DateTimeOffset));
    }

    [Theory]
    [InlineData(new byte[] { 0x80, 0 })]
    [InlineData(new byte[] { 0x81, 0 })]
    public void All_temporal_readers_reject_noncanonical_first_varint_without_advancing(byte[] bytes) {
        foreach (ScalarKind kind in Enum.GetValues<ScalarKind>()) {
            Assert.IsType<InvalidDataException>(ReadFailure(bytes, kind));
        }
    }

    [Fact]
    public void All_temporal_readers_reject_overwide_and_overflowed_first_varints() {
        byte[] uintOverflow = [0xff, 0xff, 0xff, 0xff, 0x10];
        byte[] uintOverwide = [0x80, 0x80, 0x80, 0x80, 0x80];
        Assert.IsType<InvalidDataException>(ReadFailure(uintOverflow, ScalarKind.DateOnly));
        Assert.IsType<InvalidDataException>(ReadFailure(uintOverwide, ScalarKind.DateOnly));
        foreach (ScalarKind kind in new[] { ScalarKind.TimeOnly, ScalarKind.DateTimeOffset }) {
            Assert.IsType<InvalidDataException>(ReadFailure([0xff, 0xff, 0xff, 0xff, 0xff, 0xff, 0xff, 0xff, 0xff, 2], kind));
            Assert.IsType<InvalidDataException>(ReadFailure([0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80], kind));
        }
    }

    [Theory]
    [InlineData(new byte[] { 0, 0x80, 0 })]
    [InlineData(new byte[] { 0, 0x81, 0 })]
    [InlineData(new byte[] { 0, 0xff, 0xff, 0xff, 0xff, 0x10 })]
    [InlineData(new byte[] { 0, 0x80, 0x80, 0x80, 0x80, 0x80 })]
    public void DateTimeOffset_rejects_malformed_offset_without_consuming_valid_clock(byte[] bytes) =>
        Assert.IsType<InvalidDataException>(ReadFailure(bytes, ScalarKind.DateTimeOffset));

    [Fact]
    public void Temporal_readers_reject_every_truncation_without_advancing() {
        (ScalarKind Kind, byte[] Bytes)[] cases = [
            (ScalarKind.DateOnly, [0xda, 0xf3, 0xde, 1]),
            (ScalarKind.TimeOnly, [0xff, 0xff, 0xa6, 0xd3, 0x92, 0x19]),
            (ScalarKind.DateTimeOffset, [0x80, 0xe0, 0xf6, 0xc5, 0xd5, 0x0e, 0x90, 0x0d]),
        ];
        foreach (var item in cases) {
            for (int length = 0; length < item.Bytes.Length; length++) {
                Assert.IsType<EndOfStreamException>(ReadFailure(item.Bytes[..length], item.Kind));
            }
        }
    }

    [Fact]
    public void Nested_readers_leave_following_values_and_owner_checks_final_boundary() {
        DateOnly date = new(2024, 2, 29);
        TimeOnly time = TimeOnly.MaxValue;
        DateTimeOffset timestamp = new(504000000000, TimeSpan.FromHours(14));
        ArrayBufferWriter<byte> buffer = new();
        BinaryPayloadWriter writer = new(buffer);
        writer.WriteDateOnly(date);
        writer.WriteTimeOnly(time);
        writer.WriteDateTimeOffset(timestamp);
        writer.WriteByte(0xab);
        BinaryPayloadReader reader = new(buffer.WrittenSpan);
        Assert.Equal(date, reader.ReadDateOnly());
        Assert.Equal(3, reader.ConsumedCount);
        Assert.Equal(time, reader.ReadTimeOnly());
        Assert.Equal(9, reader.ConsumedCount);
        Assert.True(timestamp.EqualsExact(reader.ReadDateTimeOffset()));
        Assert.Equal(17, reader.ConsumedCount);
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

    private enum ScalarKind { DateOnly, TimeOnly, DateTimeOffset }

    private static Exception? ReadFailure(byte[] bytes, ScalarKind kind) {
        // A previous field has already consumed its byte: failure must preserve that exact cursor.
        byte[] payload = [0x42, .. bytes];
        BinaryPayloadReader reader = new(payload);
        Assert.Equal(0x42, reader.ReadByte());
        Exception? exception = null;
        try {
            switch (kind) {
                case ScalarKind.DateOnly: reader.ReadDateOnly(); break;
                case ScalarKind.TimeOnly: reader.ReadTimeOnly(); break;
                case ScalarKind.DateTimeOffset: reader.ReadDateTimeOffset(); break;
            }
        } catch (Exception current) {
            exception = current;
        }
        Assert.Equal(1, reader.ConsumedCount);
        Assert.Equal(bytes.Length, reader.RemainingCount);
        return exception;
    }
}
