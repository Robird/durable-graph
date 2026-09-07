using System.Buffers;
using Atelia.Data;
using Atelia.RbfSegmentStore;
using SegmentStore = Atelia.RbfSegmentStore.RbfSegmentStore;

namespace Atelia.DurableGraph.StateStore.Storage.Tests;

public sealed class StateRevisionStoreTests : IDisposable {
    private readonly List<string> _storePaths = [];

    [Fact]
    public void Append_and_Read_round_trip_one_exact_Revision_Frame() {
        string storePath = NewStorePath();
        using SegmentStore segments = SegmentStore.CreateNew(storePath);
        StateRevisionStore store = new(segments);
        StateRevision expected = StateRevision.CreateObjectHeadMapBase(
            null,
            localObjects: Records(1, 2, 3),
            externalObjectHeads: []);

        FrameAddress address = store.Append(expected);
        StateRevision actual = store.Read(address);

        Assert.Equal(1u, address.FileNumber);
        AssertRevisionEqual(expected, actual);
        AssertHeads(
            store.ReadLiveObjectHeadMap(address),
            (1, address),
            (2, address),
            (3, address));
    }

    [Fact]
    public void Reopen_cold_reads_each_persisted_live_Object_head_transition() {
        string storePath = NewStorePath();
        FrameAddress f1;
        FrameAddress f2;
        FrameAddress f3;
        FrameAddress f4;
        FrameAddress f5;
        using (SegmentStore segments = SegmentStore.CreateNew(storePath)) {
            StateRevisionStore store = new(segments);
            f1 = store.Append(StateRevision.CreateObjectHeadMapBase(
                null,
                localObjects: Records(1, 2),
                externalObjectHeads: []));
            AssertHeads(store.ReadLiveObjectHeadMap(f1), (1, f1), (2, f1));

            f2 = store.Append(StateRevision.CreateObjectHeadMapDelta(
                f1,
                localObjects: Records(3, 1),
                removedObjectIds: [2]));
            AssertHeads(store.ReadLiveObjectHeadMap(f2), (1, f2), (3, f2));

            f3 = store.Append(StateRevision.CreateObjectHeadMapDelta(
                f2,
                localObjects: Records(),
                removedObjectIds: []));
            AssertHeads(store.ReadLiveObjectHeadMap(f3), (1, f2), (3, f2));

            f4 = store.Append(StateRevision.CreateObjectHeadMapDelta(
                f3,
                localObjects: Records(),
                removedObjectIds: [1]));
            AssertHeads(store.ReadLiveObjectHeadMap(f4), (3, f2));

            f5 = store.Append(StateRevision.CreateObjectHeadMapDelta(
                f4,
                localObjects: Records(1),
                removedObjectIds: []));
            AssertHeads(store.ReadLiveObjectHeadMap(f5), (1, f5), (3, f2));
        }

        using SegmentStore reopened = SegmentStore.OpenReadOnlyExisting(
            storePath);
        StateRevisionStore coldStore = new(reopened);

        AssertHeads(coldStore.ReadLiveObjectHeadMap(f1), (1, f1), (2, f1));
        AssertHeads(coldStore.ReadLiveObjectHeadMap(f2), (1, f2), (3, f2));
        AssertHeads(coldStore.ReadLiveObjectHeadMap(f3), (1, f2), (3, f2));
        AssertHeads(coldStore.ReadLiveObjectHeadMap(f4), (3, f2));
        AssertHeads(coldStore.ReadLiveObjectHeadMap(f5), (1, f5), (3, f2));
    }

