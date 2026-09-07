using System.Buffers;
using Atelia.DurableGraph.StateStore.Serialization;

namespace Atelia.DurableGraph.Tests;

public sealed class StringReadTableTests {
    public static TheoryData<string, string> GoldenBodies => new() {
        { "Ada", "07416461" },
        { "é", "02E900" }, // UTF-8 and UTF-16 tie: UTF-16 wins.
        { "中", "022D4E" },
        { string.Empty, "00" },
    };

    [Theory]
    [MemberData(nameof(GoldenBodies))]
    public void StringReadTableUsesCanonicalIndependentStringBodies(string value, string hex) {
        AssertCanonicalIndependentBody(value, hex);
    }

    [Fact]
    public void StringReadTablePreservesUnpairedSurrogateCodeUnitsInCanonicalIndependentBody() {
        // Construct inside execution: discovery transports string theory arguments through text encodings.
        string value = new(['\uD800']);
        AssertCanonicalIndependentBody(value, "0200D8");
        StringReadTable table = StringReadTable.Decode([(1u, new byte[] { 0x02, 0x00, 0xD8 })]);
        string decoded = Assert.IsType<string>(table.ResolveString(1));
        Assert.Equal(1, decoded.Length);
        Assert.Equal('\uD800', decoded[0]);
    }

    private static void AssertCanonicalIndependentBody(string value, string hex) {
        ArrayBufferWriter<byte> bytes = new();
        BinaryPayloadWriter writer = new(bytes);
        writer.WriteString(value);
        Assert.Equal(hex, Convert.ToHexString(bytes.WrittenSpan));

        // Decoding receives literal bytes, not the encoder's output or a source instance.
        StringReadTable table = StringReadTable.Decode([(uint.MaxValue, Convert.FromHexString(hex))]);
        Assert.Equal(value, table.ResolveString(uint.MaxValue));
        Assert.Same(table.ResolveString(uint.MaxValue), table.ResolveString(uint.MaxValue));
        if (value.Length == 0) {
            Assert.Same(string.Empty, table.ResolveString(uint.MaxValue));
        }
        Assert.Null(table.ResolveString(0));
    }

    [Theory]
    [InlineData("07416461")]
    [InlineData("02E900")]
    [InlineData("0200D8")]
    public void StringReadTablePreservesDifferentIdsForEqualNonemptyContents(string hex) {
        byte[] body = Convert.FromHexString(hex);
        StringReadTable table = StringReadTable.Decode([(7u, body), (3u, body), (uint.MaxValue, body)]);
        string first = Assert.IsType<string>(table.ResolveString(7));
        string second = Assert.IsType<string>(table.ResolveString(3));
        string third = Assert.IsType<string>(table.ResolveString(uint.MaxValue));
        Assert.Equal(first, second);
        Assert.Equal(first, third);
        Assert.NotSame(first, second);
        Assert.NotSame(first, third);
        Assert.NotSame(second, third);
        Assert.Same(second, table.ResolveString(3));
    }

    [Fact]
    public void StringReadTableNormalizesAllEmptyContentsButStillRequiresUniqueNonzeroIds() {
        byte[] body = [0x00];
        StringReadTable table = StringReadTable.Decode([(7u, body), (3u, body), (uint.MaxValue, body)]);
        Assert.Same(string.Empty, table.ResolveString(7));
        Assert.Same(string.Empty, table.ResolveString(3));
        Assert.Same(string.Empty, table.ResolveString(uint.MaxValue));
        Assert.Null(table.ResolveString(0));
        Assert.Throws<InvalidDataException>(() => StringReadTable.Decode([(3u, body), (3u, body)]));
        Assert.Throws<InvalidDataException>(() => StringReadTable.Decode([(0u, body)]));
    }

    [Fact]
    public void StringReadTableAllowsEmptyInputButOnlyNullCanResolve() {
        StringReadTable table = StringReadTable.Decode([]);
        Assert.Null(table.ResolveString(0));
        Assert.Throws<InvalidDataException>(() => table.ResolveString(1));
        Assert.Throws<InvalidDataException>(() => table.ResolveString(uint.MaxValue));
        Assert.Throws<ArgumentNullException>(() => StringReadTable.Decode(null!));
    }

    [Fact]
    public void StringReadTableRejectsZeroAndDuplicateEntryIdsEvenForIdenticalContents() {
        byte[] body = [0x03, 0x41];
        Assert.Throws<InvalidDataException>(() => StringReadTable.Decode([(0u, body)]));
        Assert.Throws<InvalidDataException>(() => StringReadTable.Decode([(1u, body), (1u, body)]));
        Assert.Throws<InvalidDataException>(() => StringReadTable.Decode([
            (1u, body), (1u, new byte[] { 0x03, 0x42 })]));
        Assert.Throws<InvalidDataException>(() => StringReadTable.Decode([
            (uint.MaxValue, body), (uint.MaxValue, body)]));
    }

