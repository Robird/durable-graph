using System.Reflection;
using Atelia.DurableGraph.Build;
using Atelia.DurableGraph.StateStore;
using Atelia.DurableGraph.StateStore.Serialization;
using Atelia.DurableGraph.StateStore.Storage;
using Atelia.Rbf;
using Atelia.RbfSegmentStore;
using SegmentStore = Atelia.RbfSegmentStore.RbfSegmentStore;

namespace Atelia.DurableGraph.Tests;

public sealed partial class DurableSchemaGeneratorTests {
    [Fact]
    public void LoadedWorldGeneratedHistoricalChainUpgradesRestoresAndResavesAcrossColdReopens() {
        var fixture = CompileLoadedWorldFixture();
        var prepareDelta = fixture.Host.GetMethod("Prepare1")!.CreateDelegate<Func<byte[], byte[], PreparedDelta>>();
        byte[] original = [20, 2, 10, 11, 10, 12, 12];
        byte[] middle = [20, 4, 10, 11, 10, 12, 12];
        byte[] latest = [20, 6, 10, 11, 10, 12, 12];
        PreparedDelta delta1 = prepareDelta(original, middle);
        PreparedDelta delta2 = prepareDelta(middle, latest);
        using RawBaseDirectory directory = new();
        using RawBaseDirectory schemaDirectory = new();
        Directory.CreateDirectory(schemaDirectory.Path);
        string schemaPath = Path.Combine(schemaDirectory.Path, "schemas.rbf");
        RbfSegmentStoreOptions options = new() { NewStoreLayout = RbfSegmentStoreLayout.Flat, SegmentSizeThresholdBytes = 8 };
        FrameAddress first, second, third, rewritten, unchanged, changed;
        using (var schemaFile = RbfFile.CreateNew(schemaPath))
        using (SegmentStore segments = SegmentStore.CreateNew(directory.Path, options)) {
            SchemaStore schemas = new(schemaFile);
            schemas.RegisterBatch([fixture.OldSchema]);
            StateRevisionStore store = new(segments);
            first = store.Append(StateRevision.CreateBase(null, [
                LoadedDurable(1, fixture.OldSchema, original), LoadedText(10, "same"),
                LoadedText(11, "same"), LoadedText(12, ""), LoadedText(20, "retired ancestor field"),
            ], []));
            second = store.Append(StateRevision.CreateDelta(first,
                [ObjectVersionRecord.CreateDelta(1, first, delta1.Payload)], []));
            third = store.Append(StateRevision.CreateDelta(second,
                [ObjectVersionRecord.CreateDelta(1, second, delta2.Payload)], []));
        }
        Assert.NotEqual(first.FileNumber, third.FileNumber);
        Array.Clear(original);
        delta1 = null!;
        delta2 = null!;

        // The only source for the loaded baseline is the reopened files and generated model registry.
        using (var schemaFile = RbfFile.OpenExisting(schemaPath))
        using (SegmentStore segments = SegmentStore.OpenExisting(directory.Path, options)) {
            SchemaStore schemas = new(schemaFile);
            StateRevisionStore store = new(segments);
            Assert.Equal(3, store.ReadObjectVersionChain(third, 1).Records.Count);
            object loaded = fixture.Load(store, schemas, third);
            fixture.Check(loaded, 3);
            Assert.Equal(1, fixture.UpgradeCalls()); // One upgrade after both old Deltas have been applied.
            PreparedWorldRevision plan = fixture.Prepare(loaded);
            Assert.Equal(1u, plan.WorldId);
            Assert.Equal(third, plan.Revision.ParentRevisionAddress);
            Assert.Equal<uint>([20], plan.Revision.RemovedObjectIds);
            ObjectVersionRecord rewrite = Assert.Single(plan.Revision.LocalObjects);
            Assert.Equal(ObjectVersionKind.Base, rewrite.Kind);
            Assert.Equal(1u, rewrite.ObjectId);
            BaseObjectPayload envelope = BaseObjectPayloadCodec.Decode(rewrite.Body);
            Assert.Equal(fixture.CurrentSchema.Version, envelope.SchemaKey!.Value.Version);
            Assert.Equal<byte>([7, 10, 6, 10, 11, 10, 12, 12], envelope.Body.ToArray());
            fixture.Change(loaded, 99); // Frozen output cannot follow later domain mutation.
            rewritten = store.Append(plan.Revision);
            Assert.Single(store.ReadObjectVersionChain(rewritten, 1).Records);
            Assert.Equal<uint>([1, 10, 11, 12], store.ReadLiveObjectHeads(rewritten).Keys.Order());
            fixture.Change(loaded, 3);
            PreparedWorldRevision repeated = fixture.Prepare(loaded);
            Assert.Equal(third, repeated.Revision.ParentRevisionAddress);
            Assert.Equal(ObjectVersionKind.Base, Assert.Single(repeated.Revision.LocalObjects).Kind);
            Assert.Equal(rewrite.Body.ToArray(), Assert.Single(repeated.Revision.LocalObjects).Body.ToArray());
        }

        using (var schemaFile = RbfFile.OpenExisting(schemaPath))
        using (SegmentStore segments = SegmentStore.OpenExisting(directory.Path, options)) {
            SchemaStore schemas = new(schemaFile);
            StateRevisionStore store = new(segments);
            int upgrades = fixture.UpgradeCalls();
            object loaded = fixture.Load(store, schemas, rewritten);
            fixture.Check(loaded, 3);
            Assert.Equal(upgrades, fixture.UpgradeCalls());
            PreparedWorldRevision noChange = fixture.Prepare(loaded);
            Assert.Empty(noChange.Revision.LocalObjects);
            Assert.Empty(noChange.Revision.RemovedObjectIds);
            unchanged = store.Append(noChange.Revision);
            object fresh = fixture.Load(store, schemas, unchanged);
            fixture.Change(fresh, 4);
            PreparedWorldRevision update = fixture.Prepare(fresh);
            Assert.Equal(unchanged, update.Revision.ParentRevisionAddress);
            Assert.Equal(ObjectVersionKind.Delta, Assert.Single(update.Revision.LocalObjects).Kind);
            changed = store.Append(update.Revision);
        }

        using (var schemaFile = RbfFile.OpenExisting(schemaPath))
        using (SegmentStore segments = SegmentStore.OpenReadOnlyExisting(directory.Path, options)) {
            SchemaStore schemas = new(schemaFile);
            StateRevisionStore store = new(segments);
            fixture.Check(fixture.Load(store, schemas, changed), 4);
            ObjectVersionChain chain = store.ReadObjectVersionChain(changed, 1);
            Assert.Equal(new[] { rewritten, changed }, chain.Records.Select(row => row.Address));
            fixture.Check(fixture.Load(store, schemas, first), 1);
            fixture.Check(fixture.Load(store, schemas, third), 3);
            Assert.Equal<byte>(latest, fixture.ReadStored(store, schemas, third));
            Assert.Contains(20u, store.ReadLiveObjectHeads(third).Keys);

            // A read-only Segment gives a definite pre-append failure, not an uncertain IO outcome.
            // Schema registration remains writable; its exact current definitions already exist.
            object oldOwner = fixture.Load(store, schemas, third);
            PreparedWorldRevision beforeFailure = fixture.Prepare(oldOwner);
            var bytesBefore = Directory.EnumerateFiles(directory.Path, "*", SearchOption.AllDirectories)
                .ToDictionary(path => path, File.ReadAllBytes);
            Assert.Throws<InvalidOperationException>(() => store.Append(beforeFailure.Revision));
            PreparedWorldRevision afterFailure = fixture.Prepare(oldOwner);
            Assert.Equal(third, afterFailure.Revision.ParentRevisionAddress);
            Assert.Equal(ObjectVersionKind.Base, Assert.Single(afterFailure.Revision.LocalObjects).Kind);
            Assert.Equal(Assert.Single(beforeFailure.Revision.LocalObjects).Body.ToArray(),
                Assert.Single(afterFailure.Revision.LocalObjects).Body.ToArray());
            Assert.Equal<uint>([20], afterFailure.Revision.RemovedObjectIds);
            foreach ((string path, byte[] contents) in bytesBefore) Assert.Equal(contents, File.ReadAllBytes(path));
        }
    }

