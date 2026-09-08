using Atelia.Data;
using Atelia.DurableGraph;
using Atelia.DurableGraph.StateStore;
using Atelia.DurableGraph.StateStore.Storage;
using Atelia.Rbf;
using Atelia.RbfSegmentStore;
using SegmentStore = Atelia.RbfSegmentStore.RbfSegmentStore;

namespace HistoryCapabilityPackageConsumerProbe;

internal static class Program {
    private static readonly RbfSegmentStoreOptions Options = new() { NewStoreLayout = RbfSegmentStoreLayout.Flat };
    private static readonly ReadAmplificationBaseBudgetParameters Policy = new(int.MaxValue, 1);

    private static void Main(string[] args) {
        if (args.Length != 1) { throw new ArgumentException("Pass one artifact directory."); }
        string directory = Path.GetFullPath(args[0]);
#if HISTORY_V1
        Seed(directory);
        Console.WriteLine("HistorySeed:True:LegacyBaseDelta:True:LegacyCycle:True");
#elif READERS_ONLY
        ReadWithoutUpgrade(directory);
        Console.WriteLine("ExactReadersWithoutUpgrade:True:EditableLoadRequiresUpgrade:True");
#elif HISTORY_V2
        Migrate(directory);
        Console.WriteLine("AllSourceUpgraded:True:AbstractShellNotAllocated:True:BadOrphanRejected:True:LegacyRemoved:True:HistoricalRevisionPreserved:True");
#else
        ReadAfterDeletingShell(directory);
        Console.WriteLine("DeletedShellNewRevision:True:DeletedShellOldRevisionRejected:True:HistoryFilesAreNotReaders:True");
#endif
    }

    private static StateModelRegistry Models() {
        StateModelRegistry models = new();
        World.__DurableState.RegisterModel(models);
#if HISTORY_V1 || HISTORY_V2
        Legacy.__DurableState.RegisterModel(models);
#endif
        return models;
    }

