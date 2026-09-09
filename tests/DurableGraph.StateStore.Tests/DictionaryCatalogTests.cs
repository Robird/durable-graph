using System.Buffers;
using Atelia.DurableGraph.StateStore.Serialization;
using Atelia.Rbf;

namespace Atelia.DurableGraph.StateStore.Tests;

public sealed class DictionaryCatalogTests {
    [Fact]
    public void IndependentGoldenPreservesOrderedTypeOperandsAndAllBuiltinSlots() {
        TypeExpr type = TypeExpr.Dictionary(TypeExpr.Builtin(TypeTag.String), TypeExpr.Builtin(TypeTag.Int32));
        byte[] typeGolden = Convert.FromHexString("0A01040102");
        Assert.Equal(typeGolden, WriteType(type));
        Assert.Equal(type, ReadType(typeGolden));
        Assert.NotEqual(type, ReadType(Convert.FromHexString("0A01020104")));
        for (byte tag = 1; tag <= 14; tag++) {
            byte other = (byte)(tag % 14 + 1);
            SchemaCatalogEntry row = SchemaCatalogEntry.ForDictionary(new(2), new(new(1, (TypeTag)tag), new(2, (TypeTag)other)));
            byte[] golden = [2, 1, 2, 5, 1, tag, other];
            Assert.Equal(golden, SchemaCatalogWireCodec.Write([row], CatalogTestData.Empty));
            DictionaryLayout read = Assert.Single(SchemaCatalogWireCodec.Read(golden, CatalogTestData.Empty)).Dictionary!;
            Assert.Equal(row.Dictionary, read);
            Assert.Equal(1, read.KeySlot.FieldId);
            Assert.Equal(2, read.ValueSlot.FieldId);
        }
    }

    [Fact]
    public void NestedNominalConstructorsAndNullableValueHaveIndependentGoldens() {
        TypeExpr nested = TypeExpr.Dictionary(TypeExpr.Builtin(TypeTag.String),
            TypeExpr.List(TypeExpr.VectorArray(TypeExpr.Dictionary(TypeExpr.Builtin(TypeTag.Int32), TypeExpr.Builtin(TypeTag.UInt64)))));
        byte[] typeGolden = Convert.FromHexString("0A010408040A0102010A");
        Assert.Equal(typeGolden, WriteType(nested));
        Assert.Equal(nested, ReadType(typeGolden));
        SchemaCatalogEntry row = SchemaCatalogEntry.ForDictionary(new(2), new(
            DurableFieldInfo.Reference(1, nested), DurableFieldInfo.Nullable(2, new(1, TypeTag.Int32))));
        byte[] golden = Convert.FromHexString("02010205010F0A010408040A0102010A1202");
        Assert.Equal(golden, SchemaCatalogWireCodec.Write([row], CatalogTestData.Empty));
        Assert.Equal(row.Layout, Assert.Single(SchemaCatalogWireCodec.Read(golden, CatalogTestData.Empty)).Layout);
    }

    [Fact]
    public void TwoExactSlotsReferenceEarlierNodesAndShareIdenticalDependencies() {
        DurableSchema key = Inline("K", TypeTag.Int16), value = Inline("V", TypeTag.Int64);
        SchemaCatalogEntry[] rows = [SchemaCatalogEntry.ForSchema(new(2), key), SchemaCatalogEntry.ForSchema(new(3), value),
            SchemaCatalogEntry.ForDictionary(new(4), new(InlineSlot(1, key), InlineSlot(2, value)))];
        byte[] golden = Convert.FromHexString("0203020202034B000100010107030202035600010001010304050110021003");
        Assert.Equal(golden, SchemaCatalogWireCodec.Write(rows, CatalogTestData.Empty));
        SchemaCatalogEntry[] read = SchemaCatalogWireCodec.Read(golden, CatalogTestData.Empty);
        Assert.Same(read[0].Schema, read[2].Dictionary!.KeySlot.InlineSchema);
        Assert.Same(read[1].Schema, read[2].Dictionary!.ValueSlot.InlineSchema);
        rows = [SchemaCatalogEntry.ForSchema(new(2), key),
            SchemaCatalogEntry.ForDictionary(new(3), new(InlineSlot(1, key), InlineSlot(2, key)))];
        byte[] sharedGolden = Convert.FromHexString("0202020202034B00010001010703050110021002");
        Assert.Equal(sharedGolden, SchemaCatalogWireCodec.Write(rows, CatalogTestData.Empty));
        read = SchemaCatalogWireCodec.Read(sharedGolden, CatalogTestData.Empty);
        Assert.Same(read[0].Schema, read[1].Dictionary!.KeySlot.InlineSchema);
        Assert.Same(read[0].Schema, read[1].Dictionary!.ValueSlot.InlineSchema);
        for (int length = 0; length < golden.Length; length++) {
            byte[] prefix = golden[..length];
            Exception? error = Record.Exception(() => SchemaCatalogWireCodec.Read(prefix, CatalogTestData.Empty));
            Assert.True(error is InvalidDataException or EndOfStreamException, $"Prefix {length}: {error}");
        }
    }

