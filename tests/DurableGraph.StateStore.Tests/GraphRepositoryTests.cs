using System.Buffers;
using Atelia.DurableGraph.StateStore.Serialization;
using Atelia.DurableGraph.StateStore.Storage;
using Atelia.Rbf;
using Atelia.RbfSegmentStore;
using SegmentStore = Atelia.RbfSegmentStore.RbfSegmentStore;

namespace Atelia.DurableGraph.StateStore.Tests;

public sealed class GraphRepositoryTests : IDisposable {
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"durable-graph-repository-{Guid.NewGuid():N}");
    private static readonly ReadAmplificationBaseBudgetParameters NoRebase = new(1000000, 1);
    private static readonly DurableSchema Schema = new("RepositoryNode", 1,
        new DurableFieldInfo(1, TypeTag.DurableReference, "RepositoryNode"),
        new DurableFieldInfo(2, TypeTag.DurableReference, "RepositoryNode"),
        new DurableFieldInfo(3, TypeTag.String), new DurableFieldInfo(4, TypeTag.Byte));

    [Fact]
    public void ThreeCommitsRetainInstancesAndAdvanceExactParentThenReopenFromPublishedWorldId() {
        Node child = new() { Value = 2, Text = new string('s', 5) };
        Node world = new() { Left = child, Right = child, Value = 1, Text = child.Text };
        child.Left = world;
        FrameAddress first;
        FrameAddress second;
        FrameAddress third;
        uint rootId;
        using (GraphRepository repository = CreateRepository()) {
            using GraphSession<Node> session = repository.Create(world, Models());
            Assert.Null(repository.HeadRevisionAddress);
            Assert.Null(session.ParentRevisionAddress);
            Assert.Null(session.WorldId);
            first = session.Commit(NoRebase);
            rootId = session.WorldId!.Value;
            Assert.Equal(first, repository.HeadRevisionAddress);
            Assert.Equal(rootId, repository.WorldId);
            Assert.Same(world, session.World);
            Assert.Same(child, session.World.Left);
            child.Value = 3;
            second = session.Commit(NoRebase);
            Assert.Equal(second, session.ParentRevisionAddress);
            Assert.Same(child, session.World.Right);
            child.Value = 4;
            third = session.Commit(NoRebase);
            Assert.Equal(third, session.ParentRevisionAddress);
            Assert.Same(world, child.Left);
        }
        using (SegmentStore segments = OpenState()) {
            StateRevisionStore store = new(segments);
            Assert.Null(store.Read(first).ParentRevisionAddress);
            StateRevision secondRevision = store.Read(second);
            Assert.Equal(first, secondRevision.ParentRevisionAddress);
            ObjectVersionRecord secondWrite = Assert.Single(secondRevision.LocalObjects);
            Assert.Equal(ObjectVersionKind.Delta, secondWrite.Kind);
            Assert.NotEqual(rootId, secondWrite.ObjectId); // Only the child changed.
            StateRevision thirdRevision = store.Read(third);
            Assert.Equal(second, thirdRevision.ParentRevisionAddress);
            ObjectVersionRecord thirdWrite = Assert.Single(thirdRevision.LocalObjects);
            Assert.Equal(secondWrite.ObjectId, thirdWrite.ObjectId);
            Assert.Equal(second, thirdWrite.PriorAddress);
        }
        using GraphRepository reopened = GraphRepository.OpenExisting(_root);
        Assert.Equal(third, reopened.HeadRevisionAddress);
        using GraphSession<Node> loaded = reopened.Load<Node>(Models());
        Assert.Equal(rootId, loaded.WorldId);
        Assert.Equal((byte)4, loaded.World.Left!.Value);
        Assert.Same(loaded.World.Left, loaded.World.Right);
        Assert.Same(loaded.World, loaded.World.Left.Left);
        Assert.Same(loaded.World.Text, loaded.World.Left.Text);
        Assert.NotSame(world, loaded.World);
        loaded.Commit(NoRebase);
    }

    [Fact]
    public void UnchangedCommitAdvancesRevisionWithoutRewritingObjects() {
        FrameAddress first;
        FrameAddress second;
        using (GraphRepository repository = CreateRepository()) {
            using GraphSession<Node> session = repository.Create(new Node { Value = 1 }, Models());
            first = session.Commit(NoRebase);
            second = session.Commit(NoRebase);
            Assert.NotEqual(first, second);
            Assert.Equal(second, session.ParentRevisionAddress);
        }
        using SegmentStore segments = OpenState();
        StateRevisionStore store = new(segments);
        StateRevision unchanged = store.Read(second);
        Assert.Equal(first, unchanged.ParentRevisionAddress);
        Assert.Empty(unchanged.LocalObjects);
        Assert.Empty(unchanged.RemovedObjectIds);
        Assert.Equal(store.ReadLiveObjectHeadMap(first).Keys, store.ReadLiveObjectHeadMap(second).Keys);
    }

    [Fact]
    public void RemovedInstanceReattachedLaterReceivesFreshIdAndBase() {
        Node child = new() { Value = 2 };
        child.Left = child;
        Node world = new() { Left = child, Right = child };
        FrameAddress first;
        FrameAddress removed;
        FrameAddress reattached;
        uint rootId;
        using (GraphRepository repository = CreateRepository()) {
            using GraphSession<Node> session = repository.Create(world, Models());
            first = session.Commit(NoRebase);
            rootId = session.WorldId!.Value;
            world.Left = world.Right = null;
            removed = session.Commit(NoRebase);
            world.Left = child;
            reattached = session.Commit(NoRebase);
            Assert.Same(child, session.World.Left);
        }
        using SegmentStore segments = OpenState();
        StateRevisionStore store = new(segments);
        uint oldId = Assert.Single(store.ReadLiveObjectHeadMap(first).Keys, id => id != rootId);
        Assert.Equal(new[] { oldId }, store.Read(removed).RemovedObjectIds);
        ObjectVersionRecord fresh = Assert.Single(store.Read(reattached).LocalObjects, row => row.ObjectId != rootId);
        Assert.True(fresh.ObjectId > oldId);
        Assert.Equal(ObjectVersionKind.Base, fresh.Kind);
        Assert.DoesNotContain(oldId, store.ReadLiveObjectHeadMap(reattached).Keys);
        Assert.Contains(oldId, store.ReadLiveObjectHeadMap(first).Keys);
    }

    [Fact]
    public void PostCaptureEditsAreComparedAgainstPublishedCandidateOnNextCommit() {
        Node world = new() { Value = 1 };
        FrameAddress first;
        FrameAddress second;
        using (GraphRepository repository = CreateRepository()) {
            using GraphSession<Node> session = repository.Create(world, Models());
            repository.Checkpoint = checkpoint => {
                if (checkpoint == CommitCheckpoint.AfterPrepare) world.Value = 2;
            };
            first = session.Commit(NoRebase);
            repository.Checkpoint = null;
            second = session.Commit(NoRebase);
        }
        using (SegmentStore segments = OpenState()) {
            StateRevisionStore store = new(segments);
            Assert.Equal(first, store.Read(second).ParentRevisionAddress);
            Assert.Equal(ObjectVersionKind.Delta, Assert.Single(store.Read(second).LocalObjects).Kind);
        }
        using GraphRepository reopened = GraphRepository.OpenExisting(_root);
        using GraphSession<Node> loaded = reopened.Load<Node>(Models());
        Assert.Equal((byte)2, loaded.World.Value);
    }

    [Fact]
    public void SessionExclusivityAndCreateRulesSurviveDisposalAndReopen() {
        using (GraphRepository repository = CreateRepository()) {
            using (GraphSession<Node> abandoned = repository.Create(new Node { Value = 9 }, Models())) {
                Assert.Throws<InvalidOperationException>(() => repository.Create(new Node(), Models()));
                Assert.Throws<InvalidOperationException>(() => repository.Load<Node>(Models()));
            }
            Assert.Null(repository.HeadRevisionAddress);
            using (GraphSession<Node> first = repository.Create(new Node { Value = 1 }, Models())) {
                first.Commit(NoRebase);
            }
            Assert.Throws<InvalidOperationException>(() => repository.Create(new Node(), Models()));
            using GraphSession<Node> loaded = repository.Load<Node>(Models());
            Assert.Equal((byte)1, loaded.World.Value);
            Assert.Throws<InvalidOperationException>(() => repository.Load<Node>(Models()));
        }
        using GraphRepository reopened = GraphRepository.OpenExisting(_root);
        Assert.Throws<InvalidOperationException>(() => reopened.Create(new Node(), Models()));
    }

    [Fact]
    public void CaptureFailurePreservesBaselineAndSessionCanRetry() {
        bool fail = false;
        Node world = new() { Left = new Node { Value = 2 }, Value = 1 };
        using GraphRepository repository = CreateRepository();
        using GraphSession<Node> session = repository.Create(world, Models(node => {
            if (fail && node.Value == 2) throw new InvalidDataException("capture failed on child");
        }));
        FrameAddress first = session.Commit(NoRebase);
        fail = true;
        world.Value = 3;
        InvalidDataException error = Assert.Throws<InvalidDataException>(() => session.Commit(NoRebase));
        Assert.Equal("capture failed on child", error.Message);
        Assert.Equal(first, session.ParentRevisionAddress);
        Assert.Equal(first, repository.HeadRevisionAddress);
        Assert.False(repository.IsFaulted);
        fail = false;
        Assert.NotEqual(first, session.Commit(NoRebase));
        Assert.Same(world, session.World);
    }

    [Fact]
    public void CaptureReentryIsRejectedWithoutLosingTheSession() {
        GraphSession<Node>? session = null;
        bool reenter = true;
        using GraphRepository repository = CreateRepository();
        session = repository.Create(new Node(), Models(_ => {
            if (reenter) session!.Commit(NoRebase);
        }));
        using (session) {
            Assert.Throws<InvalidOperationException>(() => session.Commit(NoRebase));
            Assert.Null(repository.HeadRevisionAddress);
            Assert.False(repository.IsFaulted);
            reenter = false;
            session.Commit(NoRebase);
        }
    }

    [Theory]
    [InlineData((int)CommitCheckpoint.AfterStateDurable)]
    [InlineData((int)CommitCheckpoint.BeforePublication)]
    public void DefinitelyUnpublishedCandidateCanBeRetriedWithoutAdvancingParent(int checkpointValue) {
        CommitCheckpoint point = (CommitCheckpoint)checkpointValue;
        FrameAddress first;
        FrameAddress retry;
        FrameAddress failedCandidate;
        using (GraphRepository repository = CreateRepository()) {
            using GraphSession<Node> session = repository.Create(new Node { Value = 1 }, Models());
            first = session.Commit(NoRebase);
            session.World.Value = 2;
            repository.Checkpoint = checkpoint => {
                if (checkpoint == point) throw new IOException("known pre-publication failure");
            };
            GraphCommitException error = Assert.Throws<GraphCommitException>(() => session.Commit(NoRebase));
            Assert.Equal(GraphCommitOutcome.NotPublished, error.Outcome);
            failedCandidate = Assert.IsType<FrameAddress>(error.CandidateRevisionAddress);
            Assert.Equal(first, repository.HeadRevisionAddress);
            Assert.Equal(first, session.ParentRevisionAddress);
            Assert.False(repository.IsFaulted);
            Assert.False(session.IsFaulted);
            repository.Checkpoint = null;
            retry = session.Commit(NoRebase);
        }
        using SegmentStore segments = OpenState();
        StateRevisionStore store = new(segments);
        Assert.Equal(first, store.Read(failedCandidate).ParentRevisionAddress);
        Assert.Equal(first, store.Read(retry).ParentRevisionAddress);
        Assert.NotEqual(failedCandidate, retry);
    }

    [Fact]
    public void FailureAfterPublicationRequiresReopenAndReportsPublishedCandidate() {
        FrameAddress published;
        using (GraphRepository repository = CreateRepository()) {
            using GraphSession<Node> session = repository.Create(new Node { Value = 7 }, Models());
            repository.Checkpoint = checkpoint => {
                if (checkpoint == CommitCheckpoint.AfterPublication) throw new IOException("lost completion");
            };
            GraphCommitException error = Assert.Throws<GraphCommitException>(() => session.Commit(NoRebase));
            Assert.Equal(GraphCommitOutcome.Published, error.Outcome);
            published = Assert.IsType<FrameAddress>(error.CandidateRevisionAddress);
            Assert.True(repository.IsFaulted);
            Assert.True(session.IsFaulted);
            Assert.Throws<InvalidOperationException>(() => session.Commit(NoRebase));
        }
        using GraphRepository reopened = GraphRepository.OpenExisting(_root);
        Assert.Equal(published, reopened.HeadRevisionAddress);
        using GraphSession<Node> recovered = reopened.Load<Node>(Models());
        Assert.Equal((byte)7, recovered.World.Value);
        recovered.Commit(NoRebase);
    }

    [Fact]
    public void PublicationAppendWithoutConfirmedBarrierReportsUnknownAndReopenResolvesCompleteRecord() {
        FrameAddress first;
        FrameAddress candidate;
        using (GraphRepository repository = CreateRepository()) {
            using GraphSession<Node> session = repository.Create(new Node { Value = 1 }, Models());
            first = session.Commit(NoRebase);
            session.World.Value = 2;
            repository.Checkpoint = checkpoint => {
                if (checkpoint == CommitCheckpoint.AfterPublicationAppend) throw new IOException("publication flush not confirmed");
            };
            GraphCommitException error = Assert.Throws<GraphCommitException>(() => session.Commit(NoRebase));
            Assert.Equal(GraphCommitOutcome.Unknown, error.Outcome);
            candidate = Assert.IsType<FrameAddress>(error.CandidateRevisionAddress);
            Assert.NotEqual(first, candidate);
            Assert.True(repository.IsFaulted);
            Assert.True(session.IsFaulted);
            Assert.Throws<InvalidOperationException>(() => _ = repository.HeadRevisionAddress);
            Assert.Throws<InvalidOperationException>(() => _ = repository.WorldId);
            Assert.Throws<InvalidOperationException>(() => session.Commit(NoRebase));
        }
        // This witnesses process-local complete-record visibility and the successful reopen
        // barrier. It does not simulate power loss or promise that the unflushed record survives it.
        using (GraphRepository reopened = GraphRepository.OpenExisting(_root)) {
            Assert.Equal(candidate, reopened.HeadRevisionAddress);
            using GraphSession<Node> recovered = reopened.Load<Node>(Models());
            Assert.Equal((byte)2, recovered.World.Value);
            Assert.Equal(candidate, recovered.ParentRevisionAddress);
            recovered.World.Value = 3;
            recovered.Commit(NoRebase);
        }
        using GraphRepository confirmed = GraphRepository.OpenExisting(_root);
        using GraphSession<Node> latest = confirmed.Load<Node>(Models());
        Assert.Equal((byte)3, latest.World.Value);
    }

    [Theory]
    [InlineData("publication", "truncate-aligned")]
    [InlineData("publication", "truncate-unaligned")]
    [InlineData("publication", "trailer-crc")]
    [InlineData("state", "truncate-aligned")]
    [InlineData("state", "truncate-unaligned")]
    [InlineData("state", "trailer-crc")]
    public void StrictOpenRejectsBadTailWithoutChangingRepositoryFiles(string component, string damage) {
        using (GraphRepository repository = CreateRepository()) {
            using GraphSession<Node> session = repository.Create(new Node { Value = 1 }, Models());
            session.Commit(NoRebase);
            session.World.Value = 2;
            session.Commit(NoRebase);
        }
        string damagedPath = component == "publication" ? Path.Combine(_root, "publication.rbf") :
            Assert.Single(Directory.GetFiles(Path.Combine(_root, "state"), "*.rbf", SearchOption.AllDirectories));
        byte[] damaged = File.ReadAllBytes(damagedPath);
        switch (damage) {
            case "truncate-aligned": damaged = damaged[..^4]; break;
            case "truncate-unaligned": damaged = damaged[..^1]; break;
            case "trailer-crc": damaged[^8] ^= 0x20; break;
            default: throw new InvalidOperationException(damage);
        }
        File.WriteAllBytes(damagedPath, damaged);
        Dictionary<string, byte[]> before = Directory.GetFiles(_root, "*", SearchOption.AllDirectories)
            .ToDictionary(static path => path, static path => File.ReadAllBytes(path));
        Assert.ThrowsAny<Exception>(() => {
            // Repository semantics stay strict even if the underlying Store option requests repair.
            using GraphRepository rejected = GraphRepository.OpenExisting(_root, new() { RecoverActiveTailOnOpen = true });
        });
        Assert.Equal(before.Keys.Order(), Directory.GetFiles(_root, "*", SearchOption.AllDirectories).Order());
        foreach ((string path, byte[] bytes) in before) Assert.Equal(bytes, File.ReadAllBytes(path));
    }

    [Fact]
    public void BeforeStateAppendFaultRequiresReopenAndKeepsPreviousPublishedHead() {
        FrameAddress first;
        using (GraphRepository repository = CreateRepository()) {
            using GraphSession<Node> session = repository.Create(new Node { Value = 1 }, Models());
            first = session.Commit(NoRebase);
            session.World.Value = 2;
            repository.Checkpoint = checkpoint => {
                if (checkpoint == CommitCheckpoint.BeforeStateAppend) throw new IOException("state write failed");
            };
            GraphCommitException error = Assert.Throws<GraphCommitException>(() => session.Commit(NoRebase));
            Assert.Equal(GraphCommitOutcome.NotPublished, error.Outcome);
            Assert.True(repository.IsFaulted);
            Assert.Throws<InvalidOperationException>(() => session.Commit(NoRebase));
        }
        using GraphRepository reopened = GraphRepository.OpenExisting(_root);
        Assert.Equal(first, reopened.HeadRevisionAddress);
        using GraphSession<Node> recovered = reopened.Load<Node>(Models());
        Assert.Equal((byte)1, recovered.World.Value);
    }

    [Theory]
    [InlineData("wrong-state-parent", "Publication disagrees with the State Revision Parent.")]
    [InlineData("missing-world", "Published World is absent from the selected Revision.")]
    public void ValidPublicationFramesWithInvalidStateRelationshipFailWithoutModifyingFiles(string damage, string expectedError) {
        FrameAddress first;
        uint worldId;
        using (GraphRepository repository = CreateRepository()) {
            using GraphSession<Node> session = repository.Create(new Node { Value = 1 }, Models());
            first = session.Commit(NoRebase);
            worldId = session.WorldId!.Value;
        }
        FrameAddress invalidCandidate;
        using (SegmentStore segments = OpenState()) {
            StateRevisionStore store = new(segments);
            StateRevision next = damage == "wrong-state-parent"
                ? StateRevision.CreateObjectHeadMapBase(null, store.Read(first).LocalObjects, [])
                : StateRevision.CreateObjectHeadMapDelta(first, [], [worldId]);
            invalidCandidate = store.AppendDurably(next);
        }
        using (IRbfFile publication = RbfFile.OpenExisting(Path.Combine(_root, "publication.rbf"))) {
            publication.Append(PublicationLog.RbfTag,
                PublicationLog.Encode(first, new PublicationHead(invalidCandidate, worldId))).Unwrap();
            publication.DurableFlush();
            // The frame CRC, address ordering and publication predecessor chain are valid.
            // Only the repository's cross-store relationship validation should reject this record.
            PublicationLog framingOnly = new(publication, (_, _) => { });
            Assert.Equal(invalidCandidate, framingOnly.Head!.RevisionAddress);
        }
        Dictionary<string, byte[]> before = Directory.GetFiles(_root, "*", SearchOption.AllDirectories)
            .ToDictionary(static path => path, static path => File.ReadAllBytes(path));
        InvalidDataException error = Assert.Throws<InvalidDataException>(() => {
            using GraphRepository rejected = GraphRepository.OpenExisting(_root);
        });
        Assert.Equal(expectedError, error.Message);
        Assert.Equal(before.Keys.Order(), Directory.GetFiles(_root, "*", SearchOption.AllDirectories).Order());
        foreach ((string path, byte[] bytes) in before) Assert.Equal(bytes, File.ReadAllBytes(path));
    }

    [Fact]
    public void LoadedUpgradeRewriteIsClearedOnlyBySuccessfulCommitAndLaterWritesUseDelta() {
        FrameAddress original;
        using (GraphRepository repository = CreateRepository()) {
            using GraphSession<Node> session = repository.Create(new Node { Value = 1 }, Models());
            original = session.Commit(NoRebase);
        }
        FrameAddress upgraded;
        FrameAddress unchanged;
        FrameAddress changed;
        using (GraphRepository repository = GraphRepository.OpenExisting(_root)) {
            using GraphSession<Node> session = repository.Load<Node>(Models(upgrade: true));
            Node sameWorld = session.World;
            Assert.Equal((byte)11, sameWorld.Value);
            repository.Checkpoint = checkpoint => {
                if (checkpoint == CommitCheckpoint.BeforePublication) throw new IOException("retry upgrade save");
            };
            Assert.Equal(GraphCommitOutcome.NotPublished,
                Assert.Throws<GraphCommitException>(() => session.Commit(NoRebase)).Outcome);
            Assert.Equal(original, session.ParentRevisionAddress);
            repository.Checkpoint = null;
            upgraded = session.Commit(NoRebase);
            unchanged = session.Commit(NoRebase);
            sameWorld.Value = 12;
            changed = session.Commit(NoRebase);
            Assert.Same(sameWorld, session.World);
        }
        using SegmentStore segments = OpenState();
        StateRevisionStore store = new(segments);
        Assert.Equal(original, store.Read(upgraded).ParentRevisionAddress);
        Assert.Equal(ObjectVersionKind.Base, Assert.Single(store.Read(upgraded).LocalObjects).Kind);
        Assert.Empty(store.Read(unchanged).LocalObjects);
        Assert.Equal(unchanged, store.Read(changed).ParentRevisionAddress);
        Assert.Equal(ObjectVersionKind.Delta, Assert.Single(store.Read(changed).LocalObjects).Kind);
    }

    private sealed class Node : DurableBase {
        internal Node? Left;
        internal Node? Right;
        internal string? Text;
        internal byte Value;
    }
    private readonly record struct State(uint Left, uint Right, uint Text, byte Value);

    private static StateModelRegistry Models(Action<Node>? onCapture = null, bool upgrade = false) {
        DurableSchema current = upgrade ? new(Schema.SchemaId, 2, Schema.Fields.ToArray()) : Schema;
        CapturedStatePreparation<State> preparation = new(current,
            static (in State state) => Base(state),
            static (in State prior, in State next) => Delta(prior, next));
        StateReaderBinding Reader(DurableSchema schema) => new StateReaderBinding<State>(schema,
            static (ref BinaryPayloadReader input) => new(input.ReadUInt32(), input.ReadUInt32(), input.ReadUInt32(), input.ReadByte()),
            static (ref BinaryPayloadReader input, in State prior) => Apply(ref input, prior), Visit);
        StateReaderBinding[] readers = upgrade ? [Reader(Schema), Reader(current)] : [Reader(current)];
        StateModelBinding model = new StateModelBinding<Node, State>(preparation, readers,
            row => upgrade && row.Schema!.Version == 1 ? row.GetState<State>() with { Value = (byte)(row.GetState<State>().Value + 10) } : row.GetState<State>(),
            static () => new Node(),
            static (Node domain, in State state, ObjectReadTable objects) => {
                domain.Left = objects.ResolveDurable<Node>(state.Left);
                domain.Right = objects.ResolveDurable<Node>(state.Right);
                domain.Text = objects.ResolveString(state.Text);
                domain.Value = state.Value;
            },
            (node, context) => {
                onCapture?.Invoke(node);
                return new(context.CaptureDurable(node.Left, Schema.SchemaId), context.CaptureDurable(node.Right, Schema.SchemaId),
                    context.CaptureString(node.Text), node.Value);
            }, Visit);
        StateModelRegistry registry = new();
        registry.Register(model);
        return registry;
    }

    private static void Visit(in State state, IStateReferenceVisitor visitor) {
        visitor.VisitDurable(state.Left, Schema.SchemaId);
        visitor.VisitDurable(state.Right, Schema.SchemaId);
        visitor.VisitString(state.Text);
    }
    private static PreparedBaseBody Base(State state) {
        ArrayBufferWriter<byte> bytes = new();
        BinaryPayloadWriter writer = new(bytes);
        writer.WriteUInt32(state.Left);
        writer.WriteUInt32(state.Right);
        writer.WriteUInt32(state.Text);
        writer.WriteByte(state.Value);
        return new(bytes.WrittenSpan);
    }
    private static PreparedDeltaBody Delta(State prior, State next) {
        byte mask = (byte)((prior.Left != next.Left ? 1 : 0) | (prior.Right != next.Right ? 2 : 0) |
            (prior.Text != next.Text ? 4 : 0) | (prior.Value != next.Value ? 8 : 0));
        ArrayBufferWriter<byte> bytes = new();
        BinaryPayloadWriter writer = new(bytes);
        writer.WriteByte(mask);
        if ((mask & 1) != 0) writer.WriteUInt32(next.Left);
        if ((mask & 2) != 0) writer.WriteUInt32(next.Right);
        if ((mask & 4) != 0) writer.WriteUInt32(next.Text);
        if ((mask & 8) != 0) writer.WriteByte(next.Value);
        return new(mask != 0, bytes.WrittenSpan);
    }
    private static State Apply(ref BinaryPayloadReader input, State prior) {
        byte mask = input.ReadByte();
        return new((mask & 1) != 0 ? input.ReadUInt32() : prior.Left,
            (mask & 2) != 0 ? input.ReadUInt32() : prior.Right,
            (mask & 4) != 0 ? input.ReadUInt32() : prior.Text,
            (mask & 8) != 0 ? input.ReadByte() : prior.Value);
    }
    private GraphRepository CreateRepository() => GraphRepository.CreateNew(_root,
        new() { NewStoreLayout = RbfSegmentStoreLayout.Flat });
    private SegmentStore OpenState() => SegmentStore.OpenExisting(Path.Combine(_root, "state"));

    public void Dispose() {
        string resolved = Path.GetFullPath(_root);
        if (!string.Equals(Path.GetDirectoryName(resolved), Path.TrimEndingDirectorySeparator(Path.GetFullPath(Path.GetTempPath())), StringComparison.OrdinalIgnoreCase) ||
            !Path.GetFileName(resolved).StartsWith("durable-graph-repository-", StringComparison.Ordinal)) {
            throw new InvalidOperationException("Refusing to delete outside this test fixture.");
        }
        if (Directory.Exists(resolved)) Directory.Delete(resolved, recursive: true);
    }
}
