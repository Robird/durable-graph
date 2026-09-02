using Atelia.MultiSegmentStateStoreProbe.Model;
using Atelia.MultiSegmentStateStoreProbe.Planning;
using Atelia.MultiSegmentStateStoreProbe.Storage;

namespace Atelia.MultiSegmentStateStoreProbe.Tests;

public sealed class InMemorySegmentStoreTests {
    [Fact]
    public void Append_is_ordered_and_read_requires_the_complete_ticket() {
        InMemorySegmentStore store = new();
        OriginFreeFramePlan plan = new(syntheticPayloadBytes: 0);
        RenderedFrameCandidate first = FramePlanRenderer.RenderAndMeasure(
            plan,
            new FileNumber(1),
            ProvisionalFrameEnvelopeEstimator.InitialTailOffsetBytes);
        store.Append(first);
        RenderedFrameCandidate second = FramePlanRenderer.RenderAndMeasure(
            plan,
            new FileNumber(1),
            store.CurrentSegment!.TailOffsetBytes);
        store.Append(second);

        InMemorySegment segment = Assert.IsType<InMemorySegment>(store.CurrentSegment);
        Assert.Equal(2, segment.FrameCount);
        Assert.Equal(60, segment.TailOffsetBytes);
        Assert.Same(first, store.Read(first.Address));
        Assert.Same(second, store.Read(second.Address));
        Assert.Throws<KeyNotFoundException>(() => store.Read(
            new AbsoluteFrameAddress(
                new FileNumber(1),
                new FrameTicket(
                    first.Address.FrameTicket.OffsetBytes,
                    first.Address.FrameTicket.LengthBytes + 4))));
    }

    [Fact]
    public void Stale_candidate_rejection_does_not_change_tail_or_frame_count() {
        InMemorySegmentStore store = new();
        RenderedFrameCandidate candidate = FramePlanRenderer.RenderAndMeasure(
            new OriginFreeFramePlan(0),
            new FileNumber(1),
            ProvisionalFrameEnvelopeEstimator.InitialTailOffsetBytes);
        store.Append(candidate);
        InMemorySegment segment = Assert.IsType<InMemorySegment>(store.CurrentSegment);
        long tailBefore = segment.TailOffsetBytes;

        Assert.Throws<InvalidDataException>(() => store.Append(candidate));

        Assert.Equal(1, store.SegmentCount);
        Assert.Equal(1, segment.FrameCount);
        Assert.Equal(tailBefore, segment.TailOffsetBytes);
    }
}