    [Fact]
    public void LoadedWorldGeneratedEmptyAliasesProduceRealDeltaAndRemoveWithoutLosingNonemptyIdentity() {
        var fixture = CompileLoadedWorldFixture();
        using RawBaseDirectory directory = new();
        using RawBaseDirectory schemaDirectory = new();
        Directory.CreateDirectory(schemaDirectory.Path);
        string schemaPath = Path.Combine(schemaDirectory.Path, "schemas.rbf");
        FrameAddress original, normalized;
        using (var schemaFile = RbfFile.CreateNew(schemaPath))
        using (SegmentStore segments = SegmentStore.CreateNew(directory.Path)) {
            SchemaStore schemas = new(schemaFile);
            schemas.RegisterBatch([fixture.CurrentSchema]);
            StateRevisionStore store = new(segments);
            original = store.Append(StateRevision.CreateBase(null, [
                LoadedDurable(1, fixture.CurrentSchema, [7, 10, 6, 10, 11, 10, 12, 13]),
                LoadedText(10, "same"), LoadedText(11, "same"), LoadedText(12, ""), LoadedText(13, ""),
            ], []));
        }
        using (var schemaFile = RbfFile.OpenExisting(schemaPath))
        using (SegmentStore segments = SegmentStore.OpenExisting(directory.Path)) {
            SchemaStore schemas = new(schemaFile);
            StateRevisionStore store = new(segments);
            object loaded = fixture.Load(store, schemas, original);
            fixture.Check(loaded, 3);
            Assert.Equal(0, fixture.UpgradeCalls());
            PreparedWorldRevision plan = fixture.Prepare(loaded);
            Assert.Equal(original, plan.Revision.ParentRevisionAddress);
            Assert.Equal<uint>([13], plan.Revision.RemovedObjectIds);
            ObjectVersionRecord delta = Assert.Single(plan.Revision.LocalObjects);
            Assert.Equal(ObjectVersionKind.Delta, delta.Kind);
            Assert.Equal(1u, delta.ObjectId);
            // Eighth body slot is the final Empty reference, not an equality-only no-op.
            Assert.Equal<byte>([128, 12], delta.Body.ToArray());
            normalized = store.Append(plan.Revision);
        }
        using (var schemaFile = RbfFile.OpenExisting(schemaPath))
        using (SegmentStore segments = SegmentStore.OpenExisting(directory.Path)) {
            SchemaStore schemas = new(schemaFile);
            StateRevisionStore store = new(segments);
            fixture.Check(fixture.Load(store, schemas, original), 3);
            object loaded = fixture.Load(store, schemas, normalized);
            fixture.Check(loaded, 3);
            Assert.Equal<uint>([1, 10, 11, 12], store.ReadLiveObjectHeads(normalized).Keys.Order());
            Assert.Empty(fixture.Prepare(loaded).Revision.LocalObjects);
            Assert.Equal(2, store.ReadObjectVersionChain(normalized, 1).Records.Count);
        }
    }

