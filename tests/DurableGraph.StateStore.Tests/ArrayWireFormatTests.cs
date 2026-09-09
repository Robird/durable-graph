using System.Buffers;
using Atelia.DurableGraph.StateStore.Serialization;
using Atelia.Rbf;

namespace Atelia.DurableGraph.StateStore.Tests;

public sealed class ArrayWireFormatTests {
    [Fact]
    public void RecursiveArrayConstructorsHaveIndependentGoldenBytes() {
        TypeExpr type = TypeExpr.Named("B",
            TypeExpr.VectorArray(TypeExpr.MultiDimArray(TypeExpr.Builtin(TypeTag.Int32), 2)),
            TypeExpr.MultiDimArray(TypeExpr.MultiDimArray(TypeExpr.Named("P"), 4), 3));
        byte[] golden = Convert.FromHexString("0203420204050102060702035000");
        Assert.Equal(golden, WriteType(type));
        Assert.Equal(type, ReadType(golden));
        for (int length = 0; length < golden.Length; length++) {
            byte[] prefix = golden[..length];
            Exception? error = Record.Exception(() => ReadType(prefix));
            Assert.True(error is InvalidDataException or EndOfStreamException, $"Prefix {length}: {error}");
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
        Assert.Throws<InvalidDataException>(() => ReadType([11, 1, 2]));
        Assert.Throws<ArgumentException>(() => WriteType(TypeExpr.VectorArray(TypeExpr.Parameter(0))));
    }

    [Fact]
    public void CatalogPersistsArrayReferencesAndNestedGenericArguments() {
        TypeExpr slot = TypeExpr.VectorArray(TypeExpr.MultiDimArray(TypeExpr.Builtin(TypeTag.Int32), 2));
        DurableSchema schema = new("A", 1, DurableFieldInfo.Reference(1, slot));
        byte[] golden = Convert.FromHexString("0201020102034100010001010F04050102");
        Assert.Equal(golden, SchemaCatalogTestData.Write([schema]));
        Assert.Equal(schema, SchemaCatalogTestData.Read(golden)[new("A", 1)]);
        byte[] old = (byte[])golden.Clone();
        old[0] = 3;
        Assert.Throws<InvalidDataException>(() => SchemaCatalogWireCodec.Read(old, SchemaCatalogTestData.Empty));

        DurableSchema generic = new(TypeExpr.Named("B", slot), 1);
        byte[] nested = SchemaCatalogTestData.Write([generic]);
        Assert.Equal(generic, SchemaCatalogTestData.Read(nested)[new(generic.Type, 1)]);
        nested[0] = 3;
        Assert.Throws<InvalidDataException>(() => SchemaCatalogWireCodec.Read(nested, SchemaCatalogTestData.Empty));
    }

    [Fact]
    public void ArrayReferenceChecksNestedAritiesWithoutAssumingElementFamilyKind() {
        DurableSchema point = new("P", 1, SchemaKind.InlineValue, new DurableFieldInfo(1, TypeTag.Int32));
        DurableSchema owner = new("A", 1, DurableFieldInfo.Reference(1, TypeExpr.VectorArray(point.Type)));
        var read = SchemaCatalogWireCodec.Read(SchemaCatalogTestData.Write([owner, point]), SchemaCatalogTestData.Empty)
            .ToDictionary(entry => entry.Id);
        Assert.Equal(point, read[new(3)].Schema);
        DurableSchema invalid = new("B", 1, DurableFieldInfo.Reference(1,
            TypeExpr.MultiDimArray(TypeExpr.Named("P", TypeExpr.Builtin(TypeTag.Int32)), 4)));
        byte[] invalidRow = SchemaCatalogTestData.Write([invalid]);
        invalidRow[2] = 4;
        Assert.Throws<InvalidDataException>(() => SchemaCatalogWireCodec.Read(invalidRow, read));
    }

    [Theory]
    [InlineData(TypeExprKind.VectorArray, "02010203010402")]
    [InlineData(TypeExprKind.Rank2Array, "02010203010502")]
    [InlineData(TypeExprKind.Rank3Array, "02010203010602")]
    [InlineData(TypeExprKind.Rank4Array, "02010203010702")]
    public void CatalogNodePreservesExactShapeConstructor(TypeExprKind constructor, string hex) {
        ArrayLayout layout = new(constructor, new DurableFieldInfo(1, TypeTag.Int32));
        byte[] golden = Convert.FromHexString(hex);
        Assert.Equal(golden, WriteArray(layout));
        Assert.Equal(layout, ReadArray(golden));
    }

    [Fact]
    public void ReferenceElementNodeSupportsJaggedGenericComposition() {
        ArrayLayout layout = new(TypeExprKind.VectorArray, DurableFieldInfo.Reference(1,
            TypeExpr.VectorArray(TypeExpr.Named("B", TypeExpr.MultiDimArray(TypeExpr.Builtin(TypeTag.String), 4)))));
        byte[] golden = Convert.FromHexString("0201020301040F0402034201070104");
        Assert.Equal(golden, WriteArray(layout));
        Assert.Equal(layout, ReadArray(golden));
        ArrayLayout strings = new(TypeExprKind.VectorArray, new DurableFieldInfo(1, TypeTag.String));
        Assert.Equal(Convert.FromHexString("02010203010404"), WriteArray(strings));
        // A second representation of the same string slot is deliberately noncanonical.
        Assert.Throws<InvalidDataException>(() => ReadArray(Convert.FromHexString("0201020301040F0104")));
    }

    [Fact]
    public void InlineElementNodeUsesEarlierExactSchemaIdAndColdReopens() {
        string path = Path.Combine(Path.GetTempPath(), $"durable-array-wire-{Guid.NewGuid():N}.rbf");
        try {
            DurableSchema point1 = new("P", 1, SchemaKind.InlineValue, new DurableFieldInfo(1, TypeTag.Int32));
            DurableSchema point2 = new("P", 2, SchemaKind.InlineValue, new DurableFieldInfo(1, TypeTag.Int64));
            ArrayLayout layout = new(TypeExprKind.VectorArray, new DurableFieldInfo(1, TypeTag.InlineValue, inlineSchema: point1));
            var registered = SchemaCatalogTestData.Registered(point1);
            byte[] golden = Convert.FromHexString("0201030301041002");
            Assert.Equal(golden, WriteArray(layout, registered));
            Assert.Same(point1, ReadArray(golden, registered).ElementSlot.InlineSchema);
            // Even a known ID cannot point at a class where an inline element is required.
            Assert.Throws<InvalidDataException>(() => ReadArray(golden, SchemaCatalogTestData.Registered(new DurableSchema("R", 1))));
            RepresentationId id;
            using (IRbfFile file = RbfFile.CreateNew(path)) {
                var schemas = new SchemaStore(file);
                schemas.Register(point2);
                schemas.Register(point1);
                id = schemas.RegisterRepresentations([ObjectLayout.ForArray(layout)])[0];
                Assert.Equal(4U, id.Value);
            }
            using (IRbfFile file = RbfFile.OpenExisting(path)) {
                var schemas = new SchemaStore(file, readOnly: true);
                var decoded = schemas.GetRepresentation(id);
                Assert.Equal(layout, decoded.Array);
                Assert.Same(schemas.GetRequired("P", 1), decoded.Array!.ElementSlot.InlineSchema);
            }
        }
        finally { if (File.Exists(path)) { File.Delete(path); } }
    }

    [Theory]
    [InlineData("02010203000402")] // Unknown codec zero.
    [InlineData("02010203020402")] // Future codec.
    [InlineData("0201020381000402")] // Noncanonical codec.
    [InlineData("02010203010302")] // Open type is not an array constructor.
    [InlineData("02010203010802")] // Unknown rank.
    [InlineData("02010203010400")] // Invalid element slot.
    [InlineData("02010203010411")] // Template parameter is not a closed element slot.
    [InlineData("0201020301040F040300")] // Open reference element operand.
    [InlineData("0201020301041000")] // Zero cannot identify an inline Schema.
    [InlineData("0201020301041001")] // String cannot identify an inline Schema.
    [InlineData("0201020301041002")] // Self dependency.
    [InlineData("0201020301041003")] // Future dependency.
    public void MalformedArrayNodesFailClosed(string hex) {
        Assert.Throws<InvalidDataException>(() => ReadArray(Convert.FromHexString(hex)));
    }

    [Fact]
    public void ArrayWrapperDoesNotConsumeAnExtraExactSchemaDepthLevel() {
        var chain = new DurableSchema[256];
        for (int index = 0; index < chain.Length; index++) {
            DurableFieldInfo field = index == 0 ? new(1, TypeTag.Int32)
                : new(1, TypeTag.InlineValue, inlineSchema: chain[index - 1]);
            chain[index] = new($"I{index:D3}", 1, SchemaKind.InlineValue, field);
        }
        var registered = SchemaCatalogWireCodec.Read(SchemaCatalogTestData.Write(chain), SchemaCatalogTestData.Empty)
            .ToDictionary(entry => entry.Id);
        ArrayLayout layout = new(TypeExprKind.VectorArray, new(1, TypeTag.InlineValue, inlineSchema: chain[^1]));
        // ID 258 array references inline ID 257, whose Schema DAG has exactly 256 levels.
        byte[] golden = Convert.FromHexString("02018202030104108102");
        Assert.Equal(golden, WriteArray(layout, registered));
        ArrayLayout decoded = ReadArray(golden, registered);
        Assert.Equal(layout, decoded);
        Assert.Same(registered[new(257)].Schema, decoded.ElementSlot.InlineSchema);
    }

    [Fact]
    public void EveryTruncatedReferenceArrayNodeFails() {
        byte[] golden = Convert.FromHexString("0201020301040F0402034201070104");
        for (int length = 0; length < golden.Length; length++) {
            byte[] prefix = golden[..length];
            Exception? error = Record.Exception(() => ReadArray(prefix));
            Assert.True(error is InvalidDataException or EndOfStreamException, $"Prefix {length}: {error}");
        }
        Assert.Equal(TypeExprKind.VectorArray, ReadArray(golden).Constructor);
    }

    private static ArrayLayout ReadArray(byte[] bytes,
        IReadOnlyDictionary<RepresentationId, SchemaCatalogEntry>? registered = null) =>
        Assert.Single(SchemaCatalogWireCodec.Read(bytes, registered ?? SchemaCatalogTestData.Empty)).Array!;

    private static byte[] WriteArray(ArrayLayout layout,
        IReadOnlyDictionary<RepresentationId, SchemaCatalogEntry>? registered = null) {
        registered ??= SchemaCatalogTestData.Empty;
        uint id = registered.Count == 0 ? 2 : registered.Keys.Max(key => key.Value) + 1;
        return SchemaCatalogWireCodec.Write([SchemaCatalogEntry.ForArray(new(id), layout)], registered);
    }

    private static byte[] WriteType(TypeExpr type) {
        ArrayBufferWriter<byte> buffer = new();
        BinaryPayloadWriter writer = new(buffer);
        TypeExprWireCodec.Write(ref writer, type);
        return buffer.WrittenSpan.ToArray();
    }

    private static TypeExpr ReadType(byte[] bytes) {
        BinaryPayloadReader reader = new(bytes);
        TypeExpr result = TypeExprWireCodec.Read(ref reader);
        reader.EnsureFullyConsumed();
        return result;
    }
}
