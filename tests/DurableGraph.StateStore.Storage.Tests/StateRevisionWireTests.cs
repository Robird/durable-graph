using System.Buffers;
using Atelia.Data;
using Atelia.DurableGraph.StateStore.Serialization;

namespace Atelia.DurableGraph.StateStore.Storage.Tests;

public sealed class StateRevisionWireTests {
    [Fact]
    public void Genesis_Base_has_stable_golden_bytes() {
        StateRevision revision = StateRevision.CreateBase(null, [new(128, []), new(1, [0xaa, 0xbb])], []);
        byte[] golden = [0x02, 0x01, 0x00, 0x02, 0x01, 0x02, 0xaa, 0xbb, 0x80, 0x01, 0x00, 0x00];

        Assert.Equal(golden, Encode(revision, new FileScope(1)));
        AssertRevisionEqual(revision, StateRevisionWireReader.Read(golden, new FileScope(1)));
    }

    [Fact]
    public void Delta_with_previous_file_parent_has_stable_golden_bytes() {
        StateRevision revision = StateRevision.CreateDelta(
            new FrameAddress(1, SizedPtr.Create(4, 4)), [new(3, [0xfe])], [2]);
        byte[] golden = [0x02, 0x02, 0x01, 0x01, 0x05, 0x01, 0x03, 0x01, 0xfe, 0x01, 0x02];

        Assert.Equal(golden, Encode(revision, new FileScope(2)));
        AssertRevisionEqual(revision, StateRevisionWireReader.Read(golden, new FileScope(2)));
    }

    [Fact]
    public void Base_round_trip_restores_absolute_parent_and_external_heads() {
        StateRevision source = StateRevision.CreateBase(
            new FrameAddress(3, SizedPtr.Create(4, 32)),
            [new(9, [90]), new(1, [10]), new(7, []), new(3, [30, 31])],
            [
                new(8, new FrameAddress(3, SizedPtr.Create(96, 32))),
                new(2, new FrameAddress(2, SizedPtr.Create(64, 32))),
            ]);
        FileScope scope = new(4);
        AssertRevisionEqual(source, StateRevisionWireReader.Read(Encode(source, scope), scope));
    }

    [Fact]
    public void Reader_accepts_handwritten_previous_file_parent_and_external_head() {
        byte[] encoded = [
            0x02, 0x01, 0x01,
            0x01, 0x05,
            0x01, 0x03, 0x02, 0xca, 0xfe,
            0x01, 0x02, 0x01, 0x05,
        ];
        FrameAddress expectedAddress = new(1, SizedPtr.Create(4, 4));
        StateRevision revision = StateRevisionWireReader.Read(encoded, new FileScope(2));

        Assert.Equal(expectedAddress, revision.ParentRevisionAddress);
        Assert.Equal([3u], revision.BaseObjectIds);
        Assert.Equal([0xca, 0xfe], revision.BaseObjects[0].Body.ToArray());
        Assert.Equal(expectedAddress, revision.ExternalObjectHeads[2]);
    }

    [Fact]
    public void Large_id_and_multibyte_body_length_have_independent_golden_encoding() {
        byte[] body = Enumerable.Range(0, 128).Select(static value => (byte)value).ToArray();
        byte[] golden = [0x02, 0x01, 0x00, 0x01, 0xff, 0xff, 0xff, 0xff, 0x0f, 0x80, 0x01, .. body, 0x00];
        StateRevision revision = StateRevision.CreateBase(null, [new(uint.MaxValue, body)], []);
        Assert.Equal(golden, Encode(revision, new FileScope(1)));
        AssertRevisionEqual(revision, StateRevisionWireReader.Read(golden, new FileScope(1)));
    }

    [Fact]
    public void Decoded_body_owns_its_bytes_independently_of_input_and_returned_copies() {
        byte[] encoded = [0x02, 0x01, 0x00, 0x01, 0x01, 0x02, 0x10, 0x20, 0x00];
        StateRevision revision = StateRevisionWireReader.Read(encoded, new FileScope(1));
        encoded.AsSpan().Fill(0xff);
        byte[] copy = revision.BaseObjects[0].Body.ToArray();
        copy.AsSpan().Clear();
        Assert.Equal([0x10, 0x20], revision.BaseObjects[0].Body.ToArray());
    }

    [Fact]
    public void Every_truncated_prefix_and_any_trailing_byte_of_golden_is_rejected() {
        byte[] golden = [0x02, 0x01, 0x00, 0x01, 0x01, 0x02, 0x10, 0x20, 0x00];
        for (int length = 0; length < golden.Length; length++) {
            AssertMalformed(golden[..length]);
        }

        AssertMalformed([.. golden, 0x00]);
        AssertMalformed([.. golden, 0xff]);
    }

    [Fact]
    public void Default_scope_is_rejected_for_address_free_genesis_Base() {
        StateRevision revision = StateRevision.CreateBase(null, [], []);
        byte[] encoded = [0x02, 0x01, 0x00, 0x00, 0x00];
        Assert.Throws<ArgumentOutOfRangeException>(() => Encode(revision, default));
        Assert.Throws<ArgumentOutOfRangeException>(() => StateRevisionWireReader.Read(encoded, default));
    }

    [Fact]
    public void Collection_count_above_provisional_limit_is_rejected_before_allocation() {
        ArrayBufferWriter<byte> buffer = new();
        BinaryPayloadWriter writer = new(buffer);
        writer.WriteSpan([0x02, 0x01, 0x00]);
        writer.WriteUInt32(StateRevisionWireFormat.MaxCollectionCount + 1u);
        InvalidDataException exception = Assert.Throws<InvalidDataException>(() =>
            StateRevisionWireReader.Read(buffer.WrittenSpan, new FileScope(1)));
        Assert.Contains("exceeds the provisional limit", exception.Message);
    }

