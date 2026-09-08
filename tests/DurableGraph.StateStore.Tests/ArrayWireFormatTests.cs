using System.Buffers;
using Atelia.DurableGraph.StateStore.Serialization;
using Atelia.Rbf;

namespace Atelia.DurableGraph.StateStore.Tests;

public sealed class ArrayWireFormatTests {
    private static readonly Dictionary<SchemaKey, DurableSchema> Empty = new();

    [Fact]
    public void RecursiveArrayConstructorsHaveIndependentGoldenBytes() {
        TypeExpr type = TypeExpr.Named("B",
            TypeExpr.VectorArray(TypeExpr.MultiDimArray(TypeExpr.Builtin(TypeTag.Int32), 2)),
            TypeExpr.MultiDimArray(TypeExpr.MultiDimArray(TypeExpr.Named("P"), 4), 3));
        byte[] golden = Convert.FromHexString("0203420204050102060702035000");
        Assert.Equal(golden, WriteType(type));
        Assert.Equal(type, ReadType(golden));
        Assert.Throws<InvalidDataException>(() => ReadType(golden, allowArrays: false));
        for (int length = 0; length < golden.Length; length++) {
            byte[] prefix = golden[..length];
            Assert.ThrowsAny<Exception>(() => ReadType(prefix));
        }
    }

    [Fact]
    public void ArrayNestingParticipatesInWireDepthLimitAndRejectsOpenOperands() {
        TypeExpr type = TypeExpr.Builtin(TypeTag.Int32);
        for (int depth = 1; depth < TypeExpr.MaximumDepth; depth++) { type = TypeExpr.VectorArray(type); }
        Assert.Equal(type, ReadType(WriteType(type)));
        byte[] tooDeep = new byte[TypeExpr.MaximumDepth + 2];
        tooDeep.AsSpan(0, TypeExpr.MaximumDepth).Fill(4);
        tooDeep[^2] = 1;
        tooDeep[^1] = 2;
        Assert.Throws<InvalidDataException>(() => ReadType(tooDeep));
        Assert.Throws<InvalidDataException>(() => ReadType([4, 3, 0]));
        Assert.Throws<InvalidDataException>(() => ReadType([8, 1, 2]));
        Assert.Throws<ArgumentException>(() => WriteType(TypeExpr.VectorArray(TypeExpr.Parameter(0))));
    }

    [Fact]
    public void SchemaV4PersistsArrayReferencesAndNestedGenericArguments() {
        TypeExpr slot = TypeExpr.VectorArray(TypeExpr.MultiDimArray(TypeExpr.Builtin(TypeTag.Int32), 2));
        DurableSchema schema = new("A", 1, DurableFieldInfo.Reference(1, slot));
        byte[] golden = Convert.FromHexString("04010203410001010001010F04050102");
        Assert.Equal(golden, SchemaBatchWireCodec.Write([schema]));
        Assert.Equal(schema, SchemaBatchWireCodec.Read(golden, Empty)[new("A", 1)]);
        byte[] old = (byte[])golden.Clone();
        old[0] = 3;
        Assert.Throws<InvalidDataException>(() => SchemaBatchWireCodec.Read(old, Empty));

        DurableSchema generic = new(TypeExpr.Named("B", slot), 1);
        byte[] nested = SchemaBatchWireCodec.Write([generic]);
        Assert.Equal(generic, SchemaBatchWireCodec.Read(nested, Empty)[new(generic.Type, 1)]);
        nested[0] = 3;
        Assert.Throws<InvalidDataException>(() => SchemaBatchWireCodec.Read(nested, Empty));
    }

