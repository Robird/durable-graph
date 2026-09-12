using System.Buffers;
using Atelia.Data;
using SegmentStore = Atelia.RbfSegmentStore.RbfSegmentStore;

namespace Atelia.DurableGraph.StateStore.Storage.Tests;

public sealed class Db069StorageAccountingTests : IDisposable {
    private readonly string _path = Path.Combine(Path.GetTempPath(), $"durable-db069-storage-{Guid.NewGuid():N}");

    [Theory]
    [InlineData(0u)]
    [InlineData(127u)]
    [InlineData(128u)]
    [InlineData(16383u)]
    [InlineData(16384u)]
    [InlineData(2097151u)]
    [InlineData(2097152u)]
    [InlineData(268435455u)]
    [InlineData(268435456u)]
    [InlineData(uint.MaxValue - 1)]
    public void Exact_Delta_payload_matches_wire_across_distance_ticket_and_body_boundaries(uint distance) {
        FileScope scope = new(checked(distance + 1));
        foreach (ulong ticket in new[] { 127UL, 129UL, 16383UL, 16385UL, ulong.MaxValue }) {
            FrameAddress prior = new(1, SizedPtr.Deserialize(ticket));
            foreach (int bodyLength in new[] { 0, 127, 128, 16383, 16384 }) {
                StateRevision revision = StateRevision.CreateObjectHeadMapDelta(prior,
                    [ObjectVersionRecord.CreateDelta(uint.MaxValue, prior, new byte[bodyLength])], []);
                ArrayBufferWriter<byte> buffer = new();
                StateRevisionWireWriter.Write(buffer, revision, scope);
                StateRevision decoded = StateRevisionWireReader.Read(buffer.WrittenSpan, scope);
                long actual = decoded.LocalObjects[0].EncodedPayloadBytes!.Value;
                Assert.Equal(actual, ObjectVersionPayloadSize.GetDeltaPayloadBytes(bodyLength, prior, scope));
                Assert.InRange(ObjectVersionPayloadSize.EstimateDeltaPayloadBytesUpperBound(bodyLength, prior) - actual, 0L, 4L);
            }
        }
    }

    [Fact]
    public void Exact_Delta_payload_uses_long_arithmetic_and_rejects_invalid_parameters() {
        FrameAddress prior = new(1, SizedPtr.Deserialize(ulong.MaxValue));
        FileScope scope = new(uint.MaxValue);
        Assert.Equal(2147483668L, ObjectVersionPayloadSize.GetDeltaPayloadBytes(int.MaxValue, prior, scope));
        Assert.Throws<ArgumentOutOfRangeException>(() => ObjectVersionPayloadSize.GetDeltaPayloadBytes(-1, prior, scope));
        Assert.Throws<ArgumentOutOfRangeException>(() => ObjectVersionPayloadSize.GetDeltaPayloadBytes(0, default, scope));
        Assert.Throws<ArgumentOutOfRangeException>(() => ObjectVersionPayloadSize.GetDeltaPayloadBytes(0, prior, default));
        FrameAddress future = new(2, SizedPtr.Create(4, 4));
        Assert.Throws<InvalidDataException>(() => ObjectVersionPayloadSize.GetDeltaPayloadBytes(0, future, new(1)));
    }

