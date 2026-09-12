using Atelia.Data;
using Atelia.DurableGraph.Storage;

namespace Atelia.DurableGraph.Persistence.Tests;

public sealed class GraphEnvelopeCodecTests {
    private static readonly FrameAddress Address = new(1, SizedPtr.Create(4, 24));

    [Fact]
    public void GoldenBytesFreezeMagicVersionCanonicalAddressAndRoot() {
        // DGH1, format 1, file 1, interleaved SizedPtr(4,24)=0x106, root 7.
        byte[] golden = Convert.FromHexString("444748310101860207");
        Assert.Equal(golden, GraphEnvelopeCodec.Encode(Address, new ObjectId(7)));
        Assert.Equal((Address, new ObjectId(7)), GraphEnvelopeCodec.Decode(golden));
        for (int length = 0; length < golden.Length; length++) {
            byte[] truncated = golden[..length];
            Assert.Throws<EndOfStreamException>(() => GraphEnvelopeCodec.Decode(truncated));
        }
        Assert.Equal(GraphFrameKind.Event, GraphEnvelopeCodec.DecodeKind(1));
        Assert.Equal(GraphFrameKind.State, GraphEnvelopeCodec.DecodeKind(2));
    }

    [Theory]
    [InlineData("004748310101860207")] // Magic.
    [InlineData("444748310201860207")] // Version.
    [InlineData("444748310100860207")] // Zero file.
    [InlineData("4447483101010007")] // Empty ticket.
    [InlineData("444748310101840207")] // Short frame.
    [InlineData("444748310101820207")] // Offset before header fence.
    [InlineData("444748310101860200")] // Null root.
    [InlineData("44474831010186020700")] // Trailing byte.
    [InlineData("44474831018100860207")] // Noncanonical file.
    [InlineData("44474831010186820007")] // Noncanonical ticket.
    [InlineData("44474831010186028700")] // Noncanonical root.
    public void MalformedEnvelopeFailsClosed(string hex) =>
        Assert.Throws<InvalidDataException>(() => GraphEnvelopeCodec.Decode(Convert.FromHexString(hex)));

    [Fact]
    public void InvalidKindsAndWriteValuesFailClosed() {
        Assert.Throws<InvalidDataException>(() => GraphEnvelopeCodec.DecodeKind(0));
        Assert.Throws<InvalidDataException>(() => GraphEnvelopeCodec.DecodeKind(3));
        Assert.Throws<InvalidDataException>(() => GraphEnvelopeCodec.DecodeKind(uint.MaxValue));
        Assert.Throws<InvalidDataException>(() => GraphEnvelopeCodec.Encode(default, new ObjectId(1)));
        Assert.Throws<InvalidDataException>(() => GraphEnvelopeCodec.Encode(Address, default));
    }
}
