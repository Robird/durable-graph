using Atelia.TwoLegRotationProbe.Model;

namespace Atelia.TwoLegRotationProbe.Tests;

public sealed class RbfFileTests {
    [Fact]
    public void Append_preserves_order_and_returns_random_read_key() {
        RbfFile file = new(7);
        Frame first = new FrameBuilder().Build();
        Frame second = new FrameBuilder().Build();

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
        file.Append(new FrameBuilder().Build());

        Assert.Throws<ArgumentOutOfRangeException>(() => file.Read(frameNumber));
    }

    [Fact]
    public void ParentId_locates_the_same_object_in_an_earlier_frame() {
        const uint objectId = 42;
        RbfFile file = new(1);
        FrameBuilder rootBuilder = new();
        rootBuilder.Add(objectId);
        Frame root = rootBuilder.Build();
        int rootId = file.Append(root);

        FrameBuilder childBuilder = new();
        childBuilder.Add(objectId).ParentId = rootId;
        Frame child = childBuilder.Build();
        file.Append(child);

        int? parentId = child.ObjectVersions[objectId].ParentId;

        Assert.NotNull(parentId);
        Assert.Same(
            root.ObjectVersions[objectId],
            file.Read(parentId.Value).ObjectVersions[objectId]);
    }
}
