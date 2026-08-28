using Atelia.TwoLegRotationProbe.Model;

namespace Atelia.TwoLegRotationProbe.Tests;

public sealed class RbfFileTests {
    [Fact]
    public void Append_preserves_order_and_returns_random_read_key() {
        RbfFile file = new(7);
        Frame first = new();
        Frame second = new();

        int firstNumber = file.Append(first);
        int secondNumber = file.Append(second);

        Assert.Equal(0, firstNumber);
        Assert.Equal(1, secondNumber);
        Assert.Equal(2, file.FrameCount);
        Assert.Same(first, file.Read(firstNumber));
        Assert.Same(second, file.Read(secondNumber));
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(1)]
    public void Read_rejects_missing_frame(int frameNumber) {
        RbfFile file = new(1);
        file.Append(new Frame());

        Assert.Throws<ArgumentOutOfRangeException>(() => file.Read(frameNumber));
    }
}
