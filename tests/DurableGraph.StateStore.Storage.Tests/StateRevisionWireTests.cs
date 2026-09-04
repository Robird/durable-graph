using System.Buffers;
using Atelia.Data;
using Atelia.DurableGraph.StateStore.Serialization;

namespace Atelia.DurableGraph.StateStore.Storage.Tests;

public sealed class StateRevisionWireTests {
    [Fact]
    public void Genesis_Base_has_stable_golden_bytes() {
        StateRevision revision = StateRevision.CreateBase(
            null,
            baseObjectIds: [1, 128],
            deltaObjectIds: [],
            externalObjectHeads: []);

        byte[] encoded = Encode(
            revision,
            new FileScope(1));

        Assert.Equal(
            [0x01, 0x01, 0x00, 0x02, 0x01, 0x80, 0x01, 0x00, 0x00],
            encoded);
    }

    [Fact]
    public void Delta_with_previous_file_parent_has_stable_golden_bytes() {
        StateRevision revision = StateRevision.CreateDelta(
            new FrameAddress(1, SizedPtr.Create(4, 4)),
            baseObjectIds: [3],
            deltaObjectIds: [],
            removedObjectIds: [2]);

        byte[] encoded = Encode(
            revision,
            new FileScope(2));

        Assert.Equal(
            [0x01, 0x02, 0x01, 0x01, 0x05, 0x01, 0x03, 0x00, 0x01, 0x02],
            encoded);
    }

    [Fact]
    public void Base_round_trip_restores_absolute_parent_and_external_heads() {
        StateRevision source = StateRevision.CreateBase(
            new FrameAddress(3, SizedPtr.Create(4, 32)),
            baseObjectIds: [9, 1],
            deltaObjectIds: [7, 3],
            externalObjectHeads: [
                new KeyValuePair<uint, FrameAddress>(
                    2,
                    new FrameAddress(2, SizedPtr.Create(64, 32))),
                new KeyValuePair<uint, FrameAddress>(
                    8,
                    new FrameAddress(3, SizedPtr.Create(96, 32))),
            ]);
        FileScope scope = new(4);

        byte[] encoded = Encode(source, scope);
        StateRevision roundTrip = StateRevisionWireReader.Read(
            encoded,
            scope);

        AssertRevisionEqual(source, roundTrip);
    }

    [Fact]
    public void Reader_accepts_handwritten_previous_file_parent_and_external_head() {
        byte[] encoded = [
            0x01, 0x01, 0x01,
            0x01, 0x05,
            0x01, 0x03,
            0x00,
            0x01, 0x02, 0x01, 0x05,
        ];
        FrameAddress expectedAddress = new(1, SizedPtr.Create(4, 4));

        StateRevision revision = StateRevisionWireReader.Read(
            encoded,
            new FileScope(2));

        Assert.Equal(expectedAddress, revision.ParentRevisionAddress);
        Assert.Equal([3u], revision.BaseObjectIds);
        Assert.Empty(revision.DeltaObjectIds);
        Assert.Equal(expectedAddress, revision.ExternalObjectHeads[2]);
    }

