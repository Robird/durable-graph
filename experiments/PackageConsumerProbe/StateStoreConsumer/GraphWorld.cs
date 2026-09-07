using Atelia.DurableGraph;
using Atelia.DurableGraph.StateStore;
using Atelia.DurableGraph.StateStore.Storage;
using Atelia.Rbf;
using Atelia.RbfSegmentStore;
using SegmentStore = Atelia.RbfSegmentStore.RbfSegmentStore;

namespace StateStorePackageConsumerProbe;

[DurableType("package.graph-world", 1)]
public sealed partial class GraphWorld : DurableBase {
    [DurableField(1)] private GraphEntity? _primary;
    [DurableField(2)] private GraphEntity? _alias;

    public GraphWorld(GraphCharacter character) {
        GraphConstruction.Count++;
        _primary = character;
        _alias = character;
    }

    private void Disconnect() { _primary = null; _alias = null; }

    internal static void Exercise(string directory) {
        Directory.CreateDirectory(directory);
        string schemaPath = Path.Combine(directory, "schemas.rbf");
        string statePath = Path.Combine(directory, "state");
        RbfSegmentStoreOptions options = new() { NewStoreLayout = RbfSegmentStoreLayout.Flat };
        ReadAmplificationBaseBudgetParameters policy = new(int.MaxValue, 1);
        StateModelRegistry models = new();
        __DurableState.RegisterModel(models);
        GraphCharacter.__DurableState.RegisterModel(models);
        GraphItem.__DurableState.RegisterModel(models);
        uint worldId, characterId;
        uint[] initialIds;
        FrameAddress initialRevision, childRevision, removedRevision;
        int constructed;

        using (var file = RbfFile.CreateNew(schemaPath))
        using (SegmentStore segments = SegmentStore.CreateNew(statePath, options)) {
            SchemaStore schemas = new(file);
            StateRevisionStore store = new(segments);
            string shared = new('G', 1);
            GraphCharacter character = new(shared, 7);
            GraphWorld world = new(character);
            constructed = GraphConstruction.Count;

            // Ordinary domain construction enters through the public planning API. No hand-built DTOs or records.
            PreparedWorldRevision first = LoadedWorld.PrepareNew(store, schemas, world, models, policy);
            worldId = first.WorldId;
            initialIds = first.Revision.LocalObjectIds.ToArray();
            Require(first.Revision.ParentRevisionAddress is null && first.Revision.LocalObjects.Count == 4 &&
                first.Revision.LocalObjects.All(record => record.Kind == ObjectVersionKind.Base),
                "New graph must contain one World, Character, Item and shared string, all as Base.");
            Require(schemas.Count == 4, "Graph registration lost the nominal target's exact ancestor Schema.");
            character.Score = 999;
            world.Disconnect(); // Neither object mutation nor removal can change the frozen first plan.
            initialRevision = store.Append(first.Revision);
        }

        using (var file = RbfFile.OpenExisting(schemaPath))
        using (SegmentStore segments = SegmentStore.OpenExisting(statePath, options)) {
            SchemaStore schemas = new(file);
            StateRevisionStore store = new(segments);
            var loaded = LoadedWorld.Load<GraphWorld>(store, schemas, initialRevision, worldId, models);
            AssertGraph(loaded.World, 7, constructed);
            GraphCharacter character = (GraphCharacter)loaded.World._primary!;
            character.Score = 8;
            PreparedWorldRevision changed = loaded.Prepare(policy);
            PreparedWorldRevision repeated = loaded.Prepare(policy);
            Require(changed.Revision.LocalObjects.Count == 1 && changed.Revision.RemovedObjectIds.Count == 0,
                "Changing only Child must leave the World, Item and string unchanged.");
            ObjectVersionRecord delta = changed.Revision.LocalObjects[0];
            characterId = delta.ObjectId;
            Require(delta.Kind == ObjectVersionKind.Delta &&
                repeated.Revision.LocalObjects.Count == 1 &&
                repeated.Revision.LocalObjects[0].Body.SequenceEqual(delta.Body),
                "Repeated child preparation must retain its identity and produce an equivalent ordinary Delta.");
            character.Score = 99;
            childRevision = store.Append(changed.Revision);
            Require(loaded.ParentRevisionAddress == initialRevision,
                "Host Append must not advance the original loaded baseline.");
            var current = LoadedWorld.Load<GraphWorld>(store, schemas, childRevision, worldId, models);
            AssertGraph(current.World, 8, constructed);
            Require(store.ReadLiveObjectHeads(childRevision)[worldId] == initialRevision &&
                store.ReadObjectVersionChain(childRevision, characterId).Records.Count == 2,
                "A child-only edit must reuse the World head and extend only the child's content chain.");
            var unchanged = current.Prepare(policy);
            Require(unchanged.Revision.LocalObjects.Count == 0 && unchanged.Revision.RemovedObjectIds.Count == 0,
                "Restored reference identities must not cause a spurious resave.");

            current.World.Disconnect();
            PreparedWorldRevision removed = current.Prepare(policy);
            Require(removed.Revision.LocalObjects.Count == 1 && removed.Revision.LocalObjects[0].ObjectId == worldId &&
                removed.Revision.RemovedObjectIds.Order().SequenceEqual(initialIds.Where(id => id != worldId).Order()),
                "Disconnecting the last World paths must remove the cyclic island and its string.");
            removedRevision = store.Append(removed.Revision);
        }

        using (var file = RbfFile.OpenReadOnlyExisting(schemaPath))
        using (SegmentStore segments = SegmentStore.OpenReadOnlyExisting(statePath, options)) {
            SchemaStore schemas = new(file, readOnly: true);
            StateRevisionStore store = new(segments);
            var removed = LoadedWorld.Load<GraphWorld>(store, schemas, removedRevision, worldId, models);
            Require(removed.World._primary is null && removed.World._alias is null &&
                store.ReadLiveObjectHeads(removedRevision).Keys.SequenceEqual(new[] { worldId }),
                "Cold reopening must retain the removed graph's exact membership.");
            AssertGraph(LoadedWorld.Load<GraphWorld>(store, schemas, initialRevision, worldId, models).World, 7, constructed);
            AssertGraph(LoadedWorld.Load<GraphWorld>(store, schemas, childRevision, worldId, models).World, 8, constructed);
        }
    }