    private static ObjectVersionRecord LoadedDurable(uint id, DurableSchema schema, byte[] body) =>
        ObjectVersionRecord.CreateBase(id, BaseObjectPayloadCodec.EncodeDurable(schema, new(body)).Payload);

    private static ObjectVersionRecord LoadedText(uint id, string text) =>
        ObjectVersionRecord.CreateBase(id, BaseObjectPayloadCodec.EncodeString(StringPayloadCodec.PrepareBase(text)).Payload);

    private static LoadedWorldFixture CompileLoadedWorldFixture() {
        using AncestryHistoryDirectory history = new();
        GeneratorTestRun initial = RunGenerator(FusedDeltaPreamble + """
            [DurableType("loaded.base", 1)]
            public abstract partial class OldBase : DurableBase { [DurableField(9)] private string? _retired; }
            [DurableType("loaded.leaf", 1)]
            public sealed partial class Leaf : OldBase {
                [DurableField(1)] private int _number;
                [DurableField(2)] private string? _name;
                [DurableField(3)] private string? _alias;
                [DurableField(4)] private string? _shared;
                [DurableField(5)] private string? _empty;
                [DurableField(6)] private string? _emptyAgain;
            }
            """);
        AssertSchemaOnlyCompiles(initial);
        new SchemaHistoryTool().Publish(history.WriteManifest(initial), history.History);
        GeneratorTestRun current = RunGenerator(LoadedWorldCurrentSource, history.ReadAdditionalTexts());
        AssertSchemaOnlyCompiles(current);
        Assembly assembly = EmitAndLoad(current.OutputCompilation);
        Assert.Null(assembly.GetType("FusedDelta.OldBase"));
        Type leaf = assembly.GetType("FusedDelta.Leaf")!;
        return new(assembly.GetType("FusedDelta.Host")!, ReadSchemaOnly(leaf, 1), ReadSchemaOnly(leaf, 2));
    }

    private sealed class LoadedWorldFixture(Type host, DurableSchema oldSchema, DurableSchema currentSchema) {
        public Type Host { get; } = host;
        public DurableSchema OldSchema { get; } = oldSchema;
        public DurableSchema CurrentSchema { get; } = currentSchema;
        public Func<StateRevisionStore, SchemaStore, FrameAddress, object> Load { get; } =
            host.GetMethod("Load")!.CreateDelegate<Func<StateRevisionStore, SchemaStore, FrameAddress, object>>();
        public Func<object, PreparedWorldRevision> Prepare { get; } =
            host.GetMethod("Prepare")!.CreateDelegate<Func<object, PreparedWorldRevision>>();
        public Action<object, int> Check { get; } = host.GetMethod("Check")!.CreateDelegate<Action<object, int>>();
        public Action<object, int> Change { get; } = host.GetMethod("Change")!.CreateDelegate<Action<object, int>>();
        public Func<int> UpgradeCalls { get; } = host.GetMethod("UpgradeCalls")!.CreateDelegate<Func<int>>();
        public Func<StateRevisionStore, SchemaStore, FrameAddress, byte[]> ReadStored { get; } =
            host.GetMethod("ReadStored")!.CreateDelegate<Func<StateRevisionStore, SchemaStore, FrameAddress, byte[]>>();
    }

