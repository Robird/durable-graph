using Atelia.MultiSegmentStateStoreProbe.Model;
using Atelia.MultiSegmentStateStoreProbe.Normalization;
using Atelia.MultiSegmentStateStoreProbe.Reconstruction;
using Atelia.MultiSegmentStateStoreProbe.Storage;
using Atelia.MultiSegmentStateStoreProbe.Workloads;
using ModelState = Atelia.MultiSegmentStateStoreProbe.Model.LogicalObjectState;

namespace Atelia.MultiSegmentStateStoreProbe.Planning;

/// <summary>
/// G3 in-memory Save boundary. Logical planning, origin-dependent rendering, append,
/// publication, and derived-cache installation are distinct phases.
/// </summary>
internal sealed class RevisionCommitSession {
    private readonly HashSet<uint> _usedObjectIds = [];

    public RevisionCommitSession(
        InMemorySegmentStore store,
        long targetFileBytes) {
        ArgumentNullException.ThrowIfNull(store);
        if (targetFileBytes < ProvisionalFrameEnvelopeEstimator.InitialTailOffsetBytes ||
            targetFileBytes > ProvisionalFrameEnvelopeEstimator.MaxFrameStartOffsetBytes) {
            throw new ArgumentOutOfRangeException(nameof(targetFileBytes));
        }

        if (store.SegmentCount != 0) {
            throw new InvalidOperationException(
                "A stage-A RevisionCommitSession must start from an empty in-memory Store.");
        }

        Store = store;
        TargetFileBytes = targetFileBytes;
    }

    public InMemorySegmentStore Store { get; }

    public long TargetFileBytes { get; }

    public AbsoluteFrameAddress? PublishedHead { get; private set; }

    public MaterializedCurrentState? CachedState { get; private set; }

    internal IReadOnlySet<uint> UsedObjectIds => _usedObjectIds;

    public RevisionCommitAttempt Commit(
        SaveStep step,
        AbsoluteFrameAddress? expectedParent,
        RevisionSaveSelection selection,
        RevisionCommitHooks? hooks = null) {
        ArgumentNullException.ThrowIfNull(step);
        ArgumentNullException.ThrowIfNull(selection);
        hooks ??= new RevisionCommitHooks();

        if (PublishedHead != expectedParent) {
            return Stale(expectedParent, PublishedHead);
        }

        NormalizedSaveFacts facts;
        RevisionPlan plan;
        try {
            facts = RevisionSaveNormalizer.Normalize(
                Store,
                expectedParent,
                _usedObjectIds,
                step);
            plan = RevisionPlanner.Create(facts, selection);
        } catch (Exception exception) when (
            exception is ArgumentException or InvalidDataException or OverflowException) {
            return new RejectedRevisionCommit(
                RevisionCommitRejectionKind.InvalidInput,
                exception.Message);
        }

        (RenderedFrameCandidate Initial, RenderedFrameCandidate Final)? placement;
        try {
            placement = RenderPlacement(plan);
        } catch (FrameCapacityException exception) {
            return new RejectedRevisionCommit(
                RevisionCommitRejectionKind.Capacity,
                exception.Message,
                exception.Rejection);
        } catch (OverflowException exception) {
            return new RejectedRevisionCommit(
                RevisionCommitRejectionKind.Capacity,
                exception.Message,
                new FrameCapacityRejection(
                    FrameCapacityLimit.FileNumber,
                    (long)Store.AppendFileNumber.Value + 1,
                    uint.MaxValue));
        }

        RenderedFrameCandidate initialCandidate = placement.Value.Initial;
        RenderedFrameCandidate finalCandidate = placement.Value.Final;
        MaterializedCurrentState admitted;
        try {
            admitted = CurrentStateMaterializer.Materialize(
                Store,
                finalCandidate.Address,
                finalCandidate);
            ValidatePostState(facts, admitted);
        } catch (InvalidDataException exception) {
            return new RejectedRevisionCommit(
                RevisionCommitRejectionKind.Admission,
                exception.Message);
        }

        AbsoluteFrameAddress? observedHead = hooks.ObserveHeadBeforeAppend is { } observe
            ? observe()
            : PublishedHead;
        if (observedHead != expectedParent || PublishedHead != expectedParent) {
            return Stale(expectedParent, observedHead);
        }

        Store.Append(finalCandidate);
        if (hooks.FailAfterAppendBeforePublish) {
            return new AppendedUnpublishedRevisionCommit(
                plan,
                finalCandidate,
                PublishedHead);
        }

        PublishedHead = finalCandidate.Address;
        _usedObjectIds.UnionWith(facts.Inserts.Select(static insert => insert.ObjectId));
        if (hooks.FailCacheInstallation) {
            CachedState = null;
            return new PublishedRevisionCommit(
                plan,
                initialCandidate,
                finalCandidate,
                finalCandidate.Address,
                CacheInstalled: false,
                CachedState: null);
        }

        try {
            CachedState = CurrentStateMaterializer.Materialize(
                Store,
                finalCandidate.Address);
        } catch (InvalidDataException) {
            CachedState = null;
            return new PublishedRevisionCommit(
                plan,
                initialCandidate,
                finalCandidate,
                finalCandidate.Address,
                CacheInstalled: false,
                CachedState: null);
        }

        return new PublishedRevisionCommit(
            plan,
            initialCandidate,
            finalCandidate,
            finalCandidate.Address,
            CacheInstalled: true,
            CachedState);
    }

    public MaterializedCurrentState LoadCurrent() => PublishedHead is { } head
        ? CurrentStateMaterializer.Materialize(Store, head)
        : throw new InvalidOperationException("The Store has no PublishedHead.");

    private (RenderedFrameCandidate Initial, RenderedFrameCandidate Final)
        RenderPlacement(RevisionPlan plan) {
        InMemorySegment? current = Store.CurrentSegment;
        FileNumber currentFile = Store.AppendFileNumber;
        long currentTail = current?.TailOffsetBytes ??
            ProvisionalFrameEnvelopeEstimator.InitialTailOffsetBytes;
        RenderedFrameCandidate initial = FramePlanRenderer.RenderAndMeasure(
            plan.EnvelopePlan,
            currentFile,
            currentTail);
        if (current is not { IsEmpty: false } ||
            initial.Layout.TailOffsetAfterBytes <= TargetFileBytes) {
            return (initial, initial);
        }

        FileNumber nextFile = currentFile.Next();
        RenderedFrameCandidate rerendered = FramePlanRenderer.RenderAndMeasure(
            plan.EnvelopePlan,
            nextFile,
            ProvisionalFrameEnvelopeEstimator.InitialTailOffsetBytes);
        return (initial, rerendered);
    }

    private static void ValidatePostState(
        NormalizedSaveFacts facts,
        MaterializedCurrentState admitted) {
        if (facts.PostLiveStates.Count != admitted.States.Count ||
            facts.PostLiveStates.Any(pair =>
                !admitted.States.TryGetValue(pair.Key, out ModelState actual) ||
                actual != pair.Value)) {
            throw new InvalidDataException(
                "Candidate reconstruction does not equal the normalized post-state.");
        }
    }

    private static RejectedRevisionCommit Stale(
        AbsoluteFrameAddress? expected,
        AbsoluteFrameAddress? actual) => new(
            RevisionCommitRejectionKind.StaleParent,
            $"Expected parent {expected?.ToString() ?? "None"}, but authority is " +
            $"{actual?.ToString() ?? "None"}.");
}
