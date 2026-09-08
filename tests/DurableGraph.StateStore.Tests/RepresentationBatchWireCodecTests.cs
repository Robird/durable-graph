using System.Buffers;
using Atelia.DurableGraph.StateStore.Serialization;
using Atelia.Rbf;

namespace Atelia.DurableGraph.StateStore.Tests;

public sealed class RepresentationBatchWireCodecTests : IDisposable {
    private readonly List<string> _paths = [];
    private static readonly DurableSchema A = new("A", 1);
    private static readonly DurableSchema Point = new("P", 2, SchemaKind.InlineValue, new DurableFieldInfo(1, TypeTag.Int32));

    [Fact]
    public void IndependentGoldenIncludesExplicitIdsAndStoreOwnedDescriptors() {
        Assert.Equal(0x31425052U, RepresentationBatchWireCodec.RbfTag);
        byte[] golden = Convert.FromHexString("0102020202034100010303010402");
        KeyValuePair<RepresentationId, ObjectLayout>[] rows = [
            new(new(2), ObjectLayout.ForDurable(A)), new(new(3), Array(TypeTag.Int32))];
        Assert.Equal(golden, RepresentationBatchWireCodec.Write(rows));
        Assert.Equal(rows, RepresentationBatchWireCodec.Read(golden, Resolve));

        byte[] inlineGolden = Convert.FromHexString("010102030104100203500002");
        ObjectLayout inline = ObjectLayout.ForArray(new(TypeExprKind.VectorArray, new(1, TypeTag.InlineValue, inlineSchema: Point)));
        Assert.Equal(inlineGolden, RepresentationBatchWireCodec.Write([new(new(2), inline)]));
        ObjectLayout decoded = RepresentationBatchWireCodec.Read(inlineGolden, Resolve)[0].Value;
        Assert.Equal(inline, decoded);
        Assert.Same(Point, decoded.Array!.ElementSlot.InlineSchema);
    }

    [Theory]
    [InlineData(127U, "7F")]
    [InlineData(128U, "8001")]
    [InlineData(16383U, "FF7F")]
    [InlineData(16384U, "808001")]
    [InlineData(uint.MaxValue, "FFFFFFFF0F")]
    public void IdWidthsUseCanonicalUnsignedEncoding(uint id, string encoding) {
        byte[] golden = Convert.FromHexString("0101" + encoding + "03010402");
        Assert.Equal(golden, RepresentationBatchWireCodec.Write([new(new(id), Array(TypeTag.Int32))]));
        Assert.Equal(new RepresentationId(id), RepresentationBatchWireCodec.Read(golden, Resolve)[0].Key);
    }

    [Fact]
    public void AllDescriptorsAndArrayRanksHaveStableIndependentBytes() {
        Assert.Equal(new byte[] { 1 }, WriteDescriptor(ObjectLayout.String));
        Assert.Equal(Convert.FromHexString("020203410001"), WriteDescriptor(ObjectLayout.ForDurable(A)));
        for (byte constructor = 4; constructor <= 7; constructor++) {
            for (byte tag = 1; tag <= 14; tag++) {
                ObjectLayout layout = ObjectLayout.ForArray(new((TypeExprKind)constructor, new(1, (TypeTag)tag)));
                byte[] golden = [3, 1, constructor, tag];
                Assert.Equal(golden, WriteDescriptor(layout));
                Assert.Equal(layout, ReadDescriptor(golden));
            }
        }
        ObjectLayout composed = ObjectLayout.ForArray(new(TypeExprKind.VectorArray, DurableFieldInfo.Reference(1,
            TypeExpr.VectorArray(TypeExpr.Named("B", TypeExpr.MultiDimArray(TypeExpr.Builtin(TypeTag.String), 4))))));
        byte[] composedGolden = Convert.FromHexString("0301040F0402034201070104");
        Assert.Equal(composedGolden, WriteDescriptor(composed));
        Assert.Equal(composed, ReadDescriptor(composedGolden));
    }

    [Fact]
    public void LegacyGrammarUsesSameResolverWithoutAcceptingNewConstructors() {
        Assert.Equal(ObjectLayout.ForDurable(A), ReadDescriptor(Convert.FromHexString("02034101"), false, true));
        Assert.Equal(ObjectLayout.ForDurable(A), ReadDescriptor(Convert.FromHexString("020203410001"), false));
        Assert.Equal(ObjectLayout.String, ReadDescriptor([1], false, true));
        Assert.Throws<InvalidDataException>(() => ReadDescriptor([3, 1, 4, 2], false));
        Assert.Throws<InvalidDataException>(() => ReadDescriptor([3, 1, 4, 2], true, true));
        byte[] arrayArgument = Convert.FromHexString("020203420104010201");
        Assert.Throws<InvalidDataException>(() => ReadDescriptor(arrayArgument, false));
    }