    [Fact]
    public void Tail_triggered_rollover_preserves_cross_Segment_parent_and_Base_heads() {
        string storePath = NewStorePath();
        RbfSegmentStoreOptions options = new() {
            NewStoreLayout = RbfSegmentStoreLayout.Flat,
            SegmentSizeThresholdBytes = 8,
        };
        FrameAddress f1;
        FrameAddress f2;
        FrameAddress f3;
        using (SegmentStore segments = SegmentStore.CreateNew(
            storePath,
            options)) {
            StateRevisionStore store = new(segments);
            f1 = store.Append(StateRevision.CreateObjectHeadMapBase(
                null,
                localObjects: Records(1, 2),
                externalObjectHeads: []));
            f2 = store.Append(StateRevision.CreateObjectHeadMapDelta(
                f1,
                localObjects: Records(3, 1),
                removedObjectIds: [2]));
            f3 = store.Append(StateRevision.CreateObjectHeadMapBase(
                f2,
                localObjects: Records(1),
                externalObjectHeads: [
                    new KeyValuePair<uint, FrameAddress>(3, f2),
                ]));

            Assert.Equal(1u, f1.FileNumber);
            Assert.Equal(2u, f2.FileNumber);
            Assert.Equal(3u, f3.FileNumber);
            AssertHeads(store.ReadLiveObjectHeadMap(f2), (1, f2), (3, f2));
            AssertHeads(store.ReadLiveObjectHeadMap(f3), (1, f3), (3, f2));
        }

        using SegmentStore reopened = SegmentStore.OpenReadOnlyExisting(
            storePath,
            options);
        StateRevisionStore coldStore = new(reopened);
        StateRevision decodedDelta = coldStore.Read(f2);
        StateRevision decodedCheckpoint = coldStore.Read(f3);

        Assert.Equal(f1, decodedDelta.ParentRevisionAddress);
        AssertHeads(coldStore.ReadLiveObjectHeadMap(f2), (1, f2), (3, f2));
        Assert.Equal(f2, decodedCheckpoint.ParentRevisionAddress);
        Assert.Equal(f2, decodedCheckpoint.ExternalObjectHeads[3]);
        AssertHeads(coldStore.ReadLiveObjectHeadMap(f3), (1, f3), (3, f2));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Exact_revision_reads_old_updated_inherited_deleted_and_reused_bodies_after_reopen(
        bool forceRollover) {
        string storePath = NewStorePath();
        RbfSegmentStoreOptions options = new() {
            NewStoreLayout = RbfSegmentStoreLayout.Flat,
            SegmentSizeThresholdBytes = forceRollover ? 8 : 64 * 1024 * 1024,
        };
        FrameAddress original;
        FrameAddress updated;
        FrameAddress removed;
        FrameAddress checkpoint;
        FrameAddress reused;
        using (SegmentStore segments = SegmentStore.CreateNew(storePath, options)) {
            StateRevisionStore store = new(segments);
            original = store.Append(StateRevision.CreateObjectHeadMapBase(null, [
                ObjectVersionRecord.CreateBase(1, [10, 11]),
                ObjectVersionRecord.CreateBase(2, [20, 21, 22]),
            ], []));
            updated = store.Append(StateRevision.CreateObjectHeadMapDelta(original, [
                ObjectVersionRecord.CreateBase(1, [12, 13, 14]),
            ], []));
            removed = store.Append(StateRevision.CreateObjectHeadMapDelta(updated, [], [2]));
            checkpoint = store.Append(StateRevision.CreateObjectHeadMapBase(removed, [], [
                new KeyValuePair<uint, FrameAddress>(1, updated),
            ]));
            reused = store.Append(StateRevision.CreateObjectHeadMapDelta(checkpoint, [
                ObjectVersionRecord.CreateBase(2, [90]),
            ], []));

            Assert.Equal(forceRollover ? 5u : 1u, reused.FileNumber);
            Verify(store);
        }

        using SegmentStore reopened = SegmentStore.OpenReadOnlyExisting(storePath, options);
        Verify(new StateRevisionStore(reopened));

        void Verify(StateRevisionStore store) {
            Assert.Equal(new byte[] { 10, 11 }, store.ReadObjectBaseBody(original, 1));
            Assert.Equal(new byte[] { 20, 21, 22 }, store.ReadObjectBaseBody(original, 2));
            Assert.Equal(new byte[] { 12, 13, 14 }, store.ReadObjectBaseBody(updated, 1));
            Assert.Equal(new byte[] { 20, 21, 22 }, store.ReadObjectBaseBody(updated, 2));
            AssertHeads(store.ReadLiveObjectHeadMap(updated), (1, updated), (2, original));
            Assert.Throws<InvalidDataException>(() => store.ReadObjectBaseBody(removed, 2));
            Assert.Throws<InvalidDataException>(() => store.ReadObjectBaseBody(checkpoint, 2));
            Assert.Equal(new byte[] { 12, 13, 14 }, store.ReadObjectBaseBody(checkpoint, 1));
            AssertHeads(store.ReadLiveObjectHeadMap(checkpoint), (1, updated));
            Assert.Equal(new byte[] { 12, 13, 14 }, store.ReadObjectBaseBody(reused, 1));
            Assert.Equal(new byte[] { 90 }, store.ReadObjectBaseBody(reused, 2));
            AssertHeads(store.ReadLiveObjectHeadMap(reused), (1, updated), (2, reused));
        }
    }

    [Fact]
    public void Object_read_rejects_external_locator_without_local_record_even_when_it_inherits_the_id() {
        string storePath = NewStorePath();
        using SegmentStore segments = SegmentStore.CreateNew(storePath);
        StateRevisionStore store = new(segments);
        FrameAddress original = store.Append(StateRevision.CreateObjectHeadMapBase(
            null, [ObjectVersionRecord.CreateBase(1, [42])], []));
        FrameAddress inherited = store.Append(StateRevision.CreateObjectHeadMapDelta(original, [], []));
        FrameAddress invalidCheckpoint = store.Append(StateRevision.CreateObjectHeadMapBase(inherited, [], [
            new KeyValuePair<uint, FrameAddress>(1, inherited),
        ]));

        Assert.Equal(new byte[] { 42 }, store.ReadObjectBaseBody(inherited, 1));
        AssertHeads(store.ReadLiveObjectHeadMap(invalidCheckpoint), (1, inherited));
        Assert.Throws<InvalidDataException>(() => store.ReadObjectBaseBody(invalidCheckpoint, 1));
        Assert.Equal(new byte[] { 42 }, store.ReadObjectBaseBody(original, 1));
    }

    [Fact]
    public void Object_read_returns_owned_bytes_independent_of_inputs_other_reads_and_store_lifetime() {
        string storePath = NewStorePath();
        byte[] source = [1, 2, 3, 4];
        StateRevision revision = StateRevision.CreateObjectHeadMapBase(
            null, [ObjectVersionRecord.CreateBase(7, source)], []);
        source.AsSpan().Fill(99);
        byte[] retainedBody;
        StateRevision retainedRevision;
        using (SegmentStore segments = SegmentStore.CreateNew(storePath)) {
            StateRevisionStore store = new(segments);
            FrameAddress address = store.Append(revision);
            retainedBody = store.ReadObjectBaseBody(address, 7);
            retainedRevision = store.Read(address);
            byte[] changedResult = store.ReadObjectBaseBody(address, 7);
            changedResult.AsSpan().Fill(88);
            Assert.Equal(new byte[] { 1, 2, 3, 4 }, store.ReadObjectBaseBody(address, 7));

            // Exercise new pooled reads after the first Frame's pool lease ended.
            for (byte value = 10; value < 42; value++) {
                FrameAddress other = store.Append(StateRevision.CreateObjectHeadMapBase(null, [
                    ObjectVersionRecord.CreateBase(7, [value, value, value, value]),
                ], []));
                Assert.Equal(new byte[] { value, value, value, value }, store.ReadObjectBaseBody(other, 7));
            }
        }

        Assert.Equal(new byte[] { 1, 2, 3, 4 }, retainedBody);
        Assert.Equal(new byte[] { 1, 2, 3, 4 }, retainedRevision.LocalObjects[0].Body.ToArray());
        Assert.Equal(new byte[] { 1, 2, 3, 4 }, revision.LocalObjects[0].Body.ToArray());
    }

    [Fact]
    public void Object_read_distinguishes_empty_body_from_missing_and_rejects_invalid_inputs() {
        string storePath = NewStorePath();
        using SegmentStore segments = SegmentStore.CreateNew(storePath);
        StateRevisionStore store = new(segments);
        FrameAddress address = store.Append(StateRevision.CreateObjectHeadMapBase(null, [
            ObjectVersionRecord.CreateBase(uint.MaxValue, []),
        ], []));

        Assert.Empty(store.ReadObjectBaseBody(address, uint.MaxValue));
        Assert.Throws<InvalidDataException>(() => store.ReadObjectBaseBody(address, 1));
        Assert.Throws<ArgumentOutOfRangeException>(() => store.ReadObjectBaseBody(address, 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => store.ReadObjectBaseBody(default, 1));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            store.ReadObjectBaseBody(new FrameAddress(1, default), 1));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            store.ReadObjectBaseBody(new FrameAddress(0, address.FrameTicket), 1));
    }

