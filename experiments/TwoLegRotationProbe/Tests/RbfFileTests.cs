using Atelia.TwoLegRotationProbe.Model;

namespace Atelia.TwoLegRotationProbe.Tests;

public sealed class RbfFileTests {
    [Fact]
    public void Empty_frame_layout_matches_rbf_v040_envelope() {
        RbfFrameLayoutEstimate layout = RbfV040Layout.Estimate(
            RbfV040Layout.InitialTailOffsetBytes,
            payloadLengthBytes: 0,
            tailMetaLengthBytes: 0);

        Assert.Equal(4, layout.FrameStartOffsetBytes);
        Assert.Equal(0, layout.PayloadLengthBytes);
        Assert.Equal(0, layout.TailMetaLengthBytes);
        Assert.Equal(0, layout.PaddingLengthBytes);
        Assert.Equal(24, layout.FrameLengthBytes);
        Assert.Equal(28, layout.AppendLengthBytes);
        Assert.Equal(32, layout.TailOffsetAfterBytes);
        Assert.Equal(new FrameTicket(4, 24), layout.Ticket);
    }

    [Theory]
    [InlineData(1, 3, 28)]
    [InlineData(2, 2, 28)]
    [InlineData(3, 1, 28)]
    [InlineData(4, 0, 28)]
    public void Payload_is_padded_to_four_bytes(
        int payloadLengthBytes,
        int expectedPaddingLengthBytes,
        int expectedFrameLengthBytes) {
        RbfFrameLayoutEstimate layout = RbfV040Layout.Estimate(
            4,
            payloadLengthBytes,
            tailMetaLengthBytes: 0);

        Assert.Equal(expectedPaddingLengthBytes, layout.PaddingLengthBytes);
        Assert.Equal(expectedFrameLengthBytes, layout.FrameLengthBytes);
    }

    [Fact]
    public void Payload_and_tail_meta_share_the_padding_calculation() {
        RbfFrameLayoutEstimate layout = RbfV040Layout.Estimate(
            frameStartOffsetBytes: 4,
            payloadLengthBytes: 10,
            tailMetaLengthBytes: 5);

        Assert.Equal(1, layout.PaddingLengthBytes);
        Assert.Equal(40, layout.FrameLengthBytes);
        Assert.Equal(44, layout.AppendLengthBytes);
        Assert.Equal(48, layout.TailOffsetAfterBytes);
    }

    [Fact]
    public void Maximum_payload_and_meta_total_produces_maximum_frame_length() {
        RbfFrameLayoutEstimate layout = RbfV040Layout.Estimate(
            frameStartOffsetBytes: 4,
            payloadLengthBytes: RbfV040Layout.MaxPayloadAndTailMetaLengthBytes,
            tailMetaLengthBytes: 0);

        Assert.Equal(0, layout.PaddingLengthBytes);
        Assert.Equal(RbfV040Layout.MaxFrameLengthBytes, layout.FrameLengthBytes);
        Assert.Equal(268_435_456, layout.AppendLengthBytes);

        RbfFrameLayoutEstimate maxTailMeta = RbfV040Layout.Estimate(
            frameStartOffsetBytes: 4,
            payloadLengthBytes:
                RbfV040Layout.MaxPayloadAndTailMetaLengthBytes - RbfV040Layout.MaxTailMetaLengthBytes,
            tailMetaLengthBytes: RbfV040Layout.MaxTailMetaLengthBytes);
        Assert.Equal(RbfV040Layout.MaxFrameLengthBytes, maxTailMeta.FrameLengthBytes);
    }

