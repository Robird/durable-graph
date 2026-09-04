using System.Buffers;
using Atelia.DurableGraph.StateStore.Serialization;

namespace Atelia.DurableGraph.StateStore.Serialization.Tests;

public sealed class BinaryPayloadPrimitiveTests {
    [Fact]
    public void Compound_payload_has_stable_golden_bytes_and_round_trips() {
        Half half = BitConverter.UInt16BitsToHalf(0xfe01);
        float single = BitConverter.Int32BitsToSingle(unchecked((int)0xffc12345));
        double @double = BitConverter.Int64BitsToDouble(unchecked((long)0xfff8123456789abc));
        ArrayBufferWriter<byte> buffer = new();
        BinaryPayloadWriter writer = new(buffer);

        writer.WriteByte(0xab);
        writer.WriteSByte(-2);
        writer.WriteBoolean(false);
        writer.WriteBoolean(true);
        writer.WriteUInt16(300);
        writer.WriteUInt32(uint.MaxValue);
        writer.WriteUInt64(ulong.MaxValue);
        writer.WriteInt16(short.MinValue);
        writer.WriteInt32(-1);
        writer.WriteInt64(long.MaxValue);
        writer.WriteHalf(half);
        writer.WriteSingle(single);
        writer.WriteDouble(@double);
        writer.WriteSpan([0xaa, 0xbb]);
        writer.WriteCount(3);
        writer.WriteBytes([0xcc, 0xdd]);

        Assert.Equal(
            [
                0xab,
                0xfe,
                0x00,
                0x01,
                0xac, 0x02,
                0xff, 0xff, 0xff, 0xff, 0x0f,
                0xff, 0xff, 0xff, 0xff, 0xff, 0xff, 0xff, 0xff, 0xff, 0x01,
                0xff, 0xff, 0x03,
                0x01,
                0xfe, 0xff, 0xff, 0xff, 0xff, 0xff, 0xff, 0xff, 0xff, 0x01,
                0x01, 0xfe,
                0x45, 0x23, 0xc1, 0xff,
                0xbc, 0x9a, 0x78, 0x56, 0x34, 0x12, 0xf8, 0xff,
                0xaa, 0xbb,
                0x03,
                0x02, 0xcc, 0xdd,
            ],
            buffer.WrittenSpan.ToArray());

        BinaryPayloadReader reader = new(buffer.WrittenSpan);
        Assert.Equal(0, reader.ConsumedCount);
        Assert.Equal(buffer.WrittenCount, reader.RemainingCount);
        Assert.Equal(0xab, reader.ReadByte());
        Assert.Equal(-2, reader.ReadSByte());
        Assert.False(reader.ReadBoolean());
        Assert.True(reader.ReadBoolean());
        Assert.Equal(300, reader.ReadUInt16());
        Assert.Equal(uint.MaxValue, reader.ReadUInt32());
        Assert.Equal(ulong.MaxValue, reader.ReadUInt64());
        Assert.Equal(short.MinValue, reader.ReadInt16());
        Assert.Equal(-1, reader.ReadInt32());
        Assert.Equal(long.MaxValue, reader.ReadInt64());
        Assert.Equal(0xfe01, BitConverter.HalfToUInt16Bits(reader.ReadHalf()));
        Assert.Equal(
            unchecked((int)0xffc12345),
            BitConverter.SingleToInt32Bits(reader.ReadSingle()));
        Assert.Equal(
            unchecked((long)0xfff8123456789abc),
            BitConverter.DoubleToInt64Bits(reader.ReadDouble()));
        Assert.Equal([0xaa, 0xbb], reader.ReadSpan(2).ToArray());
        Assert.Equal(3, reader.ReadCount());
        Assert.Equal([0xcc, 0xdd], reader.ReadBytes().ToArray());
        Assert.True(reader.End);
        Assert.Equal(buffer.WrittenCount, reader.ConsumedCount);
        Assert.Equal(0, reader.RemainingCount);
        reader.EnsureFullyConsumed();
    }

