using System.Buffers;
using System.Collections;
using Atelia.Data;
using Atelia.RbfSegmentStore;
using SegmentStore = Atelia.RbfSegmentStore.RbfSegmentStore;

namespace Atelia.DurableGraph.StateStore.Storage.Tests;

public sealed class ObjectVersionChainStoreTests : IDisposable {
    private readonly string _temporaryRoot = Path.GetFullPath(Path.GetTempPath());
    private readonly List<string> _storePaths = [];

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Exact_chains_skip_unchanged_revisions_and_survive_reopen_with_owned_bytes(bool rollover) {
        string path = NewStorePath();
        RbfSegmentStoreOptions options = Options(rollover);
        FrameAddress original;
        FrameAddress firstDelta;
        FrameAddress unchanged;
        FrameAddress secondDelta;
        ObjectVersionChain retained;
        using (SegmentStore segments = SegmentStore.CreateNew(path, options)) {
            StateRevisionStore store = new(segments);
            byte[] source = [10, 11];
            original = store.Append(StateRevision.CreateBase(null, [B(1, source), B(2, [20])], []));
            source.AsSpan().Fill(99);
            firstDelta = store.Append(StateRevision.CreateDelta(original, [D(1, original, [12])], []));
            unchanged = store.Append(StateRevision.CreateDelta(firstDelta, [], []));
            // ObjectHeadMap Base and ObjectVersion Delta are independent choices.
            secondDelta = store.Append(StateRevision.CreateBase(unchanged, [D(1, firstDelta, [13, 14])], [new(2, original)]));
            Assert.Equal(rollover ? 4u : 1u, secondDelta.FileNumber);
            retained = Verify(store);
        }

        using (SegmentStore reopened = SegmentStore.OpenReadOnlyExisting(path, options)) {
            _ = Verify(new(reopened));
        }

        Assert.Equal(new byte[] { 10, 11 }, retained.Records[0].Record.Body.ToArray());
        Assert.False(retained.Records is IList);
        Assert.False(retained.Records is ICollection);
        Assert.False(retained.Records is IList<ObjectVersionChainEntry>);
        byte[] externalCopy = retained.Records[1].Record.Body.ToArray();
        externalCopy[0] = 255;
        Assert.Equal(new byte[] { 12 }, retained.Records[1].Record.Body.ToArray());

        ObjectVersionChain Verify(StateRevisionStore store) {
            ObjectVersionChain chain = store.ReadObjectVersionChain(secondDelta, 1);
            Assert.Equal(1u, chain.ObjectId);
            Assert.Equal(secondDelta, chain.HeadAddress);
            Assert.Equal(new[] { original, firstDelta, secondDelta }, chain.Records.Select(x => x.Address));
            Assert.Equal(new[] { ObjectVersionKind.Base, ObjectVersionKind.Delta, ObjectVersionKind.Delta }, chain.Records.Select(x => x.Record.Kind));
            Assert.Equal(new byte[] { 10, 11 }, chain.Records[0].Record.Body.ToArray());
            Assert.Equal(new byte[] { 12 }, chain.Records[1].Record.Body.ToArray());
            Assert.Equal(new byte[] { 13, 14 }, chain.Records[2].Record.Body.ToArray());
            AssertCosts(chain);
            Assert.Equal(original, Assert.Single(store.ReadObjectVersionChain(original, 1).Records).Address);
            Assert.Equal(firstDelta, store.ReadObjectVersionChain(unchanged, 1).HeadAddress);
            Assert.Equal(new[] { original, firstDelta }, store.ReadObjectVersionChain(unchanged, 1).Records.Select(x => x.Address));
            Assert.Equal(original, Assert.Single(store.ReadObjectVersionChain(secondDelta, 2).Records).Address);
            Assert.Throws<InvalidDataException>(() => store.ReadObjectBase(secondDelta, 1));
            Assert.Equal(new byte[] { 20 }, store.ReadObjectBase(secondDelta, 2));
            return chain;
        }
    }