    [Theory]
    [InlineData("02010203010402")] // Unknown batch version.
    [InlineData("0100")] // Empty physical batch.
    [InlineData("01FFFFFFFF0F")] // Count would exceed remaining bytes.
    [InlineData("01010003010402")] // ID zero.
    [InlineData("01010103010402")] // Reserved string ID.
    [InlineData("0101820003010402")] // Overlong ID.
    [InlineData("0101FFFFFFFF1003010402")] // ID overflow.
    [InlineData("01010200000000")] // Unknown descriptor.
    [InlineData("01010201000000")] // String must not be registered, regardless of padding.
    [InlineData("01010203000402")] // Unknown array codec.
    [InlineData("01010203020402")] // Future array codec.
    [InlineData("0101020381000402")] // Noncanonical codec.
    [InlineData("01010203010302")] // Open constructor.
    [InlineData("01010203010802")] // Unsupported rank.
    [InlineData("01010203010400")] // Unsupported element.
    [InlineData("01010203010411")] // Open element.
    [InlineData("0101020301040F0104")] // String has its own canonical slot tag.
    [InlineData("0101020301040F040300")] // Open nested reference.
    [InlineData("01010203010410010201")] // Builtin as Schema key.
    [InlineData("0101020301040200")] // Trailing byte.
    [InlineData("010202030104020203010405")] // Repeated ID.
    [InlineData("010203030104020203010405")] // Descending ID.
    [InlineData("010202030104020303010402")] // Same layout under two IDs.
    public void MalformedBatchesFailClosed(string hex) {
        Assert.Throws<InvalidDataException>(() => RepresentationBatchWireCodec.Read(Convert.FromHexString(hex), Resolve));
    }

    [Fact]
    public void EveryTruncatedPrefixAndMissingOrMismatchedSchemaFails() {
        byte[] golden = Convert.FromHexString("01020202020341000103030104100203500002");
        Assert.Equal(2, RepresentationBatchWireCodec.Read(golden, Resolve).Length);
        for (int length = 0; length < golden.Length; length++) {
            byte[] prefix = golden[..length];
            // Lengths 2..11 fail the minimum-row count check; at length 16 the
            // string header promises a missing name byte. Other cuts end mid-read.
            Type expected = length is >= 2 and <= 11 or 16
                ? typeof(InvalidDataException) : typeof(EndOfStreamException);
            Assert.Throws(expected, () => RepresentationBatchWireCodec.Read(prefix, Resolve));
        }
        Assert.Throws<InvalidDataException>(() => RepresentationBatchWireCodec.Read(golden,
            static key => throw new InvalidDataException("Missing Schema.")));
        Assert.Throws<InvalidDataException>(() => RepresentationBatchWireCodec.Read(golden,
            static key => new DurableSchema("Wrong", 1)));
        Assert.Throws<InvalidDataException>(() => RepresentationBatchWireCodec.Read(golden,
            static key => new DurableSchema(key.Type, key.Version, SchemaKind.InlineValue)));
        byte[] inline = Convert.FromHexString("010102030104100203500002");
        Assert.Throws<InvalidDataException>(() => RepresentationBatchWireCodec.Read(inline,
            static key => new DurableSchema(key.Type, key.Version)));
    }

    [Fact]
    public void WriterRejectsEmptyReservedUnorderedAndDuplicateRepresentations() {
        ObjectLayout array = Array(TypeTag.Int32);
        Assert.Throws<ArgumentException>(() => RepresentationBatchWireCodec.Write([]));
        Assert.Throws<ArgumentException>(() => RepresentationBatchWireCodec.Write([new(new(0), array)]));
        Assert.Throws<ArgumentException>(() => RepresentationBatchWireCodec.Write([new(new(1), array)]));
        Assert.Throws<ArgumentException>(() => RepresentationBatchWireCodec.Write([new(new(2), ObjectLayout.String)]));
        Assert.Throws<ArgumentException>(() => RepresentationBatchWireCodec.Write([new(new(3), array), new(new(2), Array(TypeTag.Byte))]));
        Assert.Throws<ArgumentException>(() => RepresentationBatchWireCodec.Write([new(new(2), array), new(new(3), array)]));
    }