    private static void AssertGraph(GraphWorld world, int score, int constructed) {
        Require(world._primary is GraphCharacter && ReferenceEquals(world._primary, world._alias),
            "A nominal base slot must restore the exact registered derived instance and sharing.");
        GraphCharacter character = (GraphCharacter)world._primary!;
        Require(character.Score == score && ReferenceEquals(character.Item.Owner, character) &&
            ReferenceEquals(character.Item.Self, character.Item), "Readonly mutual/self references lost object identity.");
        Require(ReferenceEquals(character.Name, character.Item.Label) && character.Name == "G",
            "The inherited readonly name and Item label must share one string instance.");
        Require(character.Item.Cache == 0 && GraphConstruction.Count == constructed,
            "Graph restoration must not run constructors or transient field initializers.");
    }

    private static void Require(bool condition, string message) {
        if (!condition) { throw new InvalidOperationException(message); }
    }
}

[DurableType("package.graph-entity", 1)]
public abstract partial class GraphEntity : DurableBase {
    [DurableField(1)] private readonly string _name;
    protected GraphEntity(string name) { GraphConstruction.Count++; _name = name; }
    public string Name => _name;
}

[DurableType("package.graph-character", 1)]
public sealed partial class GraphCharacter : GraphEntity {
    [DurableField(1)] private int _score;
    [DurableField(2)] private readonly GraphItem _item;
    public GraphCharacter(string name, int score) : base(name) {
        GraphConstruction.Count++;
        _score = score;
        _item = new GraphItem(this, name);
    }
    public int Score { get => _score; set => _score = value; }
    public GraphItem Item => _item;
}

[DurableType("package.graph-item", 1)]
public sealed partial class GraphItem : DurableBase {
    [DurableField(1)] private readonly GraphEntity _owner;
    [DurableField(2)] private readonly GraphItem _self;
    [DurableField(3)] private readonly string _label;
    [Transient] private int _cache = 17;
    public GraphItem(GraphEntity owner, string label) {
        GraphConstruction.Count++;
        _owner = owner; _self = this; _label = label;
    }
    public GraphEntity Owner => _owner;
    public GraphItem Self => _self;
    public string Label => _label;
    public int Cache => _cache;
}

internal static class GraphConstruction {
    internal static int Count;
}