    [Theory]
    [InlineData(0L)]
    [InlineData(StateRevisionStore.DefaultReadCacheBudgetBytes)]
    public void Object_chain_counters_measure_traversal_even_when_all_records_are_cached(long cacheBudget) {
        using SegmentStore segments = SegmentStore.CreateNew(_path);
        FrameAddress head = WriteHistory(segments);
        using StateRevisionStore store = new(segments, cacheBudget);
        Assert.Equal(default, store.TraversalStatistics);

        ObjectVersionChain first = store.ReadObjectVersionChain(head, 1);
        Assert.Equal(3, first.Records.Count);
        Assert.Equal(new StateRevisionTraversalStatistics(1, 3, 8), store.TraversalStatistics);
        long decodesAfterFirst = store.ReadCacheStatistics.RevisionDecodes;

        ObjectVersionChain second = store.ReadObjectVersionChain(head, 1);
        Assert.Equal(first.ReconstructionPayloadBytes, second.ReconstructionPayloadBytes);
        Assert.Equal(new StateRevisionTraversalStatistics(2, 6, cacheBudget == 0 ? 16 : 8), store.TraversalStatistics);
        if (cacheBudget != 0) {
            Assert.Equal(decodesAfterFirst, store.ReadCacheStatistics.RevisionDecodes);
        } else {
            Assert.True(store.ReadCacheStatistics.RevisionDecodes > decodesAfterFirst);
        }
    }

    [Fact]
    public void Map_replay_counts_raw_cache_hits_but_complete_map_hits_do_not_replay() {
        using SegmentStore segments = SegmentStore.CreateNew(_path);
        FrameAddress head = WriteHistory(segments);
        using StateRevisionStore store = new(segments);
        _ = store.Read(head);
        Assert.Equal(default, store.TraversalStatistics);

        _ = store.ReadLiveObjectHeadMap(head);
        Assert.Equal(new StateRevisionTraversalStatistics(0, 0, 4), store.TraversalStatistics);
        Assert.Equal(1L, store.ReadCacheStatistics.RevisionHits);
        Assert.Equal(4L, store.ReadCacheStatistics.RevisionDecodes);

        _ = store.ReadLiveObjectHeadMap(head);
        Assert.Equal(new StateRevisionTraversalStatistics(0, 0, 4), store.TraversalStatistics);
        Assert.Equal(1L, store.ReadCacheStatistics.MapHits);
    }

    [Fact]
    public void Invalid_arguments_do_not_start_chain_reads_but_missing_membership_does() {
        using SegmentStore segments = SegmentStore.CreateNew(_path);
        FrameAddress head = WriteHistory(segments);
        using StateRevisionStore store = new(segments);
        Assert.Throws<ArgumentOutOfRangeException>(() => store.ReadObjectVersionChain(default, 1));
        Assert.Throws<ArgumentOutOfRangeException>(() => store.ReadObjectVersionChain(head, 0));
        Assert.Equal(default, store.TraversalStatistics);

        Assert.Throws<InvalidDataException>(() => store.ReadObjectVersionChain(head, 2));
        Assert.Equal(new StateRevisionTraversalStatistics(1, 0, 4), store.TraversalStatistics);
        store.Dispose();
        Assert.Throws<ObjectDisposedException>(() => store.ReadObjectVersionChain(head, 1));
        Assert.Equal(new StateRevisionTraversalStatistics(1, 0, 4), store.TraversalStatistics);
    }

    private static FrameAddress WriteHistory(SegmentStore segments) {
        using StateRevisionStore writer = new(segments);
        FrameAddress original = writer.Append(StateRevision.CreateObjectHeadMapBase(null,
            [ObjectVersionRecord.CreateBase(1, [10])], []));
        FrameAddress delta = writer.Append(StateRevision.CreateObjectHeadMapDelta(original,
            [ObjectVersionRecord.CreateDelta(1, original, [11])], []));
        FrameAddress unchanged = writer.Append(StateRevision.CreateObjectHeadMapDelta(delta, [], []));
        return writer.Append(StateRevision.CreateObjectHeadMapDelta(unchanged,
            [ObjectVersionRecord.CreateDelta(1, delta, [12])], []));
    }

    public void Dispose() {
        string resolved = Path.GetFullPath(_path);
        Assert.Equal(Path.TrimEndingDirectorySeparator(Path.GetFullPath(Path.GetTempPath())), Path.GetDirectoryName(resolved), ignoreCase: true);
        Assert.StartsWith("durable-db069-storage-", Path.GetFileName(resolved));
        if (Directory.Exists(resolved)) Directory.Delete(resolved, recursive: true);
    }
}