    [Theory]
    [InlineData("duplicate-id")]
    [InlineData("rebound-id")]
    [InlineData("duplicate-layout")]
    [InlineData("gap")]
    [InlineData("tail")]
    [InlineData("meta")]
    public void InvalidRecoveredRepresentationRowsAreNeverSkippedOrRenumbered(string kind) {
        string path = NewPath();
        using (IRbfFile file = RbfFile.CreateNew(path)) {
            new SchemaStore(file).RegisterRepresentations([Array(TypeTag.Int32)]);
            byte[] payload = kind switch {
                "duplicate-id" => RepresentationBatchWireCodec.Write([new(new(2), Array(TypeTag.Int32))]),
                "rebound-id" => RepresentationBatchWireCodec.Write([new(new(2), Array(TypeTag.Byte))]),
                "duplicate-layout" => RepresentationBatchWireCodec.Write([new(new(3), Array(TypeTag.Int32))]),
                "gap" => RepresentationBatchWireCodec.Write([new(new(4), Array(TypeTag.Byte))]),
                "tail" => [.. RepresentationBatchWireCodec.Write([new(new(3), Array(TypeTag.Byte))]), 0],
                _ => RepresentationBatchWireCodec.Write([new(new(3), Array(TypeTag.Byte))]),
            };
            file.Append(RepresentationBatchWireCodec.RbfTag, payload, kind == "meta" ? new byte[] { 1 } : []).Unwrap();
            file.DurableFlush();
        }
        byte[] original = File.ReadAllBytes(path);
        using (IRbfFile file = RbfFile.OpenExisting(path)) {
            Assert.Throws<InvalidDataException>(() => new SchemaStore(file));
        }
        Assert.Equal(original, File.ReadAllBytes(path));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void RepresentationCannotReferenceSchemasDeclaredLaterInTheLog(bool inline) {
        using IRbfFile file = RbfFile.CreateNew(NewPath());
        DurableSchema schema = inline ? Point : A;
        ObjectLayout layout = inline ? ObjectLayout.ForArray(new(TypeExprKind.VectorArray,
            new(1, TypeTag.InlineValue, inlineSchema: schema))) : ObjectLayout.ForDurable(schema);
        file.Append(RepresentationBatchWireCodec.RbfTag, RepresentationBatchWireCodec.Write([new(new(2), layout)])).Unwrap();
        file.Append(SchemaBatchWireCodec.RbfTag, SchemaBatchWireCodec.Write([schema])).Unwrap();
        file.DurableFlush();
        Assert.Throws<InvalidDataException>(() => new SchemaStore(file));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void RecoveryRejectsSchemaAndArrayReferenceKindConflictRegardlessOfFrameOrder(bool schemaFirst) {
        using IRbfFile file = RbfFile.CreateNew(NewPath());
        byte[] schema = SchemaBatchWireCodec.Write([Point]);
        byte[] representation = RepresentationBatchWireCodec.Write([new(new(2), ObjectLayout.ForArray(new(
            TypeExprKind.VectorArray, DurableFieldInfo.Reference(1, Point.Type))))]);
        if (schemaFirst) {
            file.Append(SchemaBatchWireCodec.RbfTag, schema).Unwrap();
            file.Append(RepresentationBatchWireCodec.RbfTag, representation).Unwrap();
        }
        else {
            file.Append(RepresentationBatchWireCodec.RbfTag, representation).Unwrap();
            file.Append(SchemaBatchWireCodec.RbfTag, schema).Unwrap();
        }
        file.DurableFlush();
        Assert.Throws<InvalidDataException>(() => new SchemaStore(file));
    }

    private static ObjectLayout Array(TypeTag tag) => ObjectLayout.ForArray(new(TypeExprKind.VectorArray, new(1, tag)));
    private static DurableSchema Resolve(SchemaKey key) => key == new SchemaKey("A", 1) ? A : key == new SchemaKey("P", 2) ? Point
        : throw new InvalidDataException("Missing exact Schema.");
    private static byte[] WriteDescriptor(ObjectLayout layout) {
        ArrayBufferWriter<byte> buffer = new();
        BinaryPayloadWriter writer = new(buffer);
        RepresentationDescriptorCodec.Write(ref writer, layout);
        return buffer.WrittenSpan.ToArray();
    }
    private static ObjectLayout ReadDescriptor(byte[] bytes, bool allowArrays = true, bool legacySchemaKeys = false) {
        BinaryPayloadReader reader = new(bytes);
        ObjectLayout result = RepresentationDescriptorCodec.Read(ref reader, Resolve, allowArrays, legacySchemaKeys);
        reader.EnsureFullyConsumed();
        return result;
    }
    private string NewPath() {
        string path = Path.Combine(Path.GetTempPath(), $"durable-representation-wire-{Guid.NewGuid():N}.rbf");
        _paths.Add(path);
        return path;
    }
    public void Dispose() { foreach (string path in _paths) { File.Delete(path); } }
}