    [Theory]
    [InlineData("8000")] // Overlong varint header.
    [InlineData("01")] // Empty content must use UTF-16.
    [InlineData("024100")] // ASCII must use shorter UTF-8.
    [InlineData("05C3A9")] // Equal lengths must use UTF-16.
    [InlineData("05C080")] // Invalid UTF-8.
    [InlineData("0741")] // Truncated UTF-8 payload.
    [InlineData("0200")] // Truncated UTF-16 payload.
    [InlineData("FEFFFFFF0F")] // Length exceeds the supported Int32 range.
    [InlineData("0041")] // A valid empty body followed by unrelated bytes.
    public void StringReadTableRejectsMalformedNoncanonicalTruncatedAndTrailingBodies(string hex) {
        Assert.Throws<InvalidDataException>(() =>
            StringReadTable.Decode([(1u, Convert.FromHexString(hex))]));
    }

    [Theory]
    [InlineData("")]
    [InlineData("80")]
    public void StringReadTablePreservesReaderFailureForMissingOrTruncatedHeader(string hex) {
        Assert.Throws<EndOfStreamException>(() =>
            StringReadTable.Decode([(1u, Convert.FromHexString(hex))]));
    }

    [Fact]
    public void StringReadTableDoesNotRetainInputBuffersOrDeferEnumeration() {
        byte[] first = [0x03, 0x41];
        byte[] second = [0x02, 0xE9, 0x00];
        List<(uint Id, ReadOnlyMemory<byte> Body)> records = [(1, first), (2, second)];
        StringReadTable table = StringReadTable.Decode(records);
        first.AsSpan().Fill(0xFF);
        second.AsSpan().Clear();
        records.Clear();
        Assert.Equal("A", table.ResolveString(1));
        Assert.Equal("é", table.ResolveString(2));
        Assert.Same(table.ResolveString(1), table.ResolveString(1));
    }

    [Fact]
    public void StringReadTableRecordOrderDoesNotAffectBindingsAndViewsDoNotShareAnIdNamespace() {
        (uint Id, ReadOnlyMemory<byte> Body)[] records = [
            (1, new byte[] { 0x03, 0x41 }),
            (2, new byte[] { 0x03, 0x41 }),
            (9, new byte[] { 0x03, 0x42 }),
        ];
        StringReadTable forward = StringReadTable.Decode(records);
        StringReadTable reverse = StringReadTable.Decode(records.Reverse());
        foreach (uint id in new uint[] { 1, 2, 9 }) {
            Assert.Equal(forward.ResolveString(id), reverse.ResolveString(id));
        }
        Assert.NotSame(forward.ResolveString(1), forward.ResolveString(2));
        Assert.NotSame(reverse.ResolveString(1), reverse.ResolveString(2));
        StringReadTable otherRevision = StringReadTable.Decode([(1u, new byte[] { 0x03, 0x43 })]);
        Assert.Equal("C", otherRevision.ResolveString(1));
        Assert.Equal("A", forward.ResolveString(1));
        Assert.Throws<InvalidDataException>(() => otherRevision.ResolveString(9));
    }

    [Fact]
    public void StringReadTableIteratorFailureCannotPublishAPartialTableOrChangeAnExistingView() {
        StringReadTable existing = StringReadTable.Decode([(1u, new byte[] { 0x03, 0x41 })]);
        string original = Assert.IsType<string>(existing.ResolveString(1));
        StringReadTable? failed = null;
        bool disposed = false;
        IEnumerable<(uint Id, ReadOnlyMemory<byte> Body)> FailingRecords() {
            try {
                yield return (1, new byte[] { 0x03, 0x42 });
                throw new IOException("Input enumeration failed after a valid record.");
            } finally {
                disposed = true;
            }
        }

        Assert.Throws<IOException>(() => failed = StringReadTable.Decode(FailingRecords()));
        Assert.True(disposed);
        Assert.Null(failed);
        Assert.Same(original, existing.ResolveString(1));
        Assert.Equal("A", existing.ResolveString(1));
    }

    [Fact]
    public void StringReadTableDecodeFailureLeavesAcceptedParentAndPendingCaptureUsable() {
        DurableSchema schema = new("string.read.isolation", 1, new DurableFieldInfo(1, TypeTag.String));
        CaptureSession session = new();
        Domain source = new() { Text = "parent" };
        using CaptureContext first = session.BeginCapture();
        first.AddRoot<Domain, uint>(source, schema, static (value, context) => context.CaptureString(value.Text));
        CapturedGraph parent = first.Seal();
        session.Accept(parent);

        source.Text = "pending";
        using CaptureContext next = session.BeginCapture();
        next.AddRoot<Domain, uint>(source, schema, static (value, context) => context.CaptureString(value.Text));
        CapturedGraph candidate = next.Seal();
        StringReadTable? failed = null;
        Assert.Throws<InvalidDataException>(() => failed = StringReadTable.Decode([
            (2u, new byte[] { 0x03, 0x41 }), (3u, new byte[] { 0x00, 0x41 })]));
        Assert.Null(failed);
        Assert.Same(parent, session.Current);
        Assert.Equal("parent", parent.Objects.Single(item => item.Kind == ObjectStateKind.String).StringContent);
        session.Accept(candidate);
        Assert.Same(candidate, session.Current);
        Assert.Equal(parent.RootIds[0], candidate.RootIds[0]);
        Assert.Equal("pending", candidate.Objects.Single(item => item.Kind == ObjectStateKind.String).StringContent);
    }

    private sealed class Domain : DurableBase {
        internal string? Text;
    }
}
