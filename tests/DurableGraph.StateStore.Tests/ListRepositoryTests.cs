using System.Buffers;
using Atelia.DurableGraph.StateStore.Serialization;
using Atelia.DurableGraph.StateStore.Storage;
using Atelia.Rbf;
using Atelia.RbfSegmentStore;
using SegmentStore = Atelia.RbfSegmentStore.RbfSegmentStore;

namespace Atelia.DurableGraph.StateStore.Tests;

public sealed class ListRepositoryTests : IDisposable {
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"durable-list-repository-{Guid.NewGuid():N}");
    private static readonly ReadAmplificationBaseBudgetParameters NoRebase = new(1000000, 1);
    private static readonly TypeExpr NumbersType = TypeExpr.List(TypeExpr.Builtin(TypeTag.Int32));
    private static readonly TypeExpr LinksType = TypeExpr.List(TypeExpr.Named("ListWorld"));
    private static readonly DurableSchema Schema = new("ListWorld", 1,
        DurableFieldInfo.Reference(1, NumbersType), DurableFieldInfo.Reference(2, NumbersType),
        DurableFieldInfo.Reference(3, LinksType), new DurableFieldInfo(4, TypeTag.Byte));

    [Fact]
    public void SessionFreezesWriterAndReopenSwitchesAlgorithmWithoutNewRepresentationOrForcedBase() {
        StateModelRegistry models = Models(ListDeltaAlgorithm.LocalResync);
        List<int> values = Enumerable.Range(10000, 1000).ToList();
        World world = new() { Values = values, Alias = values };
        FrameAddress seed, local, myers, position;
        ObjectId listId;
        byte[] registered;
        using (GraphRepository repository = CreateRepository()) {
            using GraphSession<World> session = repository.Create(world, models);
            // List binding is still lazy here. The session must have copied the choice,
            // rather than consulting the mutable registry when it first closes a List.
            models.UseListDeltaAlgorithm(ListDeltaAlgorithm.Position);
            seed = session.Commit(NoRebase);
            values.Insert(0, 7);
            local = session.Commit(NoRebase);
            Assert.Same(values, session.World.Values);
        }
        registered = File.ReadAllBytes(Path.Combine(_root, "schemas.rbf"));
        using (SegmentStore segments = OpenState()) {
            StateRevisionStore store = new(segments);
            ObjectVersionRecord delta = Assert.Single(store.Read(local).LocalObjects);
            listId = new(delta.ObjectId);
            // A head insertion into 1000 unchanged elements has a tiny range Delta.
            // This is a payload-size witness for the frozen LocalResync selection;
            // Position would rewrite the suffix, so this check is not just a setting getter.
            Assert.Equal(ObjectVersionKind.Delta, delta.Kind);
            Assert.True(delta.Body.Length < 40);
        }

        models.UseListDeltaAlgorithm(ListDeltaAlgorithm.BoundedMyers);
        using (GraphRepository reopened = GraphRepository.OpenExisting(_root)) {
            using GraphSession<World> session = reopened.Load<World>(models);
            Assert.Equal(7, session.World.Values![0]);
            session.World.Values.Insert(500, 8);
            myers = session.Commit(NoRebase);
        }
        Assert.Equal(registered, File.ReadAllBytes(Path.Combine(_root, "schemas.rbf")));

        models.UseListDeltaAlgorithm(ListDeltaAlgorithm.Position);
        using (GraphRepository reopened = GraphRepository.OpenExisting(_root)) {
            using GraphSession<World> session = reopened.Load<World>(models);
            session.World.Values![1] = 42; // A sparse replacement is cheap for Position too.
            position = session.Commit(NoRebase);
            Assert.Same(session.World.Values, session.World.Alias);
        }
        Assert.Equal(registered, File.ReadAllBytes(Path.Combine(_root, "schemas.rbf")));
        using (SegmentStore segments = OpenState()) {
            StateRevisionStore store = new(segments);
            Assert.Equal(seed, store.Read(local).ParentRevisionAddress);
            Assert.Equal(local, store.Read(myers).ParentRevisionAddress);
            Assert.Equal(myers, store.Read(position).ParentRevisionAddress);
            foreach (FrameAddress revision in new[] { myers, position }) {
                ObjectVersionRecord change = Assert.Single(store.Read(revision).LocalObjects);
                Assert.Equal(listId.Value, change.ObjectId);
                Assert.Equal(ObjectVersionKind.Delta, change.Kind);
            }
            using IRbfFile file = RbfFile.OpenExisting(Path.Combine(_root, "schemas.rbf"));
            SchemaStore schemas = new(file, readOnly: true);
            // Decode historical versions with an unrelated writer choice. All deltas
            // share one reader and inherit the same Base representation.
            StateModelSnapshot readers = Models().Snapshot(schemas);
            Assert.Equal(1000, RevisionDecoder.ReadSnapshot(store, schemas, seed, readers).GetRequired(listId).GetListState<int>().Count);
            Assert.Equal(1001, RevisionDecoder.ReadSnapshot(store, schemas, local, readers).GetRequired(listId).GetListState<int>().Count);
            FrozenListState<int> final = RevisionDecoder.ReadSnapshot(store, schemas, position, readers).GetRequired(listId).GetListState<int>();
            Assert.Equal(1002, final.Count);
            Assert.Equal(7, final[0]);
            Assert.Equal(42, final[1]);
            Assert.Equal(8, final[500]);
        }
    }

    [Theory]
    [InlineData(ListDeltaAlgorithm.Position)]
    [InlineData(ListDeltaAlgorithm.LocalResync)]
    [InlineData(ListDeltaAlgorithm.BoundedMyers)]
    public void SharedListCyclesResizeAndChildOnlyChangesKeepIdentityAcrossColdReopen(ListDeltaAlgorithm algorithm) {
        World world = new() { Values = Enumerable.Range(10000, 100).ToList() };
        World child = new() { Value = 3, Values = world.Values };
        world.Alias = world.Values;
        world.Links = child.Links = [world, child];
        FrameAddress seed, resize, childOnly, unchanged;
        using (GraphRepository repository = CreateRepository()) {
            using GraphSession<World> session = repository.Create(world, Models(algorithm));
            seed = session.Commit(NoRebase);
            world.Values.Add(777);
            world.Values[1] = 42;
            resize = session.Commit(NoRebase);
            Assert.Same(world.Values, session.World.Alias);
            child.Value++;
            childOnly = session.Commit(NoRebase);
            world.Values.Capacity += 100;
            unchanged = session.Commit(NoRebase);
        }
        using (SegmentStore segments = OpenState()) {
            StateRevisionStore store = new(segments);
            ObjectVersionRecord listChange = Assert.Single(store.Read(resize).LocalObjects);
            Assert.Equal(ObjectVersionKind.Delta, listChange.Kind);
            Assert.Contains(listChange.ObjectId, store.ReadLiveObjectHeadMap(seed).Keys);
            Assert.Equal(seed, store.Read(resize).ParentRevisionAddress);
            ObjectVersionRecord childChange = Assert.Single(store.Read(childOnly).LocalObjects);
            Assert.NotEqual(listChange.ObjectId, childChange.ObjectId);
            Assert.Empty(store.Read(unchanged).LocalObjects);
        }
        using GraphRepository cold = GraphRepository.OpenExisting(_root);
        using GraphSession<World> loaded = cold.Load<World>(Models());
        Assert.Equal(101, loaded.World.Values!.Count);
        Assert.Equal(42, loaded.World.Values[1]);
        Assert.Equal(777, loaded.World.Values[^1]);
        Assert.Same(loaded.World.Values, loaded.World.Alias);
        Assert.Same(loaded.World, loaded.World.Links![0]);
        Assert.Same(loaded.World.Links, loaded.World.Links[1].Links);
        Assert.Same(loaded.World.Values, loaded.World.Links[1].Values);
        Assert.Equal((byte)4, loaded.World.Links[1].Value);
        loaded.Commit(NoRebase);
    }

    [Fact]
    public void FrozenCandidateSurvivesLaterEditsAndRetryUsesUnpublishedBaseline() {
        List<int> values = Enumerable.Range(10000, 100).ToList();
        World world = new() { Values = values, Alias = values };
        FrameAddress seed, frozen, retry, failedCandidate;
        using (GraphRepository repository = CreateRepository()) {
            using GraphSession<World> session = repository.Create(world, Models());
            seed = session.Commit(NoRebase);
            values.Add(55);
            repository.Checkpoint = checkpoint => {
                if (checkpoint == CommitCheckpoint.AfterPrepare) { values[0] = 999; values.Add(66); }
            };
            frozen = session.Commit(NoRebase);
            repository.Checkpoint = checkpoint => {
                if (checkpoint == CommitCheckpoint.BeforePublication) { throw new IOException("known not published"); }
            };
            GraphCommitException failure = Assert.Throws<GraphCommitException>(() => session.Commit(NoRebase));
            Assert.Equal(GraphCommitOutcome.NotPublished, failure.Outcome);
            failedCandidate = Assert.IsType<FrameAddress>(failure.CandidateRevisionAddress);
            Assert.Equal(frozen, repository.HeadRevisionAddress);
            Assert.Equal(frozen, session.ParentRevisionAddress);
            Assert.False(session.IsFaulted);
            Assert.Same(values, session.World.Values);
            Assert.Same(values, session.World.Alias);
            Assert.Equal(102, values.Count);
            Assert.Equal(999, values[0]);
            repository.Checkpoint = null;
            retry = session.Commit(NoRebase);
        }
        using (SegmentStore segments = OpenState()) {
            StateRevisionStore store = new(segments);
            Assert.Equal(seed, store.Read(frozen).ParentRevisionAddress);
            Assert.Equal(frozen, store.Read(failedCandidate).ParentRevisionAddress);
            Assert.Equal(frozen, store.Read(retry).ParentRevisionAddress);
            Assert.NotEqual(failedCandidate, retry);
            using IRbfFile file = RbfFile.OpenExisting(Path.Combine(_root, "schemas.rbf"));
            SchemaStore schemas = new(file, readOnly: true);
            DecodedRevision decoded = RevisionDecoder.ReadSnapshot(store, schemas, frozen, Models().Snapshot(schemas));
            FrozenListState<int> old = Assert.Single(decoded.Objects, row => row.Kind == ObjectStateKind.List).GetListState<int>();
            Assert.Equal(101, old.Count);
            Assert.Equal(10000, old.Elements[0]);
            Assert.Equal(55, old.Elements[^1]);
        }
        using GraphRepository cold = GraphRepository.OpenExisting(_root);
        using GraphSession<World> loaded = cold.Load<World>(Models());
        Assert.Equal(102, loaded.World.Values!.Count);
        Assert.Equal(999, loaded.World.Values[0]);
        Assert.Equal(66, loaded.World.Values[^1]);
        Assert.Same(loaded.World.Values, loaded.World.Alias);
    }

    [Fact]
    public void ReplacementWithEqualContentCreatesNewIdAndDetachedCycleIsRemoved() {
        List<int> values = [1, 2, 3];
        World child = new();
        child.Links = [child];
        World world = new() { Values = values, Links = [child] };
        FrameAddress seed, changed;
        using (GraphRepository repository = CreateRepository()) {
            using GraphSession<World> session = repository.Create(world, Models());
            seed = session.Commit(NoRebase);
            world.Values = [.. values];
            world.Links = null;
            changed = session.Commit(NoRebase);
        }
        using SegmentStore segments = OpenState();
        StateRevisionStore store = new(segments);
        StateRevision revision = store.Read(changed);
        Assert.Equal(4, revision.RemovedObjectIds.Count);
        IReadOnlyDictionary<uint, FrameAddress> priorHeads = store.ReadLiveObjectHeadMap(seed);
        // The policy may also choose Base for the small changed World body.
        // New membership, rather than representation kind, identifies the replacement.
        ObjectVersionRecord replacement = Assert.Single(revision.LocalObjects, row => !priorHeads.ContainsKey(row.ObjectId));
        Assert.Equal(ObjectVersionKind.Base, replacement.Kind);
        using IRbfFile file = RbfFile.OpenExisting(Path.Combine(_root, "schemas.rbf"));
        SchemaStore schemas = new(file, readOnly: true);
        DecodedRevision decoded = RevisionDecoder.ReadSnapshot(store, schemas, changed, Models().Snapshot(schemas));
        ObjectStateRecord replacementState = decoded.GetRequired(new(replacement.ObjectId));
        Assert.Equal(ObjectStateKind.List, replacementState.Kind);
        Assert.Equal(values, replacementState.GetListState<int>().Elements.ToArray());
        Assert.Equal(2, store.ReadLiveObjectHeadMap(changed).Count);
        Assert.Equal(5, priorHeads.Count);
    }

    [Fact]
    public void AppendTruncateClearAndInsertRemainReadableThroughPersistedDeltaChains() {
        World world = new() { Values = Enumerable.Range(10000, 80).ToList() };
        int[] expected;
        using (GraphRepository repository = CreateRepository()) {
            using GraphSession<World> session = repository.Create(world, Models());
            session.Commit(NoRebase);
            world.Values.AddRange([0, 0, 77]);
            session.Commit(NoRebase);
            world.Values.RemoveRange(75, 8);
            session.Commit(NoRebase);
            world.Values.Insert(10, 12345);
            session.Commit(NoRebase);
            world.Values.Clear();
            session.Commit(NoRebase);
            world.Values.AddRange([0, 0, 42]);
            session.Commit(NoRebase);
            expected = world.Values.ToArray();
        }
        using GraphRepository cold = GraphRepository.OpenExisting(_root);
        using GraphSession<World> loaded = cold.Load<World>(Models());
        Assert.Equal(expected, loaded.World.Values);
    }

    private sealed class World : DurableBase {
        public List<int>? Values;
        public List<int>? Alias;
        public List<World>? Links;
        public byte Value;
    }
    private readonly record struct State(ObjectId Values, ObjectId Alias, ObjectId Links, byte Value);
    private static StateModelRegistry Models(ListDeltaAlgorithm algorithm = ListDeltaAlgorithm.LocalResync) {
        CapturedStatePreparation<State> prepare = new(Schema, Base, Delta);
        StateReaderBinding<State> reader = new(Schema, Read, Apply, Visit);
        StateModelBinding<World, State> model = new(prepare, [reader], row => row.GetState<State>(), static () => new(),
            static (World world, in State state, ObjectReadTable objects) => {
                world.Values = objects.ResolveObject<List<int>>(state.Values);
                world.Alias = objects.ResolveObject<List<int>>(state.Alias);
                world.Links = objects.ResolveObject<List<World>>(state.Links);
                world.Value = state.Value;
            }, static (world, context) => new(context.CaptureObject(world.Values, NumbersType),
                context.CaptureObject(world.Alias, NumbersType), context.CaptureObject(world.Links, LinksType), world.Value), Visit);
        StateModelRegistry registry = new();
        registry.Register(model);
        registry.UseListDeltaAlgorithm(algorithm);
        return registry;
    }
    private static void Visit(in State state, IStateReferenceVisitor visitor) {
        visitor.VisitObject(state.Values, NumbersType);
        visitor.VisitObject(state.Alias, NumbersType);
        visitor.VisitObject(state.Links, LinksType);
    }
    private static State Read(ref BinaryPayloadReader reader) => new(new(reader.ReadUInt32()), new(reader.ReadUInt32()),
        new(reader.ReadUInt32()), reader.ReadByte());
    private static PreparedBaseBody Base(in State state) {
        ArrayBufferWriter<byte> bytes = new();
        BinaryPayloadWriter writer = new(bytes);
        writer.WriteUInt32(state.Values.Value);
        writer.WriteUInt32(state.Alias.Value);
        writer.WriteUInt32(state.Links.Value);
        writer.WriteByte(state.Value);
        return new(bytes.WrittenSpan);
    }
    private static PreparedDeltaBody Delta(in State prior, in State next) {
        byte mask = (byte)((prior.Values != next.Values ? 1 : 0) | (prior.Alias != next.Alias ? 2 : 0) |
            (prior.Links != next.Links ? 4 : 0) | (prior.Value != next.Value ? 8 : 0));
        ArrayBufferWriter<byte> bytes = new();
        BinaryPayloadWriter writer = new(bytes);
        writer.WriteByte(mask);
        if ((mask & 1) != 0) { writer.WriteUInt32(next.Values.Value); }
        if ((mask & 2) != 0) { writer.WriteUInt32(next.Alias.Value); }
        if ((mask & 4) != 0) { writer.WriteUInt32(next.Links.Value); }
        if ((mask & 8) != 0) { writer.WriteByte(next.Value); }
        return new(mask != 0, bytes.WrittenSpan);
    }
    private static State Apply(ref BinaryPayloadReader reader, in State prior) {
        byte mask = reader.ReadByte();
        if (mask is 0 or > 15) { throw new InvalidDataException("Invalid test state Delta."); }
        return new((mask & 1) != 0 ? new(reader.ReadUInt32()) : prior.Values,
            (mask & 2) != 0 ? new(reader.ReadUInt32()) : prior.Alias,
            (mask & 4) != 0 ? new(reader.ReadUInt32()) : prior.Links,
            (mask & 8) != 0 ? reader.ReadByte() : prior.Value);
    }
    private GraphRepository CreateRepository() => GraphRepository.CreateNew(_root, new() { NewStoreLayout = RbfSegmentStoreLayout.Flat });
    private SegmentStore OpenState() => SegmentStore.OpenExisting(Path.Combine(_root, "state"));
    public void Dispose() {
        if (Directory.Exists(_root)) { Directory.Delete(_root, recursive: true); }
    }
}
