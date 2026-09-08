using Atelia.DurableGraph.StateStore.Serialization;
using Atelia.Rbf;

namespace Atelia.DurableGraph.StateStore.Tests;

public sealed class BaseObjectBodyCodecTests : IDisposable {
    private readonly string _path = Path.Combine(Path.GetTempPath(), $"base-object-codec-{Guid.NewGuid():N}.rbf");

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
        Assert.Equal(new byte[] { 4, 1, 0xFF, 0x80 }, text.Body.ToArray());
        DecodedBaseObjectBody decodedText = BaseObjectBodyCodec.Decode(text.Body);
        Assert.Equal(ObjectStateKind.String, decodedText.Kind);
        Assert.Equal(ObjectLayout.String, decodedText.Layout);
        Assert.Equal(RepresentationId.String, decodedText.RepresentationId);
        Assert.Equal(new byte[] { 0xFF, 0x80 }, decodedText.Body.ToArray());

        DurableSchema schema = new("A", 128, new DurableFieldInfo(1, TypeTag.Int32));
        using IRbfFile file = RbfFile.CreateNew(_path);
        SchemaStore schemas = new(file);
        RepresentationId id = schemas.RegisterRepresentations([ObjectLayout.ForDurable(schema)])[0];
        Assert.Equal(new RepresentationId(2), id);
        EncodedBaseObjectBody durable = BaseObjectBodyCodec.Encode(id, new([0xFF, 0]));
        // The schema name and version live in the directory; the Base holds only ID 2.
        Assert.Equal(new byte[] { 4, 2, 0xFF, 0 }, durable.Body.ToArray());
        DecodedBaseObjectBody decoded = BaseObjectBodyCodec.Decode(durable.Body, schemas);
        Assert.Equal(ObjectStateKind.Durable, decoded.Kind);
        Assert.Equal(schema, decoded.Layout.Schema);
        Assert.Equal(id, decoded.RepresentationId);
        Assert.Equal(new byte[] { 0xFF, 0 }, decoded.Body.ToArray());
    }

    [Fact]
    public void EmptyBodyIsValidAtTheHeaderBoundaryAndDecodeOwnsItsBody() {
        Assert.Empty(BaseObjectBodyCodec.Decode(new byte[] { 1, 1 }).Body.ToArray());
        using IRbfFile file = RbfFile.CreateNew(_path);
        SchemaStore schemas = new(file);
        DurableSchema schema = new("A", 1);
        schemas.Register(schema);
        // Retained v1 golden is a read-only compatibility witness, with no representation ID.
        byte[] wire = [1, 2, 3, 65, 1, 7, 8];
        DecodedBaseObjectBody decoded = BaseObjectBodyCodec.Decode(wire, schemas);
        wire.AsSpan().Fill(0);
        Assert.Equal(schema, decoded.Layout.Schema);
        Assert.Null(decoded.RepresentationId);
        Assert.Equal(new byte[] { 7, 8 }, decoded.Body.ToArray());
    }

    [Theory]
    [InlineData(new byte[] { 0, 1 })]
    [InlineData(new byte[] { 5, 1 })]
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
        Assert.Throws<ArgumentNullException>(() => BaseObjectBodyCodec.Encode(RepresentationId.String, null!));
        Assert.Throws<ArgumentOutOfRangeException>(() => BaseObjectBodyCodec.Encode(default, new([])));
    }

    public void Dispose() => File.Delete(_path);
}
