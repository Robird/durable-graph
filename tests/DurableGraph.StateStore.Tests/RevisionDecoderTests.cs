using Atelia.DurableGraph.StateStore.Serialization;
using Atelia.DurableGraph.StateStore.Storage;
using Atelia.Rbf;
using Atelia.RbfSegmentStore;
using SegmentStore = Atelia.RbfSegmentStore.RbfSegmentStore;

namespace Atelia.DurableGraph.StateStore.Tests;

public sealed class RevisionDecoderTests : IDisposable {
    private readonly string _root = Path.GetFullPath(Path.Combine(Path.GetTempPath(), $"durable-graph-revision-decoder-{Guid.NewGuid():N}"));
    private static readonly DurableSchema NodeSchema = new("Node", 1,
        new DurableFieldInfo(1, TypeTag.Byte), new DurableFieldInfo(2, TypeTag.String));
    private static readonly DurableSchema ByteSchema = new("Number", 1, new DurableFieldInfo(1, TypeTag.Byte));
    private int _nextFile;

    public RevisionDecoderTests() => Directory.CreateDirectory(_root);

    [Fact]
    public void DecodesFullMembershipTypedDeltaAndOneStringInstancePerIdWithoutWriting() {
        DecodedRevision decoded;
        FrameAddress latest;
        using (IRbfFile file = RbfFile.CreateNew(NextPath()))
        using (SegmentStore segments = NewSegments()) {
            SchemaStore schemas = new(file);
            schemas.Register(NodeSchema);
            StateRevisionStore store = new(segments);
            FrameAddress original = store.Append(StateRevision.CreateBase(null,
                [Node(1, 4, 3), Node(2, 5, 3), Text(3, "same"), Text(4, "same"), Text(5, ""), Text(6, "")], []));
            latest = store.Append(StateRevision.CreateDelta(original,
                [ObjectVersionRecord.CreateDelta(1, original, new byte[] { 1, 9 })], []));
            StateReaderRegistry readers = NodeReaders();
            long schemaTail = file.TailOffset;
            long stateTail = Tail(segments);
            decoded = RevisionDecoder.Read(store, schemas, latest, readers);
            Assert.Equal(schemaTail, file.TailOffset);
            Assert.Equal(stateTail, Tail(segments));
            Assert.Equal(new NodeState(4, 3), RevisionDecoder.Read(store, schemas, original, readers).GetRequired(1).GetState<NodeState>());
        }
        Assert.Equal(latest, decoded.RevisionAddress);
        Assert.Equal(new uint[] { 1, 2, 3, 4, 5, 6 }, decoded.Objects.Select(static row => row.Id));
        Assert.Equal(new NodeState(9, 3), decoded.GetRequired(1).GetState<NodeState>());
        Assert.Equal(new NodeState(5, 3), decoded.GetRequired(2).GetState<NodeState>());
        Assert.Same(decoded.GetRequired(3).StringContent, decoded.Strings.ResolveString(3));
        Assert.Same(decoded.Strings.ResolveString(3), decoded.Strings.ResolveString(decoded.GetRequired(2).GetState<NodeState>().TextId));
        Assert.Equal(decoded.Strings.ResolveString(3), decoded.Strings.ResolveString(4));
        Assert.NotSame(decoded.Strings.ResolveString(3), decoded.Strings.ResolveString(4));
        Assert.Same(string.Empty, decoded.Strings.ResolveString(5));
        Assert.Same(string.Empty, decoded.GetRequired(6).StringContent);
        Assert.Null(decoded.Strings.ResolveString(0));
        Assert.Throws<InvalidDataException>(() => decoded.GetRequired(0));
        Assert.Throws<InvalidDataException>(() => decoded.GetRequired(99));
        Assert.False(decoded.Objects is System.Collections.ICollection);
        Assert.False(decoded.Objects is IList<CapturedObject>);
        NodeState copy = decoded.GetRequired(1).GetState<NodeState>();
        copy = copy with { Value = 99 };
        Assert.Equal((byte)9, decoded.GetRequired(1).GetState<NodeState>().Value);
    }