    [Fact]
    public void Estimate_rejects_invalid_lengths_and_combined_overflow() {
        Assert.Throws<ArgumentOutOfRangeException>(() => RbfV040Layout.Estimate(4, -1, 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => RbfV040Layout.Estimate(4, 0, -1));
        Assert.Throws<ArgumentOutOfRangeException>(
            () => RbfV040Layout.Estimate(4, 0, RbfV040Layout.MaxTailMetaLengthBytes + 1));
        Assert.Throws<ArgumentOutOfRangeException>(
            () => RbfV040Layout.Estimate(
                4,
                RbfV040Layout.MaxPayloadAndTailMetaLengthBytes,
                1));
        Assert.Throws<ArgumentOutOfRangeException>(
            () => RbfV040Layout.Estimate(4, int.MaxValue, RbfV040Layout.MaxTailMetaLengthBytes));
    }

    [Fact]
    public void Native_last_start_is_legal_even_when_the_frame_ends_beyond_it() {
        RbfFrameLayoutEstimate layout = RbfV040Layout.Estimate(
            RbfV040Layout.MaxNativeFrameStartOffsetBytes,
            RbfV040Layout.MaxPayloadAndTailMetaLengthBytes,
            tailMetaLengthBytes: 0);

        Assert.Equal(RbfV040Layout.MaxNativeFrameStartOffsetBytes, layout.Ticket.OffsetBytes);
        Assert.True(layout.Ticket.EndOffsetExclusive > RbfV040Layout.MaxNativeFrameStartOffsetBytes);
        Assert.True(layout.TailOffsetAfterBytes > RbfV040Layout.MaxNativeFrameStartOffsetBytes);
        Assert.Throws<ArgumentOutOfRangeException>(
            () => RbfV040Layout.Estimate(
                RbfV040Layout.MaxNativeFrameStartOffsetBytes + RbfV040Layout.AlignmentBytes,
                0,
                0));
    }

    [Fact]
    public void DurableGraph_relative_start_has_a_separate_half_capacity_gate() {
        FrameTicket lastRelative = new(
            RbfV040Layout.MaxDurableGraphRelativeFrameStartOffsetBytes,
            RbfV040Layout.MinFrameLengthBytes);
        FrameTicket firstNativeOnly = new(
            RbfV040Layout.MaxDurableGraphRelativeFrameStartOffsetBytes + RbfV040Layout.AlignmentBytes,
            RbfV040Layout.MinFrameLengthBytes);

        Assert.True(RbfV040Layout.IsDurableGraphRelativeStartRepresentable(lastRelative));
        Assert.False(RbfV040Layout.IsDurableGraphRelativeStartRepresentable(firstNativeOnly));
        Assert.False(RbfV040Layout.IsDurableGraphRelativeStartRepresentable(default));
    }

    [Fact]
    public void Append_returns_byte_range_tickets_and_advances_tail() {
        RbfFile file = new(7);
        Frame first = new FrameBuilder().Build();
        Frame second = new FrameBuilder().Build();

        FrameTicket firstTicket = file.Append(first);
        FrameTicket secondTicket = file.Append(second);

        Assert.Equal(new FrameTicket(4, 24), firstTicket);
        Assert.Equal(new FrameTicket(32, 24), secondTicket);
        Assert.Equal(2, file.FrameCount);
        Assert.Equal(60, file.TailOffsetBytes);
        Assert.Same(first, file.Read(firstTicket));
        Assert.Same(second, file.Read(secondTicket));
        Assert.Equal(firstTicket, file.ReadLayout(firstTicket).Ticket);
    }

    [Fact]
    public void Read_requires_the_complete_ticket() {
        RbfFile file = new(1);
        FrameTicket ticket = file.Append(new FrameBuilder().Build());

        FrameTicket wrongLength = new(ticket.OffsetBytes, ticket.LengthBytes + 4);
        FrameTicket missing = new(ticket.EndOffsetExclusive + 4, 24);

        Assert.Throws<KeyNotFoundException>(() => file.Read(wrongLength));
        Assert.Throws<KeyNotFoundException>(() => file.ReadLayout(wrongLength));
        Assert.Throws<KeyNotFoundException>(() => file.Read(missing));
    }

    [Fact]
    public void Failed_append_does_not_publish_a_frame_or_advance_tail() {
        RbfFile file = new(1);
        Frame frame = new FrameBuilder().Build();

        Assert.Throws<ArgumentOutOfRangeException>(
            () => file.Append(
                frame,
                RbfV040Layout.MaxPayloadAndTailMetaLengthBytes,
                tailMetaLengthBytes: 1));

        Assert.Equal(0, file.FrameCount);
        Assert.Equal(RbfV040Layout.InitialTailOffsetBytes, file.TailOffsetBytes);
        Assert.Equal(new FrameTicket(4, 24), file.Append(frame));
    }

    [Fact]
    public void ParentTicket_locates_the_same_object_in_the_previous_file() {
        const uint objectId = 42;
        RbfFileStore store = new();
        RbfFile previousFile = store.CreateFile();
        FrameBuilder rootBuilder = new();
        rootBuilder.Add(objectId).ReconstructionObjectPayloadBytes = 0;
        Frame root = rootBuilder.Build();
        FrameTicket rootTicket = previousFile.Append(root);

        RbfFile currentFile = store.CreateFile();
        FrameBuilder childBuilder = new();
        ObjectVersionBuilder childVersion = childBuilder.Add(objectId);
        childVersion.ReconstructionObjectPayloadBytes = 0;
        childVersion.LogicalVersionOrdinal = 2;
        childVersion.ParentFrameTicket = new RelativeFrameTicket(
            IsPreviousFile: true,
            FrameTicket: rootTicket);
        Frame child = childBuilder.Build();
        currentFile.Append(child);
        FileScope scope = new(currentFile.FileNumber);

        RelativeFrameTicket? parentTicket = child.ObjectVersions[objectId].ParentFrameTicket;

        Assert.NotNull(parentTicket);
        Assert.Same(
            root.ObjectVersions[objectId],
            scope.ReadFrame(store, parentTicket.Value).ObjectVersions[objectId]);
    }
}
