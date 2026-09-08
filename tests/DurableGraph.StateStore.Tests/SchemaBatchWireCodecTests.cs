using System.Buffers;
using Atelia.DurableGraph.StateStore.Serialization;

namespace Atelia.DurableGraph.StateStore.Tests;

public sealed class SchemaBatchWireCodecTests {
    private static readonly Dictionary<SchemaKey, DurableSchema> Empty = new();

    [Fact]
    public void IndependentGoldenOrdersRowsAndFieldsAndAllowsForwardBaseReference() {
        DurableSchema ancestor = new("Z", 2, new DurableFieldInfo(3, TypeTag.String));
        DurableSchema derived = new("A", 1, [new(8, TypeTag.UInt32), new(1, TypeTag.Boolean)], ancestor);
        byte[] golden = Convert.FromHexString("03020203410001010102035A0002020101080902035A00020100010304");
        Assert.Equal(golden, SchemaBatchWireCodec.Write([ancestor, derived]));
        var result = SchemaBatchWireCodec.Read(golden, Empty);
        Assert.Equal(derived, result[new("A", 1)]);
        Assert.Equal(ancestor, result[new("Z", 2)]);
        Assert.Same(result[new("Z", 2)], result[new("A", 1)].BaseSchema);
        byte[] legacy = Convert.FromHexString("02020341010101035A020201010809035A020100010304");
        Assert.Equal(derived, SchemaBatchWireCodec.Read(legacy, Empty)[new("A", 1)]);
    }

    [Fact]
    public void AllFourteenCodesHaveFixedIndependentGoldenBytes() {
        TypeTag[] tags = [TypeTag.Boolean, TypeTag.Int32, TypeTag.Int64, TypeTag.String,
            TypeTag.Byte, TypeTag.SByte, TypeTag.Int16, TypeTag.UInt16, TypeTag.UInt32,
            TypeTag.UInt64, TypeTag.Char, TypeTag.Half, TypeTag.Single, TypeTag.Double];
        DurableSchema schema = new("T", 1, tags.Select((tag, i) => new DurableFieldInfo(i + 1, tag)).ToArray());
        byte[] golden = Convert.FromHexString("0301020354000101000E0101020203030404050506060707080809090A0A0B0B0C0C0D0D0E0E");
        Assert.Equal(golden, SchemaBatchWireCodec.Write([schema]));
        Assert.Equal(schema, SchemaBatchWireCodec.Read(golden, Empty)[new("T", 1)]);
    }

    [Theory]
    [InlineData("04010341010000")] // Unknown version.
    [InlineData("0100")] // Empty physical batch.
    [InlineData("01FFFFFFFF0F")] // Impossible count.
    [InlineData("010103410100FFFFFFFF0F")] // Impossible field count.
    [InlineData("01010341010001010F")] // Unknown type.
    [InlineData("010103410100010100")] // Invalid type.
    [InlineData("0101034102000202020101")] // Descending fields.
    [InlineData("0101034102000201010102")] // Duplicate fields.
    [InlineData("010103410100010001")] // Field zero.
    [InlineData("01010341000000")] // Version zero.
    [InlineData("0101034101800000")] // Noncanonical flag/varint sequence.
    [InlineData("01010341010200")] // Illegal base flag.
    [InlineData("0101034101000000")] // Trailing bytes.
    [InlineData("010203420100000341010000")] // Descending identities.
    [InlineData("010203410100000341010000")] // Duplicate key.
    [InlineData("010103410101035A0100")] // Missing dependency.
    [InlineData("01010341010103410100")] // Self cycle.
    [InlineData("010203410101034201000342010103410100")] // Cycle.
    [InlineData("010103410101034102000341020000")] // Trailing definition not counted.
    [InlineData("010100010000")] // Empty identity.
    public void MalformedCanonicalDefinitionsAreRejected(string hex) {
        Assert.ThrowsAny<Exception>(() => SchemaBatchWireCodec.Read(Convert.FromHexString(hex), Empty));
    }

    [Fact]
    public void EveryTruncatedPrefixIsRejected() {
        byte[] valid = SchemaBatchWireCodec.Write([new DurableSchema("A", 1, new DurableFieldInfo(1, TypeTag.Int32))]);
        for (int length = 0; length < valid.Length; length++) {
            byte[] prefix = valid[..length];
            Assert.ThrowsAny<Exception>(() => SchemaBatchWireCodec.Read(prefix, Empty));
        }
    }

    [Fact]
    public void ExistingExactAncestorResolvesAndConflictingRepeatedDefinitionFails() {
        DurableSchema ancestor = new("Base", 3, new DurableFieldInfo(1, TypeTag.Int64));
        var registered = new Dictionary<SchemaKey, DurableSchema> { [new("Base", 3)] = ancestor };
        DurableSchema derived = new("Leaf", 1, [], ancestor);
        var result = SchemaBatchWireCodec.Read(SchemaBatchWireCodec.Write([derived]), registered);
        Assert.Same(ancestor, result[new("Leaf", 1)].BaseSchema);
        Assert.Throws<InvalidDataException>(() => SchemaBatchWireCodec.Read(
            SchemaBatchWireCodec.Write([new DurableSchema("Base", 3)]), registered));
        Assert.Single(registered);
    }

    [Fact]
    public void MaximumDepthAndRepeatedIdentityAcrossVersionsAreValidatedOnDisk() {
        var valid = Chain(256);
        var result = SchemaBatchWireCodec.Read(SchemaBatchWireCodec.Write(valid), Empty);
        Assert.Equal(256, result.Count);
        Assert.Throws<InvalidDataException>(() => SchemaBatchWireCodec.Read(SchemaBatchWireCodec.Write(Chain(257)), Empty));
        byte[] repeatedIdentity = Convert.FromHexString("010203410101034102000341020000");
        Assert.Throws<InvalidDataException>(() => SchemaBatchWireCodec.Read(repeatedIdentity, Empty));
    }

    [Fact]
    public void KeysUseCanonicalStringAndPositiveUnsignedVersion() {
        var buffer = new ArrayBufferWriter<byte>();
        var writer = new BinaryPayloadWriter(buffer);
        SchemaKeyWireCodec.Write(ref writer, new SchemaKey("A", 128));
        Assert.Equal(Convert.FromHexString("020341008001"), buffer.WrittenSpan.ToArray());
        var reader = new BinaryPayloadReader(buffer.WrittenSpan);
        Assert.Equal(new SchemaKey("A", 128), SchemaKeyWireCodec.Read(ref reader));
        reader.EnsureFullyConsumed();
        Assert.Throws<ArgumentException>(() => new SchemaKey(" ", 1));
        Assert.Throws<ArgumentOutOfRangeException>(() => new SchemaKey("A", 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => new SchemaKey("A", -1));
        Assert.ThrowsAny<ArgumentException>(() => WriteDefaultKey());
    }

    private static void WriteDefaultKey() {
        var writer = new BinaryPayloadWriter(new ArrayBufferWriter<byte>());
        SchemaKeyWireCodec.Write(ref writer, default);
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
