using Atelia.MultiSegmentStateStoreProbe.Model;
using Atelia.MultiSegmentStateStoreProbe.Planning;
using Atelia.MultiSegmentStateStoreProbe.Storage;

namespace Atelia.MultiSegmentStateStoreProbe.Tests;

public sealed class SoftRolloverTests {
    private const long OneEmptyFrameTargetBytes = 32;

    [Fact]
    public void Target_must_keep_future_frame_starts_representable() {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new InMemoryFrameCommitSession(
                new InMemorySegmentStore(),
                ProvisionalFrameEnvelopeEstimator.MaxFrameStartOffsetBytes + 1));
    }

    [Fact]
    public void Nonempty_crossing_rerenders_the_same_plan_at_the_next_origin() {
        InMemoryFrameCommitSession session = new(
            new InMemorySegmentStore(),
            OneEmptyFrameTargetBytes);
        PublishedFrameCommit first = Assert.IsType<PublishedFrameCommit>(
            session.Commit(new OriginFreeFramePlan(0)));
        for (int fileNumber = 2; fileNumber <= 128; fileNumber++) {
            PublishedFrameCommit seeded = Assert.IsType<PublishedFrameCommit>(
                session.Commit(new OriginFreeFramePlan(0)));
            Assert.Equal((uint)fileNumber, seeded.PublishedHead.FileNumber.Value);
        }

        OriginFreeFramePlan boundaryPlan = new(
            syntheticPayloadBytes: 1,
            externalReferences: [first.PublishedHead]);
        PublishedFrameCommit result = Assert.IsType<PublishedFrameCommit>(
            session.Commit(boundaryPlan));

        Assert.True(result.RolledOver);
        Assert.Same(boundaryPlan, result.InitialCandidate.Plan);
        Assert.Same(boundaryPlan, result.AppendedCandidate.Plan);
        Assert.Equal(127u, Assert.Single(
            result.InitialCandidate.RelativeReferences).BackwardFileDistance);
        Assert.Equal(128u, Assert.Single(
            result.AppendedCandidate.RelativeReferences).BackwardFileDistance);
        Assert.Equal(3, result.InitialCandidate.EncodedReferenceBytes.Length);
        Assert.Equal(4, result.AppendedCandidate.EncodedReferenceBytes.Length);
        Assert.NotEqual(
            result.InitialCandidate.Layout.FrameLengthBytes,
            result.AppendedCandidate.Layout.FrameLengthBytes);
        Assert.Equal(129u, result.PublishedHead.FileNumber.Value);
        Assert.Equal(result.PublishedHead, session.PublishedHead);
        Assert.Equal(129, session.Store.SegmentCount);
        Assert.Equal(1, session.Store.GetSegment(new FileNumber(128)).FrameCount);
        Assert.Same(
            result.AppendedCandidate,
            session.Store.Read(result.PublishedHead));
    }

    [Fact]
    public void Empty_oversize_is_accepted_and_the_next_commit_rolls_naturally() {
        InMemoryFrameCommitSession session = new(
            new InMemorySegmentStore(),
            OneEmptyFrameTargetBytes);

        PublishedFrameCommit oversize = Assert.IsType<PublishedFrameCommit>(
            session.Commit(new OriginFreeFramePlan(100)));
        Assert.False(oversize.RolledOver);
        Assert.Equal(1u, oversize.PublishedHead.FileNumber.Value);
        Assert.True(
            oversize.AppendedCandidate.Layout.TailOffsetAfterBytes >
            OneEmptyFrameTargetBytes);

        PublishedFrameCommit next = Assert.IsType<PublishedFrameCommit>(
            session.Commit(new OriginFreeFramePlan(0)));
        Assert.True(next.RolledOver);
        Assert.Equal(2u, next.PublishedHead.FileNumber.Value);
        Assert.Equal(2, session.Store.SegmentCount);
    }

    [Fact]
    public void Existing_empty_max_segment_is_reused_for_its_first_frame() {
        InMemorySegmentStore store = new(new FileNumber(uint.MaxValue));
        InMemorySegment empty = store.CreateEmptyAppendSegment();
        InMemoryFrameCommitSession session = new(store, OneEmptyFrameTargetBytes);

        PublishedFrameCommit result = Assert.IsType<PublishedFrameCommit>(
            session.Commit(new OriginFreeFramePlan(100)));

        Assert.Same(empty, store.CurrentSegment);
        Assert.Equal(1, store.SegmentCount);
        Assert.Equal(1, empty.FrameCount);
        Assert.Equal(uint.MaxValue, result.PublishedHead.FileNumber.Value);
        Assert.True(result.AppendedCandidate.Layout.TailOffsetAfterBytes >
            OneEmptyFrameTargetBytes);
    }

    [Fact]
    public void Hard_bound_rejection_does_not_append_or_publish() {
        InMemoryFrameCommitSession session = new(
            new InMemorySegmentStore(),
            OneEmptyFrameTargetBytes);

        CapacityRejectedFrameCommit rejected =
            Assert.IsType<CapacityRejectedFrameCommit>(session.Commit(
                new OriginFreeFramePlan(
                    ProvisionalFrameEnvelopeEstimator
                        .MaxPayloadAndMetadataLengthBytes + 1)));

        Assert.Equal(
            FrameCapacityLimit.PayloadAndMetadataLength,
            rejected.Rejection.Limit);
        Assert.Equal(0, session.Store.SegmentCount);
        Assert.Null(session.PublishedHead);
    }

    [Fact]
    public void Payload_plus_reference_overflow_is_a_typed_rejection() {
        InMemoryFrameCommitSession session = new(
            new InMemorySegmentStore(),
            OneEmptyFrameTargetBytes);
        AbsoluteFrameAddress earlier = new(
            new FileNumber(1),
            new FrameTicket(4, 24));

        CapacityRejectedFrameCommit rejected =
            Assert.IsType<CapacityRejectedFrameCommit>(session.Commit(
                new OriginFreeFramePlan(int.MaxValue, [earlier])));

        Assert.Equal(
            FrameCapacityLimit.PayloadAndMetadataLength,
            rejected.Rejection.Limit);
        Assert.True(rejected.Rejection.AttemptedValue > int.MaxValue);
        Assert.Equal(0, session.Store.SegmentCount);
        Assert.Null(session.PublishedHead);
    }

    [Fact]
    public void File_number_overflow_is_typed_and_preserves_the_old_head() {
        InMemorySegmentStore store = new(new FileNumber(uint.MaxValue));
        InMemoryFrameCommitSession session = new(
            store,
            OneEmptyFrameTargetBytes);
        PublishedFrameCommit first = Assert.IsType<PublishedFrameCommit>(
            session.Commit(new OriginFreeFramePlan(0)));

        CapacityRejectedFrameCommit rejected =
            Assert.IsType<CapacityRejectedFrameCommit>(
                session.Commit(new OriginFreeFramePlan(0)));

        Assert.Equal(FrameCapacityLimit.FileNumber, rejected.Rejection.Limit);
        Assert.Equal((long)uint.MaxValue + 1, rejected.Rejection.AttemptedValue);
        Assert.Equal(first.PublishedHead, session.PublishedHead);
        Assert.Equal(1, store.SegmentCount);
        Assert.Equal(1, store.CurrentSegment!.FrameCount);
        Assert.Equal(32, store.CurrentSegment.TailOffsetBytes);
    }
}
