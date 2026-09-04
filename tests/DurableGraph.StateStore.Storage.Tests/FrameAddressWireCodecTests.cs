using System.Buffers;
using Atelia.Data;
using Atelia.DurableGraph.StateStore.Serialization;

namespace Atelia.DurableGraph.StateStore.Storage.Tests;

public sealed class FrameAddressWireCodecTests {
    [Theory]
    [InlineData(70_000u, 70_000u, 0u)]
    [InlineData(70_000u, 69_999u, 1u)]
    [InlineData(70_000u, 4_464u, 65_536u)]
    [InlineData(uint.MaxValue, 1u, uint.MaxValue - 1u)]
    public void Required_address_round_trips_through_wire_only_distance(
        uint currentFileNumber,
        uint targetFileNumber,
        uint expectedDistance) {
        FileScope scope = new(currentFileNumber);
        FrameAddress address = new(targetFileNumber, SizedPtr.Create(4, 32));
        ArrayBufferWriter<byte> buffer = new();
        BinaryPayloadWriter writer = new(buffer);

        FrameAddressWireCodec.Write(ref writer, scope, address);
        BinaryPayloadReader reader = new(buffer.WrittenSpan);
        FrameAddress roundTrip = FrameAddressWireCodec.Read(
            ref reader,
            scope);

        Assert.Equal(expectedDistance, ReadFirstUInt32(buffer.WrittenSpan));
        Assert.Equal(buffer.WrittenCount, reader.ConsumedCount);
        Assert.Equal(address, roundTrip);
    }

    [Fact]
    public void Small_previous_file_address_has_stable_golden_bytes() {
        FileScope scope = new(2);
        FrameAddress address = new(1, SizedPtr.Create(4, 4));
        ArrayBufferWriter<byte> buffer = new();
        BinaryPayloadWriter writer = new(buffer);

        FrameAddressWireCodec.Write(ref writer, scope, address);

        Assert.Equal([0x01, 0x05], buffer.WrittenSpan.ToArray());
    }

    [Fact]
    public void Maximum_sized_pointer_round_trips_through_ten_byte_UInt64() {
        FrameAddress address = new(
            1,
            SizedPtr.Create(SizedPtr.MaxOffset, SizedPtr.MaxLength));
        ArrayBufferWriter<byte> buffer = new();
        BinaryPayloadWriter writer = new(buffer);

        FrameAddressWireCodec.Write(
            ref writer,
            new FileScope(2),
            address);
        BinaryPayloadReader reader = new(buffer.WrittenSpan);

        FrameAddress roundTrip = FrameAddressWireCodec.Read(
            ref reader,
            new FileScope(2));

        Assert.Equal(11, buffer.WrittenCount);
        Assert.Equal(address, roundTrip);
        Assert.True(reader.End);
    }

    [Fact]
    public void Future_file_address_is_rejected_before_codec_io() {
        FileScope scope = new(2);

        Exception? futureException = null;
        ArrayBufferWriter<byte> futureBuffer = new();
        BinaryPayloadWriter futureWriter = new(futureBuffer);
        try {
            FrameAddressWireCodec.Write(
                ref futureWriter,
                scope,
                new FrameAddress(3, SizedPtr.Create(4, 4)));
        } catch (Exception exception) {
            futureException = exception;
        }

        Assert.IsType<InvalidDataException>(futureException);
        Assert.Equal(0, futureBuffer.WrittenCount);
    }

    [Fact]
    public void Semantic_decode_failures_do_not_advance_composite_reader() {
        FileScope scope = new(2);
        BinaryPayloadReader underflowReader = new([0x02, 0x05]);
        Exception? underflowException = null;
        try {
            FrameAddressWireCodec.Read(ref underflowReader, scope);
        } catch (Exception exception) {
            underflowException = exception;
        }

        BinaryPayloadReader emptyTicketReader = new([0x00, 0x00]);
        Exception? emptyTicketException = null;
        try {
            FrameAddressWireCodec.Read(ref emptyTicketReader, scope);
        } catch (Exception exception) {
            emptyTicketException = exception;
        }

        Assert.IsType<InvalidDataException>(underflowException);
        Assert.Equal(0, underflowReader.ConsumedCount);
        Assert.IsType<InvalidDataException>(emptyTicketException);
        Assert.Equal(0, emptyTicketReader.ConsumedCount);
    }

    [Fact]
    public void Truncated_ticket_does_not_advance_composite_reader() {
        BinaryPayloadReader reader = new([0x00, 0x80]);
        Exception? caught = null;

        try {
            FrameAddressWireCodec.Read(
                ref reader,
                new FileScope(2));
        } catch (Exception exception) {
            caught = exception;
        }

        Assert.IsType<EndOfStreamException>(caught);
        Assert.Equal(0, reader.ConsumedCount);
    }

    [Fact]
    public void Default_scope_is_rejected_before_codec_io() {
        ArrayBufferWriter<byte> buffer = new();
        BinaryPayloadWriter writer = new(buffer);
        FrameAddress address = new(1, SizedPtr.Create(4, 4));

        Exception? writeException = null;
        try {
            FrameAddressWireCodec.Write(ref writer, default, address);
        } catch (Exception exception) {
            writeException = exception;
        }

        BinaryPayloadReader reader = new([0x00, 0x05]);
        Exception? readException = null;
        try {
            FrameAddressWireCodec.Read(
                ref reader,
                default);
        } catch (Exception exception) {
            readException = exception;
        }

        Assert.IsType<ArgumentOutOfRangeException>(writeException);
        Assert.Equal(0, buffer.WrittenCount);
        Assert.IsType<ArgumentOutOfRangeException>(readException);
        Assert.Equal(0, reader.ConsumedCount);
    }

    private static uint ReadFirstUInt32(ReadOnlySpan<byte> source) {
        BinaryPayloadReader reader = new(source);
        return reader.ReadUInt32();
    }
}