    [Fact]
    public void ArrayReferenceChecksNestedAritiesWithoutAssumingElementFamilyKind() {
        DurableSchema point = new("P", 1, SchemaKind.InlineValue, new DurableFieldInfo(1, TypeTag.Int32));
        DurableSchema owner = new("A", 1, DurableFieldInfo.Reference(1, TypeExpr.VectorArray(point.Type)));
        var read = SchemaBatchWireCodec.Read(SchemaBatchWireCodec.Write([owner, point]), Empty);
        Assert.Equal(point, read[new("P", 1)]);
        DurableSchema invalid = new("B", 1, DurableFieldInfo.Reference(1,
            TypeExpr.MultiDimArray(TypeExpr.Named("P", TypeExpr.Builtin(TypeTag.Int32)), 4)));
        Assert.Throws<InvalidDataException>(() => SchemaBatchWireCodec.Read(SchemaBatchWireCodec.Write([invalid]), read));
    }

    [Theory]
    [InlineData(TypeExprKind.VectorArray, "0303010402AB")]
    [InlineData(TypeExprKind.Rank2Array, "0303010502AB")]
    [InlineData(TypeExprKind.Rank3Array, "0303010602AB")]
    [InlineData(TypeExprKind.Rank4Array, "0303010702AB")]
    public void ArrayBaseHeaderPreservesOpaqueBodyAndExactShapeConstructor(TypeExprKind constructor, string hex) {
        ArrayLayout layout = new(constructor, new DurableFieldInfo(1, TypeTag.Int32));
        var encoded = BaseObjectBodyCodec.EncodeArray(layout, new([0xAB]));
        Assert.Equal(Convert.FromHexString(hex), encoded.Body.ToArray());
        var decoded = BaseObjectBodyCodec.Decode(encoded.Body);
        Assert.Equal(ObjectStateKind.Array, decoded.Kind);
        Assert.Equal(layout, decoded.ArrayLayout);
        Assert.Null(decoded.SchemaKey);
        Assert.Equal(new byte[] { 0xAB }, decoded.Body.ToArray());
        byte[] old = encoded.Body.ToArray();
        old[0] = 2;
        Assert.Throws<InvalidDataException>(() => BaseObjectBodyCodec.Decode(old));
    }

    [Fact]
    public void ReferenceElementHeaderSupportsJaggedGenericComposition() {
        ArrayLayout layout = new(TypeExprKind.VectorArray, DurableFieldInfo.Reference(1,
            TypeExpr.VectorArray(TypeExpr.Named("B", TypeExpr.MultiDimArray(TypeExpr.Builtin(TypeTag.String), 4)))));
        byte[] golden = Convert.FromHexString("030301040F0402034201070104AB");
        Assert.Equal(golden, BaseObjectBodyCodec.EncodeArray(layout, new([0xAB])).Body.ToArray());
        Assert.Equal(layout, BaseObjectBodyCodec.Decode(golden).ArrayLayout);
        ArrayLayout strings = new(TypeExprKind.VectorArray, new DurableFieldInfo(1, TypeTag.String));
        Assert.Equal(Convert.FromHexString("0303010404"), BaseObjectBodyCodec.EncodeArray(strings, new([])).Body.ToArray());
        // A second representation of the same string slot is deliberately noncanonical.
        Assert.Throws<InvalidDataException>(() => BaseObjectBodyCodec.Decode(Convert.FromHexString("030301040F0104")));
    }

