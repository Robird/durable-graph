using Atelia.TwoLegRotationProbe.Model;

namespace Atelia.TwoLegRotationProbe.Tests;

public sealed class BuilderTests {
    [Fact]
    public void Build_freezes_object_membership_and_parent_values() {
        const uint firstObjectId = 11;
        const uint secondObjectId = 12;
        FrameBuilder builder = new();
        ObjectVersionBuilder firstVersion = builder.Add(firstObjectId);
        ParentId originalParentId = new(
            IsPreviousFile: true,
            FrameId: new FrameId(3));
        firstVersion.ParentId = originalParentId;

        Frame frame = builder.Build();
        firstVersion.ParentId = new ParentId(
            IsPreviousFile: false,
            FrameId: new FrameId(9));
        builder.Add(secondObjectId);

        ObjectVersion persistedVersion = Assert.Single(frame.ObjectVersions).Value;
        Assert.Equal(originalParentId, persistedVersion.ParentId);
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
    public void FrameId_rejects_a_negative_value() {
        Assert.Throws<ArgumentOutOfRangeException>(() => new FrameId(-1));
    }
}
