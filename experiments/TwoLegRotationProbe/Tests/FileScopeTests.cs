using Atelia.TwoLegRotationProbe.Model;

namespace Atelia.TwoLegRotationProbe.Tests;

public sealed class FileScopeTests {
    [Fact]
    public void PreviousFileNumber_is_derived_per_immutable_scope() {
        FileScope firstScope = new(1);
        FileScope secondScope = new(2);

        Assert.Null(firstScope.PreviousFileNumber);
        Assert.Equal(1U, secondScope.PreviousFileNumber);
    }

    [Fact]
    public void Current_leg_reads_the_scope_current_file() {
        RbfFileStore store = new();
        RbfFile currentFile = store.CreateFile();
        Frame frame = new FrameBuilder().Build();
        FrameId frameId = currentFile.Append(frame);
        FileScope scope = new(currentFile.FileNumber);
        ParentId currentLeg = new(
            IsPreviousFile: false,
            FrameId: frameId);

        Assert.Same(frame, scope.ReadFrame(store, currentLeg));
    }

    [Fact]
    public void New_scope_moves_the_previous_leg_forward() {
        RbfFileStore store = new();
        RbfFile firstFile = store.CreateFile();
        Frame firstFrame = new FrameBuilder().Build();
        FrameId sharedFrameId = firstFile.Append(firstFrame);
        RbfFile secondFile = store.CreateFile();
        Frame secondFrame = new FrameBuilder().Build();
        Assert.Equal(sharedFrameId, secondFile.Append(secondFrame));
        RbfFile thirdFile = store.CreateFile();

        ParentId previousLeg = new(
            IsPreviousFile: true,
            FrameId: sharedFrameId);
        FileScope secondScope = new(secondFile.FileNumber);
        FileScope thirdScope = new(thirdFile.FileNumber);

        Assert.Same(firstFrame, secondScope.ReadFrame(store, previousLeg));
        Assert.Same(secondFrame, thirdScope.ReadFrame(store, previousLeg));
    }

    [Fact]
    public void Previous_leg_is_invalid_for_the_first_file() {
        RbfFileStore store = new();
        RbfFile firstFile = store.CreateFile();
        firstFile.Append(new FrameBuilder().Build());
        FileScope scope = new(firstFile.FileNumber);
        ParentId invalidParent = new(
            IsPreviousFile: true,
            FrameId: new FrameId(0));

        Assert.Throws<InvalidOperationException>(() => scope.ReadFrame(store, invalidParent));
    }
}
