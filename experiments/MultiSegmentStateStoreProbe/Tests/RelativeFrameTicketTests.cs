using Atelia.MultiSegmentStateStoreProbe.Model;

namespace Atelia.MultiSegmentStateStoreProbe.Tests;

public sealed class RelativeFrameTicketTests {
    private static readonly FrameTicket Ticket = new(4, 24);

    [Theory]
    [InlineData(70_000u, 70_000u, 0u)]
    [InlineData(70_000u, 69_999u, 1u)]
    [InlineData(70_000u, 4_464u, 65_536u)]
    [InlineData(uint.MaxValue, 1u, uint.MaxValue - 1u)]
    public void Arbitrarily_old_file_within_UInt32_range_round_trips(
        uint originValue,
        uint targetValue,
        uint expectedDistance) {
        FileScope scope = new(new FileNumber(originValue));
        AbsoluteFrameAddress target = new(new FileNumber(targetValue), Ticket);

        RelativeFrameTicket relative = scope.Relativize(target);
        AbsoluteFrameAddress resolved = scope.Resolve(relative);

        Assert.Equal(expectedDistance, relative.BackwardFileDistance);
        Assert.Equal(target, resolved);
    }

    [Fact]
    public void Future_file_is_rejected() {
        FileScope scope = new(new FileNumber(10));
        AbsoluteFrameAddress future = new(new FileNumber(11), Ticket);

        Assert.Throws<InvalidDataException>(() => scope.Relativize(future));
    }

    [Theory]
    [InlineData(10u)]
    [InlineData(uint.MaxValue)]
    public void Distance_that_reaches_or_crosses_file_zero_is_rejected(uint distance) {
        FileScope scope = new(new FileNumber(10));
        RelativeFrameTicket underflow = new(distance, Ticket);

        Assert.Throws<InvalidDataException>(() => scope.Resolve(underflow));
    }

    [Fact]
    public void Required_reference_rejects_default_frame_ticket() {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new RelativeFrameTicket(0, default));
    }

    [Fact]
    public void Same_file_reference_must_be_strictly_earlier() {
        FileScope scope = new(new FileNumber(7));
        FrameTicket containing = new(100, 24);

        Assert.Equal(
            new AbsoluteFrameAddress(new FileNumber(7), new FrameTicket(4, 24)),
            scope.ResolveEarlier(
                containing,
                new RelativeFrameTicket(0, new FrameTicket(4, 24))));
        Assert.Throws<InvalidDataException>(() => scope.ResolveEarlier(
            containing,
            new RelativeFrameTicket(0, containing)));
        Assert.Throws<InvalidDataException>(() => scope.ResolveEarlier(
            containing,
            new RelativeFrameTicket(0, new FrameTicket(104, 24))));
    }

    [Fact]
    public void Older_file_is_earlier_regardless_of_its_local_offset() {
        FileScope scope = new(new FileNumber(7));
        RelativeFrameTicket older = new(1, new FrameTicket(10_000, 24));

        Assert.Equal(
            new AbsoluteFrameAddress(new FileNumber(6), older.FrameTicket),
            scope.ResolveEarlier(new FrameTicket(4, 24), older));
    }
}
