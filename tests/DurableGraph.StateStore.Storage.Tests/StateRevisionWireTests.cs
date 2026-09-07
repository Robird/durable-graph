using System.Buffers;
using Atelia.Data;
using Atelia.DurableGraph.StateStore.Serialization;

namespace Atelia.DurableGraph.StateStore.Storage.Tests;

public sealed class StateRevisionWireTests {
    [Fact]
    public void Genesis_head_map_Base_has_stable_golden_bytes() {
        StateRevision revision = StateRevision.CreateObjectHeadMapBase(null, [ObjectVersionRecord.CreateBase(128, []), ObjectVersionRecord.CreateBase(1, [0xaa, 0xbb])], []);
        byte[] golden = [0x03, 0x01, 0x00, 0x02, 0x01, 0x01, 0x02, 0xaa, 0xbb, 0x80, 0x01, 0x01, 0x00, 0x00];

        Assert.Equal(golden, Encode(revision, new FileScope(1)));
        AssertRevisionEqual(revision, StateRevisionWireReader.Read(golden, new FileScope(1)));
    }

    [Fact]
    public void Head_map_Delta_with_previous_file_parent_has_stable_golden_bytes() {
        StateRevision revision = StateRevision.CreateObjectHeadMapDelta(
            new FrameAddress(1, SizedPtr.Create(4, 4)), [ObjectVersionRecord.CreateBase(3, [0xfe])], [2]);
        byte[] golden = [0x03, 0x02, 0x01, 0x01, 0x05, 0x01, 0x03, 0x01, 0x01, 0xfe, 0x01, 0x02];

        Assert.Equal(golden, Encode(revision, new FileScope(2)));
        AssertRevisionEqual(revision, StateRevisionWireReader.Read(golden, new FileScope(2)));
    }

    [Fact]
    public void Head_map_Base_round_trip_restores_absolute_parent_and_external_heads() {
        StateRevision source = StateRevision.CreateObjectHeadMapBase(
            new FrameAddress(3, SizedPtr.Create(4, 32)),
            [ObjectVersionRecord.CreateBase(9, [90]), ObjectVersionRecord.CreateBase(1, [10]), ObjectVersionRecord.CreateBase(7, []), ObjectVersionRecord.CreateBase(3, [30, 31])],
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
            0x03, 0x01, 0x01,
            0x01, 0x05,
            0x01, 0x03, 0x01, 0x02, 0xca, 0xfe,
            0x01, 0x02, 0x01, 0x05,
        ];
        FrameAddress expectedAddress = new(1, SizedPtr.Create(4, 4));
        StateRevision revision = StateRevisionWireReader.Read(encoded, new FileScope(2));

        Assert.Equal(expectedAddress, revision.ParentRevisionAddress);
        Assert.Equal([3u], revision.LocalObjectIds);
        Assert.Equal([0xca, 0xfe], revision.LocalObjects[0].Body.ToArray());
        Assert.Equal(expectedAddress, revision.ExternalObjectHeads[2]);
    }

    [Fact]
    public void Large_id_and_multibyte_body_length_have_independent_golden_encoding() {
        byte[] body = Enumerable.Range(0, 128).Select(static value => (byte)value).ToArray();
        byte[] golden = [0x03, 0x01, 0x00, 0x01, 0xff, 0xff, 0xff, 0xff, 0x0f, 0x01, 0x80, 0x01, .. body, 0x00];
        StateRevision revision = StateRevision.CreateObjectHeadMapBase(null, [ObjectVersionRecord.CreateBase(uint.MaxValue, body)], []);
        Assert.Equal(golden, Encode(revision, new FileScope(1)));
        AssertRevisionEqual(revision, StateRevisionWireReader.Read(golden, new FileScope(1)));
    }

    [Fact]
    public void Decoded_body_owns_its_bytes_independently_of_input_and_returned_copies() {
        byte[] encoded = [0x03, 0x01, 0x00, 0x01, 0x01, 0x01, 0x02, 0x10, 0x20, 0x00];
        StateRevision revision = StateRevisionWireReader.Read(encoded, new FileScope(1));
        encoded.AsSpan().Fill(0xff);
        byte[] copy = revision.LocalObjects[0].Body.ToArray();
        copy.AsSpan().Clear();
        Assert.Equal([0x10, 0x20], revision.LocalObjects[0].Body.ToArray());
    }

    [Fact]
    public void Every_truncated_prefix_and_any_trailing_byte_of_golden_is_rejected() {
        byte[] golden = [0x03, 0x01, 0x00, 0x01, 0x01, 0x01, 0x02, 0x10, 0x20, 0x00];
        for (int length = 0; length < golden.Length; length++) {
            AssertMalformed(golden[..length]);
        }

        AssertMalformed([.. golden, 0x00]);
        AssertMalformed([.. golden, 0xff]);
    }