    private static readonly string LoadedWorldCurrentSource = FusedDeltaPreamble + """
        [DurableType("loaded.base", 2)]
        public abstract partial class NewBase : DurableBase {
            public static int ConstructorCalls;
            [DurableField(2)] private readonly byte _marker;
            [DurableField(9)] private readonly string? _baseName;
            protected NewBase(string name) { ConstructorCalls++; _marker = 99; _baseName = name; }
            public byte Marker => _marker;
            public string? BaseName => _baseName;
        }
        [DurableType("loaded.leaf", 2)]
        public sealed partial class Leaf : NewBase {
            public static int Upgrades;
            [DurableField(1)] private int _number;
            [DurableField(2)] private string? _name;
            [DurableField(3)] private string? _alias;
            [DurableField(4)] private string? _shared;
            [DurableField(5)] private string? _empty;
            [DurableField(6)] private string? _emptyAgain;
            [Transient] private int _cache = 123;
            public Leaf(string name) : base(name) { ConstructorCalls++; _name = name; }
            public void Change(int number) { _number = number; }
            public void Check(int number) {
                if (_number != number || Marker != 7 || ConstructorCalls != 0 || _cache != 0)
                    throw new Exception("Wrong persisted values or constructor/initializer ran.");
                if (_name != "same" || _alias != "same" || ReferenceEquals(_name, _alias) ||
                    !ReferenceEquals(_name, _shared) || !ReferenceEquals(_name, BaseName) ||
                    !ReferenceEquals(_empty, string.Empty) || !ReferenceEquals(_emptyAgain, string.Empty))
                    throw new Exception("Shared/distinct string identity or Empty normalization changed.");
            }
            private static void UpgradeStateV1ToV2(in __DurableBinaryBody.V1 old, out __DurableBinaryBody.V2 next) {
                Upgrades++;
                next = new __DurableBinaryBody.V2(7, old.Segment1Field2,
                    old.Segment1Field1, old.Segment1Field2, old.Segment1Field3,
                    old.Segment1Field4, old.Segment1Field5, old.Segment1Field6);
            }
        }
        public static class Host {
        """ + FusedDeltaHostMethods("Leaf", 1) + """
            public static object Load(Atelia.DurableGraph.StateStore.Storage.StateRevisionStore store,
                Atelia.DurableGraph.StateStore.SchemaStore schemas, Atelia.DurableGraph.StateStore.Storage.FrameAddress revision) {
                var models = new Atelia.DurableGraph.StateStore.StateModelRegistry();
                Leaf.__DurableBinaryBody.RegisterModel(models);
                return Atelia.DurableGraph.StateStore.LoadedWorld.Load<Leaf>(store, schemas, revision, 1, models);
            }
            public static Atelia.DurableGraph.StateStore.PreparedWorldRevision Prepare(object loaded) =>
                ((Atelia.DurableGraph.StateStore.LoadedWorld<Leaf>)loaded).Prepare(new(1000000, 1));
            public static void Check(object loaded, int expected) =>
                ((Atelia.DurableGraph.StateStore.LoadedWorld<Leaf>)loaded).World.Check(expected);
            public static void Change(object loaded, int value) =>
                ((Atelia.DurableGraph.StateStore.LoadedWorld<Leaf>)loaded).World.Change(value);
            public static int UpgradeCalls() => Leaf.Upgrades;
            public static byte[] ReadStored(Atelia.DurableGraph.StateStore.Storage.StateRevisionStore store,
                Atelia.DurableGraph.StateStore.SchemaStore schemas, Atelia.DurableGraph.StateStore.Storage.FrameAddress revision) {
                var readers = new Atelia.DurableGraph.StateStore.StateReaderRegistry();
                Leaf.__DurableBinaryBody.RegisterReaders(readers);
                var decoded = Atelia.DurableGraph.StateStore.RevisionDecoder.Read(store, schemas, revision, readers);
                var old = decoded.GetRequired(1).GetState<Leaf.__DurableBinaryBody.V1>();
                return Leaf.__DurableBinaryBody.PrepareBase(in old).Payload.ToArray();
            }
        }
        """;
}
