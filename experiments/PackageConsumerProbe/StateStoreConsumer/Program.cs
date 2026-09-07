using Atelia.DurableGraph;
using Atelia.DurableGraph.StateStore;
using Atelia.DurableGraph.StateStore.Storage;
using Atelia.Rbf;
using Atelia.RbfSegmentStore;
using SegmentStore = Atelia.RbfSegmentStore.RbfSegmentStore;

namespace StateStorePackageConsumerProbe;

internal static class Program {
    private static void Main(string[] args) {
        if (args.Length != 1) { throw new ArgumentException("Pass one artifact directory."); }
        string artifactDirectory = Path.GetFullPath(args[0]);
#if RESTORE_V1
        World.Seed(Path.Combine(artifactDirectory, "restore"));
        Console.WriteLine("HistoricalWorldSeeded:True");
#else
        Character.Exercise(artifactDirectory);
        Console.WriteLine("PersistedSchema:True:PreparedWorld:True:RawDelta:True:ColdTypedRead:True:SharedString:True:ConflictBeforeAppend:True:DecodedRevision:True");
#if RESTORE_V2
        World.Exercise(Path.Combine(artifactDirectory, "restore"));
        Console.WriteLine("HistoricalUpgrade:True:ConstructorFree:True:ReadonlyHydrate:True:ForcedBase:True:UnchangedResave:True:NormalDelta:True:ReopenedWorld:True");
        GraphWorld.Exercise(Path.Combine(artifactDirectory, "graph"));
        Console.WriteLine("PrepareNewGraph:True:SharedDerived:True:ReadonlyCycles:True:ChildOnlyDelta:True:UnreachableCycleRemoved:True:HistoricalGraphPreserved:True");
#endif
#endif
    }
}

[DurableType("package.persisted-base", 1)]
public abstract partial class NamedObject : DurableBase {
    [DurableField(1)] private string _name;
    protected NamedObject(string name) { _name = name; }
    protected string Name => _name;
}

[DurableType("package.persisted-character", 1)]
public sealed partial class Character : NamedObject {
    [DurableField(1)] private int _score;
    [DurableField(2)] private string _alias;

    private Character(string name, int score) : base(name) { _score = score; _alias = name; }

