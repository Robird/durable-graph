using System.Buffers;
using Atelia.Data;

namespace Atelia.DurableGraph.StateStore.Storage.Tests;

public sealed class ObjectVersionPayloadSizeTests {
    [Fact]
    public void Handwritten_Base_and_Delta_payloads_match_measured_costs() {
        FrameAddress prior = new(1, SizedPtr.Create(4, 4));
        FileScope scope = new(2);
        // Max ObjectId occupies five bytes but is outside both payload costs.
        byte[] baseGolden = [3, 1, 0, 1, 0xff, 0xff, 0xff, 0xff, 0x0f, 1, 2, 0xab, 0xcd, 0];
        StateRevision baseRevision = StateRevision.CreateObjectHeadMapBase(null,
            [ObjectVersionRecord.CreateBase(uint.MaxValue, [0xab, 0xcd])], []);
        Assert.Equal(baseGolden, Encode(baseRevision, scope));
        Assert.Equal(4, ReadPayloadBytes(baseGolden, scope));
        Assert.Equal(4L, ObjectVersionPayloadSize.GetBasePayloadBytes(2));

        byte[] deltaGolden = [3, 2, 1, 1, 5, 1, 0xff, 0xff, 0xff, 0xff, 0x0f, 2, 1, 5, 1, 0xab, 0];
        StateRevision deltaRevision = StateRevision.CreateObjectHeadMapDelta(prior,
            [ObjectVersionRecord.CreateDelta(uint.MaxValue, prior, [0xab])], []);
        Assert.Equal(deltaGolden, Encode(deltaRevision, scope));
        Assert.Equal(5, ReadPayloadBytes(deltaGolden, scope));
        Assert.Equal(9L, ObjectVersionPayloadSize.EstimateDeltaPayloadBytesUpperBound(1, prior));
    }

    [Theory]
    [InlineData(0, 2)]
    [InlineData(127, 129)]
    [InlineData(128, 131)]
    [InlineData(16383, 16386)]
    [InlineData(16384, 16388)]
    public void Base_length_prefix_boundaries_match_actual_wire(int bodyLength, int expectedBytes) {
        byte[] body = new byte[bodyLength];
        StateRevision revision = StateRevision.CreateObjectHeadMapBase(null,
            [ObjectVersionRecord.CreateBase(1, body)], []);
        FileScope scope = new(1);
        Assert.Equal(expectedBytes, ReadPayloadBytes(Encode(revision, scope), scope));
        Assert.Equal((long)expectedBytes, ObjectVersionPayloadSize.GetBasePayloadBytes(bodyLength));
    }

    public static TheoryData<uint, byte[]> Distances => new() {
        { 0, [0] },
        { 127, [0x7f] },
        { 128, [0x80, 1] },
        { 16383, [0xff, 0x7f] },
        { 16384, [0x80, 0x80, 1] },
        { 2097151, [0xff, 0xff, 0x7f] },
        { 2097152, [0x80, 0x80, 0x80, 1] },
        { 268435455, [0xff, 0xff, 0xff, 0x7f] },
        { 268435456, [0x80, 0x80, 0x80, 0x80, 1] },
        { uint.MaxValue - 1, [0xfe, 0xff, 0xff, 0xff, 0x0f] },
    };

    [Theory]
    [MemberData(nameof(Distances))]
    public void Delta_estimate_excess_matches_every_distance_width(uint distance, byte[] distanceBytes) {
        FrameAddress prior = new(1, SizedPtr.Create(4, 4));
        FileScope scope = new(checked(distance + 1));
        foreach (int bodyLength in new[] { 127, 128 }) {
            byte[] body = new byte[bodyLength];
            byte[] lengthBytes = bodyLength == 127 ? [0x7f] : [0x80, 1];
            byte[] payload = [2, .. distanceBytes, 5, .. lengthBytes, .. body];
            byte[] golden = [3, 2, 1, .. distanceBytes, 5, 1, 1, .. payload, 0];
            StateRevision revision = StateRevision.CreateObjectHeadMapDelta(prior,
                [ObjectVersionRecord.CreateDelta(1, prior, body)], []);
            Assert.Equal(golden, Encode(revision, scope));
            Assert.Equal(payload.Length, ReadPayloadBytes(golden, scope));
            long estimate = ObjectVersionPayloadSize.EstimateDeltaPayloadBytesUpperBound(bodyLength, prior);
            Assert.Equal(5 - distanceBytes.Length, estimate - payload.Length);
            Assert.InRange(estimate - payload.Length, 0L, 4L);
        }
    }

