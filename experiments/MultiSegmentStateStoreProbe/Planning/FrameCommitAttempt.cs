using Atelia.MultiSegmentStateStoreProbe.Model;
using Atelia.MultiSegmentStateStoreProbe.Storage;

namespace Atelia.MultiSegmentStateStoreProbe.Planning;

internal abstract record FrameCommitAttempt;

internal sealed record PublishedFrameCommit(
    RenderedFrameCandidate InitialCandidate,
    RenderedFrameCandidate AppendedCandidate,
    AbsoluteFrameAddress PublishedHead) : FrameCommitAttempt {
    public bool RolledOver => InitialCandidate.FileNumber !=
        AppendedCandidate.FileNumber;
}

internal sealed record CapacityRejectedFrameCommit(
    FrameCapacityRejection Rejection) : FrameCommitAttempt;
