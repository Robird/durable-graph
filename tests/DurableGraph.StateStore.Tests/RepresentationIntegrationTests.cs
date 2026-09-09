using System.Buffers;
using Atelia.DurableGraph.StateStore.Serialization;
using Atelia.DurableGraph.StateStore.Storage;
using Atelia.Rbf;
using Atelia.RbfSegmentStore;
using SegmentStore = Atelia.RbfSegmentStore.RbfSegmentStore;

namespace Atelia.DurableGraph.StateStore.Tests;

public sealed class RepresentationIntegrationTests : IDisposable {
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"durable-representation-integration-{Guid.NewGuid():N}");
    private static readonly RbfSegmentStoreOptions Options = new() { NewStoreLayout = RbfSegmentStoreLayout.Flat };
    private static readonly DurableSchema WorldSchema = new("W", 1,
        new DurableFieldInfo(1, TypeTag.String), new DurableFieldInfo(2, TypeTag.Byte));

    [Fact]
    public void DeltaInheritsRepresentationAcrossColdReopenAndRebaseStartsNewChain() {
        CreateEmptyRepository();
        FrameAddress first, changed, rewritten;
        RepresentationId representation;
        using (IRbfFile schemaFile = OpenSchemas())
        using (SegmentStore segments = OpenState()) {
            SchemaStore schemas = new(schemaFile);
            representation = Assert.Single(schemas.RegisterRepresentations([ObjectLayout.ForDurable(WorldSchema)]));
            StateRevisionStore states = new(segments);
            // Independent v4 header: representation 2, followed by StringId 2 and Value 7.
            byte[] initial = Convert.FromHexString("04020207");
            var text = BaseObjectBodyCodec.EncodeString(StringPayloadCodec.PrepareBase("old"));
            first = states.AppendDurably(StateRevision.CreateObjectHeadMapBase(null,
                [ObjectVersionRecord.CreateBase(1, initial), ObjectVersionRecord.CreateBase(2, text.Body)], []));
            changed = states.AppendDurably(StateRevision.CreateObjectHeadMapDelta(first,
                [ObjectVersionRecord.CreateDelta(1, first, [9])], []));
            var current = BaseObjectBodyCodec.Encode(representation, new PreparedBaseBody([2, 11]));
            rewritten = states.AppendDurably(StateRevision.CreateObjectHeadMapDelta(changed,
                [ObjectVersionRecord.CreateBase(1, current.Body)], []));
        }
        Publish(null, first);
        Publish(first, changed);
        Publish(changed, rewritten);

        using (IRbfFile schemaFile = RbfFile.OpenReadOnlyExisting(Path.Combine(_root, "schemas.rbf")))
        using (SegmentStore segments = SegmentStore.OpenReadOnlyExisting(Path.Combine(_root, "state"), Options)) {
            SchemaStore schemas = new(schemaFile, readOnly: true);
            StateRevisionStore states = new(segments);
            long schemaTail = schemaFile.TailOffset;
            StateReaderRegistry readers = WorldReaders();
            Assert.Equal((byte)7, RevisionDecoder.Read(states, schemas, first, readers).GetRequired(new(1)).GetState<WorldState>().Value);
            DecodedRevision delta = RevisionDecoder.Read(states, schemas, changed, readers);
            Assert.Equal(new WorldState(new(2), 9), delta.GetRequired(new(1)).GetState<WorldState>());
            Assert.Equal("old", delta.GetRequired(new(2)).StringContent);
            Assert.Equal((byte)11, RevisionDecoder.Read(states, schemas, rewritten, readers).GetRequired(new(1)).GetState<WorldState>().Value);
            ObjectVersionChain oldChain = states.ReadObjectVersionChain(changed, 1);
            Assert.Equal(2, oldChain.Records.Count);
            Assert.Equal(representation, BaseObjectBodyCodec.Decode(oldChain.Records[0].Record.Body, schemas).RepresentationId);
            Assert.Equal(new byte[] { 9 }, oldChain.Records[1].Record.Body.ToArray());
            ObjectVersionChain newChain = states.ReadObjectVersionChain(rewritten, 1);
            Assert.Single(newChain.Records);
            Assert.Equal(representation, BaseObjectBodyCodec.Decode(newChain.Records[0].Record.Body, schemas).RepresentationId);
            Assert.Equal(ObjectLayout.ForDurable(WorldSchema), schemas.GetRepresentation(representation));
            Assert.Equal(schemaTail, schemaFile.TailOffset);
        }
        using GraphRepository reopened = GraphRepository.OpenExisting(_root, Options);
        Assert.Equal(rewritten, reopened.HeadRevisionAddress);
        Assert.Equal(new ObjectId(1), reopened.WorldId);
    }

    [Theory]
    [InlineData("01020357010207")]
    [InlineData("020202035700010207")]
    [InlineData("030202035700010207")]
    public void StrictRepositoryReopenRejectsRetiredBaseFormatsWithoutMutatingFiles(string hex) {
        CreateEmptyRepository();
        FrameAddress published;
        using (IRbfFile schemaFile = OpenSchemas())
        using (SegmentStore segments = OpenState()) {
            new SchemaStore(schemaFile).RegisterRepresentations([ObjectLayout.ForDurable(WorldSchema)]);
            StateRevisionStore states = new(segments);
            published = states.AppendDurably(StateRevision.CreateObjectHeadMapBase(null,
                [ObjectVersionRecord.CreateBase(1, Convert.FromHexString(hex)),
                 ObjectVersionRecord.CreateBase(2, BaseObjectBodyCodec.EncodeString(StringPayloadCodec.PrepareBase("old")).Body)], []));
        }
        Publish(null, published);
        var before = Directory.GetFiles(_root, "*", SearchOption.AllDirectories).ToDictionary(path => path, File.ReadAllBytes);
        Assert.Throws<InvalidDataException>(() => {
            using GraphRepository rejected = GraphRepository.OpenExisting(_root, Options);
        });
        Assert.Equal(before.Keys.Order(), Directory.GetFiles(_root, "*", SearchOption.AllDirectories).Order());
        foreach ((string path, byte[] bytes) in before) { Assert.Equal(bytes, File.ReadAllBytes(path)); }
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void StrictRepositoryReopenRejectsUnknownRepresentationInsideOtherwiseValidFrames(bool unknownWorld) {
        CreateEmptyRepository();
        FrameAddress published;
        using (IRbfFile schemaFile = OpenSchemas())
        using (SegmentStore segments = OpenState()) {
            SchemaStore schemas = new(schemaFile);
            RepresentationId valid = Assert.Single(schemas.RegisterRepresentations([ObjectLayout.ForDurable(WorldSchema)]));
            Assert.Equal(new RepresentationId(2), valid);
            var good = BaseObjectBodyCodec.Encode(valid, new PreparedBaseBody([0, 1]));
            // Version and canonical UInt32 ID are valid. Only the repository directory cannot
            // resolve this ID; the RBF CRC, State frame and publication relationship are all valid.
            byte[] unknown = [4, 127, 0, 1];
            ObjectVersionRecord[] objects = unknownWorld
                ? [ObjectVersionRecord.CreateBase(1, unknown)]
                : [ObjectVersionRecord.CreateBase(1, good.Body), ObjectVersionRecord.CreateBase(2, unknown)];
            StateRevisionStore states = new(segments);
            published = states.AppendDurably(StateRevision.CreateObjectHeadMapBase(null, objects, []));
            Assert.Equal(objects.Length, states.Read(published).LocalObjects.Count);
        }
        Publish(null, published);
        using (IRbfFile publication = RbfFile.OpenReadOnlyExisting(Path.Combine(_root, "publication.rbf"))) {
            PublicationLog framing = new(publication, (_, _) => { });
            Assert.Equal(published, framing.Head!.RevisionAddress);
        }
        var before = Directory.GetFiles(_root, "*", SearchOption.AllDirectories)
            .ToDictionary(path => path, File.ReadAllBytes);
        Assert.Throws<InvalidDataException>(() => {
            using GraphRepository rejected = GraphRepository.OpenExisting(_root, Options);
        });
        Assert.Equal(before.Keys.Order(), Directory.GetFiles(_root, "*", SearchOption.AllDirectories).Order());
        foreach ((string path, byte[] bytes) in before) { Assert.Equal(bytes, File.ReadAllBytes(path)); }
    }

    [Fact]
    public void PersistedArrayIdCanBindRetainedValueReaderWithoutDomainDeclarationButDoesNotSupplyMissingCode() {
        CreateEmptyRepository();
        DurableSchema point = new("RetiredPoint", 1, SchemaKind.InlineValue, new DurableFieldInfo(1, TypeTag.Int32));
        ObjectLayout layout = ObjectLayout.ForArray(new(TypeExprKind.VectorArray,
            new DurableFieldInfo(1, TypeTag.InlineValue, inlineSchema: point)));
        RepresentationId representation;
        FrameAddress revision;
        using (IRbfFile schemaFile = OpenSchemas())
        using (SegmentStore segments = OpenState()) {
            SchemaStore schemas = new(schemaFile);
            representation = Assert.Single(schemas.RegisterRepresentations([layout]));
            ArrayBufferWriter<byte> buffer = new();
            BinaryPayloadWriter writer = new(buffer);
            writer.WriteUInt32(2);
            writer.WriteInt32(3);
            writer.WriteInt32(7);
            var body = BaseObjectBodyCodec.Encode(representation, new PreparedBaseBody(buffer.WrittenSpan));
            revision = new StateRevisionStore(segments).AppendDurably(StateRevision.CreateObjectHeadMapBase(null,
                [ObjectVersionRecord.CreateBase(12, body.Body)], []));
        }
        using IRbfFile reopened = RbfFile.OpenReadOnlyExisting(Path.Combine(_root, "schemas.rbf"));
        using SegmentStore stateFiles = SegmentStore.OpenReadOnlyExisting(Path.Combine(_root, "state"), Options);
        SchemaStore recovered = new(reopened, readOnly: true);
        long tail = reopened.TailOffset;
        StateRevisionStore states = new(stateFiles);
        StateReaderRegistry retained = RetiredPointReaders(includeCode: true);
        StateModelSnapshot snapshot = retained.Snapshot(recovered);
        ObjectReaderBinding reader = recovered.ResolveReader(representation, snapshot);
        Assert.Equal(layout, reader.Layout);
        Assert.Throws<InvalidDataException>(() => snapshot.GetDomainType(layout.Type));
        var decoded = RevisionDecoder.Read(states, recovered, revision, retained).GetRequired(new(12)).GetArrayState<int>();
        Assert.Equal(2, decoded.Shape.Count);
        Assert.Equal(3, decoded[0]);
        Assert.Equal(7, decoded[1]);

        // Metadata remains available, including after a successful binding with another snapshot.
        // Neither a retained template alone nor the SchemaStore's earlier reader can supply code.
        StateReaderRegistry metadataOnly = RetiredPointReaders(includeCode: false);
        Assert.Equal(layout, recovered.GetRepresentation(representation));
        Assert.Throws<InvalidDataException>(() => recovered.ResolveReader(representation, metadataOnly.Snapshot(recovered)));
        Assert.Throws<InvalidDataException>(() => RevisionDecoder.Read(states, recovered, revision, metadataOnly));
        Assert.Equal(tail, reopened.TailOffset);
    }

    private readonly record struct WorldState(ObjectId Text, byte Value);

    private static StateReaderRegistry WorldReaders() {
        StateReaderRegistry readers = new();
        readers.Register(new StateReaderBinding<WorldState>(WorldSchema,
            static (ref BinaryPayloadReader input) => new(new ObjectId(input.ReadUInt32()), input.ReadByte()),
            static (ref BinaryPayloadReader input, in WorldState prior) => prior with { Value = input.ReadByte() },
            static (in WorldState state, IStateReferenceVisitor visitor) => visitor.VisitString(state.Text)));
        return readers;
    }

    private static StateReaderRegistry RetiredPointReaders(bool includeCode) {
        StateReaderRegistry readers = new();
        readers.Register(new StateDefinitionBinding("RetiredPoint", SchemaKind.InlineValue, 0, null,
            [new("RetiredPoint", 1, SchemaKind.InlineValue, 0, [new(1, TypeExpr.Builtin(TypeTag.Int32))])],
            historicalValueFactory: includeCode ? static (schema, _) => new(
                new(1, TypeTag.InlineValue, inlineSchema: schema), typeof(int), typeof(Int32StateOps)) : null));
        return readers;
    }

    private void CreateEmptyRepository() {
        using GraphRepository repository = GraphRepository.CreateNew(_root, Options);
    }

    private IRbfFile OpenSchemas() => RbfFile.OpenExisting(Path.Combine(_root, "schemas.rbf"));
    private SegmentStore OpenState() => SegmentStore.OpenExisting(Path.Combine(_root, "state"), Options);

    private void Publish(FrameAddress? prior, FrameAddress next) {
        using IRbfFile file = RbfFile.OpenExisting(Path.Combine(_root, "publication.rbf"));
        PublicationLog publication = new(file, (_, _) => { });
        publication.Publish(prior, new(next, new ObjectId(1)));
    }

    public void Dispose() {
        string resolved = Path.GetFullPath(_root);
        if (!string.Equals(Path.GetDirectoryName(resolved), Path.TrimEndingDirectorySeparator(Path.GetFullPath(Path.GetTempPath())), StringComparison.OrdinalIgnoreCase) ||
            !Path.GetFileName(resolved).StartsWith("durable-representation-integration-", StringComparison.Ordinal)) {
            throw new InvalidOperationException("Refusing to delete outside this test fixture.");
        }
        if (Directory.Exists(resolved)) { Directory.Delete(resolved, recursive: true); }
    }
}
