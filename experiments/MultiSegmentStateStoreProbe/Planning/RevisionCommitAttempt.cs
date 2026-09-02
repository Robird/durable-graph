using Atelia.MultiSegmentStateStoreProbe.Model;
using Atelia.MultiSegmentStateStoreProbe.Reconstruction;
using Atelia.MultiSegmentStateStoreProbe.Storage;

namespace Atelia.MultiSegmentStateStoreProbe.Planning;

internal abstract record RevisionCommitAttempt;

internal enum RevisionCommitRejectionKind : byte {
    StaleParent = 1,
    InvalidInput = 2,
    Capacity = 3,
    Admission = 4,
}

internal sealed record RejectedRevisionCommit(
    RevisionCommitRejectionKind Kind,
    string Message,
    FrameCapacityRejection? Capacity = null) : RevisionCommitAttempt;

internal sealed record AppendedUnpublishedRevisionCommit(
    RevisionPlan Plan,
    RenderedFrameCandidate AppendedCandidate,
    AbsoluteFrameAddress? RetainedPublishedHead) : RevisionCommitAttempt;

internal sealed record PublishedRevisionCommit(
    RevisionPlan Plan,
    RenderedFrameCandidate InitialCandidate,
    RenderedFrameCandidate AppendedCandidate,
    AbsoluteFrameAddress PublishedHead,
    bool CacheInstalled,
    MaterializedCurrentState? CachedState) : RevisionCommitAttempt {
    public bool RolledOver => InitialCandidate.FileNumber !=
        AppendedCandidate.FileNumber;
}

internal sealed class RevisionCommitHooks {
    /// <summary>
    /// Test seam for re-reading external head authority immediately before append.
    /// It does not mutate this session's head.
    /// </summary>
    public Func<AbsoluteFrameAddress?>? ObserveHeadBeforeAppend { get; init; }

    public bool FailAfterAppendBeforePublish { get; init; }

    public bool FailCacheInstallation { get; init; }
}
