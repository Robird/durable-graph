using Atelia.DurableGraph.StateStore.Serialization;

namespace Atelia.DurableGraph.Tests;

public sealed class StateReaderBindingTests {
    private static readonly DurableSchema Schema = new("reader.test", 1,
        new DurableFieldInfo(1, TypeTag.Byte), new DurableFieldInfo(2, TypeTag.String));

    [Fact]
    public void ReaderReconstructsTypedChainAndReturnsIndependentStateCopies() {
        BodySource source = new([10, 7], [1, 11], [2, 9], [1, 12]);
        StateReaderBinding<State> binding = new(Schema, ReadBase, ApplyDelta, ValidateStrings);
        CapturedObject row = binding.Read(3, source);

        Assert.Equal(new[] { 0, 1, 2, 3 }, source.Requests);
        Assert.Equal(3u, row.Id);
        Assert.Same(Schema, row.Schema);
        Assert.Equal(CapturedObjectKind.Durable, row.Kind);
        Assert.Null(row.Preparation);
        State copy = row.GetState<State>();
        Assert.Equal((byte)12, copy.Value);
        Assert.Equal(9u, copy.Text);
        copy.Value = 99;
        copy.Text = 99;
        Assert.Equal((byte)12, row.GetState<State>().Value);
        Assert.Equal(9u, row.GetState<State>().Text);
        // Only the final DTO is validated: string 7 is no longer referenced.
        binding.ValidateReferences(row, StringReadTable.FromDecoded([(9u, "current")]));
        Assert.Throws<InvalidDataException>(() => binding.ValidateReferences(row, StringReadTable.FromDecoded([])));
    }

