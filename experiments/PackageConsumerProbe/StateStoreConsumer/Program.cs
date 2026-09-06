using Atelia.DurableGraph;
using Atelia.DurableGraph.StateStore;
using Atelia.DurableGraph.StateStore.Storage;
using Atelia.Rbf;
using Atelia.RbfSegmentStore;
using SegmentStore = Atelia.RbfSegmentStore.RbfSegmentStore;

namespace StateStorePackageConsumerProbe;

internal static class Program {
    private static void Main(string[] args) {
        if (args.Length != 1) { throw new ArgumentException("Pass one unused artifact directory."); }
        Character.Exercise(Path.GetFullPath(args[0]));
        Console.WriteLine("PersistedSchema:True:BaseTypeReference:True:RawDelta:True:ColdTypedRead:True:SharedString:True:ConflictBeforeAppend:True");
    }
}

[DurableType("package.persisted-base", 1, SchemaOnly = true, GenerateBinaryBody = true)]
public abstract partial class NamedObject : DurableBase {
    [DurableField(1)] private string _name;
    protected NamedObject(string name) { _name = name; }
}

[DurableType("package.persisted-character", 1, SchemaOnly = true, GenerateBinaryBody = true)]
public sealed partial class Character : NamedObject {
    [DurableField(1)] private int _score;
    [DurableField(2)] private string _alias;

    private Character(string name, int score) : base(name) { _score = score; _alias = name; }

