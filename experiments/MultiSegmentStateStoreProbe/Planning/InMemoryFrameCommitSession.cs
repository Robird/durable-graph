using Atelia.MultiSegmentStateStoreProbe.Model;
using Atelia.MultiSegmentStateStoreProbe.Storage;

namespace Atelia.MultiSegmentStateStoreProbe.Planning;

/// <summary>
/// G1 append/publish seam. Soft placement may re-render one logical plan at the next
/// origin; it never changes the plan or retries a different representation.
/// </summary>
internal sealed class InMemoryFrameCommitSession {
    public InMemoryFrameCommitSession(
        InMemorySegmentStore store,
        long targetFileBytes) {
        ArgumentNullException.ThrowIfNull(store);
        if (targetFileBytes < ProvisionalFrameEnvelopeEstimator.InitialTailOffsetBytes) {
            throw new ArgumentOutOfRangeException(nameof(targetFileBytes));
        }

        if (targetFileBytes >
            ProvisionalFrameEnvelopeEstimator.MaxFrameStartOffsetBytes) {
            throw new ArgumentOutOfRangeException(
                nameof(targetFileBytes),
                targetFileBytes,
                "The target must not exceed the largest representable Frame start.");
        }

        Store = store;
        TargetFileBytes = targetFileBytes;
    }

    public InMemorySegmentStore Store { get; }

    public long TargetFileBytes { get; }

    public AbsoluteFrameAddress? PublishedHead { get; private set; }

    public FrameCommitAttempt Commit(OriginFreeFramePlan plan) {
        ArgumentNullException.ThrowIfNull(plan);
        InMemorySegment? current = Store.CurrentSegment;
        FileNumber currentFileNumber = Store.AppendFileNumber;
        long currentTail = current?.TailOffsetBytes ??
            ProvisionalFrameEnvelopeEstimator.InitialTailOffsetBytes;

        RenderedFrameCandidate initialCandidate;
        try {
            initialCandidate = FramePlanRenderer.RenderAndMeasure(
                plan,
                currentFileNumber,
                currentTail);
        } catch (FrameCapacityException exception) {
            return new CapacityRejectedFrameCommit(exception.Rejection);
        }

        RenderedFrameCandidate appendedCandidate = initialCandidate;
        if (current is { IsEmpty: false } &&
            initialCandidate.Layout.TailOffsetAfterBytes > TargetFileBytes) {
            FileNumber nextFileNumber;
            try {
                nextFileNumber = currentFileNumber.Next();
            } catch (OverflowException) {
                return new CapacityRejectedFrameCommit(new FrameCapacityRejection(
                    FrameCapacityLimit.FileNumber,
                    checked((long)currentFileNumber.Value + 1),
                    uint.MaxValue));
            }

            try {
                appendedCandidate = FramePlanRenderer.RenderAndMeasure(
                    plan,
                    nextFileNumber,
                    ProvisionalFrameEnvelopeEstimator.InitialTailOffsetBytes);
            } catch (FrameCapacityException exception) {
                return new CapacityRejectedFrameCommit(exception.Rejection);
            }
        }

        Store.Append(appendedCandidate);
        PublishedHead = appendedCandidate.Address;
        return new PublishedFrameCommit(
            initialCandidate,
            appendedCandidate,
            appendedCandidate.Address);
    }
}