    [Fact]
    public void Append_rejects_stale_other_branch_and_wrong_ticket_priors_before_writing() {
        using SegmentStore segments = SegmentStore.CreateNew(NewStorePath());
        StateRevisionStore store = new(segments);
        FrameAddress original = store.Append(StateRevision.CreateBase(null, [B(1, [1])], []));
        FrameAddress updated = store.Append(StateRevision.CreateDelta(original, [D(1, original, [2])], []));
        FrameAddress otherBranch = store.Append(StateRevision.CreateDelta(original, [B(1, [3])], []));
        FrameAddress wrongTicket = new(updated.FileNumber, SizedPtr.Create(updated.FrameTicket.Offset, updated.FrameTicket.Length + 4));
        foreach (FrameAddress prior in new[] { original, otherBranch, wrongTicket }) {
            AssertAppendRejectedWithoutWrite(segments, store, StateRevision.CreateDelta(updated, [D(1, prior, [4])], []));
        }
        FrameAddress valid = store.Append(StateRevision.CreateDelta(updated, [D(1, updated, [4])], []));
        Assert.Equal(3, store.ReadObjectVersionChain(valid, 1).Records.Count);
    }

    [Fact]
    public void Failed_multi_object_preflight_does_not_even_trigger_pending_rollover() {
        using SegmentStore segments = SegmentStore.CreateNew(NewStorePath(), Options(true));
        StateRevisionStore store = new(segments);
        FrameAddress original = store.Append(StateRevision.CreateBase(null, [B(1, [1]), B(2, [2])], []));
        FrameAddress updated = store.Append(StateRevision.CreateDelta(original, [B(2, [20])], []));
        uint activeBefore = segments.ActiveSegmentNumber;
        StateRevision invalid = StateRevision.CreateDelta(updated, [D(1, original, [3]), D(2, original, [4])], []);
        Assert.Throws<InvalidDataException>(() => store.Append(invalid));
        Assert.Equal(activeBefore, segments.ActiveSegmentNumber);
        FrameAddress valid = store.Append(StateRevision.CreateDelta(updated, [D(1, original, [3]), D(2, updated, [4])], []));
        Assert.Equal(activeBefore + 1, valid.FileNumber);
        Assert.Equal(new[] { original, valid }, store.ReadObjectVersionChain(valid, 1).Records.Select(x => x.Address));
        Assert.Equal(new[] { updated, valid }, store.ReadObjectVersionChain(valid, 2).Records.Select(x => x.Address));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Read_checks_edges_even_for_frames_that_bypass_Append(bool wrongBranch) {
        using SegmentStore segments = SegmentStore.CreateNew(NewStorePath());
        StateRevisionStore store = new(segments);
        FrameAddress original = store.Append(StateRevision.CreateBase(null, [B(1, [1])], []));
        FrameAddress updated = store.Append(StateRevision.CreateDelta(original, [D(1, original, [2])], []));
        FrameAddress otherBranch = store.Append(StateRevision.CreateDelta(original, [B(1, [3])], []));
        FrameAddress corrupt = RawAppend(segments, StateRevision.CreateDelta(updated, [D(1, wrongBranch ? otherBranch : original, [4])], []));
        Assert.Equal(corrupt, store.ReadLiveObjectHeads(corrupt)[1]);
        Assert.Equal(ObjectVersionKind.Delta, Assert.Single(store.Read(corrupt).LocalObjects).Kind);
        Assert.Throws<InvalidDataException>(() => store.ReadObjectVersionChain(corrupt, 1));
        // Append intentionally checks only the immediate edge, not the prior's ancestry.
        FrameAddress child = store.Append(StateRevision.CreateDelta(corrupt, [D(1, corrupt, [5])], []));
        Assert.Throws<InvalidDataException>(() => store.ReadObjectVersionChain(child, 1));
    }

    [Fact]
    public void Prior_must_have_a_local_record_and_cannot_fall_back_to_its_parent() {
        using SegmentStore segments = SegmentStore.CreateNew(NewStorePath());
        StateRevisionStore store = new(segments);
        FrameAddress original = store.Append(StateRevision.CreateBase(null, [B(1, [1])], []));
        FrameAddress unchanged = store.Append(StateRevision.CreateDelta(original, [], []));
        FrameAddress invalidMap = store.Append(StateRevision.CreateBase(unchanged, [], [new(1, unchanged)]));
        Assert.Equal(unchanged, store.ReadLiveObjectHeads(invalidMap)[1]);
        Assert.Throws<InvalidDataException>(() => store.ReadObjectVersionChain(invalidMap, 1));
        StateRevision delta = StateRevision.CreateDelta(invalidMap, [D(1, unchanged, [2])], []);
        AssertAppendRejectedWithoutWrite(segments, store, delta);
        FrameAddress corrupt = RawAppend(segments, delta);
        Assert.Throws<InvalidDataException>(() => store.ReadObjectVersionChain(corrupt, 1));
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void Read_rejects_self_and_forward_parent_or_prior(bool badParent, bool forward) {
        using SegmentStore segments = SegmentStore.CreateNew(NewStorePath());
        StateRevisionStore store = new(segments);
        FrameAddress original = store.Append(StateRevision.CreateBase(null, [B(1, [1])], []));
        FrameAddress corrupt;
        using (RbfSegmentWriterLease writer = segments.OpenActiveWriter()) {
            FrameAddress nonEarlier = new(writer.SegmentNumber, SizedPtr.Create(writer.File.TailOffset + (forward ? 4 : 0), 4));
            StateRevision revision = StateRevision.CreateDelta(badParent ? nonEarlier : original,
                [D(1, badParent ? original : nonEarlier, [2])], []);
            corrupt = RawAppend(writer, revision);
        }
        Assert.Single(store.Read(corrupt).LocalObjects);
        Assert.Throws<InvalidDataException>(() => store.ReadObjectVersionChain(corrupt, 1));
    }

    [Fact]
    public void Removed_id_requires_new_Base_and_later_Delta_cannot_rejoin_old_occupant() {
        using SegmentStore segments = SegmentStore.CreateNew(NewStorePath());
        StateRevisionStore store = new(segments);
        FrameAddress old = store.Append(StateRevision.CreateBase(null, [B(1, [1])], []));
        FrameAddress removed = store.Append(StateRevision.CreateDelta(old, [], [1]));
        StateRevision invalidInsert = StateRevision.CreateDelta(removed, [D(1, old, [2])], []);
        AssertAppendRejectedWithoutWrite(segments, store, invalidInsert);
        Assert.Throws<InvalidDataException>(() => store.ReadObjectVersionChain(RawAppend(segments, invalidInsert), 1));
        Assert.Throws<InvalidDataException>(() => store.ReadObjectVersionChain(removed, 1));
        FrameAddress fresh = store.Append(StateRevision.CreateDelta(removed, [B(1, [90])], []));
        AssertAppendRejectedWithoutWrite(segments, store, StateRevision.CreateDelta(fresh, [D(1, old, [91])], []));
        FrameAddress updated = store.Append(StateRevision.CreateDelta(fresh, [D(1, fresh, [91])], []));
        Assert.Equal(new[] { fresh, updated }, store.ReadObjectVersionChain(updated, 1).Records.Select(x => x.Address));
        Assert.Equal(new byte[] { 1 }, Assert.Single(store.ReadObjectVersionChain(old, 1).Records).Record.Body.ToArray());
    }

    [Fact]
    public void Complete_map_can_reselect_old_head_as_a_shallow_declaration() {
        using SegmentStore segments = SegmentStore.CreateNew(NewStorePath());
        StateRevisionStore store = new(segments);
        FrameAddress old = store.Append(StateRevision.CreateBase(null, [B(1, [1])], []));
        FrameAddress removed = store.Append(StateRevision.CreateDelta(old, [], [1]));
        FrameAddress fresh = store.Append(StateRevision.CreateDelta(removed, [B(1, [90])], []));
        FrameAddress reselected = store.Append(StateRevision.CreateBase(fresh, [], [new(1, old)]));
        FrameAddress delta = store.Append(StateRevision.CreateDelta(reselected, [D(1, old, [2])], []));
        // Agreement with exact Parent is not global entity identity authentication.
        Assert.Equal(new[] { old, delta }, store.ReadObjectVersionChain(delta, 1).Records.Select(x => x.Address));
        Assert.Equal(fresh, Assert.Single(store.ReadObjectVersionChain(fresh, 1).Records).Address);
    }

    [Fact]
    public void New_Base_stops_content_validation_and_resets_H_even_if_older_Delta_edge_is_invalid() {
        using SegmentStore segments = SegmentStore.CreateNew(NewStorePath());
        StateRevisionStore store = new(segments);
        FrameAddress original = store.Append(StateRevision.CreateBase(null, [B(1, new byte[128])], []));
        FrameAddress validDelta = store.Append(StateRevision.CreateDelta(original, [D(1, original, [1])], []));
        FrameAddress corrupt = RawAppend(segments, StateRevision.CreateDelta(validDelta, [D(1, original, [2])], []));
        Assert.Throws<InvalidDataException>(() => store.ReadObjectVersionChain(corrupt, 1));
        FrameAddress reset = store.Append(StateRevision.CreateDelta(corrupt, [B(1, [])], []));
        FrameAddress latest = store.Append(StateRevision.CreateDelta(reset, [D(1, reset, [3])], []));
        ObjectVersionChain resetChain = store.ReadObjectVersionChain(reset, 1);
        Assert.Equal(reset, Assert.Single(resetChain.Records).Address);
        Assert.Equal(2L, resetChain.ReconstructionBytes); // Base kind + zero body length.
        ObjectVersionChain chain = store.ReadObjectVersionChain(latest, 1);
        Assert.Equal(new[] { reset, latest }, chain.Records.Select(x => x.Address));
        AssertCosts(chain);
        Assert.True(chain.ReconstructionBytes < store.ReadObjectVersionChain(validDelta, 1).ReconstructionBytes);
    }

    [Fact]
    public void H_uses_each_records_actual_scope_across_varint_length_and_file_distance_boundaries() {
        string path = NewStorePath();
        RbfSegmentStoreOptions options = Options(true);
        FrameAddress original;
        FrameAddress firstDelta;
        FrameAddress latest;
        using (SegmentStore segments = SegmentStore.CreateNew(path, options)) {
            StateRevisionStore store = new(segments);
            original = store.Append(StateRevision.CreateBase(null, [B(128, new byte[127])], []));
            firstDelta = store.Append(StateRevision.CreateDelta(original, [D(128, original, new byte[128])], []));
            FrameAddress parent = firstDelta;
            for (int index = 0; index < 127; index++) {
                parent = store.Append(StateRevision.CreateDelta(parent, [], []));
            }
            latest = store.Append(StateRevision.CreateDelta(parent, [D(128, firstDelta, [])], []));
            Assert.Equal(128u, latest.FileNumber - firstDelta.FileNumber);
        }
        using SegmentStore reopened = SegmentStore.OpenReadOnlyExisting(path, options);
        StateRevisionStore coldStore = new(reopened);
        ObjectVersionChain chain = coldStore.ReadObjectVersionChain(latest, 128);
        Assert.Equal(new[] { original, firstDelta, latest }, chain.Records.Select(x => x.Address));
        Assert.Equal(129, chain.Records[0].PayloadBytes); // kind + one-byte length + 127 bytes.
        Assert.Equal(1 + 1 + EncodeVarint(original.FrameTicket.Serialize()).Length + 2 + 128, chain.Records[1].PayloadBytes);
        Assert.Equal(1 + 2 + EncodeVarint(firstDelta.FrameTicket.Serialize()).Length + 1, chain.Records[2].PayloadBytes);
        AssertCosts(chain);
        Assert.Equal(chain.Records[0].PayloadBytes + (long)chain.Records[1].PayloadBytes,
            coldStore.ReadObjectVersionChain(firstDelta, 128).ReconstructionBytes);
    }

    [Fact]
    public void Chain_read_rejects_invalid_requests_and_distinguishes_empty_Base_from_missing_id() {
        using SegmentStore segments = SegmentStore.CreateNew(NewStorePath());
        StateRevisionStore store = new(segments);
        FrameAddress original = store.Append(StateRevision.CreateBase(null, [B(uint.MaxValue, [])], []));
        Assert.Empty(Assert.Single(store.ReadObjectVersionChain(original, uint.MaxValue).Records).Record.Body.ToArray());
        Assert.Throws<InvalidDataException>(() => store.ReadObjectVersionChain(original, 1));
        Assert.Throws<ArgumentOutOfRangeException>(() => store.ReadObjectVersionChain(original, 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => store.ReadObjectVersionChain(default, 1));
    }

    private static void AssertCosts(ObjectVersionChain chain) {
        long total = 0;
        foreach (ObjectVersionChainEntry entry in chain.Records) {
            // Independent wire construction: no production reader/writer or size estimator.
            List<byte> payload = [(byte)entry.Record.Kind];
            if (entry.Record.PriorAddress is { } prior) {
                payload.AddRange(EncodeVarint(entry.Address.FileNumber - prior.FileNumber));
                payload.AddRange(EncodeVarint(prior.FrameTicket.Serialize()));
            }
            payload.AddRange(EncodeVarint((ulong)entry.Record.Body.Length));
            payload.AddRange(entry.Record.Body.ToArray());
            Assert.Equal(payload.Count, entry.PayloadBytes);
            total += payload.Count;
        }
        Assert.Equal(total, chain.ReconstructionBytes);
    }

    private static byte[] EncodeVarint(ulong value) {
        List<byte> bytes = [];
        while (value >= 128) {
            bytes.Add((byte)((value & 127) | 128));
            value >>= 7;
        }
        bytes.Add((byte)value);
        return bytes.ToArray();
    }

    private static void AssertAppendRejectedWithoutWrite(SegmentStore segments, StateRevisionStore store, StateRevision revision) {
        long before;
        using (RbfSegmentWriterLease writer = segments.OpenActiveWriter()) {
            before = writer.File.TailOffset;
        }
        Assert.Throws<InvalidDataException>(() => store.Append(revision));
        using (RbfSegmentWriterLease writer = segments.OpenActiveWriter()) {
            Assert.Equal(before, writer.File.TailOffset);
        }
    }

    private static FrameAddress RawAppend(SegmentStore segments, StateRevision revision) {
        using RbfSegmentWriterLease writer = segments.OpenActiveWriter();
        return RawAppend(writer, revision);
    }

    private static FrameAddress RawAppend(RbfSegmentWriterLease writer, StateRevision revision) {
        ArrayBufferWriter<byte> payload = new();
        StateRevisionWireWriter.Write(payload, revision, new FileScope(writer.SegmentNumber));
        SizedPtr ticket = writer.File.Append(StateRevisionWireFormat.RbfTag, payload.WrittenSpan).Unwrap();
        return new FrameAddress(writer.SegmentNumber, ticket);
    }

    private static ObjectVersionRecord B(uint id, ReadOnlySpan<byte> body) => ObjectVersionRecord.CreateBase(id, body);
    private static ObjectVersionRecord D(uint id, FrameAddress prior, ReadOnlySpan<byte> body) => ObjectVersionRecord.CreateDelta(id, prior, body);
    private static RbfSegmentStoreOptions Options(bool rollover) => new() {
        NewStoreLayout = RbfSegmentStoreLayout.Flat,
        SegmentSizeThresholdBytes = rollover ? 8 : 64 * 1024 * 1024,
    };

    private string NewStorePath() {
        string path = Path.GetFullPath(Path.Combine(_temporaryRoot, $"durable-graph-object-chain-{Guid.NewGuid():N}"));
        _storePaths.Add(path);
        return path;
    }

    public void Dispose() {
        foreach (string path in _storePaths) {
            string resolved = Path.GetFullPath(path);
            if (!string.Equals(Path.GetDirectoryName(resolved), Path.TrimEndingDirectorySeparator(_temporaryRoot),
                StringComparison.OrdinalIgnoreCase) ||
                !Path.GetFileName(resolved).StartsWith("durable-graph-object-chain-", StringComparison.Ordinal)) {
                throw new InvalidOperationException("Refusing to delete outside this fixture's temporary directory.");
            }
            if (Directory.Exists(path)) {
                Directory.Delete(path, recursive: true);
            }
        }
    }
}
