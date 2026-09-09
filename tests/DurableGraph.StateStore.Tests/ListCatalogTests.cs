using System.Buffers;
using Atelia.DurableGraph.StateStore.Serialization;
using Atelia.Rbf;

namespace Atelia.DurableGraph.StateStore.Tests;

public sealed class ListCatalogTests {
    [Fact]
    public void IndependentGoldenCoversShortestRowAndAllBuiltinSlots() {
        Assert.Equal(Convert.FromHexString("080102"), WriteType(TypeExpr.List(TypeExpr.Builtin(TypeTag.Int32))));
        Assert.Equal(TypeExpr.List(TypeExpr.Builtin(TypeTag.Int32)), ReadType(Convert.FromHexString("080102")));
        for (byte tag = 1; tag <= 14; tag++) {
            SchemaCatalogEntry row = SchemaCatalogEntry.ForList(new(2), new(new(1, (TypeTag)tag)));
            byte[] golden = [2, 1, 2, 4, 2, tag];
            Assert.Equal(golden, SchemaCatalogWireCodec.Write([row], CatalogTestData.Empty));
            Assert.Equal(row.Layout, Assert.Single(SchemaCatalogWireCodec.Read(golden, CatalogTestData.Empty)).Layout);
        }
    }

    [Fact]
    public void NestedConstructorsAndInlineDependencyHaveIndependentGoldens() {
        TypeExpr nested = TypeExpr.List(TypeExpr.VectorArray(TypeExpr.List(TypeExpr.Builtin(TypeTag.Int32))));
        byte[] typeGolden = Convert.FromHexString("0804080102");
        Assert.Equal(typeGolden, WriteType(nested));
        Assert.Equal(nested, ReadType(typeGolden));
        SchemaCatalogEntry row = SchemaCatalogEntry.ForList(new(2), new(DurableFieldInfo.Reference(1, nested)));
        byte[] golden = Convert.FromHexString("02010204020F0804080102");
        Assert.Equal(golden, SchemaCatalogWireCodec.Write([row], CatalogTestData.Empty));
        Assert.Equal(row.Layout, Assert.Single(SchemaCatalogWireCodec.Read(golden, CatalogTestData.Empty)).Layout);
        DurableSchema point = new("P", 1, SchemaKind.InlineValue, new DurableFieldInfo(1, TypeTag.Int32));
        SchemaCatalogEntry[] entries = [SchemaCatalogEntry.ForSchema(new(2), point),
            SchemaCatalogEntry.ForList(new(3), new(new(1, TypeTag.InlineValue, inlineSchema: point)))];
        byte[] inlineGolden = Convert.FromHexString("020202020203500001000101020304021002");
        Assert.Equal(inlineGolden, SchemaCatalogWireCodec.Write(entries, CatalogTestData.Empty));
        SchemaCatalogEntry[] read = SchemaCatalogWireCodec.Read(inlineGolden, CatalogTestData.Empty);
        Assert.Same(read[0].Schema, read[1].List!.ElementSlot.InlineSchema);
        for (int i = 0; i < inlineGolden.Length; i++) {
            byte[] prefix = inlineGolden[..i];
            Exception? error = Record.Exception(() => SchemaCatalogWireCodec.Read(prefix, CatalogTestData.Empty));
            Assert.True(error is InvalidDataException or EndOfStreamException, $"Prefix {i}: {error}");
        }
    }

    [Theory]
    [InlineData("020102040002")] // Codec zero.
    [InlineData("020102040102")] // Retired codec.
    [InlineData("020102040302")] // Future codec.
    [InlineData("02010204820002")] // Noncanonical codec.
    [InlineData("020102040200")] // Unknown slot.
    [InlineData("020102040211")] // Open slot.
    [InlineData("02010204020F0104")] // String has a canonical dedicated slot.
    [InlineData("02010204021000")] // Null dependency.
    [InlineData("02010204021001")] // Builtin string dependency.
    [InlineData("02010204021002")] // Self dependency.
    [InlineData("02010204021003")] // Forward dependency.
    [InlineData("02010204020200")] // Extra byte.
    [InlineData("02020204020203040202")] // Duplicate layout at another ID.
    [InlineData("020102050102")] // Unknown catalog node.
    public void InvalidListRowsFailClosed(string hex) {
        Assert.Throws<InvalidDataException>(() => SchemaCatalogWireCodec.Read(Convert.FromHexString(hex), CatalogTestData.Empty));
    }