    [Fact]
    public void Default_scope_is_rejected_for_address_free_genesis_head_map_Base() {
        StateRevision revision = StateRevision.CreateObjectHeadMapBase(null, [], []);
        byte[] encoded = [0x03, 0x01, 0x00, 0x00, 0x00];
        Assert.Throws<ArgumentOutOfRangeException>(() => Encode(revision, default));
        Assert.Throws<ArgumentOutOfRangeException>(() => StateRevisionWireReader.Read(encoded, default));
    }

    [Fact]
    public void Collection_count_above_provisional_limit_is_rejected_before_allocation() {
        ArrayBufferWriter<byte> buffer = new();
        BinaryPayloadWriter writer = new(buffer);
        writer.WriteSpan([0x03, 0x01, 0x00]);
        writer.WriteUInt32(StateRevisionWireFormat.MaxCollectionCount + 1u);
        InvalidDataException exception = Assert.Throws<InvalidDataException>(() =>
            StateRevisionWireReader.Read(buffer.WrittenSpan, new FileScope(1)));
        Assert.Contains("exceeds the provisional limit", exception.Message);
    }

    [Theory]
    [InlineData(new byte[] { 3, 1, 0, 2, 1, 0, 0 })]
    [InlineData(new byte[] { 3, 1, 0, 0, 2, 1, 1, 5 })]
    public void Collection_minimum_entry_size_is_checked_before_allocation(byte[] encoded) {
        InvalidDataException exception = Assert.Throws<InvalidDataException>(() =>
            StateRevisionWireReader.Read(encoded, new FileScope(2)));
        Assert.Contains("remaining payload bounds", exception.Message);
    }

    [Fact]
    public void Collection_count_within_limit_but_above_remaining_is_rejected_before_allocation() {
        ArrayBufferWriter<byte> buffer = new();
        BinaryPayloadWriter writer = new(buffer);
        writer.WriteSpan([0x03, 0x01, 0x00]);
        writer.WriteUInt32(StateRevisionWireFormat.MaxCollectionCount);
        InvalidDataException exception = Assert.Throws<InvalidDataException>(() =>
            StateRevisionWireReader.Read(buffer.WrittenSpan, new FileScope(1)));
        Assert.Contains("remaining payload bounds", exception.Message);
    }