    internal static void Exercise(string directory) {
        Directory.CreateDirectory(directory);
        string schemaPath = Path.Combine(directory, "schemas.rbf");
        string statePath = Path.Combine(directory, "state");
        FrameAddress firstRevision, secondRevision;
        uint characterId, stringId;
        RbfSegmentStoreOptions options = new() { NewStoreLayout = RbfSegmentStoreLayout.Flat };
        ReadAmplificationBaseBudgetParameters policy = new(int.MaxValue, 1);
        StateModelRegistry models = new();
        __DurableState.RegisterModel(models);

        using (var schemaFile = RbfFile.CreateNew(schemaPath))
        using (SegmentStore segments = SegmentStore.CreateNew(statePath, options)) {
            SchemaStore schemas = new(schemaFile);
            StateRevisionStore store = new(segments);
            string shared = new('A', 1);
            Character source = new(shared, 7);

            PreparedWorldRevision first = LoadedWorld.PrepareNew(store, schemas, source, models, policy);
            characterId = first.WorldId;
            stringId = first.Revision.LocalObjectIds.Single(id => id != characterId);
            Require(first.Revision.ParentRevisionAddress is null && first.Revision.LocalObjects.Count == 2 &&
                first.Revision.LocalObjects.All(record => record.Kind == ObjectVersionKind.Base),
                "A new Character and its shared string must be prepared as Base records.");
            Require(schemas.Count == 2 && schemas.GetRequired(Schema.SchemaId, 1).BaseSchema!.Equals(NamedObject.Schema),
                "Ancestor closure registration failed.");

            long registeredLength = new FileInfo(schemaPath).Length;
            schemas.RegisterBatch([Schema]);
            Require(new FileInfo(schemaPath).Length == registeredLength, "Idempotent registration appended bytes.");
            DurableSchema conflict = new(Schema.SchemaId, 1, []);
            bool rejected = false;
            try { schemas.RegisterBatch([new DurableSchema("package.unpublished", 1, []), conflict]); }
            catch (SchemaConflictException) { rejected = true; }
            Require(rejected && schemas.Count == 2 && new FileInfo(schemaPath).Length == registeredLength,
                "Schema conflict must reject the complete batch before append.");

            source._score = 999; // The prepared plan must retain the frozen score 7.
            firstRevision = store.Append(first.Revision);
            LoadedWorld<Character> loaded = LoadedWorld.Load<Character>(store, schemas, firstRevision, characterId, models);
            Require(loaded.World._score == 7 && loaded.World.Name == "A" &&
                ReferenceEquals(loaded.World.Name, loaded.World._alias),
                "High-level loading lost the frozen state or shared string identity.");
            loaded.World._score = 8;
            PreparedWorldRevision changed = loaded.Prepare(policy);
            Require(changed.Revision.LocalObjects.Count == 1 && changed.Revision.RemovedObjectIds.Count == 0,
                "Changing only the score must prepare one object update.");
            ObjectVersionRecord delta = changed.Revision.LocalObjects[0];
            Require(delta.ObjectId == characterId && delta.Kind == ObjectVersionKind.Delta &&
                delta.Body.SequenceEqual(new byte[] { 2, 16 }), "Unexpected raw Delta body.");
            secondRevision = store.Append(changed.Revision);
        }

        using var coldSchemaFile = RbfFile.OpenReadOnlyExisting(schemaPath);
        SchemaStore coldSchemas = new(coldSchemaFile, readOnly: true);
        using SegmentStore coldSegments = SegmentStore.OpenReadOnlyExisting(statePath, options);
        StateRevisionStore cold = new(coldSegments);
        Require(coldSchemas.Count == 2 && coldSchemas.GetRequired(Schema.SchemaId, 1).BaseSchema!.Equals(NamedObject.Schema),
            "Cold Schema recovery lost its exact ancestor.");
        ObjectVersionChain characterChain = cold.ReadObjectVersionChain(secondRevision, characterId);
        Require(characterChain.Records.Count == 2 && characterChain.Records[1].Record.Kind == ObjectVersionKind.Delta &&
            characterChain.Records[1].Record.Body.SequenceEqual(new byte[] { 2, 16 }),
            "Persisted Delta acquired a Base type header.");

        StateReaderRegistry readers = new();
        __DurableState.RegisterReaders(readers);
        __DurableState.RegisterReaders(readers); // Stable generated registration is idempotent.
        DecodedRevision current = RevisionDecoder.Read(cold, coldSchemas, secondRevision, readers);
        DecodedRevision previous = RevisionDecoder.Read(cold, coldSchemas, firstRevision, readers);
        Require(current.RevisionAddress == secondRevision && previous.RevisionAddress == firstRevision &&
            current.Objects.Count == 2 && previous.Objects.Count == 2 &&
            current.Objects.Select(row => row.Id).SequenceEqual(new[] { characterId, stringId }),
            "Revision decoding lost complete ordered live membership or its query address.");
        var currentState = current.GetRequired(characterId).GetState<__DurableState.V1>();
        var priorState = previous.GetRequired(characterId).GetState<__DurableState.V1>();
        Require(currentState.Segment1Field1 == 8 && priorState.Segment1Field1 == 7,
            "Cold static body reconstruction ignored the selected revision.");
        Require(current.GetRequired(stringId).StringContent == "A", "Built-in string decoding failed.");
        StringReadTable strings = current.Strings;
        Require(ReferenceEquals(current.GetRequired(stringId).StringContent, strings.ResolveString(stringId)) &&
            ReferenceEquals(previous.GetRequired(stringId).StringContent, previous.Strings.ResolveString(stringId)) &&
            ReferenceEquals(strings.ResolveString(currentState.Segment0Field1), strings.ResolveString(currentState.Segment1Field2)),
            "Shared inherited and leaf string fields lost reference identity.");
    }

    private static void Require(bool condition, string message) {
        if (!condition) { throw new InvalidOperationException(message); }
    }
}
