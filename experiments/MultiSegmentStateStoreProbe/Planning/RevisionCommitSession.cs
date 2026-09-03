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
        long rolloverThresholdBytes) {
        ArgumentNullException.ThrowIfNull(store);
        if (rolloverThresholdBytes <=
                ProvisionalFrameEnvelopeEstimator.InitialTailOffsetBytes ||
            rolloverThresholdBytes >
                ProvisionalFrameEnvelopeEstimator.MaxFrameStartOffsetBytes ||
            (rolloverThresholdBytes &
                ProvisionalFrameEnvelopeEstimator.AlignmentMask) != 0) {
            throw new ArgumentOutOfRangeException(nameof(rolloverThresholdBytes));
        }

        if (store.SegmentCount != 0) {
            throw new InvalidOperationException(
                "A stage-A RevisionCommitSession must start from an empty in-memory Store.");
        }

        Store = store;
        RolloverThresholdBytes = rolloverThresholdBytes;
    }

    public InMemorySegmentStore Store { get; }

    public long RolloverThresholdBytes { get; }

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

        RenderedFrameCandidate finalCandidate;
        try {
            finalCandidate = RenderForAppendDestination(plan);
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
                finalCandidate,
                finalCandidate.Address,
                CacheInstalled: false,
                CachedState: null);
        }

        return new PublishedRevisionCommit(
            plan,
            finalCandidate,
            finalCandidate.Address,
            CacheInstalled: true,
            CachedState);
    }

    public MaterializedCurrentState LoadCurrent() => PublishedHead is { } head
        ? CurrentStateMaterializer.Materialize(Store, head)
        : throw new InvalidOperationException("The Store has no PublishedHead.");

    private RenderedFrameCandidate RenderForAppendDestination(RevisionPlan plan) {
        InMemorySegment? current = Store.CurrentSegment;
        FileNumber currentFile = Store.AppendFileNumber;
        long currentTail = current?.TailOffsetBytes ??
            ProvisionalFrameEnvelopeEstimator.InitialTailOffsetBytes;
        if (current is not null && currentTail >= RolloverThresholdBytes) {
            currentFile = currentFile.Next();
            currentTail = ProvisionalFrameEnvelopeEstimator.InitialTailOffsetBytes;
        }

        return FramePlanRenderer.RenderAndMeasure(
            plan.EnvelopePlan,
            currentFile,
            currentTail);
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
