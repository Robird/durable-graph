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
        StateRevision expected = StateRevision.CreateBase(
            null,
            baseObjectIds: [1, 2, 3],
            deltaObjectIds: [],
            externalObjectHeads: []);

        FrameAddress address = store.Append(expected);
        StateRevision actual = store.Read(address);

        Assert.Equal(1u, address.FileNumber);
        AssertRevisionEqual(expected, actual);
        AssertHeads(
            store.ReadLiveObjectHeads(address),
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
            f1 = store.Append(StateRevision.CreateBase(
                null,
                baseObjectIds: [1, 2],
                deltaObjectIds: [],
                externalObjectHeads: []));
            AssertHeads(store.ReadLiveObjectHeads(f1), (1, f1), (2, f1));

            f2 = store.Append(StateRevision.CreateDelta(
                f1,
                baseObjectIds: [3],
                deltaObjectIds: [1],
                removedObjectIds: [2]));
            AssertHeads(store.ReadLiveObjectHeads(f2), (1, f2), (3, f2));

            f3 = store.Append(StateRevision.CreateDelta(
                f2,
                baseObjectIds: [],
                deltaObjectIds: [],
                removedObjectIds: []));
            AssertHeads(store.ReadLiveObjectHeads(f3), (1, f2), (3, f2));

            f4 = store.Append(StateRevision.CreateDelta(
                f3,
                baseObjectIds: [],
                deltaObjectIds: [],
                removedObjectIds: [1]));
            AssertHeads(store.ReadLiveObjectHeads(f4), (3, f2));

            f5 = store.Append(StateRevision.CreateDelta(
                f4,
                baseObjectIds: [1],
                deltaObjectIds: [],
                removedObjectIds: []));
            AssertHeads(store.ReadLiveObjectHeads(f5), (1, f5), (3, f2));
        }

        using SegmentStore reopened = SegmentStore.OpenReadOnlyExisting(
            storePath);
        StateRevisionStore coldStore = new(reopened);

        AssertHeads(coldStore.ReadLiveObjectHeads(f1), (1, f1), (2, f1));
        AssertHeads(coldStore.ReadLiveObjectHeads(f2), (1, f2), (3, f2));
        AssertHeads(coldStore.ReadLiveObjectHeads(f3), (1, f2), (3, f2));
        AssertHeads(coldStore.ReadLiveObjectHeads(f4), (3, f2));
        AssertHeads(coldStore.ReadLiveObjectHeads(f5), (1, f5), (3, f2));
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
            f1 = store.Append(StateRevision.CreateBase(
                null,
                baseObjectIds: [1, 2],
                deltaObjectIds: [],
                externalObjectHeads: []));
            f2 = store.Append(StateRevision.CreateDelta(
                f1,
                baseObjectIds: [3],
                deltaObjectIds: [1],
                removedObjectIds: [2]));
            f3 = store.Append(StateRevision.CreateBase(
                f2,
                baseObjectIds: [],
                deltaObjectIds: [1],
                externalObjectHeads: [
                    new KeyValuePair<uint, FrameAddress>(3, f2),
                ]));

            Assert.Equal(1u, f1.FileNumber);
            Assert.Equal(2u, f2.FileNumber);
            Assert.Equal(3u, f3.FileNumber);
            AssertHeads(store.ReadLiveObjectHeads(f2), (1, f2), (3, f2));
            AssertHeads(store.ReadLiveObjectHeads(f3), (1, f3), (3, f2));
        }

        using SegmentStore reopened = SegmentStore.OpenReadOnlyExisting(
            storePath,
            options);
        StateRevisionStore coldStore = new(reopened);
        StateRevision decodedDelta = coldStore.Read(f2);
        StateRevision decodedCheckpoint = coldStore.Read(f3);

        Assert.Equal(f1, decodedDelta.ParentRevisionAddress);
        AssertHeads(coldStore.ReadLiveObjectHeads(f2), (1, f2), (3, f2));
        Assert.Equal(f2, decodedCheckpoint.ParentRevisionAddress);
        Assert.Equal(f2, decodedCheckpoint.ExternalObjectHeads[3]);
        AssertHeads(coldStore.ReadLiveObjectHeads(f3), (1, f3), (3, f2));
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
                StateRevision.CreateBase(null, [1], [], []),
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
        StateRevision invalidForCurrentOrigin = StateRevision.CreateBase(
            new FrameAddress(2, SizedPtr.Create(4, 4)),
            [],
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
            StateRevision.CreateBase(null, [1], [], []));
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
            StateRevision.CreateBase(null, [1], [], []));
        StateRevision invalidForNextOrigin = StateRevision.CreateBase(
            new FrameAddress(3, SizedPtr.Create(4, 4)),
            [],
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
            StateRevision.CreateBase(null, [2], [], []));
        Assert.Equal(2u, segmentAfterFailure);
        Assert.Equal(segmentAfterFailure, appended.FileNumber);
        Assert.Equal(tailAfterFailure, appended.FrameTicket.Offset);
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
        Assert.Equal(expected.BaseObjectIds, actual.BaseObjectIds);
        Assert.Equal(expected.DeltaObjectIds, actual.DeltaObjectIds);
        Assert.Equal(expected.RemovedObjectIds, actual.RemovedObjectIds);
        Assert.Equal(
            expected.ExternalObjectHeads.ToArray(),
            actual.ExternalObjectHeads.ToArray());
    }

    private static void AssertHeads(
        IReadOnlyDictionary<uint, FrameAddress> actual,
        params (uint ObjectId, FrameAddress Head)[] expected) {
        Assert.Equal(
            expected,
            actual.Select(pair => (pair.Key, pair.Value)).ToArray());
    }
}
