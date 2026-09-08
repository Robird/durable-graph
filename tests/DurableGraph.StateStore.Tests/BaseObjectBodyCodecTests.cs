using Atelia.DurableGraph.StateStore.Serialization;

namespace Atelia.DurableGraph.StateStore.Tests;

public sealed class BaseObjectBodyCodecTests {
    [Fact]
    public void TypedBaseEnvelopeHelpersRemainInternalImplementationDetails() {
        Type[] helperTypes = [
            typeof(BaseObjectBodyCodec),
            typeof(EncodedBaseObjectBody),
            typeof(DecodedBaseObjectBody),
        ];
        Assert.All(helperTypes, static type => Assert.False(type.IsPublic));
        Assert.Null(typeof(TypedObjectVersionReader).Assembly.GetType("Atelia.DurableGraph.StateStore.BaseObjectPayloadCodec"));
        Assert.Null(typeof(TypedObjectVersionReader).Assembly.GetType("Atelia.DurableGraph.StateStore.BaseObjectPayload"));
    }

    [Fact]
    public void HeadersHaveIndependentGoldenBytesAndPreserveOpaqueBodies() {
        EncodedBaseObjectBody text = BaseObjectBodyCodec.EncodeString(new([0xFF, 0x80]));
        Assert.Equal(new byte[] { 2, 1, 0xFF, 0x80 }, text.Body.ToArray());
        DecodedBaseObjectBody decodedText = BaseObjectBodyCodec.Decode(text.Body);
        Assert.Equal(ObjectStateKind.String, decodedText.Kind);
        Assert.Null(decodedText.SchemaKey);
        Assert.Equal(new byte[] { 0xFF, 0x80 }, decodedText.Body.ToArray());

        DurableSchema schema = new("A", 128, new DurableFieldInfo(1, TypeTag.Int32));
        EncodedBaseObjectBody durable = BaseObjectBodyCodec.EncodeDurable(schema, new([0xFF, 0]));
        // String "A": UTF8 header 3, ASCII 65; positive UInt32 128: 80 01.
        Assert.Equal(new byte[] { 2, 2, 2, 3, 65, 0, 0x80, 1, 0xFF, 0 }, durable.Body.ToArray());
        DecodedBaseObjectBody decoded = BaseObjectBodyCodec.Decode(durable.Body);
        Assert.Equal(ObjectStateKind.Durable, decoded.Kind);
        Assert.Equal(new SchemaKey("A", 128), decoded.SchemaKey);
        Assert.Equal(new byte[] { 0xFF, 0 }, decoded.Body.ToArray());
    }

    [Fact]
    public void EmptyBodyIsValidAtTheHeaderBoundaryAndDecodeOwnsItsBody() {
        Assert.Empty(BaseObjectBodyCodec.Decode(new byte[] { 1, 1 }).Body.ToArray());
        byte[] wire = [1, 2, 3, 65, 1, 7, 8];
        DecodedBaseObjectBody decoded = BaseObjectBodyCodec.Decode(wire);
        wire.AsSpan().Fill(0);
        Assert.Equal(new SchemaKey("A", 1), decoded.SchemaKey);
        Assert.Equal(new byte[] { 7, 8 }, decoded.Body.ToArray());
    }

    [Theory]
    [InlineData(new byte[] { 0, 1 })]
    [InlineData(new byte[] { 3, 1 })]
    [InlineData(new byte[] { 1, 0 })]
    [InlineData(new byte[] { 1, 3 })]
    [InlineData(new byte[] { 1, 2, 0, 1 })] // Empty SchemaId.
    [InlineData(new byte[] { 1, 2, 3, 32, 1 })] // Whitespace SchemaId.
    [InlineData(new byte[] { 1, 2, 3, 65, 0 })] // Zero version.
    [InlineData(new byte[] { 1, 2, 3, 65, 0x81, 0 })] // Noncanonical version.
    [InlineData(new byte[] { 1, 2, 3, 65, 0x80, 0x80, 0x80, 0x80, 8 })] // > Int32.MaxValue.
    public void UnknownAndNoncanonicalHeadersAreRejected(byte[] wire) {
        Assert.Throws<InvalidDataException>(() => BaseObjectBodyCodec.Decode(wire));
    }

    [Theory]
    [InlineData(new byte[] { })]
    [InlineData(new byte[] { 1 })]
    [InlineData(new byte[] { 1, 2 })]
    [InlineData(new byte[] { 1, 2, 3, 65 })]
    [InlineData(new byte[] { 1, 2, 3, 65, 0x80 })]
    public void TruncatedHeadersAreRejected(byte[] wire) {
        Assert.Throws<EndOfStreamException>(() => BaseObjectBodyCodec.Decode(wire));
    }

    [Fact]
    public void NullInputsAreRejected() {
        Assert.Throws<ArgumentNullException>(() => BaseObjectBodyCodec.EncodeString(null!));
        Assert.Throws<ArgumentNullException>(() => BaseObjectBodyCodec.EncodeDurable(null!, new([])));
        Assert.Throws<ArgumentNullException>(() => BaseObjectBodyCodec.EncodeDurable(new("A", 1), null!));
    }
}
