using Atelia.DurableGraph.StateStore.Serialization;
using Atelia.DurableGraph.StateStore.Storage;
using Atelia.Rbf;
using Atelia.RbfSegmentStore;
using SegmentStore = Atelia.RbfSegmentStore.RbfSegmentStore;

namespace Atelia.DurableGraph.StateStore.Tests;

public sealed class TypedObjectVersionReaderTests : IDisposable {
    private readonly string _root = Path.GetFullPath(Path.Combine(Path.GetTempPath(), $"durable-graph-typed-reader-{Guid.NewGuid():N}"));
    private int _nextFile;

    public TypedObjectVersionReaderTests() => Directory.CreateDirectory(_root);

    [Fact]
    public void ExactHistoricalReaderReconstructsRawDeltasAfterColdSchemaReopen() {
        DurableSchema ancestor = new("Ancestor", 1);
        DurableSchema old = new("Leaf", 1, [new(1, TypeTag.Byte)], ancestor);
        DurableSchema current = new("Leaf", 2, [new(1, TypeTag.UInt16)], ancestor);
        string path = NextPath();
        using (IRbfFile file = RbfFile.CreateNew(path)) {
            SchemaStore schemas = new(file);
            schemas.RegisterBatch([old, current]);
        }
        ObjectVersionChain chain = Chain(BaseObjectBodyCodec.EncodeDurable(old, new([21])), [1, 25], [1, 26]);
        Assert.Equal(new byte[] { 1, 25 }, chain.Records[1].Record.Body.ToArray());
        Assert.Equal(new byte[] { 1, 26 }, chain.Records[2].Record.Body.ToArray());
        using IRbfFile reopened = RbfFile.OpenReadOnlyExisting(path);
        SchemaStore restored = new(reopened, readOnly: true);
        int calls = 0;
        byte Read(ref BinaryPayloadReader reader) { calls++; return reader.ReadByte(); }
        byte Apply(ref BinaryPayloadReader reader, in byte prior) {
            calls++;
            Assert.Equal(1, reader.ReadByte()); // One changed slot, followed by its replacement value.
            return reader.ReadByte();
        }
        Assert.Equal((byte)26, TypedObjectVersionReader.ReadDurable<byte>(chain, restored, old, Read, Apply));
        Assert.Equal(3, calls);
        calls = 0;
        Assert.Throws<InvalidDataException>(() => TypedObjectVersionReader.ReadDurable<byte>(chain, restored, current, Read, Apply));
        Assert.Equal(0, calls);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public void FullDefinitionMismatchRejectsBeforeAnyBodyCallback(int mismatch) {
        DurableSchema ancestor = new("Ancestor", 1, new DurableFieldInfo(1, TypeTag.Byte));
        DurableSchema stored = new("Leaf", 1, [new(1, TypeTag.Int32)], ancestor);
        DurableSchema wrong = mismatch switch {
            0 => new("Leaf", 1, [new(1, TypeTag.Int64)], ancestor),
            1 => new("Leaf", 1, [new(2, TypeTag.Int32)], ancestor),
            2 => new("Leaf", 1, [new(1, TypeTag.Int32)], new("Ancestor", 1, new DurableFieldInfo(1, TypeTag.SByte))),
            _ => new("Leaf", 1, [new(1, TypeTag.Int32)], new("Ancestor", 2, new DurableFieldInfo(1, TypeTag.Byte))),
        };
        using IRbfFile file = RbfFile.CreateNew(NextPath());
        SchemaStore schemas = new(file);
        schemas.RegisterBatch([stored]);
        ObjectVersionChain chain = Chain(BaseObjectBodyCodec.EncodeDurable(stored, new([])), Array.Empty<byte>());
        int calls = 0;
        int Read(ref BinaryPayloadReader reader) { calls++; return 0; }
        int Apply(ref BinaryPayloadReader reader, in int prior) { calls++; return prior; }
        Assert.Throws<InvalidDataException>(() => TypedObjectVersionReader.ReadDurable<int>(chain, schemas, wrong, Read, Apply));
        Assert.Equal(0, calls);
    }

    [Fact]
    public void MissingSchemaWrongKindAndOpaqueRawBaseNeverInvokeCallbacks() {
        DurableSchema schema = new("A", 1);
        using IRbfFile file = RbfFile.CreateNew(NextPath());
        SchemaStore schemas = new(file);
        ObjectVersionChain missing = Chain(BaseObjectBodyCodec.EncodeDurable(schema, new([7])));
        ObjectVersionChain text = Chain(BaseObjectBodyCodec.EncodeString(new([0])));
        ObjectVersionChain opaque = Chain(new([7]));
        int calls = 0;
        int Read(ref BinaryPayloadReader reader) { calls++; return 0; }
        int Apply(ref BinaryPayloadReader reader, in int prior) { calls++; return prior; }
        Assert.Throws<SchemaNotFoundException>(() => TypedObjectVersionReader.ReadDurable<int>(missing, schemas, schema, Read, Apply));
        Assert.Throws<InvalidDataException>(() => TypedObjectVersionReader.ReadDurable<int>(text, schemas, schema, Read, Apply));
        Assert.Throws<InvalidDataException>(() => TypedObjectVersionReader.ReadDurable<int>(opaque, schemas, schema, Read, Apply));
        Assert.Equal(0, calls);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void EveryBodyMustBeFullyConsumed(bool trailingBase) {
        DurableSchema schema = new("A", 1, new DurableFieldInfo(1, TypeTag.Byte));
        using IRbfFile file = RbfFile.CreateNew(NextPath());
        SchemaStore schemas = new(file);
        schemas.RegisterBatch([schema]);
        ObjectVersionChain chain = Chain(
            BaseObjectBodyCodec.EncodeDurable(schema, new(trailingBase ? new byte[] { 1, 2 } : new byte[] { 1 })),
            trailingBase ? new byte[] { 1, 3 } : new byte[] { 1, 3, 4 });
        int deltaCalls = 0;
        byte Read(ref BinaryPayloadReader reader) => reader.ReadByte();
        byte Apply(ref BinaryPayloadReader reader, in byte prior) {
            deltaCalls++;
            Assert.Equal(1, reader.ReadByte());
            return reader.ReadByte();
        }
        Assert.Throws<InvalidDataException>(() => TypedObjectVersionReader.ReadDurable<byte>(chain, schemas, schema, Read, Apply));
        Assert.Equal(trailingBase ? 0 : 1, deltaCalls);
    }

    [Fact]
    public void BuiltInStringNeedsNoSchemaStoreAndEmptyIsCanonical() {
        ObjectVersionChain text = Chain(BaseObjectBodyCodec.EncodeString(StringPayloadCodec.PrepareBase("hello")));
        string first = TypedObjectVersionReader.ReadString(text);
        string second = TypedObjectVersionReader.ReadString(text);
        Assert.Equal("hello", first);
        Assert.Equal(first, second);
        Assert.NotSame(first, second);
        Assert.Same(string.Empty, TypedObjectVersionReader.ReadString(Chain(BaseObjectBodyCodec.EncodeString(new([0])))));
        Assert.Throws<InvalidDataException>(() => TypedObjectVersionReader.ReadString(Chain(BaseObjectBodyCodec.EncodeString(new([0])), Array.Empty<byte>())));
        Assert.Throws<InvalidDataException>(() => TypedObjectVersionReader.ReadString(Chain(BaseObjectBodyCodec.EncodeString(new([0, 1])))));
        Assert.Throws<InvalidDataException>(() => TypedObjectVersionReader.ReadString(Chain(BaseObjectBodyCodec.EncodeDurable(new("A", 1), new([0])))));
    }

    private ObjectVersionChain Chain(EncodedBaseObjectBody encodedBaseBody, params byte[][] deltas) {
        using SegmentStore segments = SegmentStore.CreateNew(NextPath(), new() { NewStoreLayout = RbfSegmentStoreLayout.Flat });
        StateRevisionStore store = new(segments);
        FrameAddress address = store.Append(StateRevision.CreateObjectHeadMapBase(null, [ObjectVersionRecord.CreateBase(1, encodedBaseBody.Body)], []));
        foreach (byte[] delta in deltas) {
            address = store.Append(StateRevision.CreateObjectHeadMapDelta(address, [ObjectVersionRecord.CreateDelta(1, address, delta)], []));
        }
        return store.ReadObjectVersionChain(address, 1);
    }

    private string NextPath() => Path.Combine(_root, (++_nextFile).ToString(System.Globalization.CultureInfo.InvariantCulture));

    public void Dispose() {
        string resolved = Path.GetFullPath(_root);
        if (!string.Equals(Path.GetDirectoryName(resolved), Path.TrimEndingDirectorySeparator(Path.GetFullPath(Path.GetTempPath())), StringComparison.OrdinalIgnoreCase) ||
            !Path.GetFileName(resolved).StartsWith("durable-graph-typed-reader-", StringComparison.Ordinal)) {
            throw new InvalidOperationException("Refusing to delete outside the fixture's temporary directory.");
        }
        Directory.Delete(resolved, recursive: true);
    }
}
