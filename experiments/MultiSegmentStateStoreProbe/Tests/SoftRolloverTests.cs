using Atelia.MultiSegmentStateStoreProbe.Model;
using Atelia.MultiSegmentStateStoreProbe.Planning;
using Atelia.MultiSegmentStateStoreProbe.Storage;

namespace Atelia.MultiSegmentStateStoreProbe.Tests;

public sealed class SoftRolloverTests {
    private const long OneEmptyFrameRolloverThresholdBytes = 32;

    [Fact]
    public void Threshold_must_be_above_the_header_aligned_and_keep_frame_starts_representable() {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new InMemoryFrameCommitSession(
                new InMemorySegmentStore(),
                ProvisionalFrameEnvelopeEstimator.InitialTailOffsetBytes));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new InMemoryFrameCommitSession(
                new InMemorySegmentStore(),
                ProvisionalFrameEnvelopeEstimator.InitialTailOffsetBytes + 1));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new InMemoryFrameCommitSession(
                new InMemorySegmentStore(),
                ProvisionalFrameEnvelopeEstimator.MaxFrameStartOffsetBytes + 1));

        _ = new InMemoryFrameCommitSession(
            new InMemorySegmentStore(),
            ProvisionalFrameEnvelopeEstimator.MaxFrameStartOffsetBytes);
    }

    [Fact]
    public void Crossing_append_stays_and_the_next_commit_rotates_before_render() {
        InMemoryFrameCommitSession session = new(
            new InMemorySegmentStore(),
            rolloverThresholdBytes: 64);
        PublishedFrameCommit first = Assert.IsType<PublishedFrameCommit>(
            session.Commit(new OriginFreeFramePlan(0)));
        Assert.Equal(32, session.Store.CurrentSegment!.TailOffsetBytes);

        PublishedFrameCommit crossing = Assert.IsType<PublishedFrameCommit>(
            session.Commit(new OriginFreeFramePlan(syntheticPayloadBytes: 40)));
        Assert.Equal(first.PublishedHead.FileNumber, crossing.PublishedHead.FileNumber);
        Assert.True(session.Store.CurrentSegment.TailOffsetBytes > 64);
        Assert.Equal(1, session.Store.SegmentCount);

        OriginFreeFramePlan nextPlan = new(
            syntheticPayloadBytes: 1,
            externalReferences: [first.PublishedHead]);
        PublishedFrameCommit next = Assert.IsType<PublishedFrameCommit>(
            session.Commit(nextPlan));
        Assert.Equal(2u, next.PublishedHead.FileNumber.Value);
        Assert.Same(nextPlan, next.AppendedCandidate.Plan);
        Assert.Equal(1u, Assert.Single(
            next.AppendedCandidate.RelativeReferences).BackwardFileDistance);
        Assert.Equal(next.PublishedHead, session.PublishedHead);
        Assert.Equal(2, session.Store.SegmentCount);
    }

    [Fact]
    public void Explicit_origins_reencode_the_same_absolute_reference() {
        AbsoluteFrameAddress earlier = new(
            new FileNumber(1),
            new FrameTicket(4, 24));
        OriginFreeFramePlan plan = new(
            syntheticPayloadBytes: 1,
            externalReferences: [earlier]);

        RenderedFrameCandidate distance127 = FramePlanRenderer.RenderAndMeasure(
            plan,
            new FileNumber(128),
            ProvisionalFrameEnvelopeEstimator.InitialTailOffsetBytes);
        RenderedFrameCandidate distance128 = FramePlanRenderer.RenderAndMeasure(
            plan,
            new FileNumber(129),
            ProvisionalFrameEnvelopeEstimator.InitialTailOffsetBytes);

        Assert.Equal(127u, Assert.Single(
            distance127.RelativeReferences).BackwardFileDistance);
        Assert.Equal(128u, Assert.Single(
            distance128.RelativeReferences).BackwardFileDistance);
        Assert.Equal(3, distance127.EncodedReferenceBytes.Length);
        Assert.Equal(4, distance128.EncodedReferenceBytes.Length);
        Assert.NotEqual(
            distance127.Layout.FrameLengthBytes,
            distance128.Layout.FrameLengthBytes);
        Assert.Equal(earlier, new FileScope(distance127.FileNumber).ResolveEarlier(
            distance127.Address.FrameTicket,
            Assert.Single(distance127.RelativeReferences)));
        Assert.Equal(earlier, new FileScope(distance128.FileNumber).ResolveEarlier(
            distance128.Address.FrameTicket,
            Assert.Single(distance128.RelativeReferences)));
    }

    [Fact]
    public void Tail_equal_to_threshold_rotates_before_render() {
        InMemoryFrameCommitSession session = new(
            new InMemorySegmentStore(),
            OneEmptyFrameRolloverThresholdBytes);
        PublishedFrameCommit first = Assert.IsType<PublishedFrameCommit>(
            session.Commit(new OriginFreeFramePlan(0)));
        Assert.Equal(OneEmptyFrameRolloverThresholdBytes,
            session.Store.CurrentSegment!.TailOffsetBytes);

        PublishedFrameCommit second = Assert.IsType<PublishedFrameCommit>(
            session.Commit(new OriginFreeFramePlan(0)));

        Assert.Equal(1u, first.PublishedHead.FileNumber.Value);
        Assert.Equal(2u, second.PublishedHead.FileNumber.Value);
        Assert.Equal(2, session.Store.SegmentCount);
    }

    [Fact]
    public void Empty_oversize_is_accepted_and_the_next_commit_rolls_naturally() {
        InMemoryFrameCommitSession session = new(
            new InMemorySegmentStore(),
            OneEmptyFrameRolloverThresholdBytes);

        PublishedFrameCommit oversize = Assert.IsType<PublishedFrameCommit>(
            session.Commit(new OriginFreeFramePlan(100)));
        Assert.Equal(1u, oversize.PublishedHead.FileNumber.Value);
        Assert.True(
            oversize.AppendedCandidate.Layout.TailOffsetAfterBytes >
            OneEmptyFrameRolloverThresholdBytes);

        PublishedFrameCommit next = Assert.IsType<PublishedFrameCommit>(
            session.Commit(new OriginFreeFramePlan(0)));
        Assert.Equal(2u, next.PublishedHead.FileNumber.Value);
        Assert.Equal(2, session.Store.SegmentCount);
    }

    [Fact]
    public void Existing_empty_max_segment_is_reused_for_its_first_frame() {
        InMemorySegmentStore store = new(new FileNumber(uint.MaxValue));
        InMemorySegment empty = store.CreateEmptyAppendSegment();
        InMemoryFrameCommitSession session = new(
            store,
            OneEmptyFrameRolloverThresholdBytes);

        PublishedFrameCommit result = Assert.IsType<PublishedFrameCommit>(
            session.Commit(new OriginFreeFramePlan(100)));

        Assert.Same(empty, store.CurrentSegment);
        Assert.Equal(1, store.SegmentCount);
        Assert.Equal(1, empty.FrameCount);
        Assert.Equal(uint.MaxValue, result.PublishedHead.FileNumber.Value);
        Assert.True(result.AppendedCandidate.Layout.TailOffsetAfterBytes >
            OneEmptyFrameRolloverThresholdBytes);
    }

    [Fact]
    public void Hard_bound_rejection_does_not_append_or_publish() {
        InMemoryFrameCommitSession session = new(
            new InMemorySegmentStore(),
            OneEmptyFrameRolloverThresholdBytes);

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
            OneEmptyFrameRolloverThresholdBytes);
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
            OneEmptyFrameRolloverThresholdBytes);
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
