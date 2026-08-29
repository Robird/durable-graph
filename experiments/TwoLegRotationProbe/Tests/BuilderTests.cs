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
            FrameTicket: new FrameTicket(4, 24));
        firstVersion.Kind = ObjectVersionKind.Delta;
        firstVersion.PayloadBytes = 8;
        firstVersion.ReconstructionObjectPayloadBytes = 103;
        firstVersion.ResultBasePayloadBytes = 100;
        firstVersion.ExpectedParentBasePayloadBytes = 95;
        firstVersion.LogicalVersionOrdinal = 2;
        firstVersion.ParentFrameTicket = originalParentFrameTicket;

        Frame frame = builder.Build();
        firstVersion.Kind = ObjectVersionKind.Base;
        firstVersion.PayloadBytes = 0;
        firstVersion.ReconstructionObjectPayloadBytes = 0;
        firstVersion.ResultBasePayloadBytes = 0;
        firstVersion.ExpectedParentBasePayloadBytes = null;
        firstVersion.LogicalVersionOrdinal = 1;
        firstVersion.ParentFrameTicket = new RelativeFrameTicket(
            IsPreviousFile: false,
            FrameTicket: new FrameTicket(32, 24));
        builder.Add(secondObjectId);

        ObjectVersion persistedVersion = Assert.Single(frame.ObjectVersions).Value;
        Assert.Equal(ObjectVersionKind.Delta, persistedVersion.Kind);
        Assert.Equal(8, persistedVersion.PayloadBytes);
        Assert.Equal(103, persistedVersion.ReconstructionObjectPayloadBytes);
        Assert.Equal(100, persistedVersion.ResultBasePayloadBytes);
        Assert.Equal(95, persistedVersion.ExpectedParentBasePayloadBytes);
        Assert.Equal(2, persistedVersion.LogicalVersionOrdinal);
        Assert.Equal(originalParentFrameTicket, persistedVersion.ParentFrameTicket);
        Assert.False(frame.ObjectVersions.ContainsKey(secondObjectId));
    }

    [Fact]
    public void Build_rejects_invalid_object_version_shapes() {
        RelativeFrameTicket parent = new(
            IsPreviousFile: false,
            FrameTicket: new FrameTicket(4, 24));
        ObjectVersionBuilder[] invalidBuilders = [
            new() { Kind = (ObjectVersionKind)99, ReconstructionObjectPayloadBytes = 0 },
            new() { ReconstructionObjectPayloadBytes = 0, ResultBasePayloadBytes = -1 },
            new() { ReconstructionObjectPayloadBytes = 0, LogicalVersionOrdinal = 0 },
            new() {
                Kind = ObjectVersionKind.Delta,
                PayloadBytes = 1,
                ReconstructionObjectPayloadBytes = 1,
                ResultBasePayloadBytes = 1,
                ExpectedParentBasePayloadBytes = 0,
            },
            new() { ReconstructionObjectPayloadBytes = 0, LogicalVersionOrdinal = 2 },
            new() {
                ReconstructionObjectPayloadBytes = 0,
                LogicalVersionOrdinal = 2,
                ParentFrameTicket = parent,
                ExpectedParentBasePayloadBytes = 0,
            },
            new() {
                PayloadBytes = 1,
                ReconstructionObjectPayloadBytes = 1,
                ResultBasePayloadBytes = 2,
            },
            new() {
                Kind = ObjectVersionKind.Delta,
                PayloadBytes = 1,
                ReconstructionObjectPayloadBytes = 1,
                ResultBasePayloadBytes = 1,
                LogicalVersionOrdinal = 2,
                ParentFrameTicket = parent,
            },
            new() {
                Kind = ObjectVersionKind.Delta,
                PayloadBytes = 1,
                ReconstructionObjectPayloadBytes = 1,
                ResultBasePayloadBytes = 1,
                ExpectedParentBasePayloadBytes = -1,
                LogicalVersionOrdinal = 2,
                ParentFrameTicket = parent,
            },
            new() {
                Kind = ObjectVersionKind.Delta,
                ReconstructionObjectPayloadBytes = 0,
                ResultBasePayloadBytes = 1,
                ExpectedParentBasePayloadBytes = 0,
                LogicalVersionOrdinal = 2,
                ParentFrameTicket = parent,
            },
        ];

        foreach (ObjectVersionBuilder builder in invalidBuilders) {
            Assert.ThrowsAny<ArgumentException>(() => builder.Build());
        }
    }

    [Fact]
    public void Build_requires_and_validates_reconstruction_payload_bytes() {
        RelativeFrameTicket parent = new(
            IsPreviousFile: false,
            FrameTicket: new FrameTicket(4, 24));
        ObjectVersionBuilder missing = new();
        ObjectVersionBuilder baseMismatch = new() {
            PayloadBytes = 10,
            ReconstructionObjectPayloadBytes = 11,
            ResultBasePayloadBytes = 10,
        };
        ObjectVersionBuilder deltaTooSmall = new() {
            Kind = ObjectVersionKind.Delta,
            PayloadBytes = 10,
            ReconstructionObjectPayloadBytes = 9,
            ResultBasePayloadBytes = 10,
            ExpectedParentBasePayloadBytes = 10,
            LogicalVersionOrdinal = 2,
            ParentFrameTicket = parent,
        };

        Assert.Throws<InvalidOperationException>(() => missing.Build());
        Assert.Throws<ArgumentException>(() => baseMismatch.Build());
        Assert.Throws<ArgumentException>(() => deltaTooSmall.Build());
    }

    [Fact]
    public void Built_frame_exposes_a_read_only_dictionary() {
        FrameBuilder builder = new();
        builder.Add(1).ReconstructionObjectPayloadBytes = 0;
        Frame frame = builder.Build();
        IDictionary<uint, ObjectVersion> dictionary =
            Assert.IsAssignableFrom<IDictionary<uint, ObjectVersion>>(frame.ObjectVersions);

        Assert.True(dictionary.IsReadOnly);
        Assert.Throws<NotSupportedException>(
            () => dictionary.Add(
                2,
                new ObjectVersionBuilder { ReconstructionObjectPayloadBytes = 0 }.Build()));
    }

    [Fact]
    public void FrameTicket_rejects_invalid_offsets_and_lengths() {
        Assert.Throws<ArgumentOutOfRangeException>(() => new FrameTicket(0, 24));
        Assert.Throws<ArgumentOutOfRangeException>(() => new FrameTicket(5, 24));
        Assert.Throws<ArgumentOutOfRangeException>(() => new FrameTicket(4, 20));
        Assert.Throws<ArgumentOutOfRangeException>(() => new FrameTicket(4, 25));
        Assert.Throws<ArgumentOutOfRangeException>(
            () => new FrameTicket(
                RbfV040Layout.MaxNativeFrameStartOffsetBytes + RbfV040Layout.AlignmentBytes,
                24));
        Assert.Throws<ArgumentOutOfRangeException>(
            () => new FrameTicket(4, RbfV040Layout.MaxFrameLengthBytes + 4));

        FrameTicket maximum = new(
            RbfV040Layout.MaxNativeFrameStartOffsetBytes,
            RbfV040Layout.MaxFrameLengthBytes);
        Assert.Equal(
            RbfV040Layout.MaxNativeFrameStartOffsetBytes + RbfV040Layout.MaxFrameLengthBytes,
            maximum.EndOffsetExclusive);
    }

    [Fact]
    public void AbsoluteFrameAddress_rejects_zero_file_number() {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => new AbsoluteFrameAddress(0, new FrameTicket(4, 24)));
    }
}
