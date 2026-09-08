using Atelia.DurableGraph;
using Atelia.DurableGraph.StateStore;
using Atelia.DurableGraph.StateStore.Storage;
using Atelia.Rbf;
using Atelia.RbfSegmentStore;
using SegmentStore = Atelia.RbfSegmentStore.RbfSegmentStore;

namespace StateStorePackageConsumerProbe;

// This first build publishes real package-generated history and seeds V1 state for the V2 process.
[DurableType("package.restore-world", 1)]
public sealed partial class World : DurableBase {
    [DurableField(1)] private int _score;
    [DurableField(2)] private readonly string _name;
    [DurableField(4)] private readonly ulong _createdAtTicks;

    public World(int score, string name) { _score = score; _name = name; _createdAtTicks = 638_625_600_000_000_000; }

    internal static void Seed(string directory) {
        Directory.CreateDirectory(directory);
        string schemaPath = Path.Combine(directory, "schemas.rbf");
        string statePath = Path.Combine(directory, "state");
        RbfSegmentStoreOptions options = new() { NewStoreLayout = RbfSegmentStoreLayout.Flat };
        ReadAmplificationBaseBudgetParameters policy = new(int.MaxValue, 1);
        StateModelRegistry models = new();
        __DurableState.RegisterModel(models);
        FrameAddress revision;
        ObjectId worldId;

        using (var file = RbfFile.CreateNew(schemaPath))
        using (SegmentStore segments = SegmentStore.CreateNew(statePath, options)) {
            SchemaStore schemas = new(file);
            StateRevisionStore store = new(segments);
            PreparedWorldRevision initial = LoadedWorld.PrepareNew(store, schemas, new World(7, "A"), models, policy);
            worldId = initial.WorldId;
            revision = store.Append(initial.Revision);

            LoadedWorld<World> second = LoadedWorld.Load<World>(store, schemas, revision, worldId, models);
            second.World._score = 8;
            revision = store.Append(second.Prepare(policy).Revision);

            LoadedWorld<World> third = LoadedWorld.Load<World>(store, schemas, revision, worldId, models);
            Require(third.World._createdAtTicks == 638_625_600_000_000_000, "A score Delta changed World's creation timestamp.");
            third.World._score = 9;
            revision = store.Append(third.Prepare(policy).Revision);
            Require(store.ReadObjectVersionChain(revision, worldId.Value).Records.Count == 3,
                "The V1 process did not persist its Base plus two Delta records.");
        }

        File.WriteAllText(Path.Combine(directory, "v1-revision.txt"),
            $"{revision.FileNumber}:{revision.FrameTicket.Packed}:{worldId.Value}");
    }

    private static void Require(bool condition, string message) {
        if (!condition) { throw new InvalidOperationException(message); }
    }
}