    [Fact]
    public void Writer_rejects_collection_above_its_reader_limit_before_writing() {
        StateRevision revision = StateRevision.CreateObjectHeadMapBase(
            null,
            Enumerable.Range(1, StateRevisionWireFormat.MaxCollectionCount + 1)
                .Select(value => ObjectVersionRecord.CreateBase((uint)value, [])),
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
        { "old Base-only v2", [2, 1, 0, 0, 0] },
        { "unknown version", [4] },
        { "unknown map kind", [3, 0xff] },
        { "invalid parent marker", [3, 1, 0xff] },
        { "map Delta requires parent", [3, 2, 0, 0, 0] },
        { "noncanonical local count", [3, 1, 0, 0x80, 0] },
        { "zero local ID", [3, 1, 0, 1, 0, 1, 0, 0] },
        { "duplicate local ID", [3, 1, 0, 2, 1, 1, 0, 1, 1, 0, 0] },
        { "descending local ID", [3, 1, 0, 2, 2, 1, 0, 1, 1, 0, 0] },
        { "noncanonical local ID", [3, 1, 0, 1, 0x81, 0, 1, 0, 0] },
        { "unknown object kind", [3, 1, 0, 1, 1, 0xff, 0, 0] },
        { "zero object kind", [3, 1, 0, 1, 1, 0, 0, 0] },
        { "noncanonical body length", [3, 1, 0, 1, 1, 1, 0x80, 0, 0] },
        { "body length exceeds Int32", [3, 1, 0, 1, 1, 1, 0x80, 0x80, 0x80, 0x80, 8, 0] },
        { "body length exceeds remaining", [3, 1, 0, 1, 1, 1, 0x7f, 0] },
        { "local and external overlap", [3, 1, 0, 1, 1, 1, 0, 1, 1, 1, 5] },
        { "duplicate external ID", [3, 1, 0, 0, 2, 1, 1, 5, 1, 1, 5] },
        { "descending external ID", [3, 1, 0, 0, 2, 2, 1, 5, 1, 1, 5] },
        { "zero external ID", [3, 1, 0, 0, 1, 0, 1, 5] },
        { "local and removed overlap", [3, 2, 1, 1, 5, 1, 1, 1, 0, 1, 1] },
        { "duplicate removed ID", [3, 2, 1, 1, 5, 0, 2, 1, 1] },
        { "descending removed ID", [3, 2, 1, 1, 5, 0, 2, 2, 1] },
        { "zero removed ID", [3, 2, 1, 1, 5, 0, 1, 0] },
        { "empty parent ticket", [3, 2, 1, 1, 0, 0, 0] },
        { "empty external ticket", [3, 1, 0, 0, 1, 1, 1, 0] },
        { "object Delta requires parent even in map Base", [3, 1, 0, 1, 1, 2, 1, 5, 0, 0] },
        { "empty prior ticket", [3, 1, 1, 1, 5, 1, 1, 2, 1, 0, 0, 0] },
        { "noncanonical prior distance", [3, 1, 1, 1, 5, 1, 1, 2, 0x81, 0, 5, 0, 0] },
        { "noncanonical prior ticket", [3, 1, 1, 1, 5, 1, 1, 2, 1, 0x85, 0, 0, 0] },
        { "prior distance exceeds file scope", [3, 1, 1, 1, 5, 1, 1, 2, 2, 5, 0, 0] },
    };

    [Theory]
    [InlineData(ObjectHeadMapKind.Base)]
    [InlineData(ObjectHeadMapKind.Delta)]
    public void Mixed_objects_have_independent_golden_bytes_in_both_map_kinds(ObjectHeadMapKind mapKind) {
        FrameAddress prior = new(1, SizedPtr.Create(4, 4));
        ObjectVersionRecord[] records = [
            ObjectVersionRecord.CreateDelta(128, prior, [0xfe]),
            ObjectVersionRecord.CreateBase(1, [0xab]),
        ];
        StateRevision source = mapKind == ObjectHeadMapKind.Base
            ? StateRevision.CreateObjectHeadMapBase(prior, records, [])
            : StateRevision.CreateObjectHeadMapDelta(prior, records, []);
        byte[] golden = [3, (byte)mapKind, 1, 1, 5, 2, 1, 1, 1, 0xab, 0x80, 1, 2, 1, 5, 1, 0xfe, 0];
        Assert.Equal(golden, Encode(source, new FileScope(2)));
        StateRevision decoded = StateRevisionWireReader.Read(golden, new FileScope(2));
        AssertRevisionEqual(source, decoded);
        Assert.Equal(3, decoded.LocalObjects[0].EncodedPayloadBytes);
        Assert.Equal(5, decoded.LocalObjects[1].EncodedPayloadBytes);
        for (int length = 0; length < golden.Length; length++) {
            AssertMalformed(golden[..length]);
        }
        AssertMalformed([.. golden, 0]);
    }

    [Theory]
    [InlineData(127, 127)]
    [InlineData(128, 127)]
    [InlineData(127, 128)]
    [InlineData(128, 128)]
    public void Payload_cost_measures_original_scope_and_length_excluding_object_key(int distance, int bodyLength) {
        FrameAddress prior = new(1, SizedPtr.Create(4, 4));
        FileScope scope = new((uint)distance + 1);
        byte[] distanceBytes = distance == 127 ? [0x7f] : [0x80, 1];
        byte[] lengthBytes = bodyLength == 127 ? [0x7f] : [0x80, 1];
        byte[] body = Enumerable.Repeat((byte)0xab, bodyLength).ToArray();
        // The handwritten payload excludes the five-byte ObjectId key and every shared byte.
        byte[] payload = [2, .. distanceBytes, 5, .. lengthBytes, .. body];
        byte[] golden = [3, 1, 1, .. distanceBytes, 5, 1, 0xff, 0xff, 0xff, 0xff, 0x0f, .. payload, 0];
        StateRevision source = StateRevision.CreateObjectHeadMapBase(prior, [ObjectVersionRecord.CreateDelta(uint.MaxValue, prior, body)], []);
        Assert.Equal(golden, Encode(source, scope));
        StateRevision decoded = StateRevisionWireReader.Read(golden, scope);
        Assert.Equal(payload.Length, decoded.LocalObjects[0].EncodedPayloadBytes);
        Assert.Equal(prior, decoded.LocalObjects[0].PriorAddress);

        // Re-encoding in another file has its own measured size without changing the old record.
        StateRevision later = StateRevisionWireReader.Read(Encode(decoded, new FileScope(16_385)), new FileScope(16_385));
        Assert.Equal(1 + 3 + 1 + lengthBytes.Length + bodyLength, later.LocalObjects[0].EncodedPayloadBytes);
        Assert.Equal(payload.Length, decoded.LocalObjects[0].EncodedPayloadBytes);
    }

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
        Assert.Equal(expected.LocalObjectIds, actual.LocalObjectIds);
        Assert.Equal(expected.LocalObjects.Count, actual.LocalObjects.Count);
        for (int index = 0; index < expected.LocalObjects.Count; index++) {
            Assert.Equal(expected.LocalObjects[index].ObjectId, actual.LocalObjects[index].ObjectId);
            Assert.Equal(expected.LocalObjects[index].Kind, actual.LocalObjects[index].Kind);
            Assert.Equal(expected.LocalObjects[index].PriorAddress, actual.LocalObjects[index].PriorAddress);
            Assert.Equal(expected.LocalObjects[index].Body.ToArray(), actual.LocalObjects[index].Body.ToArray());
        }

        Assert.Equal(expected.RemovedObjectIds, actual.RemovedObjectIds);
        Assert.Equal(expected.ExternalObjectHeads.ToArray(), actual.ExternalObjectHeads.ToArray());
    }
}
