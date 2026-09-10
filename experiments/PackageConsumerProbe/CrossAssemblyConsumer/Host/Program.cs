using Atelia.Data;
using Atelia.DurableGraph;
using Atelia.DurableGraph.StateStore;
using Atelia.DurableGraph.StateStore.Storage;
using Atelia.Rbf;
using Atelia.RbfSegmentStore;
using CrossAssembly.App;
using CrossAssembly.Domain;
using SegmentStore = Atelia.RbfSegmentStore.RbfSegmentStore;

internal static class Program {
    private static readonly RbfSegmentStoreOptions Options = new() { NewStoreLayout = RbfSegmentStoreLayout.Flat };
    private static readonly ReadAmplificationBaseBudgetParameters Policy = new(int.MaxValue, 1);
    private static void Main(string[] args) {
        if (args.Length != 2) throw new ArgumentException("Expected seed|upgrade and database directory.");
        if (args[0] == "seed") Seed(args[1]);
        else if (args[0] == "upgrade") Upgrade(args[1]);
        else throw new ArgumentException("Unknown mode.");
    }
    private static StateModelRegistry Models() {
        var models = new StateModelRegistry();
        AppCatalog.Register(models);
        DomainCatalog.Register(models);
        return models;
    }
    private static StateReaderRegistry Readers() {
        var readers = new StateReaderRegistry();
        DomainCatalog.RegisterReaders(readers);
        AppCatalog.RegisterReaders(readers);
        return readers;
    }
    private static void Seed(string directory) {
        Require(!DomainCatalog.LegacyClrAbsent, "V1 must contain its original inline CLR type.");
        FrameAddress historical, noChange;
        ObjectId worldId;
        using (GraphRepository repository = GraphRepository.CreateNew(directory, Options)) {
            World world = World.Create();
            using GraphSession<World> session = repository.Create(world, Models());
            session.Commit(Policy);
            worldId = session.WorldId!.Value;
            world.Node.Value++;
            historical = session.Commit(Policy);
            noChange = session.Commit(Policy);
            Require(ReferenceEquals(session.World, world), "Commit replaced the domain graph.");
        }
        Inspect(directory, (store, schemas) => {
            ObjectId nodeId = CheckHistorical(store, schemas, historical, worldId);
            CheckSingleWrite(store, historical, nodeId, ObjectVersionKind.Delta);
            Require(store.Read(noChange).LocalObjects.Count == 0, "Unchanged graph wrote objects.");
        });
        File.WriteAllText(Path.Combine(directory, "historical.txt"), $"{historical.FileNumber}:{historical.FrameTicket.Packed}:{worldId.Value}");
        using (GraphRepository repository = GraphRepository.OpenExisting(directory, Options)) {
            using GraphSession<World> session = repository.Load<World>(Models());
            CheckGraph(session.World, 11);
            noChange = session.Commit(Policy);
        }
        Inspect(directory, (store, _) => Require(store.Read(noChange).LocalObjects.Count == 0, "Cold recapture wrote objects."));
        Console.WriteLine("CrossAssemblySeed:True:IndependentCatalogs:True:SharedCycle:True:ChildOnlyDelta:True:ColdNoChange:True");
    }
    private static void Upgrade(string directory) {
        Require(DomainCatalog.LegacyClrAbsent, "V2 must delete the old inline CLR declaration.");
        string[] parts = File.ReadAllText(Path.Combine(directory, "historical.txt")).Split(':');
        FrameAddress historical = new(uint.Parse(parts[0]), SizedPtr.FromPacked(ulong.Parse(parts[1])));
        ObjectId worldId = new(uint.Parse(parts[2])), nodeId = default;
        Inspect(directory, (store, schemas) => nodeId = CheckHistorical(store, schemas, historical, worldId));
        Require(DomainCatalog.UpgradeCalls == 0, "Exact reading ran business upgrades.");
        FrameAddress rewritten, noChange, changed;
        using (GraphRepository repository = GraphRepository.OpenExisting(directory, Options)) {
            using GraphSession<World> session = repository.Load<World>(Models());
            CheckGraph(session.World, 1011);
            Require(DomainCatalog.UpgradeCalls == 1, "Expected exactly one explicit Node upgrade.");
            rewritten = session.Commit(Policy);
            noChange = session.Commit(Policy);
            session.World.Node.Value++;
            changed = session.Commit(Policy);
        }
        Inspect(directory, (store, schemas) => {
            CheckSingleWrite(store, rewritten, nodeId, ObjectVersionKind.Base);
            Require(store.Read(noChange).LocalObjects.Count == 0, "Upgraded state failed NoChange.");
            CheckSingleWrite(store, changed, nodeId, ObjectVersionKind.Delta);
            CheckHistorical(store, schemas, historical, worldId);
        });
        using (GraphRepository repository = GraphRepository.OpenExisting(directory, Options)) {
            using GraphSession<World> session = repository.Load<World>(Models());
            CheckGraph(session.World, 1012);
            Require(DomainCatalog.UpgradeCalls == 1, "Reopening current state repeated upgrade.");
        }
        Console.WriteLine("CrossAssemblyUpgrade:True:DeletedInlineClr:True:HistoricalExact:True:TargetOnlyBase:True:NoChangeThenDelta:True:ColdReopen:True");
    }
    private static ObjectId CheckHistorical(StateRevisionStore store, SchemaStore schemas, FrameAddress address, ObjectId worldId) {
        DecodedRevision decoded = RevisionDecoder.Read(store, schemas, address, Readers());
        ObjectId nodeId = AppCatalog.GetNodeId(decoded.GetRequired(worldId));
        DomainCatalog.CheckHistorical(decoded.GetRequired(nodeId), 11);
        return nodeId;
    }
    private static void CheckSingleWrite(StateRevisionStore store, FrameAddress address, ObjectId id, ObjectVersionKind kind) {
        var rows = store.Read(address).LocalObjects;
        Require(rows.Count == 1 && rows[0].ObjectId == id.Value && rows[0].Kind == kind,
            $"Expected only target object {id.Value} to write {kind}; nominal-only World must not change.");
    }
    private static void CheckGraph(World world, long value) => Require(world.Node.Value == value &&
        ReferenceEquals(world.Node, world.Alias) && ReferenceEquals(world.Node, world.Node.Next), "Domain data or shared/self identity changed.");
    private static void Inspect(string directory, Action<StateRevisionStore, SchemaStore> action) {
        using var file = RbfFile.OpenReadOnlyExisting(Path.Combine(directory, "schemas.rbf"));
        using SegmentStore segments = SegmentStore.OpenReadOnlyExisting(Path.Combine(directory, "state"), Options);
        action(new StateRevisionStore(segments), new SchemaStore(file, readOnly: true));
    }
    private static void Require(bool condition, string message) {
        if (!condition) throw new InvalidOperationException(message);
    }
}