    [Fact]
    public void Read_rejects_a_valid_Rbf_Frame_with_the_wrong_tag() {
        string storePath = NewStorePath();
        using SegmentStore segments = SegmentStore.CreateNew(storePath);
        SizedPtr ticket;
        using (RbfSegmentWriterLease writer = segments.OpenActiveWriter()) {
            ticket = writer.File.Append(0x01020304, [1, 2, 3, 4]).Unwrap();
        }

        StateRevisionStore store = new(segments);
        FrameAddress address = new(1, ticket);

        Assert.Throws<InvalidDataException>(() => store.Read(address));
    }

    [Fact]
    public void Read_rejects_a_StateRevision_Frame_with_TailMeta() {
        string storePath = NewStorePath();
        using SegmentStore segments = SegmentStore.CreateNew(storePath);
        SizedPtr ticket;
        using (RbfSegmentWriterLease writer = segments.OpenActiveWriter()) {
            ArrayBufferWriter<byte> payload = new();
            StateRevisionWireWriter.Write(
                payload,
                StateRevision.CreateObjectHeadMapBase(null, Records(1), []),
                new FileScope(writer.SegmentNumber));
            ticket = writer.File.Append(
                StateRevisionWireFormat.RbfTag,
                payload.WrittenSpan,
                tailMeta: [1, 2, 3, 4]).Unwrap();
        }

        StateRevisionStore store = new(segments);

        Assert.Throws<InvalidDataException>(() =>
            store.Read(new FrameAddress(1, ticket)));
    }

