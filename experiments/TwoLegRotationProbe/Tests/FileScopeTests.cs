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
        FrameTicket frameTicket = currentFile.Append(frame);
        FileScope scope = new(currentFile.FileNumber);
        RelativeFrameTicket currentLeg = new(
            IsPreviousFile: false,
            FrameTicket: frameTicket);

        Assert.Equal(
            new AbsoluteFrameAddress(currentFile.FileNumber, frameTicket),
            scope.Resolve(currentLeg));
        Assert.Same(frame, scope.ReadFrame(store, currentLeg));
    }

    [Fact]
    public void New_scope_moves_the_previous_leg_forward() {
        RbfFileStore store = new();
        RbfFile firstFile = store.CreateFile();
        Frame firstFrame = new FrameBuilder().Build();
        FrameTicket sharedFrameTicket = firstFile.Append(firstFrame);
        RbfFile secondFile = store.CreateFile();
        Frame secondFrame = new FrameBuilder().Build();
        Assert.Equal(sharedFrameTicket, secondFile.Append(secondFrame));
        RbfFile thirdFile = store.CreateFile();

        RelativeFrameTicket previousLeg = new(
            IsPreviousFile: true,
            FrameTicket: sharedFrameTicket);
        FileScope secondScope = new(secondFile.FileNumber);
        FileScope thirdScope = new(thirdFile.FileNumber);

        Assert.Equal(
            new AbsoluteFrameAddress(firstFile.FileNumber, sharedFrameTicket),
            secondScope.Resolve(previousLeg));
        Assert.Equal(
            new AbsoluteFrameAddress(secondFile.FileNumber, sharedFrameTicket),
            thirdScope.Resolve(previousLeg));
        Assert.Same(firstFrame, secondScope.ReadFrame(store, previousLeg));
        Assert.Same(secondFrame, thirdScope.ReadFrame(store, previousLeg));
    }

    [Fact]
    public void Previous_leg_is_invalid_for_the_first_file() {
        RbfFileStore store = new();
        RbfFile firstFile = store.CreateFile();
        firstFile.Append(new FrameBuilder().Build());
        FileScope scope = new(firstFile.FileNumber);
        RelativeFrameTicket invalidParent = new(
            IsPreviousFile: true,
            FrameTicket: new FrameTicket(4, 24));

        Assert.Throws<InvalidOperationException>(() => scope.Resolve(invalidParent));
        Assert.Throws<InvalidOperationException>(() => scope.ReadFrame(store, invalidParent));
    }

    [Fact]
    public void Current_and_previous_absolute_addresses_roundtrip_through_relative_tickets() {
        FileScope scope = new(3);
        FrameTicket ticket = new(4, 24);
        AbsoluteFrameAddress current = new(3, ticket);
        AbsoluteFrameAddress previous = new(2, ticket);

        RelativeFrameTicket currentRelative = scope.Relativize(current);
        RelativeFrameTicket previousRelative = scope.Relativize(previous);

        Assert.False(currentRelative.IsPreviousFile);
        Assert.True(previousRelative.IsPreviousFile);
        Assert.Equal(current, scope.Resolve(currentRelative));
        Assert.Equal(previous, scope.Resolve(previousRelative));
    }

    [Theory]
    [InlineData(1U)]
    [InlineData(4U)]
    public void Relativize_rejects_addresses_outside_the_two_file_scope(uint fileNumber) {
        FileScope scope = new(3);
        AbsoluteFrameAddress address = new(fileNumber, new FrameTicket(4, 24));

        Assert.Throws<InvalidOperationException>(() => scope.Relativize(address));
    }

    [Fact]
    public void First_file_scope_rejects_every_non_current_absolute_address() {
        FileScope scope = new(1);
        AbsoluteFrameAddress future = new(2, new FrameTicket(4, 24));

        Assert.Throws<InvalidOperationException>(() => scope.Relativize(future));
    }
}
