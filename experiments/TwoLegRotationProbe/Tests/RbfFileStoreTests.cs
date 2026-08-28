using Atelia.TwoLegRotationProbe.Model;

namespace Atelia.TwoLegRotationProbe.Tests;

public sealed class RbfFileStoreTests {
    [Fact]
    public void CreateFile_assigns_one_based_monotonic_numbers() {
        RbfFileStore store = new();

        RbfFile first = store.CreateFile();
        RbfFile second = store.CreateFile();

        Assert.Equal(1U, first.FileNumber);
        Assert.Equal(2U, second.FileNumber);
        Assert.Equal(2, store.FileCount);
        Assert.Same(first, store.GetFile(1));
        Assert.Same(second, store.GetFile(2));
    }

    [Theory]
    [InlineData(0U)]
    [InlineData(2U)]
    public void GetFile_rejects_unknown_number(uint fileNumber) {
        RbfFileStore store = new();
        store.CreateFile();

        Assert.Throws<KeyNotFoundException>(() => store.GetFile(fileNumber));
    }

    [Fact]
    public void ReadFrame_uses_an_absolute_address() {
        RbfFileStore store = new();
        RbfFile file = store.CreateFile();
        Frame frame = new FrameBuilder().Build();
        FrameTicket ticket = file.Append(frame);

        Assert.Same(frame, store.ReadFrame(new AbsoluteFrameAddress(file.FileNumber, ticket)));
    }
}