    [Fact]
    public void ReaderAcceptsBaseOnlyAndNullStringReference() {
        StateReaderBinding<State> binding = new(Schema, ReadBase, ApplyDelta, ValidateStrings);
        CapturedObject row = binding.Read(uint.MaxValue, new BodySource([5, 0]));
        Assert.Equal((byte)5, row.GetState<State>().Value);
        binding.ValidateReferences(row, StringReadTable.FromDecoded([]));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void TrailingBytesRejectTheWholeReadBeforeAnyLaterBody(bool inBase) {
        BodySource source = inBase
            ? new([10, 0, 99], [1, 12])
            : new([10, 0], [1, 12, 99], [1, 13]);
        StateReaderBinding<State> binding = new(Schema, ReadBase, ApplyDelta, ValidateStrings);
        CapturedObject? result = null;
        Assert.Throws<InvalidDataException>(() => result = binding.Read(1, source));
        Assert.Null(result);
        Assert.Equal(inBase ? new[] { 0 } : new[] { 0, 1 }, source.Requests);
    }

    [Fact]
    public void TruncatedDeltaPropagatesWithoutReturningAPartialDto() {
        StateReaderBinding<State> binding = new(Schema, ReadBase, ApplyDelta, ValidateStrings);
        CapturedObject? result = null;
        Assert.Throws<EndOfStreamException>(() => result = binding.Read(1, new BodySource([10, 0], [1])));
        Assert.Null(result);
    }

    [Fact]
    public void EmptySourceAndZeroObjectIdAreRejectedBeforeReading() {
        StateReaderBinding<State> binding = new(Schema, ReadBase, ApplyDelta, ValidateStrings);
        BodySource empty = new();
        Assert.Throws<InvalidDataException>(() => binding.Read(1, empty));
        Assert.Empty(empty.Requests);
        BodySource source = new([10, 0]);
        Assert.Throws<InvalidDataException>(() => binding.Read(0, source));
        Assert.Empty(source.Requests);
        Assert.Throws<ArgumentNullException>(() => binding.Read(1, null!));
    }

    [Fact]
    public void ReferenceValidationRejectsWrongKindSchemaAncestorOrDtoBeforeCallback() {
        int calls = 0;
        StateReaderBinding<State> binding = new(Schema, ReadBase, ApplyDelta,
            (in State state, StringReadTable table) => calls++);
        StringReadTable table = StringReadTable.FromDecoded([]);
        DurableSchema wrongFields = new(Schema.SchemaId, Schema.Version,
            new DurableFieldInfo(1, TypeTag.UInt32), new DurableFieldInfo(2, TypeTag.String));
        DurableSchema wrongVersion = new(Schema.SchemaId, 2, Schema.Fields.ToArray());
        DurableSchema wrongAncestor = new(Schema.SchemaId, Schema.Version, Schema.Fields.ToArray(),
            new DurableSchema("reader.ancestor", 1));
        CapturedObject[] invalidRows = [
            new(1, "text"),
            new(1, wrongFields, new State()),
            new(1, wrongVersion, new State()),
            new(1, wrongAncestor, new State()),
            new(1, Schema, 123u),
        ];
        foreach (CapturedObject row in invalidRows) {
            Assert.Throws<InvalidDataException>(() => binding.ValidateReferences(row, table));
        }
        Assert.Equal(0, calls);
        // Equivalent immutable definitions need not be the same CLR instance.
        DurableSchema equal = new(Schema.SchemaId, Schema.Version, Schema.Fields.ToArray());
        binding.ValidateReferences(new CapturedObject(1, equal, new State()), table);
        Assert.Equal(1, calls);
    }

    [Fact]
    public void BindingRejectsMissingSchemaOrCallbacks() {
        Assert.Throws<ArgumentNullException>(() => new StateReaderBinding<State>(null!, ReadBase, ApplyDelta, ValidateStrings));
        Assert.Throws<ArgumentNullException>(() => new StateReaderBinding<State>(Schema, null!, ApplyDelta, ValidateStrings));
        Assert.Throws<ArgumentNullException>(() => new StateReaderBinding<State>(Schema, ReadBase, null!, ValidateStrings));
        Assert.Throws<ArgumentNullException>(() => new StateReaderBinding<State>(Schema, ReadBase, ApplyDelta, null!));
    }

    [Fact]
    public void DecodedStringTableCopiesBindingsAndRetainsDistinctNonemptyInstances() {
        string first = new(['x']);
        string second = new(['x']);
        List<(uint Id, string Value)> records = [(7, first), (9, second), (11, string.Empty), (12, string.Empty)];
        StringReadTable table = StringReadTable.FromDecoded(records);
        records[0] = (7, "replacement");
        records.Clear();
        Assert.Same(first, table.ResolveString(7));
        Assert.Same(second, table.ResolveString(9));
        Assert.NotSame(table.ResolveString(7), table.ResolveString(9));
        Assert.Equal(table.ResolveString(7), table.ResolveString(9));
        Assert.Same(string.Empty, table.ResolveString(11));
        Assert.Same(string.Empty, table.ResolveString(12));
        Assert.Null(table.ResolveString(0));
        Assert.Throws<InvalidDataException>(() => table.ResolveString(8));
    }

    [Fact]
    public void DecodedStringTableRejectsInvalidIdentitiesAndMissingContents() {
        string shared = new(['x']);
        Assert.Throws<InvalidDataException>(() => StringReadTable.FromDecoded([(0u, shared)]));
        Assert.Throws<InvalidDataException>(() => StringReadTable.FromDecoded([(1u, shared), (1u, "other")]));
        Assert.Throws<InvalidDataException>(() => StringReadTable.FromDecoded([(1u, shared), (2u, shared)]));
        Assert.Throws<InvalidDataException>(() => StringReadTable.FromDecoded([(1u, null!)]));
        Assert.Throws<InvalidDataException>(() => StringReadTable.FromDecoded([(1u, string.Empty), (1u, string.Empty)]));
        Assert.Throws<ArgumentNullException>(() => StringReadTable.FromDecoded(null!));
        Assert.Null(StringReadTable.FromDecoded([]).ResolveString(0));
    }

    [Fact]
    public void DecodedStringEnumerationFailureReturnsNoPartialTable() {
        bool disposed = false;
        IEnumerable<(uint Id, string Value)> Records() {
            try {
                yield return (1, "first");
                throw new IOException("late failure");
            } finally {
                disposed = true;
            }
        }
        StringReadTable? result = null;
        Assert.Throws<IOException>(() => result = StringReadTable.FromDecoded(Records()));
        Assert.Null(result);
        Assert.True(disposed);
    }

    private struct State {
        public byte Value;
        public uint Text;
    }

    private static State ReadBase(ref BinaryPayloadReader reader) => new() {
        Value = reader.ReadByte(),
        Text = reader.ReadUInt32(),
    };

    private static State ApplyDelta(ref BinaryPayloadReader reader, in State prior) {
        State result = prior;
        byte bitmap = reader.ReadByte();
        if ((bitmap & 1) != 0) {
            result.Value = reader.ReadByte();
        }
        if ((bitmap & 2) != 0) {
            result.Text = reader.ReadUInt32();
        }
        return result;
    }

    private static void ValidateStrings(in State state, StringReadTable table) => table.ResolveString(state.Text);

    private sealed class BodySource(params byte[][] bodies) : IStateBodySource {
        public List<int> Requests { get; } = [];
        public int Count => bodies.Length;
        public ReadOnlySpan<byte> GetBody(int index) {
            Requests.Add(index);
            return bodies[index];
        }
    }
}