    public static TheoryData<ulong, byte[]> Tickets => new() {
        { 127, [0x7f] },
        // Low bit retains a nonempty ticket on the far side of each varint boundary.
        { 129, [0x81, 1] },
        { 16383, [0xff, 0x7f] },
        { 16385, [0x81, 0x80, 1] },
        { 0x7fff_ffff_ffff_ffffUL, [0xff, 0xff, 0xff, 0xff, 0xff, 0xff, 0xff, 0xff, 0x7f] },
        { 0x8000_0000_0000_0001UL, [0x81, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 1] },
        { ulong.MaxValue, [0xff, 0xff, 0xff, 0xff, 0xff, 0xff, 0xff, 0xff, 0xff, 1] },
    };

    [Theory]
    [MemberData(nameof(Tickets))]
    public void Delta_ticket_length_is_exact_even_at_UInt64_boundary(ulong serializedTicket, byte[] ticketBytes) {
        FrameAddress prior = new(1, SizedPtr.Deserialize(serializedTicket));
        Assert.Equal(serializedTicket, prior.FrameTicket.Serialize());
        FileScope scope = new(2);
        byte[] payload = [2, 1, .. ticketBytes, 0];
        byte[] golden = [3, 2, 1, 1, .. ticketBytes, 1, 1, .. payload, 0];
        StateRevision revision = StateRevision.CreateObjectHeadMapDelta(prior,
            [ObjectVersionRecord.CreateDelta(1, prior, [])], []);
        Assert.Equal(golden, Encode(revision, scope));
        Assert.Equal(payload.Length, ReadPayloadBytes(golden, scope));
        Assert.Equal(payload.Length + 4L, ObjectVersionPayloadSize.EstimateDeltaPayloadBytesUpperBound(0, prior));
    }

    [Fact]
    public void Max_body_length_uses_long_arithmetic_without_allocating_body() {
        FrameAddress prior = new(uint.MaxValue, SizedPtr.Deserialize(ulong.MaxValue));
        Assert.Equal(2147483653L, ObjectVersionPayloadSize.GetBasePayloadBytes(int.MaxValue));
        Assert.Equal(2147483668L, ObjectVersionPayloadSize.EstimateDeltaPayloadBytesUpperBound(int.MaxValue, prior));
    }

    [Fact]
    public void Negative_length_and_default_prior_are_rejected() {
        FrameAddress prior = new(1, SizedPtr.Create(4, 4));
        Assert.Throws<ArgumentOutOfRangeException>(() => ObjectVersionPayloadSize.GetBasePayloadBytes(-1));
        Assert.Throws<ArgumentOutOfRangeException>(() => ObjectVersionPayloadSize.EstimateDeltaPayloadBytesUpperBound(-1, prior));
        Assert.Throws<ArgumentOutOfRangeException>(() => ObjectVersionPayloadSize.EstimateDeltaPayloadBytesUpperBound(0, default));
    }

    private static byte[] Encode(StateRevision revision, FileScope scope) {
        ArrayBufferWriter<byte> buffer = new();
        StateRevisionWireWriter.Write(buffer, revision, scope);
        return buffer.WrittenSpan.ToArray();
    }

    private static int ReadPayloadBytes(byte[] bytes, FileScope scope) =>
        StateRevisionWireReader.Read(bytes, scope).LocalObjects[0].EncodedPayloadBytes!.Value;
}
