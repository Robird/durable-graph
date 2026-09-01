using Atelia.MultiSegmentStateStoreProbe.Model;

namespace Atelia.MultiSegmentStateStoreProbe.Tests;

public sealed class BackwardFrameReferenceTests {
    [Theory]
    [InlineData(70_000u, 70_000u, 0u)]
    [InlineData(70_000u, 69_999u, 1u)]
    [InlineData(70_000u, 4_464u, 65_536u)]
    [InlineData(uint.MaxValue, 1u, uint.MaxValue - 1u)]
    public void Arbitrarily_old_file_within_UInt32_range_round_trips(
        uint originValue,
        uint targetValue,
        uint expectedDistance) {
        FileNumber origin = new(originValue);
        AbsoluteFrameAddress target = new(new FileNumber(targetValue), 123);

        BackwardFrameReference relative =
            BackwardFrameReferenceResolver.Relativize(origin, target);
        AbsoluteFrameAddress resolved =
            BackwardFrameReferenceResolver.Resolve(origin, relative);

        Assert.Equal(expectedDistance, relative.BackwardFileDistance);
        Assert.Equal(target, resolved);
    }

    [Fact]
    public void Future_file_is_rejected() {
        FileNumber origin = new(10);
        AbsoluteFrameAddress future = new(new FileNumber(11), 123);

        Assert.Throws<InvalidDataException>(() =>
            BackwardFrameReferenceResolver.Relativize(origin, future));
    }

    [Fact]
    public void Distance_that_reaches_file_zero_is_rejected() {
        FileNumber origin = new(10);
        BackwardFrameReference underflow = new(10, 123);

        Assert.Throws<InvalidDataException>(() =>
            BackwardFrameReferenceResolver.Resolve(origin, underflow));
    }

    [Fact]
    public void Required_reference_rejects_zero_frame_ticket() {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new BackwardFrameReference(0, 0));
    }
}
