using System.Buffers;
using Atelia.DurableGraph.StateStore.Serialization;

namespace Atelia.DurableGraph.StateStore.Serialization.Tests;

public class StringPayloadCodecTests {
    public static TheoryData<string, byte[]> GoldenStrings => new() {
        { string.Empty, [0x00] },
        { "A", [0x03, 0x41] },
        { "你", [0x02, 0x60, 0x4F] },
        { "é", [0x02, 0xE9, 0x00] },
        { "🌟", [0x04, 0x3C, 0xD8, 0x1F, 0xDF] },
    };

    public static TheoryData<string?, byte[]> NullableGoldenStrings => new() {
        { null, [0x00] },
        { string.Empty, [0x01] },
        { "A", [0x04, 0x41] },
        { "你", [0x03, 0x60, 0x4F] },
    };

    public static TheoryData<byte[]> InvalidPayloads => new() {
        { [0x80, 0x00] }, // Overlong header.
        { [0x01] }, // Noncanonical UTF-8 representation of the empty string.
        { [0x03, 0xFF] }, // Invalid one-byte UTF-8 payload.
        { [0x03, 0x80] }, // Invalid UTF-8 continuation byte.
        { [0x05, 0xC0, 0x80] }, // Overlong UTF-8 scalar.
        { [0x07, 0xED, 0xA0, 0x80] }, // UTF-8 encoding of a surrogate.
        { [0x09, 0xF4, 0x90, 0x80, 0x80] }, // Scalar above U+10FFFF.
        { [0x05, 0x41] }, // Truncated two-byte UTF-8 payload.
        { [0x04, 0x41, 0x00] }, // Truncated four-byte UTF-16LE payload.
        { [0x02, 0x41, 0x00] }, // Noncanonical UTF-16LE alternative for ASCII.
        { [0x07, 0xE4, 0xBD, 0xA0] }, // Noncanonical UTF-8 alternative for CJK.
        { [0x05, 0xC3, 0xA9] }, // Noncanonical UTF-8 tie; UTF-16LE must win.
        { [0x09, 0xF0, 0x9F, 0x8C, 0x9F] }, // Noncanonical UTF-8 tie for a surrogate pair.
        { [0xFF, 0xFF, 0xFF, 0xFF, 0x0F] }, // Int32.MaxValue UTF-8 bytes, no payload.
        { [0xFE, 0xFF, 0xFF, 0xFF, 0x0F] }, // UTF-16LE byte count exceeds Int32.MaxValue.
    };

    [Theory]
    [MemberData(nameof(GoldenStrings))]
    public void Writer_matches_golden_encoding(string value, byte[] expected) =>
        Assert.Equal(expected, Write(value));

    [Theory]
    [MemberData(nameof(GoldenStrings))]
    public void Reader_accepts_independent_golden_encoding(
        string expected,
        byte[] encoded) =>
        Assert.Equal(expected, Read(encoded));

    [Theory]
    [MemberData(nameof(NullableGoldenStrings))]
    public void Nullable_writer_matches_golden_encoding(
        string? value,
        byte[] expected) =>
        Assert.Equal(expected, WriteNullable(value));

    [Theory]
    [MemberData(nameof(NullableGoldenStrings))]
    public void Nullable_reader_accepts_independent_golden_encoding(
        string? expected,
        byte[] encoded) =>
        Assert.Equal(expected, ReadNullable(encoded));

    [Fact]
    public void SupplementalCharacters_RoundTrip() {
        const string value = "A🌟𐐷你";

        Assert.Equal(value, Read(Write(value)));
    }

    [Fact]
    public void UnpairedSurrogate_UsesUtf16LeAndRoundTripsCodeUnit() {
        string value = new(['\uD800']);

        byte[] encoded = Write(value);

        Assert.Equal([0x02, 0x00, 0xD8], encoded);
        string decoded = Read(encoded);
        Assert.Equal(value.Length, decoded.Length);
        Assert.Equal(value[0], decoded[0]);
    }

    [Fact]
    public void Unpaired_surrogate_does_not_take_lossy_shorter_Utf8_path() {
        string value = $"AAAAAAAAAA{new string(['\uD800'])}";

        byte[] encoded = Write(value);

        Assert.Equal(0x16, encoded[0]);
        Assert.Equal(value, Read(encoded));
    }

    [Theory]
    [MemberData(nameof(InvalidPayloads))]
    public void Read_InvalidOrNoncanonicalPayload_ThrowsInvalidDataException(byte[] data) {
        Assert.Throws<InvalidDataException>(() => Read(data));
    }

    [Theory]
    [InlineData(new byte[] { 0x02 })]
    [InlineData(new byte[] { 0x03, 0x41, 0x00 })]
    public void Nullable_reader_rejects_noncanonical_present_payload(byte[] data) {
        Assert.Throws<InvalidDataException>(() => ReadNullable(data));
    }

    [Fact]
    public void Failed_string_read_does_not_advance_reader() {
        BinaryPayloadReader reader = new([0x05, 0xC3, 0xA9]);
        Exception? exception = null;

        try {
            reader.ReadString();
        } catch (Exception current) {
            exception = current;
        }

        Assert.IsType<InvalidDataException>(exception);
        Assert.Equal(0, reader.ConsumedCount);
        Assert.Equal(3, reader.RemainingCount);
    }

    [Fact]
    public void Failed_nullable_string_read_does_not_advance_reader() {
        BinaryPayloadReader reader = new([
            0xff, 0xff, 0xff, 0xff, 0x0f,
        ]);
        Exception? exception = null;

        try {
            reader.ReadNullableString();
        } catch (Exception current) {
            exception = current;
        }

        Assert.IsType<InvalidDataException>(exception);
        Assert.Equal(0, reader.ConsumedCount);
        Assert.Equal(5, reader.RemainingCount);
    }

    [Fact]
    public void NullablePresentHeader_RejectsRawUInt32Maximum() {
        Assert.Throws<OverflowException>(() => NullablePayloadHeader.EncodePresent(uint.MaxValue));
    }

    private static byte[] Write(string value) {
        var destination = new ArrayBufferWriter<byte>();
        BinaryPayloadWriter writer = new(destination);
        writer.WriteString(value);
        return destination.WrittenSpan.ToArray();
    }

    private static byte[] WriteNullable(string? value) {
        var destination = new ArrayBufferWriter<byte>();
        BinaryPayloadWriter writer = new(destination);
        writer.WriteNullableString(value);
        return destination.WrittenSpan.ToArray();
    }

    private static string Read(byte[] data) {
        var reader = new BinaryPayloadReader(data);
        string value = reader.ReadString();
        reader.EnsureFullyConsumed();
        return value;
    }

    private static string? ReadNullable(byte[] data) {
        var reader = new BinaryPayloadReader(data);
        string? value = reader.ReadNullableString();
        reader.EnsureFullyConsumed();
        return value;
    }
}
