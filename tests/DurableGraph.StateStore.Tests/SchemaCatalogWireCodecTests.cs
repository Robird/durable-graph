namespace Atelia.DurableGraph.StateStore.Tests;

public sealed class SchemaCatalogWireCodecTests {
    [Fact]
    public void IndependentGoldenUsesEarlierBaseIdAndOrderedFields() {
        DurableSchema ancestor = new("Z", 2, new DurableFieldInfo(3, TypeTag.String));
        DurableSchema derived = new("A", 1, [new(8, TypeTag.UInt32), new(1, TypeTag.Boolean)], ancestor);
        byte[] golden = Convert.FromHexString("0102020102035A00020001030403010203410001020201010809");
        Assert.Equal(0x31424353U, SchemaCatalogWireCodec.RbfTag);
        Assert.Equal(golden, SchemaCatalogTestData.Write([ancestor, derived]));
        var result = SchemaCatalogTestData.Read(golden);
        Assert.Equal(derived, result[new("A", 1)]);
        Assert.Same(result[new("Z", 2)], result[new("A", 1)].BaseSchema);
    }

    [Fact]
    public void AllFourteenCodesHaveFixedIndependentGoldenBytes() {
        TypeTag[] tags = [TypeTag.Boolean, TypeTag.Int32, TypeTag.Int64, TypeTag.String,
            TypeTag.Byte, TypeTag.SByte, TypeTag.Int16, TypeTag.UInt16, TypeTag.UInt32,
            TypeTag.UInt64, TypeTag.Char, TypeTag.Half, TypeTag.Single, TypeTag.Double];
        DurableSchema schema = new("T", 1, tags.Select((tag, i) => new DurableFieldInfo(i + 1, tag)).ToArray());
        byte[] golden = Convert.FromHexString("010102010203540001000E0101020203030404050506060707080809090A0A0B0B0C0C0D0D0E0E");
        Assert.Equal(golden, SchemaCatalogTestData.Write([schema]));
        Assert.Equal(schema, SchemaCatalogTestData.Read(golden)[new("T", 1)]);
    }

    [Theory]
    [InlineData("0201020102034100010000")] // Unknown batch version.
    [InlineData("0100")] // Empty physical batch.
    [InlineData("01FFFFFFFF0F")] // Impossible row count.
    [InlineData("01010201020341000100FFFFFFFF0F")] // Impossible field count.
    [InlineData("01010201020341000100010111")] // Unknown slot.
    [InlineData("01010201020341000100010100")] // Invalid slot.
    [InlineData("010102010203410001000202020101")] // Descending fields.
    [InlineData("010102010203410001000201010102")] // Duplicate fields.
    [InlineData("01010201020341000100010001")] // Field zero.
    [InlineData("0101020102034100010001808080800801")] // Field exceeds Int32.
    [InlineData("0101020102034100000000")] // Schema version zero.
    [InlineData("010102010203410080808080080000")] // Schema version exceeds Int32.
    [InlineData("010102010203410001800000")] // Noncanonical base ID zero.
    [InlineData("010102010203410001000000")] // Trailing bytes.
    [InlineData("0101020102034100010300")] // Future base.
    [InlineData("0101020102034100010200")] // Self base.
    [InlineData("0101020102034100010100")] // String cannot be a base.
    [InlineData("01010201020000010000")] // Empty identity.
    [InlineData("010102010102010000")] // Schema nominal type cannot be builtin.
    [InlineData("0101020002034100010000")] // Unknown row kind.
    [InlineData("0101030102034100010000")] // Initial ID gap.
    [InlineData("0101020102034100010000030102034200010000")] // Uncounted row.
    public void MalformedCanonicalDefinitionsAreRejected(string hex) {
        Assert.Throws<InvalidDataException>(() => SchemaCatalogWireCodec.Read(Convert.FromHexString(hex), SchemaCatalogTestData.Empty));
    }

    [Fact]
    public void EveryTruncatedPrefixIsRejected() {
        byte[] valid = Convert.FromHexString("01010201020341000100010102");
        Assert.Single(SchemaCatalogWireCodec.Read(valid, SchemaCatalogTestData.Empty));
        for (int length = 0; length < valid.Length; length++) {
            byte[] prefix = valid[..length];
            Exception? error = Record.Exception(() => SchemaCatalogWireCodec.Read(prefix, SchemaCatalogTestData.Empty));
            Assert.True(error is InvalidDataException or EndOfStreamException, $"Prefix {length}: {error}");
        }
    }

    [Fact]
    public void ExistingExactAncestorResolvesAndRepeatedKeyCannotAliasAnotherId() {
        DurableSchema ancestor = new("Base", 3, new DurableFieldInfo(1, TypeTag.Int64));
        var registered = SchemaCatalogTestData.Registered(ancestor);
        DurableSchema derived = new("Leaf", 1, [], ancestor);
        var result = SchemaCatalogTestData.Read(SchemaCatalogTestData.Write([derived], registered), registered);
        Assert.Same(ancestor, result[new("Leaf", 1)].BaseSchema);
        // ID 3 has the same key as existing ID 2, but a different body.
        byte[] conflict = Convert.FromHexString("0101030102094261736500030000");
        Assert.Throws<InvalidDataException>(() => SchemaCatalogWireCodec.Read(conflict, registered));
        byte[] identicalAlias = Convert.FromHexString("01010301020942617365000300010103");
        Assert.Throws<InvalidDataException>(() => SchemaCatalogWireCodec.Read(identicalAlias, registered));
        Assert.Single(registered);
    }

    [Fact]
    public void MaximumDepthIncludesPreviouslyRegisteredDependencies() {
        DurableSchema[] valid = Chain(256);
        var registered = SchemaCatalogWireCodec.Read(SchemaCatalogTestData.Write(valid), SchemaCatalogTestData.Empty)
            .ToDictionary(entry => entry.Id);
        Assert.Equal(256, registered.Count);
        // ID 258, Type256 v1, base ID 257 gives depth 257.
        byte[] beyondLimit = Convert.FromHexString("0101820201020F547970653235360001810200");
        Assert.Throws<InvalidDataException>(() => SchemaCatalogWireCodec.Read(beyondLimit, registered));
        // Two different versions of family A in one exact ancestor lineage are invalid.
        byte[] repeatedIdentity = Convert.FromHexString("0102020102034100010000030102034100020200");
        Assert.Throws<InvalidDataException>(() => SchemaCatalogWireCodec.Read(repeatedIdentity, SchemaCatalogTestData.Empty));
    }

    [Fact]
    public void DefinitionVersionIsCanonicalAndSchemaKeyRemainsAnInMemoryConstraint() {
        DurableSchema schema = new("A", 128);
        byte[] golden = Convert.FromHexString("010102010203410080010000");
        Assert.Equal(golden, SchemaCatalogTestData.Write([schema]));
        Assert.Equal(schema, SchemaCatalogTestData.Read(golden)[new("A", 128)]);
        Assert.Throws<ArgumentException>(() => new SchemaKey(" ", 1));
        Assert.Throws<ArgumentOutOfRangeException>(() => new SchemaKey("A", 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => new SchemaKey("A", -1));
    }

    internal static DurableSchema[] Chain(int depth) {
        var result = new DurableSchema[depth];
        DurableSchema? ancestor = null;
        for (int index = 0; index < depth; index++) {
            result[index] = ancestor = new DurableSchema($"Type{index:D3}", 1, [], ancestor);
        }
        return result;
    }
}