    private static StateReaderRegistry Readers() {
        StateReaderRegistry readers = new();
        World.__DurableState.RegisterReaders(readers);
#if HISTORY_V1 || HISTORY_V2
        Legacy.__DurableState.RegisterReaders(readers);
#endif
        return readers;
    }

#if HISTORY_V1
    private static void Seed(string directory) {
        Directory.CreateDirectory(directory);
        using var file = RbfFile.CreateNew(Path.Combine(directory, "schemas.rbf"));
        using SegmentStore segments = SegmentStore.CreateNew(Path.Combine(directory, "state"), Options);
        SchemaStore schemas = new(file);
        StateRevisionStore store = new(segments);
        StateModelRegistry models = Models();
        PreparedWorldRevision initial = LoadedWorld.PrepareNew(store, schemas, new World(7, new Legacy(11)), models, Policy);
        uint worldId = initial.WorldId;
        uint legacyId = initial.Revision.LocalObjectIds.Single(id => id != worldId);
        Require(initial.Revision.LocalObjects.Count == 2 &&
            initial.Revision.LocalObjects.All(row => row.Kind == ObjectVersionKind.Base), "Expected two initial Base objects.");
        FrameAddress initialRevision = store.Append(initial.Revision);
        LoadedWorld<World> loaded = LoadedWorld.Load<World>(store, schemas, initialRevision, worldId, models);
        Require(loaded.World.Legacy.HasSelfCycle, "V1 self-cycle was not restored.");
        loaded.World.Legacy.Change(12);
        PreparedWorldRevision prepared = loaded.Prepare(Policy);
        Require(prepared.Revision.LocalObjects.Count == 1 && prepared.Revision.LocalObjects[0].ObjectId == legacyId &&
            prepared.Revision.LocalObjects[0].Kind == ObjectVersionKind.Delta, "Expected a child-only Legacy Delta.");
        FrameAddress historical = store.Append(prepared.Revision);
        Require(store.ReadObjectVersionChain(historical, legacyId).Records.Count == 2, "Legacy Base/Delta chain missing.");
        WriteAddress(directory, "historical", historical, worldId, legacyId);
    }
#elif READERS_ONLY
    private static void ReadWithoutUpgrade(string directory) {
        using var file = RbfFile.OpenReadOnlyExisting(Path.Combine(directory, "schemas.rbf"));
        using SegmentStore segments = SegmentStore.OpenReadOnlyExisting(Path.Combine(directory, "state"), Options);
        SchemaStore schemas = new(file, readOnly: true);
        StateRevisionStore store = new(segments);
        var (historical, worldId, legacyId) = ReadAddress(directory, "historical");
        DecodedRevision decoded = RevisionDecoder.Read(store, schemas, historical, Readers());
        Require(decoded.Objects.Count == 2 &&
            decoded.GetRequired(legacyId).GetState<Legacy.__DurableState.V1>().Segment0Field1 == 12,
            "Exact readers must reconstruct historical Base/Delta without any Upgrade methods compiled.");
        ExpectInvalidData(() => LoadedWorld.Load<World>(store, schemas, historical, worldId, Models()),
            "Missing single-object upgrade");
    }
#elif HISTORY_V2
    private static void Migrate(string directory) {
        using var file = RbfFile.OpenExisting(Path.Combine(directory, "schemas.rbf"));
        using SegmentStore segments = SegmentStore.OpenExisting(Path.Combine(directory, "state"), Options);
        SchemaStore schemas = new(file);
        StateRevisionStore store = new(segments);
        var (historical, worldId, legacyId) = ReadAddress(directory, "historical");
        StateModelRegistry models = Models();

        DecodedRevision decoded = RevisionDecoder.Read(store, schemas, historical, Readers());
        Require(decoded.Objects.Count == 2 && World.UpgradeCalls == 0 && Legacy.UpgradeCalls == 0,
            "Exact reading must not normalize either object.");
        Legacy.ProduceInvalidReference = true;
        ExpectInvalidData(() => LoadedWorld.Load<World>(store, schemas, historical, worldId, models));
        Require(World.UpgradeCalls == 1 && Legacy.UpgradeCalls == 1,
            "Even a newly orphaned shell must upgrade and have its invalid current reference rejected.");
        Legacy.ProduceInvalidReference = false;
        World.UpgradeCalls = Legacy.UpgradeCalls = 0;
        LoadedWorld<World> loaded = LoadedWorld.Load<World>(store, schemas, historical, worldId, models);
        Require(loaded.World.Score == 107 && World.UpgradeCalls == 1 && Legacy.UpgradeCalls == 1 &&
            Legacy.LastHistoricalValue == 12, "The full source directory was not upgraded from its exact stored values.");
        Require(typeof(Legacy).IsAbstract, "The migration shell must be abstract.");
        bool allocationRejected = false;
        try { Legacy.__DurableState.Allocate(); }
        catch (InvalidOperationException) { allocationRejected = true; }
        Require(allocationRejected, "Abstract shell allocation must fail, so successful Load proves it was not allocated.");
        PreparedWorldRevision prepared = loaded.Prepare(Policy);
        Require(prepared.Revision.RemovedObjectIds.SequenceEqual(new[] { legacyId }) &&
            prepared.Revision.LocalObjects.Count == 1 && prepared.Revision.LocalObjects[0].ObjectId == worldId &&
            prepared.Revision.LocalObjects[0].Kind == ObjectVersionKind.Base,
            "Migration must force the upgraded World Base and remove the complete orphaned Legacy object.");
        FrameAddress migrated = store.Append(prepared.Revision);
        Require(store.ReadLiveObjectHeadMap(migrated).Keys.SequenceEqual(new[] { worldId }), "Migrated membership retains Legacy.");
        Require(RevisionDecoder.Read(store, schemas, historical, Readers()).Objects.Count == 2,
            "Removing Legacy from the new Revision altered the historical Revision.");
        WriteAddress(directory, "migrated", migrated, worldId, legacyId);
    }
#else
    private static void ReadAfterDeletingShell(string directory) {
        using var file = RbfFile.OpenReadOnlyExisting(Path.Combine(directory, "schemas.rbf"));
        using SegmentStore segments = SegmentStore.OpenReadOnlyExisting(Path.Combine(directory, "state"), Options);
        SchemaStore schemas = new(file, readOnly: true);
        StateRevisionStore store = new(segments);
        var (historical, worldId, _) = ReadAddress(directory, "historical");
        var (migrated, newWorldId, _) = ReadAddress(directory, "migrated");
        Require(worldId == newWorldId, "Migration changed World identity.");
        Require(typeof(World).Assembly.GetType("HistoryCapabilityPackageConsumerProbe.Legacy") is null,
            "The third consumer must contain no Legacy CLR shell.");
        Require(schemas.GetRequired("package.history-legacy", 1).Version == 1,
            "Legacy metadata must still exist independently of its absent executable reader.");
        LoadedWorld<World> loaded = LoadedWorld.Load<World>(store, schemas, migrated, worldId, Models());
        Require(loaded.World.Score == 107 && World.UpgradeCalls == 0 &&
            RevisionDecoder.Read(store, schemas, migrated, Readers()).Objects.Count == 1,
            "The migrated Revision should load with only the surviving World model.");
        ExpectInvalidData(() => RevisionDecoder.Read(store, schemas, historical, Readers()),
            "No declaration factory is registered for package.history-legacy");
        ExpectInvalidData(() => LoadedWorld.Load<World>(store, schemas, historical, worldId, Models()),
            "No declaration factory is registered for package.history-legacy");
    }
#endif

    private static void WriteAddress(string directory, string name, FrameAddress revision, uint worldId, uint legacyId) =>
        File.WriteAllText(Path.Combine(directory, name + ".txt"),
            $"{revision.FileNumber}:{revision.FrameTicket.Packed}:{worldId}:{legacyId}");

    private static (FrameAddress Revision, uint WorldId, uint LegacyId) ReadAddress(string directory, string name) {
        string[] parts = File.ReadAllText(Path.Combine(directory, name + ".txt")).Split(':');
        Require(parts.Length == 4, "Malformed address handoff.");
        return (new FrameAddress(uint.Parse(parts[0]), SizedPtr.FromPacked(ulong.Parse(parts[1]))),
            uint.Parse(parts[2]), uint.Parse(parts[3]));
    }

    private static void ExpectInvalidData(Action action, string? message = null) {
        try { action(); }
        catch (InvalidDataException error) when (message is null || error.Message.Contains(message, StringComparison.Ordinal)) { return; }
        throw new InvalidOperationException("Expected a specific InvalidDataException.");
    }

    internal static void Require(bool condition, string message) {
        if (!condition) { throw new InvalidOperationException(message); }
    }
}