    [Theory]
    [InlineData("02010205000202")] // Codec zero.
    [InlineData("02010205020202")] // Unknown codec.
    [InlineData("0201020581000202")] // Noncanonical codec.
    [InlineData("02010205010002")] // Unknown key slot.
    [InlineData("02010205010200")] // Unknown value slot.
    [InlineData("02010205011102")] // Open key slot.
    [InlineData("02010205010211")] // Open value slot.
    [InlineData("02010205010F010402")] // String requires its dedicated key slot.
    [InlineData("0201020501020F0104")] // String requires its dedicated value slot.
    [InlineData("0201020501100002")] // Null key dependency.
    [InlineData("0201020501021001")] // String is not an inline value dependency.
    [InlineData("0201020501100202")] // Self key dependency.
    [InlineData("0201020501021003")] // Forward value dependency.
    [InlineData("0201020501020200")] // Trailing byte.
    [InlineData("020202050102030305010203")] // Duplicate layout under another ID.
    [InlineData("02010206010202")] // Unknown row kind.
    public void InvalidDictionaryRowsFailClosed(string hex) {
        Assert.Throws<InvalidDataException>(() => SchemaCatalogWireCodec.Read(Convert.FromHexString(hex), CatalogTestData.Empty));
    }

    [Fact]
    public void DictionaryTypesKeepDepthNodeAndClosureLimitsForBothOperands() {
        TypeExpr scalar = TypeExpr.Builtin(TypeTag.Int32);
        TypeExpr type = scalar;
        for (int depth = 1; depth < TypeExpr.MaximumDepth; depth++) { type = TypeExpr.Dictionary(scalar, type); }
        Assert.Equal(type, ReadType(WriteType(type)));
        byte[] deep = [.. Enumerable.Range(0, TypeExpr.MaximumDepth).SelectMany(_ => new byte[] { 10, 1, 2 }), 1, 2];
        Assert.Throws<InvalidDataException>(() => ReadType(deep));
        Assert.Throws<InvalidDataException>(() => ReadType([10, 3, 0, 1, 2]));
        Assert.Throws<InvalidDataException>(() => ReadType([10, 1, 2, 3, 0]));
        Assert.Throws<InvalidDataException>(() => ReadType([11, 1, 2, 1, 2]));
        Assert.Throws<ArgumentException>(() => WriteType(TypeExpr.Dictionary(TypeExpr.Parameter(0), scalar)));
        Assert.Throws<ArgumentException>(() => WriteType(TypeExpr.Dictionary(scalar, TypeExpr.Parameter(0))));
        Assert.Throws<InvalidDataException>(() => ReadType(ExpandedDictionaryType(12)));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void EitherExactSlotConflictRejectsTheBatchBeforeAppendAndIdAllocation(bool keyConflict) {
        string path = Path.Combine(Path.GetTempPath(), $"dictionary-catalog-{Guid.NewGuid():N}.rbf");
        DurableSchema key = Inline("K", TypeTag.Int32), value = Inline("V", TypeTag.Int32);
        ObjectLayout layout = Dictionary(key, value);
        RepresentationId id;
        try {
            using (IRbfFile file = RbfFile.CreateNew(path)) {
                SchemaStore schemas = new(file);
                RepresentationId[] ids = schemas.RegisterRepresentations([layout, layout]);
                Assert.Equal(new uint[] { 4, 4 }, ids.Select(item => item.Value));
                id = ids[0];
                Assert.Equal(2, schemas.Count);
                long tail = file.TailOffset;
                Assert.Equal(ids, schemas.RegisterRepresentations([layout, layout]));
                Assert.Equal(tail, file.TailOffset);
                ObjectLayout bad = keyConflict ? Dictionary(Inline("K", TypeTag.Byte), value) : Dictionary(key, Inline("V", TypeTag.Byte));
                ObjectLayout fresh = ObjectLayout.ForDictionary(new(new(1, TypeTag.Boolean), new(2, TypeTag.Int64)));
                Assert.Throws<SchemaConflictException>(() => schemas.RegisterRepresentations([fresh, bad]));
                Assert.Equal(tail, file.TailOffset);
                Assert.False(schemas.IsFaulted);
                Assert.Equal(5U, schemas.RegisterRepresentations([fresh])[0].Value);
            }
            using IRbfFile fileRead = RbfFile.OpenExisting(path);
            SchemaStore cold = new(fileRead, readOnly: true);
            Assert.Equal(layout, cold.GetRepresentation(id));
            Assert.Same(cold.GetRequired("K", 1), cold.GetRepresentation(id).Dictionary!.KeySlot.InlineSchema);
            Assert.Same(cold.GetRequired("V", 1), cold.GetRepresentation(id).Dictionary!.ValueSlot.InlineSchema);
        }
        finally { File.Delete(path); }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void EitherReferenceSlotRejectsInlineKindConflictInEitherOrder(bool keyReference) {
        DurableSchema inline = Inline("P", TypeTag.Int32);
        DurableFieldInfo reference = DurableFieldInfo.Reference(keyReference ? 1 : 2, inline.Type);
        DictionaryLayout dictionary = new(keyReference ? reference : new(1, TypeTag.Int32),
            keyReference ? new(2, TypeTag.Int32) : reference);
        var inlineFirst = CatalogTestData.Index([SchemaCatalogEntry.ForSchema(new(2), inline)]);
        SchemaCatalogEntry row = SchemaCatalogEntry.ForDictionary(new(3), dictionary);
        Assert.Throws<ArgumentException>(() => SchemaCatalogWireCodec.Write([row], inlineFirst));
        byte[] invalid = Convert.FromHexString(keyReference ? "02010305010F0203500002" : "0201030501020F02035000");
        Assert.Throws<InvalidDataException>(() => SchemaCatalogWireCodec.Read(invalid, inlineFirst));
        var dictionaryFirst = CatalogTestData.Index([SchemaCatalogEntry.ForDictionary(new(2), dictionary)]);
        Assert.Throws<ArgumentException>(() => SchemaCatalogWireCodec.Write([SchemaCatalogEntry.ForSchema(new(3), inline)], dictionaryFirst));
        Assert.Throws<InvalidDataException>(() => SchemaCatalogWireCodec.Read(Convert.FromHexString("02010302020350000100010102"), dictionaryFirst));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void BothNominalOperandsValidateArityWithoutOwningReferenceTargetVersions(bool keyOperand) {
        DurableSchema point = Inline("P", TypeTag.Int32);
        TypeExpr scalar = TypeExpr.Builtin(TypeTag.Int32);
        TypeExpr nominal = keyOperand ? TypeExpr.Dictionary(point.Type, scalar) : TypeExpr.Dictionary(scalar, point.Type);
        DurableSchema owner = new("O", 1, DurableFieldInfo.Reference(1, nominal));
        Assert.Equal(owner, SchemaCatalogTestData.Read(SchemaCatalogTestData.Write([owner, point]))[new("O", 1)]);
        TypeExpr wrongArity = TypeExpr.Named("P", scalar);
        TypeExpr invalid = keyOperand ? TypeExpr.Dictionary(wrongArity, scalar) : TypeExpr.Dictionary(scalar, wrongArity);
        SchemaCatalogEntry[] rows = [SchemaCatalogEntry.ForSchema(new(2), point),
            SchemaCatalogEntry.ForDictionary(new(3), new(new(1, TypeTag.Int32), DurableFieldInfo.Reference(2, TypeExpr.List(invalid))))];
        Assert.Throws<ArgumentException>(() => SchemaCatalogWireCodec.Write(rows, CatalogTestData.Empty));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void BothExactDependenciesRejectClassAndContainerNodes(bool keyDependency) {
        byte[] row = Convert.FromHexString(keyDependency ? "0201030501100202" : "0201030501021002");
        var classIndex = CatalogTestData.Index([SchemaCatalogEntry.ForSchema(new(2), new("C", 1))]);
        var listIndex = CatalogTestData.Index([SchemaCatalogEntry.ForList(new(2), new(new(1, TypeTag.Int32)))]);
        var dictionaryIndex = CatalogTestData.Index([SchemaCatalogEntry.ForDictionary(new(2), new(new(1, TypeTag.Int32), new(2, TypeTag.Int32)))]);
        Assert.Throws<InvalidDataException>(() => SchemaCatalogWireCodec.Read(row, classIndex));
        Assert.Throws<InvalidDataException>(() => SchemaCatalogWireCodec.Read(row, listIndex));
        Assert.Throws<InvalidDataException>(() => SchemaCatalogWireCodec.Read(row, dictionaryIndex));
    }

    [Fact]
    public void BothSlotsCanShareTheMaximumInlineDependencyDepth() {
        var chain = new DurableSchema[256];
        for (int i = 0; i < chain.Length; i++) {
            chain[i] = new($"I{i:D3}", 1, SchemaKind.InlineValue,
                i == 0 ? new(1, TypeTag.Int32) : InlineSlot(1, chain[i - 1]));
        }
        var registered = CatalogTestData.Index(SchemaCatalogWireCodec.Read(SchemaCatalogTestData.Write(chain), CatalogTestData.Empty));
        SchemaCatalogEntry row = SchemaCatalogEntry.ForDictionary(new(258), new(InlineSlot(1, chain[^1]), InlineSlot(2, chain[^1])));
        byte[] golden = Convert.FromHexString("020182020501108102108102");
        Assert.Equal(golden, SchemaCatalogWireCodec.Write([row], registered));
        DictionaryLayout read = Assert.Single(SchemaCatalogWireCodec.Read(golden, registered)).Dictionary!;
        Assert.Same(registered[new(257)].Schema, read.KeySlot.InlineSchema);
        Assert.Same(read.KeySlot.InlineSchema, read.ValueSlot.InlineSchema);
    }

    private static DurableSchema Inline(string id, TypeTag tag) => new(id, 1, SchemaKind.InlineValue, new DurableFieldInfo(1, tag));
    private static DurableFieldInfo InlineSlot(int fieldId, DurableSchema schema) => new(fieldId, TypeTag.InlineValue, inlineSchema: schema);
    private static ObjectLayout Dictionary(DurableSchema key, DurableSchema value) => ObjectLayout.ForDictionary(new(InlineSlot(1, key), InlineSlot(2, value)));
    private static byte[] ExpandedDictionaryType(int depth) => depth == 0 ? [1, 2]
        : [10, .. ExpandedDictionaryType(depth - 1), .. ExpandedDictionaryType(depth - 1)];
    private static byte[] WriteType(TypeExpr type) {
        ArrayBufferWriter<byte> bytes = new();
        BinaryPayloadWriter writer = new(bytes);
        TypeExprWireCodec.Write(ref writer, type);
        return bytes.WrittenSpan.ToArray();
    }
    private static TypeExpr ReadType(byte[] bytes) {
        BinaryPayloadReader reader = new(bytes);
        TypeExpr type = TypeExprWireCodec.Read(ref reader);
        reader.EnsureFullyConsumed();
        return type;
    }
}