    [Fact]
    public void Reader_accepts_independent_compound_golden() {
        BinaryPayloadReader reader = new([
            0x01,
            0xac, 0x02,
            0x01,
            0x00, 0x00, 0x00, 0x80,
            0xaa, 0xbb,
            0x03,
            0x02, 0xcc, 0xdd,
        ]);

        Assert.True(reader.ReadBoolean());
        Assert.Equal(300, reader.ReadUInt16());
        Assert.Equal(-1, reader.ReadInt32());
        Assert.Equal(
            unchecked((int)0x80000000),
            BitConverter.SingleToInt32Bits(reader.ReadSingle()));
        Assert.Equal([0xaa, 0xbb], reader.ReadSpan(2).ToArray());
        Assert.Equal(3, reader.ReadCount());
        Assert.Equal([0xcc, 0xdd], reader.ReadBytes().ToArray());
        Assert.True(reader.End);
        reader.EnsureFullyConsumed();
    }

    [Fact]
    public void Integral_boundaries_round_trip() {
        ArrayBufferWriter<byte> buffer = new();
        BinaryPayloadWriter writer = new(buffer);

        writer.WriteUInt16(0);
        writer.WriteUInt16(ushort.MaxValue);
        writer.WriteUInt32(0);
        writer.WriteUInt32(uint.MaxValue);
        writer.WriteUInt64(0);
        writer.WriteUInt64(ulong.MaxValue);
        writer.WriteInt16(short.MinValue);
        writer.WriteInt16(short.MaxValue);
        writer.WriteInt32(int.MinValue);
        writer.WriteInt32(int.MaxValue);
        writer.WriteInt64(long.MinValue);
        writer.WriteInt64(long.MaxValue);

        BinaryPayloadReader reader = new(buffer.WrittenSpan);
        Assert.Equal(0, reader.ReadUInt16());
        Assert.Equal(ushort.MaxValue, reader.ReadUInt16());
        Assert.Equal(0u, reader.ReadUInt32());
        Assert.Equal(uint.MaxValue, reader.ReadUInt32());
        Assert.Equal(0ul, reader.ReadUInt64());
        Assert.Equal(ulong.MaxValue, reader.ReadUInt64());
        Assert.Equal(short.MinValue, reader.ReadInt16());
        Assert.Equal(short.MaxValue, reader.ReadInt16());
        Assert.Equal(int.MinValue, reader.ReadInt32());
        Assert.Equal(int.MaxValue, reader.ReadInt32());
        Assert.Equal(long.MinValue, reader.ReadInt64());
        Assert.Equal(long.MaxValue, reader.ReadInt64());
        Assert.True(reader.End);
    }

    [Theory]
    [InlineData(0u, 1)]
    [InlineData(127u, 1)]
    [InlineData(128u, 2)]
    [InlineData(65_536u, 3)]
    [InlineData(uint.MaxValue, 5)]
    public void UInt32_uses_the_shortest_codeword_at_width_boundaries(
        uint value,
        int expectedWidth) {
        ArrayBufferWriter<byte> buffer = new();
        BinaryPayloadWriter writer = new(buffer);

        writer.WriteUInt32(value);

        Assert.Equal(expectedWidth, buffer.WrittenCount);
        BinaryPayloadReader reader = new(buffer.WrittenSpan);
        Assert.Equal(value, reader.ReadUInt32());
        Assert.True(reader.End);
    }

    [Fact]
    public void Integer_writers_request_only_their_maximum_codeword_width() {
        BoundedBufferWriter uint16Buffer = new(3);
        BoundedBufferWriter uint32Buffer = new(5);
        BinaryPayloadWriter uint16Writer = new(uint16Buffer);
        BinaryPayloadWriter uint32Writer = new(uint32Buffer);

        uint16Writer.WriteUInt16(ushort.MaxValue);
        uint32Writer.WriteUInt32(uint.MaxValue);

        Assert.Equal([0xff, 0xff, 0x03], uint16Buffer.WrittenSpan.ToArray());
        Assert.Equal(
            [0xff, 0xff, 0xff, 0xff, 0x0f],
            uint32Buffer.WrittenSpan.ToArray());
    }

    [Theory]
    [InlineData(new byte[] { 0x80, 0x00 })]
    [InlineData(new byte[] { 0x80, 0x80, 0x80, 0x80, 0x00 })]
    [InlineData(new byte[] { 0xff, 0xff, 0xff, 0xff, 0x10 })]
    [InlineData(new byte[] { 0x80, 0x80, 0x80, 0x80, 0x80 })]
    public void UInt32_rejects_noncanonical_or_overflow_input_without_advancing(
        byte[] source) {
        Exception exception = CaptureUInt32FailureAndAssertCursor(source);

        Assert.IsType<InvalidDataException>(exception);
    }

