using Atelia.DurableGraph.StateStore.Serialization;
using Atelia.DurableGraph.StateStore.Storage;
using Atelia.Rbf;
using Atelia.RbfSegmentStore;
using SegmentStore = Atelia.RbfSegmentStore.RbfSegmentStore;

namespace Atelia.DurableGraph.StateStore.Tests;

public sealed class InlineSchemaStoreTests : IDisposable {
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"durable-inline-schema-{Guid.NewGuid():N}");
    private int _sequence;
    private static readonly Dictionary<SchemaKey, DurableSchema> Empty = new();

    public InlineSchemaStoreTests() => Directory.CreateDirectory(_root);

    [Fact]
    public void V4IndependentGoldenBindsExactForwardInlineSchemaAndV2RemainsReadable() {
        DurableSchema value = new("Z", 2, SchemaKind.InlineValue, new DurableFieldInfo(1, TypeTag.Int32));
        DurableSchema owner = new("A", 1, new DurableFieldInfo(1, TypeTag.InlineValue, inlineSchema: value));
        byte[] golden = Convert.FromHexString("04020203410001010001011002035A000202035A00020200010102");
        Assert.Equal(golden, SchemaBatchWireCodec.Write([value, owner]));
        var read = SchemaBatchWireCodec.Read(golden, Empty);
        Assert.Equal(owner, read[new("A", 1)]);
        Assert.Equal(SchemaKind.ReferenceObject, read[new("A", 1)].Kind);
        Assert.Equal(SchemaKind.InlineValue, read[new("Z", 2)].Kind);
        Assert.Same(read[new("Z", 2)], read[new("A", 1)].Fields[0].InlineSchema);
        byte[] legacy = Convert.FromHexString("02020341010100010110035A02035A020200010102");
        Assert.Equal(owner, SchemaBatchWireCodec.Read(legacy, Empty)[new("A", 1)]);
        for (int length = 0; length < golden.Length; length++) {
            byte[] prefix = golden[..length];
            Assert.ThrowsAny<Exception>(() => SchemaBatchWireCodec.Read(prefix, Empty));
        }
    }

    [Fact]
    public void V1RemainsReadableAndV4AppendsWithoutRewritingOldBytes() {
        string path = NextPath();
        byte[] legacy = Convert.FromHexString("010203410101035A020201010809035A0200010304");
        byte[] original;
        using (IRbfFile file = RbfFile.CreateNew(path)) {
            file.Append(SchemaBatchWireCodec.RbfTag, legacy).Unwrap();
            file.DurableFlush();
        }
        original = File.ReadAllBytes(path);
        DurableSchema value = new("Value", 1, SchemaKind.InlineValue, new DurableFieldInfo(1, TypeTag.Byte));
        DurableSchema owner = new("Owner", 1, new DurableFieldInfo(1, TypeTag.InlineValue, inlineSchema: value));
        using (IRbfFile file = RbfFile.OpenExisting(path)) {
            SchemaStore store = new(file);
            Assert.Equal(2, store.Count);
            Assert.Equal(SchemaKind.ReferenceObject, store.GetRequired("Z", 2).Kind);
            Assert.Same(store.GetRequired("Z", 2), store.GetRequired("A", 1).BaseSchema);
            store.Register(owner);
            Assert.Equal(4, store.Count);
            var scan = file.ScanForward().GetEnumerator();
            Assert.True(scan.MoveNext());
            using RbfPooledFrame first = file.ReadPooledFrame(scan.Current.Ticket).Unwrap();
            Assert.Equal(legacy, first.PayloadAndMeta.ToArray());
            Assert.True(scan.MoveNext());
            using RbfPooledFrame second = file.ReadPooledFrame(scan.Current.Ticket).Unwrap();
            Assert.Equal(4, second.PayloadAndMeta[0]);
        }
        Assert.Equal(original, File.ReadAllBytes(path)[..original.Length]);
        using IRbfFile reopened = RbfFile.OpenReadOnlyExisting(path);
        SchemaStore restored = new(reopened, readOnly: true);
        Assert.Equal(owner, restored.GetRequired("Owner", 1));
        Assert.Equal(4, restored.Count);
    }

    [Fact]
    public void BaseAndInlineClosureIsRegisteredAtomicallyAndSharedOnReopen() {
        DurableSchema point = new("Point", 1, SchemaKind.InlineValue, new DurableFieldInfo(1, TypeTag.String));
        DurableSchema pair = new("Pair", 1, SchemaKind.InlineValue,
            new DurableFieldInfo(1, TypeTag.InlineValue, inlineSchema: point),
            new DurableFieldInfo(2, TypeTag.InlineValue, inlineSchema: point),
            new DurableFieldInfo(3, TypeTag.ObjectReference, "Owner"));
        DurableSchema ancestor = new("Ancestor", 1, new DurableFieldInfo(1, TypeTag.InlineValue, inlineSchema: point));
        DurableSchema owner = new("Owner", 1, [new(1, TypeTag.InlineValue, inlineSchema: pair)], ancestor);
        string path = NextPath();
        using (IRbfFile file = RbfFile.CreateNew(path)) {
            SchemaStore store = new(file);
            store.Register(owner);
            Assert.Equal(4, store.Count);
            long tail = file.TailOffset;
            store.Register(owner);
            Assert.Equal(tail, file.TailOffset);
            DurableSchema changedPoint = new("Point", 1, SchemaKind.InlineValue, new DurableFieldInfo(1, TypeTag.Int32));
            DurableSchema changedOwner = new("Fresh", 1, new DurableFieldInfo(1, TypeTag.InlineValue, inlineSchema: changedPoint));
            Assert.Throws<SchemaConflictException>(() => store.RegisterBatch([new("Independent", 1), changedOwner]));
            Assert.Equal(tail, file.TailOffset);
            Assert.Equal(4, store.Count);
            Assert.False(store.IsFaulted);
        }
        using IRbfFile reopened = RbfFile.OpenReadOnlyExisting(path);
        SchemaStore restored = new(reopened, readOnly: true);
        Assert.Equal(owner, restored.GetRequired("Owner", 1));
        Assert.Same(restored.GetRequired("Point", 1), restored.GetRequired("Ancestor", 1).Fields[0].InlineSchema);
        Assert.Same(restored.GetRequired("Point", 1), restored.GetRequired("Pair", 1).Fields[0].InlineSchema);
        Assert.Same(restored.GetRequired("Point", 1), restored.GetRequired("Pair", 1).Fields[1].InlineSchema);
    }

    [Fact]
    public void InlineVersionChangeConflictsUnlessOwnerAlsoChangesVersion() {
        DurableSchema value1 = new("Value", 1, SchemaKind.InlineValue, new DurableFieldInfo(1, TypeTag.Int32));
        DurableSchema value2 = new("Value", 2, SchemaKind.InlineValue, new DurableFieldInfo(1, TypeTag.Int64));
        DurableSchema owner1 = new("Owner", 1, new DurableFieldInfo(1, TypeTag.InlineValue, inlineSchema: value1));
        DurableSchema wrong = new("Owner", 1, new DurableFieldInfo(1, TypeTag.InlineValue, inlineSchema: value2));
        DurableSchema owner2 = new("Owner", 2, new DurableFieldInfo(1, TypeTag.InlineValue, inlineSchema: value2));
        using IRbfFile file = RbfFile.CreateNew(NextPath());
        SchemaStore store = new(file);
        store.Register(owner1);
        long tail = file.TailOffset;
        Assert.Throws<SchemaConflictException>(() => store.Register(wrong));
        Assert.Equal(tail, file.TailOffset);
        Assert.Equal(2, store.Count);
        store.Register(owner2);
        Assert.Equal(4, store.Count);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void FamilyKindCannotChangeWithinOrAcrossRegistrationBatches(bool sameBatch) {
        DurableSchema reference = new("A", 1);
        DurableSchema inline = new("A", 2, SchemaKind.InlineValue);
        using IRbfFile file = RbfFile.CreateNew(NextPath());
        SchemaStore store = new(file);
        if (!sameBatch) { store.Register(reference); }
        long tail = file.TailOffset;
        Assert.Throws<ArgumentException>(() => store.RegisterBatch(sameBatch ? [reference, inline] : [inline]));
        Assert.Equal(tail, file.TailOffset);
        Assert.Equal(sameBatch ? 0 : 1, store.Count);
        Assert.False(store.IsFaulted);
        var registered = sameBatch ? Empty : new Dictionary<SchemaKey, DurableSchema> { [new("A", 1)] = reference };
        byte[] batch = SchemaBatchWireCodec.Write(sameBatch ? [reference, inline] : [inline]);
        Assert.Throws<InvalidDataException>(() => SchemaBatchWireCodec.Read(batch, registered));
        Assert.Equal(sameBatch ? 0 : 1, registered.Count);
    }

    [Theory]
    [InlineData("0201034101000000")] // Kind zero.
    [InlineData("0201034101030000")] // Unknown kind.
    [InlineData("0201034101020103410100")] // Inline Schema has a base.
    [InlineData("0202034101010103420100034201020000")] // Reference base names inline kind.
    [InlineData("02020341010100010110034201034201010000")] // Inline slot names reference kind.
    [InlineData("02010341010100010110034201")] // Missing exact inline dependency.
    [InlineData("02010341010200010110034101")] // Inline self cycle.
    [InlineData("020203410102000101100342010342010200010110034101")] // Two-node inline cycle.
    [InlineData("010103410100010110034201")] // v1 cannot silently accept inline extension.
    public void MalformedInlineLayoutsFailBeforeMerging(string hex) {
        var registered = new Dictionary<SchemaKey, DurableSchema> { [new("Existing", 1)] = new("Existing", 1) };
        Assert.Throws<InvalidDataException>(() => SchemaBatchWireCodec.Read(Convert.FromHexString(hex), registered));
        Assert.Single(registered);
    }

    [Fact]
    public void InlineDepthLimitIncludesCachedAndPreviouslyRegisteredDependencies() {
        DurableSchema[] chain = InlineChain(257);
        var atLimit = chain[..256];
        var registered = SchemaBatchWireCodec.Read(SchemaBatchWireCodec.Write(atLimit), Empty);
        Assert.Equal(256, registered.Count);
        Assert.Throws<InvalidDataException>(() => SchemaBatchWireCodec.Read(SchemaBatchWireCodec.Write(chain), Empty));
        Assert.Throws<InvalidDataException>(() => SchemaBatchWireCodec.Read(SchemaBatchWireCodec.Write([chain[^1]]), registered));
        using IRbfFile file = RbfFile.CreateNew(NextPath());
        SchemaStore store = new(file);
        store.Register(chain[255]);
        long tail = file.TailOffset;
        Assert.Throws<ArgumentException>(() => store.Register(chain[256]));
        Assert.Equal(tail, file.TailOffset);
        Assert.Equal(256, store.Count);
        Assert.False(store.IsFaulted);
    }

    [Fact]
    public void InlineSchemaCannotIdentifyObjectBaseOrBeReadAsAnObject() {
        DurableSchema inline = new("A", 1, SchemaKind.InlineValue);
        Assert.Throws<ArgumentException>(() => BaseObjectBodyCodec.EncodeDurable(inline, new([])));
        using IRbfFile file = RbfFile.CreateNew(NextPath());
        SchemaStore schemas = new(file);
        schemas.Register(inline);
        Assert.Throws<InvalidDataException>(() => TypedObjectVersionReader.MatchSchema(schemas, new("A", 1), inline));
        Assert.Throws<InvalidDataException>(() => TypedObjectVersionReader.MatchSchema(schemas, new("A", 1), new("A", 1)));
    }

    [Fact]
    public void ChangedNestedDefinitionRejectsTypedReadBeforeBodyCallback() {
        DurableSchema value = new("Value", 1, SchemaKind.InlineValue, new DurableFieldInfo(1, TypeTag.Byte));
        DurableSchema changed = new("Value", 1, SchemaKind.InlineValue, new DurableFieldInfo(1, TypeTag.SByte));
        DurableSchema owner = new("Owner", 1, new DurableFieldInfo(1, TypeTag.InlineValue, inlineSchema: value));
        DurableSchema wrong = new("Owner", 1, new DurableFieldInfo(1, TypeTag.InlineValue, inlineSchema: changed));
        using IRbfFile file = RbfFile.CreateNew(NextPath());
        SchemaStore schemas = new(file);
        schemas.Register(owner);
        using SegmentStore segments = SegmentStore.CreateNew(NextPath(), new() { NewStoreLayout = RbfSegmentStoreLayout.Flat });
        StateRevisionStore states = new(segments);
        FrameAddress address = states.Append(StateRevision.CreateObjectHeadMapBase(null,
            [ObjectVersionRecord.CreateBase(1, BaseObjectBodyCodec.EncodeDurable(owner, new([1])).Body)], []));
        ObjectVersionChain chain = states.ReadObjectVersionChain(address, 1);
        int calls = 0;
        byte Read(ref BinaryPayloadReader reader) { calls++; return reader.ReadByte(); }
        byte Apply(ref BinaryPayloadReader reader, in byte previous) { calls++; return previous; }
        Assert.Throws<InvalidDataException>(() => TypedObjectVersionReader.ReadDurable<byte>(chain, schemas, wrong, Read, Apply));
        Assert.Equal(0, calls);
    }

    [Fact]
    public void PublishedBasePointingAtInlineSchemaRefusesRepositoryOpen() {
        string repositoryPath = NextPath();
        using (GraphRepository repository = GraphRepository.CreateNew(repositoryPath)) { }
        using (IRbfFile schemasFile = RbfFile.OpenExisting(Path.Combine(repositoryPath, "schemas.rbf"))) {
            new SchemaStore(schemasFile).Register(new("A", 1, SchemaKind.InlineValue));
        }
        FrameAddress address;
        using (SegmentStore segments = SegmentStore.OpenExisting(Path.Combine(repositoryPath, "state"))) {
            StateRevisionStore states = new(segments);
            // Valid object envelope syntax, but its exact key identifies an inline value.
            address = states.AppendDurably(StateRevision.CreateObjectHeadMapBase(null,
                [ObjectVersionRecord.CreateBase(1, Convert.FromHexString("0102034101"))], []));
        }
        using (IRbfFile publication = RbfFile.OpenExisting(Path.Combine(repositoryPath, "publication.rbf"))) {
            PublicationLog log = new(publication, static (_, _) => { });
            log.Publish(null, new(address, new ObjectId(1)));
        }
        Dictionary<string, byte[]> before = Directory.GetFiles(repositoryPath, "*", SearchOption.AllDirectories)
            .ToDictionary(static path => path, File.ReadAllBytes);
        Assert.Throws<InvalidDataException>(() => GraphRepository.OpenExisting(repositoryPath));
        foreach ((string path, byte[] bytes) in before) { Assert.Equal(bytes, File.ReadAllBytes(path)); }
    }

    private static DurableSchema[] InlineChain(int length) {
        DurableSchema[] chain = new DurableSchema[length];
        for (int index = 0; index < length; index++) {
            chain[index] = new($"Value{index:D3}", 1, SchemaKind.InlineValue,
                index == 0 ? [] : [new(1, TypeTag.InlineValue, inlineSchema: chain[index - 1])]);
        }
        return chain;
    }

    private string NextPath() => Path.Combine(_root, (++_sequence).ToString(System.Globalization.CultureInfo.InvariantCulture));

    public void Dispose() {
        string resolved = Path.GetFullPath(_root);
        if (!string.Equals(Path.GetDirectoryName(resolved), Path.TrimEndingDirectorySeparator(Path.GetFullPath(Path.GetTempPath())), StringComparison.OrdinalIgnoreCase) ||
            !Path.GetFileName(resolved).StartsWith("durable-inline-schema-", StringComparison.Ordinal)) {
            throw new InvalidOperationException("Refusing to delete outside the fixture temporary directory.");
        }
        Directory.Delete(resolved, recursive: true);
    }
}
