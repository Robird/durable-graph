using Atelia.MultiSegmentStateStoreProbe.Model;
using Atelia.MultiSegmentStateStoreProbe.Storage;

namespace Atelia.MultiSegmentStateStoreProbe.Planning;

/// <summary>
/// G1 append/publish seam. The existing tail selects the append Segment before the
/// logical plan is rendered once at that final origin.
/// </summary>
internal sealed class InMemoryFrameCommitSession {
    public InMemoryFrameCommitSession(
        InMemorySegmentStore store,
        long rolloverThresholdBytes) {
        ArgumentNullException.ThrowIfNull(store);
        if (rolloverThresholdBytes <=
                ProvisionalFrameEnvelopeEstimator.InitialTailOffsetBytes ||
            rolloverThresholdBytes >
                ProvisionalFrameEnvelopeEstimator.MaxFrameStartOffsetBytes ||
            (rolloverThresholdBytes &
                ProvisionalFrameEnvelopeEstimator.AlignmentMask) != 0) {
            throw new ArgumentOutOfRangeException(
                nameof(rolloverThresholdBytes),
                rolloverThresholdBytes,
                "The rollover threshold must be aligned, greater than the header-only " +
                "tail, and no greater than the largest representable Frame start.");
        }

        Store = store;
        RolloverThresholdBytes = rolloverThresholdBytes;
    }

    public InMemorySegmentStore Store { get; }

    public long RolloverThresholdBytes { get; }

    public AbsoluteFrameAddress? PublishedHead { get; private set; }

    public FrameCommitAttempt Commit(OriginFreeFramePlan plan) {
        ArgumentNullException.ThrowIfNull(plan);
        InMemorySegment? current = Store.CurrentSegment;
        FileNumber currentFileNumber = Store.AppendFileNumber;
        long currentTail = current?.TailOffsetBytes ??
            ProvisionalFrameEnvelopeEstimator.InitialTailOffsetBytes;
        if (current is not null && currentTail >= RolloverThresholdBytes) {
            try {
                currentFileNumber = currentFileNumber.Next();
            } catch (OverflowException) {
                return new CapacityRejectedFrameCommit(new FrameCapacityRejection(
                    FrameCapacityLimit.FileNumber,
                    checked((long)currentFileNumber.Value + 1),
                    uint.MaxValue));
            }

            currentTail = ProvisionalFrameEnvelopeEstimator.InitialTailOffsetBytes;
        }

        RenderedFrameCandidate appendedCandidate;
        try {
            appendedCandidate = FramePlanRenderer.RenderAndMeasure(
                plan,
                currentFileNumber,
                currentTail);
        } catch (FrameCapacityException exception) {
            return new CapacityRejectedFrameCommit(exception.Rejection);
        }

        Store.Append(appendedCandidate);
        PublishedHead = appendedCandidate.Address;
        return new PublishedFrameCommit(
            appendedCandidate,
            appendedCandidate.Address);
    }
}