    [Fact]
    public void InlineElementHeaderUsesStoredExactSchemaAndColdReopens() {
        string path = Path.Combine(Path.GetTempPath(), $"durable-array-wire-{Guid.NewGuid():N}.rbf");
        try {
            DurableSchema point1 = new("P", 1, SchemaKind.InlineValue, new DurableFieldInfo(1, TypeTag.Int32));
            DurableSchema point2 = new("P", 2, SchemaKind.InlineValue, new DurableFieldInfo(1, TypeTag.Int64));
            ArrayLayout layout = new(TypeExprKind.VectorArray, new DurableFieldInfo(1, TypeTag.InlineValue, inlineSchema: point1));
            byte[] golden = Convert.FromHexString("03030104100203500001AB");
            Assert.Equal(golden, BaseObjectBodyCodec.EncodeArray(layout, new([0xAB])).Body.ToArray());
            Assert.Throws<InvalidDataException>(() => BaseObjectBodyCodec.Decode(golden));
            using (IRbfFile file = RbfFile.CreateNew(path)) {
                var schemas = new SchemaStore(file);
                schemas.Register(point2);
                Assert.Throws<InvalidDataException>(() => BaseObjectBodyCodec.Decode(golden, schemas));
                schemas.Register(point1);
                schemas.Register(new DurableSchema("R", 1));
                Assert.Throws<InvalidDataException>(() => BaseObjectBodyCodec.Decode(Convert.FromHexString("03030104100203520001"), schemas));
            }
            using (IRbfFile file = RbfFile.OpenExisting(path)) {
                var schemas = new SchemaStore(file, readOnly: true);
                var decoded = BaseObjectBodyCodec.Decode(golden, schemas);
                Assert.Equal(layout, decoded.ArrayLayout);
                Assert.Same(schemas.GetRequired("P", 1), decoded.ArrayLayout!.ElementSlot.InlineSchema);
            }
        }
        finally { if (File.Exists(path)) { File.Delete(path); } }
    }

    [Theory]
    [InlineData("0303000402")] // Unknown codec zero.
    [InlineData("0303020402")] // Future codec.
    [InlineData("030381000402")] // Noncanonical codec.
    [InlineData("0303010302")] // Open type is not an array constructor.
    [InlineData("0303010802")] // Unknown rank.
    [InlineData("0303010400")] // Invalid element slot.
    [InlineData("0303010411")] // Template parameter is not a closed element slot.
    [InlineData("030301040F040300")] // Open reference element operand.
    [InlineData("0303010410010201")] // Builtin cannot identify an inline Schema.
    public void MalformedArrayHeadersFailClosed(string hex) {
        Assert.Throws<InvalidDataException>(() => BaseObjectBodyCodec.Decode(Convert.FromHexString(hex)));
    }

    [Fact]
    public void EveryTruncatedReferenceArrayHeaderFails() {
        byte[] golden = Convert.FromHexString("030301040F0402034201070104");
        for (int length = 0; length < golden.Length; length++) {
            byte[] prefix = golden[..length];
            Assert.ThrowsAny<Exception>(() => BaseObjectBodyCodec.Decode(prefix));
        }
        Assert.Empty(BaseObjectBodyCodec.Decode(golden).Body.ToArray());
    }

    [Fact]
    public void LegacyBaseV2RejectsArrayArgumentsAndKeepsNonArrayBodies() {
        DurableSchema schema = new(TypeExpr.Named("B", TypeExpr.VectorArray(TypeExpr.Builtin(TypeTag.Int32))), 1);
        byte[] encoded = BaseObjectBodyCodec.EncodeDurable(schema, new([])).Body.ToArray();
        Assert.Equal(new SchemaKey(schema.Type, 1), BaseObjectBodyCodec.Decode(encoded).SchemaKey);
        encoded[0] = 2;
        Assert.Throws<InvalidDataException>(() => BaseObjectBodyCodec.Decode(encoded));
        Assert.Equal(new SchemaKey("A", 1), BaseObjectBodyCodec.Decode(Convert.FromHexString("02020203410001AB")).SchemaKey);
    }

    private static byte[] WriteType(TypeExpr type) {
        ArrayBufferWriter<byte> buffer = new();
        BinaryPayloadWriter writer = new(buffer);
        TypeExprWireCodec.Write(ref writer, type);
        return buffer.WrittenSpan.ToArray();
    }

    private static TypeExpr ReadType(byte[] bytes, bool allowArrays = true) {
        BinaryPayloadReader reader = new(bytes);
        TypeExpr result = TypeExprWireCodec.Read(ref reader, allowArrays);
        reader.EnsureFullyConsumed();
        return result;
    }
}
