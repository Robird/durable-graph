using Atelia.DurableGraph;
using Atelia.DurableGraph.StateStore;
using Atelia.DurableGraph.StateStore.Serialization;
using Atelia.DurableGraph.StateStore.Storage;
using Atelia.Rbf;
using Atelia.RbfSegmentStore;
using SegmentStore = Atelia.RbfSegmentStore.RbfSegmentStore;

namespace StateStorePackageConsumerProbe;

[DurableType("package.restore-world", 2, SchemaOnly = true, GenerateBinaryBody = true)]
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

    private static void UpgradeStateV1ToV2(in __DurableBinaryBody.V1 old, out __DurableBinaryBody.V2 next) {
        _upgradeCalls++;
        next = new(old.Segment0Field1 + 100, old.Segment0Field2, 73);
    }

    internal static void Exercise(string directory) {
        Directory.CreateDirectory(directory);
        string schemaPath = Path.Combine(directory, "schemas.rbf");
        string statePath = Path.Combine(directory, "state");
        RbfSegmentStoreOptions options = new() { NewStoreLayout = RbfSegmentStoreLayout.Flat };
        FrameAddress oldRevision, upgradedRevision, finalRevision;
        StateModelRegistry models = new();
        __DurableBinaryBody.RegisterModel(models);
        __DurableBinaryBody.RegisterModel(models);
        ReadAmplificationBaseBudgetParameters policy = new(int.MaxValue, 1);

        using (var file = RbfFile.CreateNew(schemaPath))
        using (SegmentStore segments = SegmentStore.CreateNew(statePath, options)) {
            SchemaStore schemas = new(file);
            StateRevisionStore store = new(segments);
            schemas.RegisterBatch([__DurableBinaryBody.V1.Schema]);
            var first = new __DurableBinaryBody.V1(7, 2);
            var second = new __DurableBinaryBody.V1(8, 2);
            var third = new __DurableBinaryBody.V1(9, 2);
            var body = BaseObjectPayloadCodec.EncodeDurable(__DurableBinaryBody.V1.Schema, __DurableBinaryBody.PrepareBase(in first));
            var text = BaseObjectPayloadCodec.EncodeString(StringPayloadCodec.PrepareBase("A"));
            FrameAddress initial = store.Append(StateRevision.CreateBase(null, [
                ObjectVersionRecord.CreateBase(1, body.Payload), ObjectVersionRecord.CreateBase(2, text.Payload)
            ], []));
            var delta1 = __DurableBinaryBody.PrepareDelta(in first, in second);
            FrameAddress middle = store.Append(StateRevision.CreateDelta(initial,
                [ObjectVersionRecord.CreateDelta(1, initial, delta1.Payload)], []));
            var delta2 = __DurableBinaryBody.PrepareDelta(in second, in third);
            oldRevision = store.Append(StateRevision.CreateDelta(middle,
                [ObjectVersionRecord.CreateDelta(1, middle, delta2.Payload)], []));
        }

        // The public loading/planning API must consume only reopened storage and explicit World ID.
        using (var file = RbfFile.OpenExisting(schemaPath))
        using (SegmentStore segments = SegmentStore.OpenExisting(statePath, options)) {
            SchemaStore schemas = new(file);
            StateRevisionStore store = new(segments);
            Require(store.ReadObjectVersionChain(oldRevision, 1).Records.Count == 3, "Old Delta chain missing.");
            var loaded = LoadedWorld.Load<World>(store, schemas, oldRevision, 1, models);
            Require(loaded.ParentRevisionAddress == oldRevision && loaded.WorldId == 1 && _upgradeCalls == 1,
                "Loading lost Parent/World identity or upgraded more than once.");
            Require(loaded.World._score == 109 && loaded.World._name == "A" && loaded.World._generation == 73 &&
                loaded.World._cache == 0 && _constructorCalls == 0, "Upgrade/readonly/constructor-free restoration failed.");
            var prepared = loaded.Prepare(policy);
            Require(prepared.WorldId == 1 && prepared.Revision.ParentRevisionAddress == oldRevision &&
                prepared.Revision.LocalObjects.Count == 1 && prepared.Revision.LocalObjects[0].Kind == ObjectVersionKind.Base,
                "An upgraded unchanged object must be rewritten as current Base.");
            loaded.World._score = 999; // The prepared payload must remain independent of later edits.
            upgradedRevision = store.Append(prepared.Revision);
            Require(loaded.ParentRevisionAddress == oldRevision, "Append advanced the original loaded owner.");
            var current = LoadedWorld.Load<World>(store, schemas, upgradedRevision, prepared.WorldId, models);
            Require(current.World._score == 109 && _upgradeCalls == 1, "Prepared bytes changed or current load reran Upgrade.");
            var unchanged = current.Prepare(policy);
            Require(unchanged.Revision.LocalObjects.Count == 0 && unchanged.Revision.RemovedObjectIds.Count == 0,
                "Current unchanged World should require no object write without policy motive.");
            current.World._score = 110;
            var edited = current.Prepare(policy);
            Require(edited.Revision.LocalObjects.Count == 1 && edited.Revision.LocalObjects[0].Kind == ObjectVersionKind.Delta,
                "A subsequent same-Schema edit should use ordinary Delta.");
            finalRevision = store.Append(edited.Revision);
        }

        using (var file = RbfFile.OpenReadOnlyExisting(schemaPath))
        using (SegmentStore segments = SegmentStore.OpenReadOnlyExisting(statePath, options)) {
            SchemaStore schemas = new(file, readOnly: true);
            StateRevisionStore store = new(segments);
            var final = LoadedWorld.Load<World>(store, schemas, finalRevision, 1, models);
            Require(final.World._score == 110 && final.World._generation == 73 && final.World._name == "A" &&
                final.World._cache == 0 && _constructorCalls == 0 && _upgradeCalls == 1,
                "Cold reopening failed to restore the current Base plus Delta.");
            Require(store.ReadObjectVersionChain(finalRevision, 1).Records.Count == 2,
                "Forced Base failed to cut the historical content chain.");
            var oldAgain = LoadedWorld.Load<World>(store, schemas, oldRevision, 1, models);
            Require(oldAgain.World._score == 109 && _upgradeCalls == 2, "The original historical revision was not preserved.");
        }
    }

    private static void Require(bool condition, string message) {
        if (!condition) { throw new InvalidOperationException(message); }
    }
}