    [Fact]
    public void UInt32_rejects_truncated_input_without_advancing() {
        Exception exception = CaptureUInt32FailureAndAssertCursor([0x80]);

        Assert.IsType<EndOfStreamException>(exception);
    }

    [Fact]
    public void UInt64_rejects_max_width_overlong_and_overflow_without_advancing() {
        byte[] overlong = [
            0x80, 0x80, 0x80, 0x80, 0x80,
            0x80, 0x80, 0x80, 0x80, 0x00,
        ];
        byte[] overflow = [
            0xff, 0xff, 0xff, 0xff, 0xff,
            0xff, 0xff, 0xff, 0xff, 0x02,
        ];

        Assert.IsType<InvalidDataException>(
            CaptureUInt64FailureAndAssertCursor(overlong));
        Assert.IsType<InvalidDataException>(
            CaptureUInt64FailureAndAssertCursor(overflow));
    }

    [Fact]
    public void UInt16_overflow_does_not_advance() {
        BinaryPayloadReader reader = new([0x80, 0x80, 0x04]);
        Exception? exception = null;

        try {
            reader.ReadUInt16();
        } catch (Exception current) {
            exception = current;
        }

        Assert.IsType<InvalidDataException>(exception);
        Assert.Equal(0, reader.ConsumedCount);
        Assert.Equal(3, reader.RemainingCount);
    }

    [Fact]
    public void UInt16_rejects_max_width_overlong_without_advancing() {
        BinaryPayloadReader reader = new([0x80, 0x80, 0x00]);
        Exception? exception = null;

        try {
            reader.ReadUInt16();
        } catch (Exception current) {
            exception = current;
        }

        Assert.IsType<InvalidDataException>(exception);
        Assert.Equal(0, reader.ConsumedCount);
        Assert.Equal(3, reader.RemainingCount);
    }

    [Fact]
    public void Signed_ZigZag_has_stable_golden_bytes() {
        ArrayBufferWriter<byte> buffer = new();
        BinaryPayloadWriter writer = new(buffer);

        writer.WriteInt16(0);
        writer.WriteInt16(-1);
        writer.WriteInt16(1);
        writer.WriteInt16(-2);

        Assert.Equal([0x00, 0x01, 0x02, 0x03], buffer.WrittenSpan.ToArray());

        BinaryPayloadReader reader = new([0x00, 0x01, 0x02, 0x03]);
        Assert.Equal(0, reader.ReadInt16());
        Assert.Equal(-1, reader.ReadInt16());
        Assert.Equal(1, reader.ReadInt16());
        Assert.Equal(-2, reader.ReadInt16());
        Assert.True(reader.End);
    }

    [Fact]
    public void Invalid_boolean_does_not_advance() {
        BinaryPayloadReader reader = new([0x02, 0x01]);
        Exception? exception = null;

        try {
            reader.ReadBoolean();
        } catch (Exception current) {
            exception = current;
        }

        Assert.IsType<InvalidDataException>(exception);
        Assert.Equal(0, reader.ConsumedCount);
        Assert.Equal(2, reader.RemainingCount);
    }

    [Fact]
    public void Fixed_width_truncation_does_not_advance() {
        BinaryPayloadReader reader = new([0x00, 0x01, 0x02]);
        Exception? exception = null;

        try {
            reader.ReadSingle();
        } catch (Exception current) {
            exception = current;
        }

        Assert.IsType<EndOfStreamException>(exception);
        Assert.Equal(0, reader.ConsumedCount);
        Assert.Equal(3, reader.RemainingCount);
    }

    [Fact]
    public void Count_overflow_and_truncated_bytes_roll_back_completely() {
        BinaryPayloadReader countReader = new([0x80, 0x80, 0x80, 0x80, 0x08]);
        BinaryPayloadReader bytesReader = new([0x03, 0xaa, 0xbb]);
        Exception? countException = null;
        Exception? bytesException = null;

        try {
            countReader.ReadCount();
        } catch (Exception current) {
            countException = current;
        }

        try {
            bytesReader.ReadBytes();
        } catch (Exception current) {
            bytesException = current;
        }

        Assert.IsType<InvalidDataException>(countException);
        Assert.Equal(0, countReader.ConsumedCount);
        Assert.Equal(5, countReader.RemainingCount);
        Assert.IsType<EndOfStreamException>(bytesException);
        Assert.Equal(0, bytesReader.ConsumedCount);
        Assert.Equal(3, bytesReader.RemainingCount);
    }