    [Fact]
    public void EmptyMembershipIsARealRevisionAndInvalidAddressDoesNotMeanEmpty() {
        using IRbfFile file = RbfFile.CreateNew(NextPath());
        using SegmentStore segments = NewSegments();
        SchemaStore schemas = new(file);
        StateRevisionStore store = new(segments);
        FrameAddress address = store.Append(StateRevision.CreateBase(null, [], []));
        DecodedRevision result = RevisionDecoder.Read(store, schemas, address, new());
        Assert.Empty(result.Objects);
        Assert.Null(result.Strings.ResolveString(0));
        Assert.Throws<ArgumentOutOfRangeException>(() => RevisionDecoder.Read(store, schemas, default, new()));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void TargetRevisionReferenceValidationRejectsRemovedOrWrongKindString(bool wrongKind) {
        using IRbfFile file = RbfFile.CreateNew(NextPath());
        using SegmentStore segments = NewSegments();
        SchemaStore schemas = new(file);
        schemas.RegisterBatch([NodeSchema, ByteSchema]);
        StateRevisionStore store = new(segments);
        FrameAddress original = store.Append(StateRevision.CreateBase(null, [Node(1, 4, 2), Text(2, "value")], []));
        StateReaderRegistry readers = NodeReaders();
        readers.Register(ByteBinding(ByteSchema));
        DecodedRevision previous = RevisionDecoder.Read(store, schemas, original, readers);
        FrameAddress invalid = store.Append(StateRevision.CreateDelta(original,
            wrongKind ? [Durable(2, ByteSchema, [7])] : [], wrongKind ? [] : [2]));
        long stateTail = Tail(segments);
        long schemaTail = file.TailOffset;
        DecodedRevision? result = null;
        Assert.Throws<InvalidDataException>(() => result = RevisionDecoder.Read(store, schemas, invalid, readers));
        Assert.Null(result);
        Assert.Equal(stateTail, Tail(segments));
        Assert.Equal(schemaTail, file.TailOffset);
        Assert.Equal("value", previous.Strings.ResolveString(2));
        Assert.Equal("value", RevisionDecoder.Read(store, schemas, original, readers).Strings.ResolveString(2));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public void UnknownDefinitionReaderOrFutureVersionDoesNotFallBack(int missing) {
        using IRbfFile file = RbfFile.CreateNew(NextPath());
        using SegmentStore segments = NewSegments();
        SchemaStore schemas = new(file);
        DurableSchema stored = missing == 2 ? new("Number", 2, new DurableFieldInfo(1, TypeTag.Byte)) : ByteSchema;
        if (missing != 0) { schemas.Register(stored); }
        StateRevisionStore store = new(segments);
        FrameAddress address = store.Append(StateRevision.CreateBase(null, [Durable(1, stored, [7])], []));
        StateReaderRegistry readers = new();
        int calls = 0;
        if (missing != 1) { readers.Register(ByteBinding(ByteSchema, () => calls++)); }
        if (missing == 0) {
            Assert.Throws<SchemaNotFoundException>(() => RevisionDecoder.Read(store, schemas, address, readers));
        } else {
            Assert.Throws<InvalidDataException>(() => RevisionDecoder.Read(store, schemas, address, readers));
        }
        Assert.Equal(0, calls);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void FullSchemaMismatchIncludesAncestorAndPrecedesThisObjectsBody(bool ancestorMismatch) {
        using IRbfFile file = RbfFile.CreateNew(NextPath());
        using SegmentStore segments = NewSegments();
        DurableSchema stored = new("Leaf", 1, [new(1, TypeTag.Byte)], new("Ancestor", 1));
        DurableSchema wrong = ancestorMismatch
            ? new("Leaf", 1, [new(1, TypeTag.Byte)], new("Ancestor", 2))
            : new("Leaf", 1, [new(1, TypeTag.SByte)], new("Ancestor", 1));
        SchemaStore schemas = new(file);
        schemas.Register(stored);
        StateRevisionStore store = new(segments);
        FrameAddress address = store.Append(StateRevision.CreateBase(null, [Durable(1, stored, [7])], []));
        StateReaderRegistry readers = new();
        int calls = 0;
        readers.Register(ByteBinding(wrong, () => calls++));
        Assert.Throws<InvalidDataException>(() => RevisionDecoder.Read(store, schemas, address, readers));
        Assert.Equal(0, calls);
    }

    [Fact]
    public void LateUnknownReaderDoesNotPublishPartialRowsOrUseCallbackRegistration() {
        using IRbfFile file = RbfFile.CreateNew(NextPath());
        using SegmentStore segments = NewSegments();
        DurableSchema second = new("Other", 1, new DurableFieldInfo(1, TypeTag.Byte));
        SchemaStore schemas = new(file);
        schemas.RegisterBatch([ByteSchema, second]);
        StateRevisionStore store = new(segments);
        FrameAddress address = store.Append(StateRevision.CreateBase(null,
            [Durable(1, ByteSchema, [7]), Durable(2, second, [8])], []));
        StateReaderRegistry readers = new();
        StateReaderBinding<byte> late = ByteBinding(second);
        int calls = 0;
        readers.Register(ByteBinding(ByteSchema, () => { calls++; readers.Register(late); }));
        DecodedRevision? result = null;
        Assert.Throws<InvalidDataException>(() => result = RevisionDecoder.Read(store, schemas, address, readers));
        Assert.Null(result);
        Assert.Equal(1, calls); // Object-first processing permits earlier pure callbacks.
        DecodedRevision retried = RevisionDecoder.Read(store, schemas, address, readers);
        Assert.Equal((byte)8, retried.GetRequired(2).GetState<byte>());
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public void RejectsLateMalformedBodiesAndStringDelta(int malformed) {
        using IRbfFile file = RbfFile.CreateNew(NextPath());
        using SegmentStore segments = NewSegments();
        SchemaStore schemas = new(file);
        schemas.Register(NodeSchema);
        StateRevisionStore store = new(segments);
        FrameAddress original = store.Append(StateRevision.CreateBase(null,
            [Node(1, 4, 0), malformed == 3 ? Text(2, "text") : Node(2, 5, 0)], []));
        ObjectVersionRecord bad = malformed switch {
            0 => Durable(2, NodeSchema, [5, 0, 0]), // Trailing Base byte.
            1 => ObjectVersionRecord.CreateDelta(2, original, new byte[] { 1 }), // Missing changed value.
            2 => ObjectVersionRecord.CreateDelta(2, original, new byte[] { 1, 9, 0 }), // Trailing Delta byte.
            _ => ObjectVersionRecord.CreateDelta(2, original, Array.Empty<byte>()),
        };
        FrameAddress address = store.Append(StateRevision.CreateDelta(original, [bad], []));
        DecodedRevision? result = null;
        if (malformed == 1) {
            Assert.Throws<EndOfStreamException>(() => result = RevisionDecoder.Read(store, schemas, address, NodeReaders()));
        } else {
            Assert.Throws<InvalidDataException>(() => result = RevisionDecoder.Read(store, schemas, address, NodeReaders()));
        }
        Assert.Null(result);
    }

    [Fact]
    public void DeclaredHeadMustContainLocalObjectAndCannotFallbackToParent() {
        using IRbfFile file = RbfFile.CreateNew(NextPath());
        using SegmentStore segments = NewSegments();
        SchemaStore schemas = new(file);
        StateRevisionStore store = new(segments);
        FrameAddress original = store.Append(StateRevision.CreateBase(null, [Text(1, "text")], []));
        FrameAddress unchanged = store.Append(StateRevision.CreateDelta(original, [], []));
        FrameAddress corrupt = store.Append(StateRevision.CreateBase(unchanged, [], [new(1, unchanged)]));
        Assert.Throws<InvalidDataException>(() => RevisionDecoder.Read(store, schemas, corrupt, new()));
    }

    private readonly record struct NodeState(byte Value, uint TextId);

    private static StateReaderRegistry NodeReaders() {
        StateReaderRegistry readers = new();
        readers.Register(new StateReaderBinding<NodeState>(NodeSchema,
            static (ref BinaryPayloadReader reader) => new(reader.ReadByte(), reader.ReadUInt32()),
            static (ref BinaryPayloadReader reader, in NodeState prior) => {
                byte bitmap = reader.ReadByte();
                if (bitmap == 0 || bitmap > 3) { throw new InvalidDataException("Invalid test DTO Delta bitmap."); }
                byte value = (bitmap & 1) != 0 ? reader.ReadByte() : prior.Value;
                uint textId = (bitmap & 2) != 0 ? reader.ReadUInt32() : prior.TextId;
                return new(value, textId);
            },
            static (in NodeState state, StringReadTable table) => { _ = table.ResolveString(state.TextId); }));
        return readers;
    }

    private static StateReaderBinding<byte> ByteBinding(DurableSchema schema, Action? onRead = null) => new(schema,
        (ref BinaryPayloadReader reader) => { onRead?.Invoke(); return reader.ReadByte(); },
        static (ref BinaryPayloadReader reader, in byte prior) => reader.ReadByte(),
        static (in byte state, StringReadTable table) => { });

    private static ObjectVersionRecord Node(uint id, byte value, byte textId) => Durable(id, NodeSchema, [value, textId]);
    private static ObjectVersionRecord Durable(uint id, DurableSchema schema, ReadOnlySpan<byte> body) =>
        ObjectVersionRecord.CreateBase(id, BaseObjectPayloadCodec.EncodeDurable(schema, new(body)).Payload);
    private static ObjectVersionRecord Text(uint id, string value) =>
        ObjectVersionRecord.CreateBase(id, BaseObjectPayloadCodec.EncodeString(StringPayloadCodec.PrepareBase(value)).Payload);

    private SegmentStore NewSegments() => SegmentStore.CreateNew(NextPath(), new() { NewStoreLayout = RbfSegmentStoreLayout.Flat });
    private string NextPath() => Path.Combine(_root, (++_nextFile).ToString(System.Globalization.CultureInfo.InvariantCulture));
    private static long Tail(SegmentStore segments) {
        using RbfSegmentWriterLease writer = segments.OpenActiveWriter();
        return writer.File.TailOffset;
    }

    public void Dispose() {
        string resolved = Path.GetFullPath(_root);
        if (!string.Equals(Path.GetDirectoryName(resolved), Path.TrimEndingDirectorySeparator(Path.GetFullPath(Path.GetTempPath())), StringComparison.OrdinalIgnoreCase) ||
            !Path.GetFileName(resolved).StartsWith("durable-graph-revision-decoder-", StringComparison.Ordinal)) {
            throw new InvalidOperationException("Refusing to delete outside the fixture's temporary directory.");
        }
        Directory.Delete(resolved, recursive: true);
    }
}