    [Fact]
    public void ListTypeUsesTheSameDepthAndClosureLimits() {
        TypeExpr type = TypeExpr.Builtin(TypeTag.Int32);
        for (int i = 1; i < TypeExpr.MaximumDepth; i++) { type = TypeExpr.List(type); }
        Assert.Equal(type, ReadType(WriteType(type)));
        byte[] tooDeep = [.. Enumerable.Repeat((byte)8, TypeExpr.MaximumDepth), 1, 2];
        Assert.Throws<InvalidDataException>(() => ReadType(tooDeep));
        Assert.Throws<InvalidDataException>(() => ReadType([8, 3, 0]));
        Assert.Throws<InvalidDataException>(() => ReadType([10, 1, 2]));
        Assert.Throws<ArgumentException>(() => WriteType(TypeExpr.List(TypeExpr.Parameter(0))));
    }

    [Fact]
    public void ExactDependenciesDeduplicateAndConflictBeforeAppendThenColdReopen() {
        string path = Path.Combine(Path.GetTempPath(), $"list-catalog-{Guid.NewGuid():N}.rbf");
        DurableSchema p1 = new("P", 1, SchemaKind.InlineValue, new DurableFieldInfo(1, TypeTag.Int32));
        DurableSchema p2 = new("P", 2, SchemaKind.InlineValue, new DurableFieldInfo(1, TypeTag.Int64));
        ObjectLayout list1 = InlineList(p1), list2 = InlineList(p2);
        RepresentationId[] ids;
        try {
            using (IRbfFile file = RbfFile.CreateNew(path)) {
                SchemaStore schemas = new(file);
                ids = schemas.RegisterRepresentations([list1, list2, list1]);
                Assert.Equal(new uint[] { 3, 5, 3 }, ids.Select(id => id.Value));
                Assert.Equal(2, schemas.Count);
                Assert.Throws<InvalidDataException>(() => schemas.GetRepresentation(new(2)));
                long tail = file.TailOffset;
                Assert.Equal(ids, schemas.RegisterRepresentations([list1, list2, list1]));
                Assert.Equal(tail, file.TailOffset);
                DurableSchema bad = new("P", 1, SchemaKind.InlineValue, new DurableFieldInfo(1, TypeTag.Byte));
                Assert.Throws<SchemaConflictException>(() => schemas.RegisterRepresentations([
                    ObjectLayout.ForList(new(new(1, TypeTag.Boolean))), InlineList(bad)]));
                Assert.Equal(tail, file.TailOffset);
                Assert.False(schemas.IsFaulted);
                Assert.Equal(6U, schemas.RegisterRepresentations([ObjectLayout.ForList(new(new(1, TypeTag.Boolean)))])[0].Value);
            }
            using IRbfFile reopened = RbfFile.OpenExisting(path);
            SchemaStore cold = new(reopened, readOnly: true);
            Assert.Equal(list1, cold.GetRepresentation(ids[0]));
            Assert.Equal(list2, cold.GetRepresentation(ids[1]));
            Assert.Same(cold.GetRequired("P", 1), cold.GetRepresentation(ids[0]).List!.ElementSlot.InlineSchema);
        } finally { File.Delete(path); }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ReferenceKindConflictIsRejectedInEitherRegistrationOrder(bool schemaFirst) {
        DurableSchema point = new("P", 1, SchemaKind.InlineValue);
        SchemaCatalogEntry schema = SchemaCatalogEntry.ForSchema(new(schemaFirst ? 2U : 3U), point);
        SchemaCatalogEntry list = SchemaCatalogEntry.ForList(new(schemaFirst ? 3U : 2U), new(DurableFieldInfo.Reference(1, point.Type)));
        SchemaCatalogEntry[] rows = schemaFirst ? [schema, list] : [list, schema];
        Assert.Throws<ArgumentException>(() => SchemaCatalogWireCodec.Write(rows, CatalogTestData.Empty));
        Assert.Throws<InvalidDataException>(() => SchemaCatalogWireCodec.Read(CatalogTestData.Encode(rows), CatalogTestData.Empty));
    }

    [Fact]
    public void ReferenceListsDoNotOwnExactElementVersionsButStillValidateNominalArity() {
        DurableSchema point = new("P", 1, SchemaKind.InlineValue);
        DurableSchema owner = new("O", 1, DurableFieldInfo.Reference(1, TypeExpr.List(point.Type)));
        Assert.Equal(owner, SchemaCatalogTestData.Read(SchemaCatalogTestData.Write([owner, point]))[new("O", 1)]);
        SchemaCatalogEntry[] bad = [SchemaCatalogEntry.ForSchema(new(2), point),
            SchemaCatalogEntry.ForList(new(3), new(DurableFieldInfo.Reference(1,
                TypeExpr.List(TypeExpr.Named("P", TypeExpr.Builtin(TypeTag.Int32))))))];
        Assert.Throws<ArgumentException>(() => SchemaCatalogWireCodec.Write(bad, CatalogTestData.Empty));
    }

    [Fact]
    public void InlineDependencyRejectsClassAndContainerNodesAndKeepsSchemaDepthLimit() {
        byte[] dependency = Convert.FromHexString("02010304021002");
        var classIndex = CatalogTestData.Index([SchemaCatalogEntry.ForSchema(new(2), new("C", 1))]);
        var listIndex = CatalogTestData.Index([SchemaCatalogEntry.ForList(new(2), new(new(1, TypeTag.Int32)))]);
        Assert.Throws<InvalidDataException>(() => SchemaCatalogWireCodec.Read(dependency, classIndex));
        Assert.Throws<InvalidDataException>(() => SchemaCatalogWireCodec.Read(dependency, listIndex));

        var chain = new DurableSchema[256];
        for (int i = 0; i < chain.Length; i++) {
            DurableFieldInfo field = i == 0 ? new(1, TypeTag.Int32) : new(1, TypeTag.InlineValue, inlineSchema: chain[i - 1]);
            chain[i] = new($"I{i:D3}", 1, SchemaKind.InlineValue, field);
        }
        var registered = CatalogTestData.Index(SchemaCatalogWireCodec.Read(SchemaCatalogTestData.Write(chain), CatalogTestData.Empty));
        SchemaCatalogEntry list = SchemaCatalogEntry.ForList(new(258), new(new(1, TypeTag.InlineValue, inlineSchema: chain[^1])));
        byte[] golden = Convert.FromHexString("020182020402108102");
        Assert.Equal(golden, SchemaCatalogWireCodec.Write([list], registered));
        Assert.Same(registered[new(257)].Schema,
            Assert.Single(SchemaCatalogWireCodec.Read(golden, registered)).List!.ElementSlot.InlineSchema);
    }

    private static ObjectLayout InlineList(DurableSchema schema) => ObjectLayout.ForList(new(new(1, TypeTag.InlineValue, inlineSchema: schema)));
    private static byte[] WriteType(TypeExpr type) {
        ArrayBufferWriter<byte> bytes = new();
        BinaryPayloadWriter writer = new(bytes);
        TypeExprWireCodec.Write(ref writer, type);
        return bytes.WrittenSpan.ToArray();
    }
    private static TypeExpr ReadType(byte[] bytes) {
        BinaryPayloadReader reader = new(bytes);
        TypeExpr result = TypeExprWireCodec.Read(ref reader);
        reader.EnsureFullyConsumed();
        return result;
    }
}