    [Fact]
    public void Span_argument_failure_and_truncation_do_not_advance() {
        BinaryPayloadReader reader = new([0x01]);
        Exception? negativeException = null;
        Exception? truncatedException = null;

        try {
            reader.ReadSpan(-1);
        } catch (Exception current) {
            negativeException = current;
        }

        try {
            reader.ReadSpan(2);
        } catch (Exception current) {
            truncatedException = current;
        }

        Assert.IsType<ArgumentOutOfRangeException>(negativeException);
        Assert.IsType<EndOfStreamException>(truncatedException);
        Assert.Equal(0, reader.ConsumedCount);
        Assert.Equal(1, reader.RemainingCount);
    }

    [Fact]
    public void Ensure_fully_consumed_rejects_trailing_bytes_without_advancing() {
        BinaryPayloadReader reader = new([0x01]);
        Exception? exception = null;

        try {
            reader.EnsureFullyConsumed();
        } catch (Exception current) {
            exception = current;
        }

        Assert.IsType<InvalidDataException>(exception);
        Assert.Equal(0, reader.ConsumedCount);
    }

    [Fact]
    public void Writer_rejects_null_downstream_and_negative_count_before_output() {
        Exception? constructorException = null;
        try {
            _ = new BinaryPayloadWriter(null!);
        } catch (Exception current) {
            constructorException = current;
        }

        ArrayBufferWriter<byte> buffer = new();
        BinaryPayloadWriter writer = new(buffer);
        Exception? countException = null;
        try {
            writer.WriteCount(-1);
        } catch (Exception current) {
            countException = current;
        }

        Assert.IsType<ArgumentNullException>(constructorException);
        Assert.IsType<ArgumentOutOfRangeException>(countException);
        Assert.Equal(0, buffer.WrittenCount);
    }

    private static Exception CaptureUInt32FailureAndAssertCursor(byte[] source) {
        BinaryPayloadReader reader = new(source);
        try {
            reader.ReadUInt32();
        } catch (Exception exception) {
            Assert.Equal(0, reader.ConsumedCount);
            Assert.Equal(source.Length, reader.RemainingCount);
            return exception;
        }

        throw new InvalidOperationException("Expected UInt32 decoding to fail.");
    }

    private static Exception CaptureUInt64FailureAndAssertCursor(byte[] source) {
        BinaryPayloadReader reader = new(source);
        try {
            reader.ReadUInt64();
        } catch (Exception exception) {
            Assert.Equal(0, reader.ConsumedCount);
            Assert.Equal(source.Length, reader.RemainingCount);
            return exception;
        }

        throw new InvalidOperationException("Expected UInt64 decoding to fail.");
    }

    private sealed class BoundedBufferWriter : IBufferWriter<byte> {
        private readonly byte[] _buffer;
        private int _writtenCount;

        internal BoundedBufferWriter(int capacity) {
            _buffer = new byte[capacity];
        }

        internal ReadOnlySpan<byte> WrittenSpan => _buffer.AsSpan(0, _writtenCount);

        public void Advance(int count) {
            if (count < 0 || count > _buffer.Length - _writtenCount) {
                throw new ArgumentOutOfRangeException(nameof(count));
            }

            _writtenCount += count;
        }

        public Memory<byte> GetMemory(int sizeHint = 0) {
            ValidateSizeHint(sizeHint);
            return _buffer.AsMemory(_writtenCount);
        }

        public Span<byte> GetSpan(int sizeHint = 0) {
            ValidateSizeHint(sizeHint);
            return _buffer.AsSpan(_writtenCount);
        }

        private void ValidateSizeHint(int sizeHint) {
            if (sizeHint < 0 || sizeHint > _buffer.Length - _writtenCount) {
                throw new ArgumentOutOfRangeException(nameof(sizeHint));
            }
        }
    }
}
