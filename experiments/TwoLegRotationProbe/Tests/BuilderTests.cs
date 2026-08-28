using Atelia.TwoLegRotationProbe.Model;

namespace Atelia.TwoLegRotationProbe.Tests;

public sealed class BuilderTests {
    [Fact]
    public void Build_freezes_object_membership_and_parent_values() {
        const uint firstObjectId = 11;
        const uint secondObjectId = 12;
        FrameBuilder builder = new();
        ObjectVersionBuilder firstVersion = builder.Add(firstObjectId);
        RelativeFrameTicket originalParentFrameTicket = new(
            IsPreviousFile: true,
            FrameTicket: new FrameTicket(3));
        firstVersion.ParentFrameTicket = originalParentFrameTicket;

        Frame frame = builder.Build();
        firstVersion.ParentFrameTicket = new RelativeFrameTicket(
            IsPreviousFile: false,
            FrameTicket: new FrameTicket(9));
        builder.Add(secondObjectId);

        ObjectVersion persistedVersion = Assert.Single(frame.ObjectVersions).Value;
        Assert.Equal(originalParentFrameTicket, persistedVersion.ParentFrameTicket);
        Assert.False(frame.ObjectVersions.ContainsKey(secondObjectId));
    }

    [Fact]
    public void Built_frame_exposes_a_read_only_dictionary() {
        FrameBuilder builder = new();
        builder.Add(1);
        Frame frame = builder.Build();
        IDictionary<uint, ObjectVersion> dictionary =
            Assert.IsAssignableFrom<IDictionary<uint, ObjectVersion>>(frame.ObjectVersions);

        Assert.True(dictionary.IsReadOnly);
        Assert.Throws<NotSupportedException>(() => dictionary.Add(2, new ObjectVersionBuilder().Build()));
    }

    [Fact]
    public void FrameTicket_rejects_a_negative_value() {
        Assert.Throws<ArgumentOutOfRangeException>(() => new FrameTicket(-1));
    }
}
