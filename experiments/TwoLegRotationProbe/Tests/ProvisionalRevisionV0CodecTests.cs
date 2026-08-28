using Atelia.TwoLegRotationProbe.Encoding;
using Atelia.TwoLegRotationProbe.Model;

namespace Atelia.TwoLegRotationProbe.Tests;

public sealed class ProvisionalRevisionV0CodecTests {
    [Theory]
    [InlineData(0UL, 1)]
    [InlineData(1UL, 1)]
    [InlineData(127UL, 1)]
    [InlineData(128UL, 2)]
    [InlineData(16_383UL, 2)]
    [InlineData(16_384UL, 3)]
    [InlineData(ulong.MaxValue, 10)]
    public void Canonical_unsigned_base128_width_has_exact_boundaries(
        ulong value,
        int expectedWidth) {
        Assert.Equal(expectedWidth, CanonicalUnsignedBase128.GetEncodedWidth(value));
    }

    [Theory]
    [InlineData(4L, 24, 262UL)]
    [InlineData(4L, 124, 1_799UL)]
    [InlineData(4L, 128, 65_540UL)]
    [InlineData(32L, 24, 290UL)]
    public void SizedPtr_projection_matches_known_current_vectors(
        long offsetBytes,
        int lengthBytes,
        ulong expectedSerialized) {
        FrameTicket ticket = new(offsetBytes, lengthBytes);

        ulong serialized = ProvisionalSizedPtrProjection.Serialize(ticket);

        Assert.Equal(expectedSerialized, serialized);
        Assert.Equal(ticket, ProvisionalSizedPtrProjection.Deserialize(serialized));
    }

    [Fact]
    public void SizedPtr_projection_roundtrips_relative_capacity_boundaries() {
        FrameTicket[] tickets = [
            new FrameTicket(4, RbfV040Layout.MinFrameLengthBytes),
            new FrameTicket(4, RbfV040Layout.MaxFrameLengthBytes),
            new FrameTicket(
                RbfV040Layout.MaxDurableGraphRelativeFrameStartOffsetBytes,
                RbfV040Layout.MaxFrameLengthBytes),
        ];

        foreach (FrameTicket ticket in tickets) {
            ulong serialized = ProvisionalSizedPtrProjection.Serialize(ticket);
            Assert.Equal(ticket, ProvisionalSizedPtrProjection.Deserialize(serialized));
        }
    }

    [Fact]
    public void Relative_ticket_grammar_reserves_optional_and_invalid_tokens() {
        Assert.Equal(0UL, ProvisionalRelativeFrameTicketCodec.NoneToken);
        Assert.Equal(1UL, ProvisionalRelativeFrameTicketCodec.InvalidToken);
        Assert.Equal(0UL, ProvisionalRelativeFrameTicketCodec.EncodeOptional(null));
        Assert.Null(ProvisionalRelativeFrameTicketCodec.DecodeOptional(0));
        Assert.Throws<InvalidDataException>(
            () => ProvisionalRelativeFrameTicketCodec.DecodeOptional(1));
        Assert.Throws<InvalidDataException>(
            () => ProvisionalRelativeFrameTicketCodec.DecodeRequired(0));
        Assert.Throws<InvalidDataException>(
            () => ProvisionalRelativeFrameTicketCodec.DecodeRequired(1));
    }

    [Theory]
    [InlineData(false, 524UL)]
    [InlineData(true, 525UL)]
    public void Required_relative_ticket_preserves_file_selector(
        bool isPreviousFile,
        ulong expectedToken) {
        RelativeFrameTicket ticket = new(
            isPreviousFile,
            new FrameTicket(4, 24));

        ulong token = ProvisionalRelativeFrameTicketCodec.EncodeRequired(ticket);

        Assert.Equal(expectedToken, token);
        Assert.Equal(ticket, ProvisionalRelativeFrameTicketCodec.DecodeRequired(token));
    }

