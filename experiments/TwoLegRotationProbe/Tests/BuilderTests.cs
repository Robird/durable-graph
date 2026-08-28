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
        firstVersion.Kind = ObjectVersionKind.Delta;
        firstVersion.PayloadBytes = 8;
        firstVersion.ResultBasePayloadBytes = 100;
        firstVersion.ExpectedParentBasePayloadBytes = 95;
        firstVersion.VersionOrdinal = 2;
        firstVersion.ParentFrameTicket = originalParentFrameTicket;

        Frame frame = builder.Build();
        firstVersion.Kind = ObjectVersionKind.Base;
        firstVersion.PayloadBytes = 0;
        firstVersion.ResultBasePayloadBytes = 0;
        firstVersion.ExpectedParentBasePayloadBytes = null;
        firstVersion.VersionOrdinal = 1;
        firstVersion.ParentFrameTicket = new RelativeFrameTicket(
            IsPreviousFile: false,
            FrameTicket: new FrameTicket(9));
        builder.Add(secondObjectId);

        ObjectVersion persistedVersion = Assert.Single(frame.ObjectVersions).Value;
        Assert.Equal(ObjectVersionKind.Delta, persistedVersion.Kind);
        Assert.Equal(8, persistedVersion.PayloadBytes);
        Assert.Equal(100, persistedVersion.ResultBasePayloadBytes);
        Assert.Equal(95, persistedVersion.ExpectedParentBasePayloadBytes);
        Assert.Equal(2, persistedVersion.VersionOrdinal);
        Assert.Equal(originalParentFrameTicket, persistedVersion.ParentFrameTicket);
        Assert.False(frame.ObjectVersions.ContainsKey(secondObjectId));
    }

    [Fact]
    public void Build_rejects_invalid_object_version_shapes() {
        RelativeFrameTicket parent = new(
            IsPreviousFile: false,
            FrameTicket: new FrameTicket(0));
        ObjectVersionBuilder[] invalidBuilders = [
            new() { Kind = (ObjectVersionKind)99 },
            new() { ResultBasePayloadBytes = -1 },
            new() { VersionOrdinal = 0 },
            new() {
                Kind = ObjectVersionKind.Delta,
                PayloadBytes = 1,
                ResultBasePayloadBytes = 1,
                ExpectedParentBasePayloadBytes = 0,
            },
            new() { ParentFrameTicket = parent },
            new() { VersionOrdinal = 2 },
            new() {
                VersionOrdinal = 2,
                ParentFrameTicket = parent,
                ExpectedParentBasePayloadBytes = 0,
            },
            new() {
                PayloadBytes = 1,
                ResultBasePayloadBytes = 2,
            },
            new() {
                Kind = ObjectVersionKind.Delta,
                PayloadBytes = 1,
                ResultBasePayloadBytes = 1,
                VersionOrdinal = 2,
                ParentFrameTicket = parent,
            },
            new() {
                Kind = ObjectVersionKind.Delta,
                PayloadBytes = 1,
                ResultBasePayloadBytes = 1,
                ExpectedParentBasePayloadBytes = -1,
                VersionOrdinal = 2,
                ParentFrameTicket = parent,
            },
            new() {
                Kind = ObjectVersionKind.Delta,
                ResultBasePayloadBytes = 1,
                ExpectedParentBasePayloadBytes = 0,
                VersionOrdinal = 2,
                ParentFrameTicket = parent,
            },
        ];

        foreach (ObjectVersionBuilder builder in invalidBuilders) {
            Assert.ThrowsAny<ArgumentException>(() => builder.Build());
        }
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

    [Fact]
    public void AbsoluteFrameAddress_rejects_zero_file_number() {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => new AbsoluteFrameAddress(0, new FrameTicket(0)));
    }
}
