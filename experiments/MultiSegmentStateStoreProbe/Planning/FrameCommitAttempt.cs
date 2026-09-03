using Atelia.MultiSegmentStateStoreProbe.Model;
using Atelia.MultiSegmentStateStoreProbe.Storage;

namespace Atelia.MultiSegmentStateStoreProbe.Planning;

internal abstract record FrameCommitAttempt;

internal sealed record PublishedFrameCommit(
    RenderedFrameCandidate AppendedCandidate,
    AbsoluteFrameAddress PublishedHead) : FrameCommitAttempt;

internal sealed record CapacityRejectedFrameCommit(
    FrameCapacityRejection Rejection) : FrameCommitAttempt;