    [Fact]
    public void Relative_ticket_accepts_last_DurableGraph_start_and_rejects_next_start() {
        RelativeFrameTicket last = new(
            false,
            new FrameTicket(
                RbfV040Layout.MaxDurableGraphRelativeFrameStartOffsetBytes,
                RbfV040Layout.MinFrameLengthBytes));
        RelativeFrameTicket firstNativeOnly = new(
            false,
            new FrameTicket(
                RbfV040Layout.MaxDurableGraphRelativeFrameStartOffsetBytes +
                    RbfV040Layout.AlignmentBytes,
                RbfV040Layout.MinFrameLengthBytes));

        ulong lastToken = ProvisionalRelativeFrameTicketCodec.EncodeRequired(last);

        Assert.Equal(last, ProvisionalRelativeFrameTicketCodec.DecodeRequired(lastToken));
        Assert.Throws<ArgumentOutOfRangeException>(
            () => ProvisionalRelativeFrameTicketCodec.EncodeRequired(firstNativeOnly));
    }

    [Fact]
    public void Ovd_self_is_one_byte_and_resolves_from_the_containing_frame() {
        FrameTicket containingTicket = new(132, 148);

        ulong token = ProvisionalObjectVersionDictionaryBinding.EncodeSelf();

        Assert.Equal(1UL, token);
        Assert.Equal(1, CanonicalUnsignedBase128.GetEncodedWidth(token));
        Assert.Equal(
            new AbsoluteFrameAddress(7, containingTicket),
            ProvisionalObjectVersionDictionaryBinding.Resolve(
                token,
                originFileNumber: 7,
                containingTicket));
    }

    [Theory]
    [InlineData(false, 7U)]
    [InlineData(true, 6U)]
    public void Ovd_external_binding_resolves_exact_current_or_previous_file(
        bool isPreviousFile,
        uint expectedFileNumber) {
        FrameTicket containing = new(132, 148);
        FrameTicket target = new(4, 124);
        ulong token = ProvisionalObjectVersionDictionaryBinding.EncodeExternal(
            new RelativeFrameTicket(isPreviousFile, target));

        AbsoluteFrameAddress resolved = ProvisionalObjectVersionDictionaryBinding.Resolve(
            token,
            originFileNumber: 7,
            containing);

        Assert.Equal(new AbsoluteFrameAddress(expectedFileNumber, target), resolved);
    }

    [Fact]
    public void Ovd_rejects_invalid_and_explicit_relative_alias_of_containing_frame() {
        FrameTicket containing = new(4, 124);
        ulong alias = ProvisionalObjectVersionDictionaryBinding.EncodeExternal(
            new RelativeFrameTicket(false, containing));

        Assert.Throws<InvalidDataException>(
            () => ProvisionalObjectVersionDictionaryBinding.Resolve(
                ProvisionalObjectVersionDictionaryBinding.InvalidToken,
                originFileNumber: 7,
                containing));
        Assert.Throws<InvalidDataException>(
            () => ProvisionalObjectVersionDictionaryBinding.Resolve(
                alias,
                originFileNumber: 7,
                containing));
    }

    [Fact]
    public void Self_is_contextual_to_Ovd_and_not_part_of_general_relative_ticket() {
        Assert.DoesNotContain(
            typeof(RelativeFrameTicket).GetProperties(),
            static property => property.Name.Contains("Self", StringComparison.Ordinal));
        Assert.Throws<InvalidDataException>(
            () => ProvisionalRelativeFrameTicketCodec.DecodeRequired(
                ProvisionalObjectVersionDictionaryBinding.SelfToken));
    }

    [Fact]
    public void Rejected_literal_self_ticket_design_has_two_stable_fixed_points() {
        const long frameStartBytes = 4;
        const int nonSelfPayloadAndMetaBytes = 98;
        const int selfTicketCount = 1;
        List<(int AssumedWidth, int FrameLength)> fixedPoints = [];

        for (int assumedWidth = 1; assumedWidth <= 10; assumedWidth++) {
            int bodyBytes = checked(
                nonSelfPayloadAndMetaBytes + (selfTicketCount * assumedWidth));
            RbfFrameLayoutEstimate layout = RbfV040Layout.Estimate(
                frameStartBytes,
                payloadLengthBytes: bodyBytes,
                tailMetaLengthBytes: 0);
            ulong relativeTicket = ProvisionalRelativeFrameTicketCodec.EncodeRequired(
                new RelativeFrameTicket(IsPreviousFile: false, layout.Ticket));
            int actualWidth = CanonicalUnsignedBase128.GetEncodedWidth(relativeTicket);
            if (actualWidth == assumedWidth) {
                fixedPoints.Add((assumedWidth, layout.FrameLengthBytes));
            }
        }

        Assert.Equal([(2, 124), (3, 128)], fixedPoints);
    }
}