    [Fact]
    public void Append_encoding_failure_does_not_append_a_partial_Frame_and_releases_lease() {
        string storePath = NewStorePath();
        using SegmentStore segments = SegmentStore.CreateNew(storePath);
        StateRevisionStore store = new(segments);
        StateRevision invalidForCurrentOrigin = StateRevision.CreateObjectHeadMapBase(
            new FrameAddress(2, SizedPtr.Create(4, 4)),
            [],
            []);
        long tailBefore;
        using (RbfSegmentWriterLease writer = segments.OpenActiveWriter()) {
            tailBefore = writer.File.TailOffset;
        }

        Assert.Throws<InvalidDataException>(() =>
            store.Append(invalidForCurrentOrigin));

        using (RbfSegmentWriterLease writer = segments.OpenActiveWriter()) {
            Assert.Equal(tailBefore, writer.File.TailOffset);
        }

        FrameAddress appended = store.Append(
            StateRevision.CreateObjectHeadMapBase(null, Records(1), []));
        Assert.Equal(1u, appended.FileNumber);
    }

    [Fact]
    public void Encoding_failure_after_rollover_leaves_a_reusable_header_only_Segment() {
        string storePath = NewStorePath();
        RbfSegmentStoreOptions options = new() {
            NewStoreLayout = RbfSegmentStoreLayout.Flat,
            SegmentSizeThresholdBytes = 8,
        };
        using SegmentStore segments = SegmentStore.CreateNew(storePath, options);
        StateRevisionStore store = new(segments);
        FrameAddress first = store.Append(
            StateRevision.CreateObjectHeadMapBase(null, Records(1), []));
        StateRevision invalidForNextOrigin = StateRevision.CreateObjectHeadMapBase(
            new FrameAddress(3, SizedPtr.Create(4, 4)),
            [],
            []);

        Assert.Equal(1u, first.FileNumber);
        Assert.Throws<InvalidDataException>(() =>
            store.Append(invalidForNextOrigin));

        uint segmentAfterFailure;
        long tailAfterFailure;
        using (RbfSegmentWriterLease writer = segments.OpenActiveWriter()) {
            segmentAfterFailure = writer.SegmentNumber;
            tailAfterFailure = writer.File.TailOffset;
        }

        FrameAddress appended = store.Append(
            StateRevision.CreateObjectHeadMapBase(null, Records(2), []));
        Assert.Equal(2u, segmentAfterFailure);
        Assert.Equal(segmentAfterFailure, appended.FileNumber);
        Assert.Equal(tailAfterFailure, appended.FrameTicket.Offset);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Encoding_failure_after_buffering_body_aborts_frame_and_reuses_writer(
        bool forceRollover) {
        string storePath = NewStorePath();
        RbfSegmentStoreOptions options = new() {
            NewStoreLayout = RbfSegmentStoreLayout.Flat,
            SegmentSizeThresholdBytes = forceRollover ? 8 : 64 * 1024 * 1024,
        };
        using SegmentStore segments = SegmentStore.CreateNew(storePath, options);
        StateRevisionStore store = new(segments);
        long headerOnlyTail;
        using (RbfSegmentWriterLease writer = segments.OpenActiveWriter()) {
            headerOnlyTail = writer.File.TailOffset;
        }

        FrameAddress first = store.Append(StateRevision.CreateObjectHeadMapBase(null, Records(1), []));
        long expectedTail = headerOnlyTail;
        if (!forceRollover) {
            using RbfSegmentWriterLease writer = segments.OpenActiveWriter();
            expectedTail = writer.File.TailOffset;
        }

        uint expectedSegment = forceRollover ? 2u : 1u;
        byte[] body = new byte[100 * 1024];
        for (int index = 0; index < body.Length; index++) {
            body[index] = (byte)index;
        }

        // External addresses follow all local bodies on wire, so this failure
        // happens after the nonempty body has been buffered into the Frame builder.
        StateRevision invalid = StateRevision.CreateObjectHeadMapBase(null, [
            ObjectVersionRecord.CreateBase(2, body),
        ], [
            new KeyValuePair<uint, FrameAddress>(3,
                new FrameAddress(expectedSegment + 1, SizedPtr.Create(4, 4))),
        ]);
        Assert.Throws<InvalidDataException>(() => store.Append(invalid));

        using (RbfSegmentWriterLease writer = segments.OpenActiveWriter()) {
            Assert.Equal(expectedSegment, writer.SegmentNumber);
            Assert.Equal(expectedTail, writer.File.TailOffset);
        }

        FrameAddress recovered = store.Append(StateRevision.CreateObjectHeadMapBase(null, [
            ObjectVersionRecord.CreateBase(2, body),
        ], []));
        Assert.Equal(expectedSegment, recovered.FileNumber);
        Assert.Equal(expectedTail, recovered.FrameTicket.Offset);
        Assert.Equal(body, store.ReadObjectBaseBody(recovered, 2));
        Assert.Equal(new byte[] { 1 }, store.ReadObjectBaseBody(first, 1));
    }

    public void Dispose() {
        foreach (string storePath in _storePaths) {
            if (Directory.Exists(storePath)) {
                Directory.Delete(storePath, recursive: true);
            }
        }
    }

    private string NewStorePath() {
        string path = Path.Combine(
            Path.GetTempPath(),
            $"durable-graph-state-store-{Guid.NewGuid():N}");
        _storePaths.Add(path);
        return path;
    }

    private static void AssertRevisionEqual(
        StateRevision expected,
        StateRevision actual) {
        Assert.Equal(expected.ParentRevisionAddress, actual.ParentRevisionAddress);
        Assert.Equal(expected.ObjectHeadMapKind, actual.ObjectHeadMapKind);
        Assert.Equal(expected.LocalObjectIds, actual.LocalObjectIds);
        Assert.Equal(expected.LocalObjects.Count, actual.LocalObjects.Count);
        for (int index = 0; index < expected.LocalObjects.Count; index++) {
            Assert.Equal(expected.LocalObjects[index].Kind, actual.LocalObjects[index].Kind);
            Assert.Equal(expected.LocalObjects[index].PriorAddress, actual.LocalObjects[index].PriorAddress);
            Assert.Equal(
                expected.LocalObjects[index].Body.ToArray(),
                actual.LocalObjects[index].Body.ToArray());
        }
        Assert.Equal(expected.RemovedObjectIds, actual.RemovedObjectIds);
        Assert.Equal(
            expected.ExternalObjectHeads.ToArray(),
            actual.ExternalObjectHeads.ToArray());
    }

    private static ObjectVersionRecord[] Records(params uint[] ids) =>
        ids.Select(id => ObjectVersionRecord.CreateBase(id, [(byte)id])).ToArray();

    private static void AssertHeads(
        IReadOnlyDictionary<uint, FrameAddress> actual,
        params (uint ObjectId, FrameAddress Head)[] expected) {
        Assert.Equal(
            expected,
            actual.Select(pair => (pair.Key, pair.Value)).ToArray());
    }
}
