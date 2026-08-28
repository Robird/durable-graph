using Atelia.TwoLegRotationProbe.Model;

namespace Atelia.TwoLegRotationProbe.Tests;

public sealed class RbfFileTests {
    [Fact]
    public void Append_preserves_order_and_returns_random_read_key() {
        RbfFile file = new(7);
        Frame first = new FrameBuilder().Build();
        Frame second = new FrameBuilder().Build();

        FrameId firstId = file.Append(first);
        FrameId secondId = file.Append(second);

        Assert.Equal(new FrameId(0), firstId);
        Assert.Equal(new FrameId(1), secondId);
        Assert.Equal(2, file.FrameCount);
        Assert.Same(first, file.Read(firstId));
        Assert.Same(second, file.Read(secondId));
    }

    [Fact]
    public void Read_rejects_missing_frame() {
        RbfFile file = new(1);
        file.Append(new FrameBuilder().Build());

        Assert.Throws<ArgumentOutOfRangeException>(() => file.Read(new FrameId(1)));
    }

    [Fact]
    public void ParentId_locates_the_same_object_in_the_previous_file() {
        const uint objectId = 42;
        RbfFileStore store = new();
        RbfFile previousFile = store.CreateFile();
        FrameBuilder rootBuilder = new();
        rootBuilder.Add(objectId);
        Frame root = rootBuilder.Build();
        FrameId rootId = previousFile.Append(root);

        RbfFile currentFile = store.CreateFile();
        FrameBuilder childBuilder = new();
        childBuilder.Add(objectId).ParentId = new ParentId(
            IsPreviousFile: true,
            FrameId: rootId);
        Frame child = childBuilder.Build();
        currentFile.Append(child);
        FileScope scope = new(currentFile.FileNumber);

        ParentId? parentId = child.ObjectVersions[objectId].ParentId;

        Assert.NotNull(parentId);
        Assert.Same(
            root.ObjectVersions[objectId],
            scope.ReadFrame(store, parentId.Value).ObjectVersions[objectId]);
    }
}
