using Atelia.Data;
using Atelia.DurableGraph;
using Atelia.DurableGraph.StateStore;
using Atelia.DurableGraph.StateStore.Storage;
using Atelia.Rbf;
using Atelia.RbfSegmentStore;
using SegmentStore = Atelia.RbfSegmentStore.RbfSegmentStore;

namespace StateStorePackageConsumerProbe;

[DurableType("package.restore-world", 2)]
public sealed partial class World : DurableBase {
    [DurableField(1)] private int _score;
    [DurableField(2)] private readonly string _name;
    [DurableField(3)] private readonly int _generation;
    [Transient] private int _cache = 37;
    private static int _constructorCalls;
    private static int _upgradeCalls;

    public World(int score, string name) {
        _constructorCalls++;
        _score = score; _name = name; _generation = -1;
    }

    private static void UpgradeStateV1ToV2(in __DurableState.V1 old, out __DurableState.V2 next) {
        _upgradeCalls++;
        next = new(old.Segment0Field1 + 100, old.Segment0Field2, 73);
    }

    internal static void Exercise(string directory) {
        string schemaPath = Path.Combine(directory, "schemas.rbf");
        string statePath = Path.Combine(directory, "state");
        (FrameAddress oldRevision, uint worldId) = ReadV1Address(Path.Combine(directory, "v1-revision.txt"));
        RbfSegmentStoreOptions options = new() { NewStoreLayout = RbfSegmentStoreLayout.Flat };
        FrameAddress upgradedRevision, finalRevision;
        StateModelRegistry models = new();
        __DurableState.RegisterModel(models);
        __DurableState.RegisterModel(models);
        ReadAmplificationBaseBudgetParameters policy = new(int.MaxValue, 1);

        // The V2 process consumes the actual V1 process's files and exact Revision address.
        using (var file = RbfFile.OpenExisting(schemaPath))
        using (SegmentStore segments = SegmentStore.OpenExisting(statePath, options)) {
            SchemaStore schemas = new(file);
            StateRevisionStore store = new(segments);
            Require(store.ReadObjectVersionChain(oldRevision, worldId).Records.Count == 3, "Old Delta chain missing.");
            LoadedWorld<World> loaded = LoadedWorld.Load<World>(store, schemas, oldRevision, worldId, models);
            Require(loaded.ParentRevisionAddress == oldRevision && loaded.WorldId == worldId && _upgradeCalls == 1,
                "Loading lost Parent/World identity or upgraded more than once.");
            Require(loaded.World._score == 109 && loaded.World._name == "A" && loaded.World._generation == 73 &&
                loaded.World._cache == 0 && _constructorCalls == 0, "Upgrade/readonly/constructor-free restoration failed.");
            PreparedWorldRevision prepared = loaded.Prepare(policy);
            Require(prepared.WorldId == worldId && prepared.Revision.ParentRevisionAddress == oldRevision &&
                prepared.Revision.LocalObjects.Count == 1 && prepared.Revision.LocalObjects[0].Kind == ObjectVersionKind.Base,
                "An upgraded unchanged object must be rewritten as current Base.");
            loaded.World._score = 999; // The prepared body must remain independent of later edits.
            upgradedRevision = store.Append(prepared.Revision);
            Require(loaded.ParentRevisionAddress == oldRevision, "Append advanced the original loaded owner.");
            LoadedWorld<World> current = LoadedWorld.Load<World>(store, schemas, upgradedRevision, worldId, models);
            Require(current.World._score == 109 && _upgradeCalls == 1, "Prepared bytes changed or current load reran Upgrade.");
            PreparedWorldRevision unchanged = current.Prepare(policy);
            Require(unchanged.Revision.LocalObjects.Count == 0 && unchanged.Revision.RemovedObjectIds.Count == 0,
                "Current unchanged World should require no object write without policy motive.");
            current.World._score = 110;
            PreparedWorldRevision edited = current.Prepare(policy);
            Require(edited.Revision.LocalObjects.Count == 1 && edited.Revision.LocalObjects[0].Kind == ObjectVersionKind.Delta,
                "A subsequent same-Schema edit should use ordinary Delta.");
            finalRevision = store.Append(edited.Revision);
        }

        using (var file = RbfFile.OpenReadOnlyExisting(schemaPath))
        using (SegmentStore segments = SegmentStore.OpenReadOnlyExisting(statePath, options)) {
            SchemaStore schemas = new(file, readOnly: true);
            StateRevisionStore store = new(segments);
            LoadedWorld<World> final = LoadedWorld.Load<World>(store, schemas, finalRevision, worldId, models);
            Require(final.World._score == 110 && final.World._generation == 73 && final.World._name == "A" &&
                final.World._cache == 0 && _constructorCalls == 0 && _upgradeCalls == 1,
                "Cold reopening failed to restore the current Base plus Delta.");
            Require(store.ReadObjectVersionChain(finalRevision, worldId).Records.Count == 2,
                "Forced Base failed to cut the historical content chain.");
            LoadedWorld<World> oldAgain = LoadedWorld.Load<World>(store, schemas, oldRevision, worldId, models);
            Require(oldAgain.World._score == 109 && _upgradeCalls == 2, "The original historical revision was not preserved.");
        }
    }

    private static (FrameAddress Revision, uint WorldId) ReadV1Address(string path) {
        string[] parts = File.ReadAllText(path).Split(':');
        if (parts.Length != 3 || !uint.TryParse(parts[0], out uint fileNumber) ||
            !ulong.TryParse(parts[1], out ulong packedTicket) || !uint.TryParse(parts[2], out uint worldId)) {
            throw new InvalidDataException("Invalid V1 Revision address handoff.");
        }
        return (new FrameAddress(fileNumber, SizedPtr.FromPacked(packedTicket)), worldId);
    }

    private static void Require(bool condition, string message) {
        if (!condition) { throw new InvalidOperationException(message); }
    }
}
