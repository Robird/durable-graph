using Atelia.DurableGraph.StateStore.Serialization;

namespace Atelia.DurableGraph.StateStore.Serialization.Tests;

public sealed class PreparedBaseTests {
    [Fact]
    public void OwnsItsInputSliceAndDoesNotExposeWritableStorage() {
        byte[] source = [0xFF, 0x02, 0x16, 0xFF];
        PreparedBase prepared = new(source.AsSpan(1, 2));
        Array.Clear(source);
        byte[] copy = prepared.Payload.ToArray();
        Array.Clear(copy);

        Assert.Equal<byte>([0x02, 0x16], prepared.Payload.ToArray());
        Assert.Equal(typeof(ReadOnlySpan<byte>), typeof(PreparedBase).GetProperty(nameof(PreparedBase.Payload))!.PropertyType);
        Assert.Null(typeof(PreparedBase).GetProperty(nameof(PreparedBase.Payload))!.SetMethod);
        Assert.Empty(typeof(PreparedBase).GetFields());
    }

    [Fact]
    public void EmptyBodyIsValid() {
        PreparedBase prepared = new([]);
        Assert.True(prepared.Payload.IsEmpty);
    }

    [Theory]
    [InlineData("", "00")]
    [InlineData("A", "0341")]
    [InlineData("\u00E9", "02E900")]
    [InlineData("\u4E2D", "022D4E")]
    public void StringPreparationUsesCanonicalContentAndOwnedReusableBytes(string value, string expectedHex) {
        PreparedBase prepared = StringPayloadCodec.PrepareBase(value);
        byte[] copy = prepared.Payload.ToArray();
        Array.Clear(copy);
        _ = StringPayloadCodec.PrepareBase("later preparation");

        Assert.Equal(Convert.FromHexString(expectedHex), prepared.Payload.ToArray());
        BinaryPayloadReader reader = new(prepared.Payload);
        string restored = reader.ReadString();
        reader.EnsureFullyConsumed();
        Assert.Equal(value, restored);
        if (value.Length == 0) { Assert.Same(string.Empty, restored); }
    }

    [Fact]
    public void StringPreparationPreservesUnpairedSurrogatesAndRejectsNull() {
        PreparedBase prepared = StringPayloadCodec.PrepareBase(new string('\uD800', 1));
        Assert.Equal<byte>([0x02, 0x00, 0xD8], prepared.Payload.ToArray());
        Assert.Throws<ArgumentNullException>(() => StringPayloadCodec.PrepareBase(null!));
    }
}
