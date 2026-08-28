using Atelia.TwoLegRotationProbe.Model;

namespace Atelia.TwoLegRotationProbe.Tests;

public sealed class RbfFileTests {
    [Fact]
    public void Append_preserves_order_and_returns_random_read_key() {
        RbfFile file = new(7);
        Frame first = new FrameBuilder().Build();
        Frame second = new FrameBuilder().Build();

        FrameTicket firstTicket = file.Append(first);
        FrameTicket secondTicket = file.Append(second);

        Assert.Equal(new FrameTicket(0), firstTicket);
        Assert.Equal(new FrameTicket(1), secondTicket);
        Assert.Equal(2, file.FrameCount);
        Assert.Same(first, file.Read(firstTicket));
        Assert.Same(second, file.Read(secondTicket));
    }

    [Fact]
    public void Read_rejects_missing_frame() {
        RbfFile file = new(1);
        file.Append(new FrameBuilder().Build());

        Assert.Throws<ArgumentOutOfRangeException>(() => file.Read(new FrameTicket(1)));
    }

    [Fact]
    public void ParentTicket_locates_the_same_object_in_the_previous_file() {
        const uint objectId = 42;
        RbfFileStore store = new();
        RbfFile previousFile = store.CreateFile();
        FrameBuilder rootBuilder = new();
        rootBuilder.Add(objectId);
        Frame root = rootBuilder.Build();
        FrameTicket rootTicket = previousFile.Append(root);

        RbfFile currentFile = store.CreateFile();
        FrameBuilder childBuilder = new();
        ObjectVersionBuilder childVersion = childBuilder.Add(objectId);
        childVersion.VersionOrdinal = 2;
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