    internal static void Exercise(string directory) {
        Directory.CreateDirectory(directory);
        string schemaPath = Path.Combine(directory, "schemas.rbf");
        string statePath = Path.Combine(directory, "state");
        FrameAddress firstRevision, secondRevision;
        uint firstId, secondId, stringId;
        RbfSegmentStoreOptions options = new() { NewStoreLayout = RbfSegmentStoreLayout.Flat };

        using (var schemaFile = RbfFile.CreateNew(schemaPath))
        using (SegmentStore segments = SegmentStore.CreateNew(statePath, options)) {
            SchemaStore schemas = new(schemaFile);
            StateRevisionStore store = new(segments);
            CaptureSession session = new();
            string shared = new('A', 1);
            Character first = new(shared, 7), second = new(shared, 9);
            CapturedGraph graph = Capture(session, first, second);
            firstId = graph.RootIds[0]; secondId = graph.RootIds[1];
            stringId = graph.Objects.Single(item => item.Kind == CapturedObjectKind.String).Id;
            Require(firstId == 1 && secondId == 2 && stringId == 3, "Unexpected capture IDs.");
            first._score = 8; // Prepare must retain the frozen score 7.
            PreparedCapturedGraph prepared = session.Prepare(graph);
            schemas.RegisterBatch(prepared.Objects.Where(row => row.Current.Schema is not null).Select(row => row.Current.Schema!));
            Require(schemas.Count == 2 && schemas.GetRequired(Schema.SchemaId, 1).Equals(Schema), "Ancestor closure registration failed.");

            long registeredLength = new FileInfo(schemaPath).Length;
            schemas.RegisterBatch([Schema]);
            Require(new FileInfo(schemaPath).Length == registeredLength, "Idempotent registration appended bytes.");
            DurableSchema conflict = new(Schema.SchemaId, 1, []);
            bool rejected = false;
            try { schemas.RegisterBatch([new DurableSchema("package.unpublished", 1, []), conflict]); }
            catch (SchemaConflictException) { rejected = true; }
            Require(rejected && schemas.Count == 2 && new FileInfo(schemaPath).Length == registeredLength,
                "Schema conflict must reject the complete batch before append.");

            List<ObjectVersionRecord> records = [];
            foreach (PreparedCapturedObject row in prepared.Objects) {
                var content = row.Current.Kind == CapturedObjectKind.String
                    ? BaseObjectPayloadCodec.EncodeString(row.BaseContent)
                    : BaseObjectPayloadCodec.EncodeDurable(row.Current.Schema!, row.BaseContent);
                records.Add(ObjectVersionRecord.CreateBase(row.Current.Id, content.Payload));
            }
            firstRevision = store.Append(StateRevision.CreateBase(null, records, []));
            session.Accept(graph); // Explicit fixture baseline, not a published repository head.

            CapturedGraph next = Capture(session, first, second);
            first._score = 999;
            PreparedCapturedGraph nextPrepared = session.Prepare(next);
            var changed = nextPrepared.Objects.Single(row => row.Current.Id == firstId);
            Require(changed.DeltaContent is { HasChanges: true } &&
                changed.DeltaContent.Payload.SequenceEqual(new byte[] { 2, 16 }), "Unexpected raw Delta body.");
            Require(nextPrepared.Objects.Single(row => row.Current.Id == secondId).DeltaContent is { HasChanges: false },
                "The second owner should remain unchanged.");
            // Deliberately choose one Delta to witness the public package boundary, not the policy.
            var delta = ObjectVersionRecord.CreateDelta(firstId, firstRevision, changed.DeltaContent!.Payload);
            secondRevision = store.Append(StateRevision.CreateDelta(firstRevision, [delta], []));
            session.Discard(next);
        }

        using var coldSchemaFile = RbfFile.OpenReadOnlyExisting(schemaPath);
        SchemaStore coldSchemas = new(coldSchemaFile, readOnly: true);
        using SegmentStore coldSegments = SegmentStore.OpenReadOnlyExisting(statePath, options);
        StateRevisionStore cold = new(coldSegments);
        Require(coldSchemas.Count == 2 && coldSchemas.GetRequired(Schema.SchemaId, 1).BaseSchema!.Equals(NamedObject.Schema),
            "Cold Schema recovery lost its exact ancestor.");
        ObjectVersionChain firstChain = cold.ReadObjectVersionChain(secondRevision, firstId);
        Require(firstChain.Records.Count == 2 && firstChain.Records[1].Record.Kind == ObjectVersionKind.Delta &&
            firstChain.Records[1].Record.Body.SequenceEqual(new byte[] { 2, 16 }), "Persisted Delta acquired a header.");
        var envelope = BaseObjectPayloadCodec.Decode(firstChain.Records[0].Record.Body);
        Require(envelope.Kind == CapturedObjectKind.Durable && envelope.SchemaKey == new SchemaKey(Schema.SchemaId, 1) &&
            envelope.Body.SequenceEqual(new byte[] { 3, 14, 3 }), "Base did not preserve its reference and frozen body.");

        var firstState = TypedObjectVersionReader.ReadDurable(firstChain, coldSchemas, __DurableBinaryBody.V1.Schema,
            __DurableBinaryBody.ReadV1, __DurableBinaryBody.ApplyDeltaV1);
        var secondState = TypedObjectVersionReader.ReadDurable(cold.ReadObjectVersionChain(secondRevision, secondId), coldSchemas,
            __DurableBinaryBody.V1.Schema, __DurableBinaryBody.ReadV1, __DurableBinaryBody.ApplyDeltaV1);
        var oldState = TypedObjectVersionReader.ReadDurable(cold.ReadObjectVersionChain(firstRevision, firstId), coldSchemas,
            __DurableBinaryBody.V1.Schema, __DurableBinaryBody.ReadV1, __DurableBinaryBody.ApplyDeltaV1);
        Require(firstState.Segment1Field1 == 8 && secondState.Segment1Field1 == 9 && oldState.Segment1Field1 == 7,
            "Cold static body reconstruction ignored the selected revision.");

        ObjectVersionChain stringChain = cold.ReadObjectVersionChain(secondRevision, stringId);
        Require(TypedObjectVersionReader.ReadString(stringChain) == "A", "Built-in string decoding failed.");
        var stringEnvelope = BaseObjectPayloadCodec.Decode(stringChain.Records[0].Record.Body);
        Require(stringEnvelope.SchemaKey is null, "Built-in string must not require SchemaStore.");
        StringReadTable strings = StringReadTable.Decode([(stringId, (ReadOnlyMemory<byte>)stringEnvelope.Body.ToArray())]);
        __DurableBinaryBody.ValidateStringReferences(in firstState, strings);
        __DurableBinaryBody.ValidateStringReferences(in secondState, strings);
        Require(ReferenceEquals(strings.ResolveString(firstState.Segment0Field1), strings.ResolveString(secondState.Segment1Field2)) &&
            ReferenceEquals(strings.ResolveString(firstState.Segment1Field2), strings.ResolveString(secondState.Segment0Field1)),
            "Shared strings lost reference identity across inherited owners.");
    }

    private static CapturedGraph Capture(CaptureSession session, Character first, Character second) {
        CaptureContext context = session.BeginCapture();
        __DurableBinaryBody.AddRoot(context, first);
        __DurableBinaryBody.AddRoot(context, second);
        return context.Seal();
    }

    private static void Require(bool condition, string message) {
        if (!condition) { throw new InvalidOperationException(message); }
    }
}