    [Theory]
    [InlineData(new byte[] { 2, 1, 0, 2, 1, 0, 0 })]
    [InlineData(new byte[] { 2, 1, 0, 0, 2, 1, 1, 5 })]
    public void Collection_minimum_entry_size_is_checked_before_allocation(byte[] encoded) {
        InvalidDataException exception = Assert.Throws<InvalidDataException>(() =>
            StateRevisionWireReader.Read(encoded, new FileScope(2)));
        Assert.Contains("remaining payload bounds", exception.Message);
    }

    [Fact]
    public void Collection_count_within_limit_but_above_remaining_is_rejected_before_allocation() {
        ArrayBufferWriter<byte> buffer = new();
        BinaryPayloadWriter writer = new(buffer);
        writer.WriteSpan([0x02, 0x01, 0x00]);
        writer.WriteUInt32(StateRevisionWireFormat.MaxCollectionCount);
        InvalidDataException exception = Assert.Throws<InvalidDataException>(() =>
            StateRevisionWireReader.Read(buffer.WrittenSpan, new FileScope(1)));
        Assert.Contains("remaining payload bounds", exception.Message);
    }

    [Fact]
    public void Writer_rejects_collection_above_its_reader_limit_before_writing() {
        StateRevision revision = StateRevision.CreateBase(
            null,
            Enumerable.Range(1, StateRevisionWireFormat.MaxCollectionCount + 1)
                .Select(value => new BaseObjectRecord((uint)value, [])),
            []);
        ArrayBufferWriter<byte> buffer = new();
        Assert.Throws<InvalidDataException>(() => StateRevisionWireWriter.Write(buffer, revision, new FileScope(1)));
        Assert.Equal(0, buffer.WrittenCount);
    }

    [Theory]
    [MemberData(nameof(MalformedPayloads))]
    public void Malformed_payload_fails_closed(string description, byte[] payload) {
        Assert.False(string.IsNullOrWhiteSpace(description));
        AssertMalformed(payload);
    }

    public static TheoryData<string, byte[]> MalformedPayloads => new() {
        { "old membership-only v1", [1, 1, 0, 0, 0, 0] },
        { "unknown version", [3] },
        { "unknown map kind", [2, 0xff] },
        { "invalid parent marker", [2, 1, 0xff] },
        { "map Delta requires parent", [2, 2, 0, 0, 0] },
        { "noncanonical local count", [2, 1, 0, 0x80, 0] },
        { "zero local ID", [2, 1, 0, 1, 0, 0, 0] },
        { "duplicate local ID", [2, 1, 0, 2, 1, 0, 1, 0, 0] },
        { "descending local ID", [2, 1, 0, 2, 2, 0, 1, 0, 0] },
        { "noncanonical local ID", [2, 1, 0, 1, 0x81, 0, 0, 0] },
        { "noncanonical body length", [2, 1, 0, 1, 1, 0x80, 0, 0] },
        { "body length exceeds Int32", [2, 1, 0, 1, 1, 0x80, 0x80, 0x80, 0x80, 8, 0] },
        { "body length exceeds remaining", [2, 1, 0, 1, 1, 0x7f, 0] },
        { "local and external overlap", [2, 1, 0, 1, 1, 0, 1, 1, 1, 5] },
        { "duplicate external ID", [2, 1, 0, 0, 2, 1, 1, 5, 1, 1, 5] },
        { "descending external ID", [2, 1, 0, 0, 2, 2, 1, 5, 1, 1, 5] },
        { "zero external ID", [2, 1, 0, 0, 1, 0, 1, 5] },
        { "local and removed overlap", [2, 2, 1, 1, 5, 1, 1, 0, 1, 1] },
        { "duplicate removed ID", [2, 2, 1, 1, 5, 0, 2, 1, 1] },
        { "descending removed ID", [2, 2, 1, 1, 5, 0, 2, 2, 1] },
        { "zero removed ID", [2, 2, 1, 1, 5, 0, 1, 0] },
        { "empty parent ticket", [2, 2, 1, 1, 0, 0, 0] },
        { "empty external ticket", [2, 1, 0, 0, 1, 1, 1, 0] },
    };

    private static void AssertMalformed(byte[] payload) {
        Exception? exception = Record.Exception(() => StateRevisionWireReader.Read(payload, new FileScope(2)));
        Assert.True(exception is InvalidDataException or EndOfStreamException, $"Unexpected exception: {exception}");
    }

    private static byte[] Encode(StateRevision revision, FileScope scope) {
        ArrayBufferWriter<byte> destination = new();
        StateRevisionWireWriter.Write(destination, revision, scope);
        return destination.WrittenSpan.ToArray();
    }

    private static void AssertRevisionEqual(StateRevision expected, StateRevision actual) {
        Assert.Equal(expected.ParentRevisionAddress, actual.ParentRevisionAddress);
        Assert.Equal(expected.ObjectHeadMapKind, actual.ObjectHeadMapKind);
        Assert.Equal(expected.BaseObjectIds, actual.BaseObjectIds);
        Assert.Equal(expected.BaseObjects.Count, actual.BaseObjects.Count);
        for (int index = 0; index < expected.BaseObjects.Count; index++) {
            Assert.Equal(expected.BaseObjects[index].ObjectId, actual.BaseObjects[index].ObjectId);
            Assert.Equal(expected.BaseObjects[index].Body.ToArray(), actual.BaseObjects[index].Body.ToArray());
        }

        Assert.Equal(expected.RemovedObjectIds, actual.RemovedObjectIds);
        Assert.Equal(expected.ExternalObjectHeads.ToArray(), actual.ExternalObjectHeads.ToArray());
    }
}