    [Fact]
    public void Default_scope_is_rejected_for_address_free_genesis_Base() {
        StateRevision revision = StateRevision.CreateBase(null, [], [], []);
        byte[] encoded = [0x01, 0x01, 0x00, 0x00, 0x00, 0x00];

        Assert.Throws<ArgumentOutOfRangeException>(() =>
            Encode(revision, default));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            StateRevisionWireReader.Read(encoded, default));
    }

    [Fact]
    public void Collection_count_above_provisional_limit_is_rejected_before_allocation() {
        ArrayBufferWriter<byte> buffer = new();
        BinaryPayloadWriter writer = new(buffer);
        writer.WriteSpan([0x01, 0x01, 0x00]);
        writer.WriteUInt32(StateRevisionWireFormat.MaxCollectionCount + 1u);

        InvalidDataException exception = Assert.Throws<InvalidDataException>(() =>
            StateRevisionWireReader.Read(
                buffer.WrittenSpan,
                new FileScope(1)));

        Assert.Contains("exceeds the provisional limit", exception.Message);
    }

    [Fact]
    public void Collection_count_within_limit_but_above_remaining_is_rejected_before_allocation() {
        ArrayBufferWriter<byte> buffer = new();
        BinaryPayloadWriter writer = new(buffer);
        writer.WriteSpan([0x01, 0x01, 0x00]);
        writer.WriteUInt32(StateRevisionWireFormat.MaxCollectionCount);

        InvalidDataException exception = Assert.Throws<InvalidDataException>(() =>
            StateRevisionWireReader.Read(
                buffer.WrittenSpan,
                new FileScope(1)));

        Assert.Contains("remaining payload bounds", exception.Message);
    }

    [Fact]
    public void Writer_rejects_collection_above_its_reader_limit() {
        StateRevision revision = StateRevision.CreateBase(
            null,
            Enumerable.Range(
                    1,
                    StateRevisionWireFormat.MaxCollectionCount + 1)
                .Select(value => (uint)value),
            [],
            []);

        Assert.Throws<InvalidDataException>(() =>
            Encode(
                revision,
                new FileScope(1)));
    }

    [Theory]
    [MemberData(nameof(MalformedPayloads))]
    public void Malformed_payload_fails_closed(byte[] payload) {
        Exception? exception = Record.Exception(() =>
            StateRevisionWireReader.Read(
                payload,
                new FileScope(2)));

        Assert.True(
            exception is InvalidDataException or EndOfStreamException,
            $"Unexpected exception: {exception}");
    }

    public static TheoryData<byte[]> MalformedPayloads => new() {
        Array.Empty<byte>(),
        new byte[] { 0x01 },
        new byte[] { 0x02 },
        new byte[] { 0x01, 0xff },
        new byte[] { 0x01, 0x01, 0xff },
        new byte[] { 0x01, 0x02, 0x00, 0x00, 0x00, 0x00 },
        new byte[] { 0x01, 0x01, 0x00, 0x00, 0x01, 0x02, 0x00 },
        new byte[] { 0x01, 0x01, 0x00, 0x80, 0x00 },
        new byte[] { 0x01, 0x01, 0x00, 0x01, 0x00, 0x00, 0x00 },
        new byte[] { 0x01, 0x01, 0x00, 0x02, 0x02, 0x01, 0x00, 0x00 },
        new byte[] { 0x01, 0x01, 0x00, 0x00, 0x00, 0x00, 0xff },
        new byte[] { 0x01, 0x01, 0x00, 0x7f },
        new byte[] { 0x01, 0x01, 0x00, 0x00, 0x00 },
        new byte[] {
            0x01, 0x01, 0x00,
            0x01, 0x01,
            0x00,
            0x01, 0x01, 0x01, 0x05,
        },
    };

    private static byte[] Encode(
        StateRevision revision,
        FileScope scope) {
        ArrayBufferWriter<byte> destination = new();
        StateRevisionWireWriter.Write(
            destination,
            revision,
            scope);
        return destination.WrittenSpan.ToArray();
    }

    private static void AssertRevisionEqual(
        StateRevision expected,
        StateRevision actual) {
        Assert.Equal(expected.ParentRevisionAddress, actual.ParentRevisionAddress);
        Assert.Equal(expected.ObjectHeadMapKind, actual.ObjectHeadMapKind);
        Assert.Equal(expected.BaseObjectIds, actual.BaseObjectIds);
        Assert.Equal(expected.DeltaObjectIds, actual.DeltaObjectIds);
        Assert.Equal(expected.RemovedObjectIds, actual.RemovedObjectIds);
        Assert.Equal(
            expected.ExternalObjectHeads.ToArray(),
            actual.ExternalObjectHeads.ToArray());
    }
}
