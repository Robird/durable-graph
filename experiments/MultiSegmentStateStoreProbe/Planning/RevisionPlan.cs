using Atelia.MultiSegmentStateStoreProbe.Normalization;

namespace Atelia.MultiSegmentStateStoreProbe.Planning;

/// <summary>
/// The one frozen logical Save candidate. Placement may render it at more than one origin,
/// but may not change its representation decisions or absolute dependencies.
/// </summary>
internal sealed class RevisionPlan {
    internal RevisionPlan(
        NormalizedSaveFacts facts,
        RevisionSaveSelection selection,
        OriginFreeRevisionFramePlan framePlan) {
        Facts = facts ?? throw new ArgumentNullException(nameof(facts));
        Selection = selection ?? throw new ArgumentNullException(nameof(selection));
        FramePlan = framePlan ?? throw new ArgumentNullException(nameof(framePlan));
        EnvelopePlan = new OriginFreeFramePlan(framePlan);
    }

    public NormalizedSaveFacts Facts { get; }

    public RevisionSaveSelection Selection { get; }

    public OriginFreeRevisionFramePlan FramePlan { get; }

    public OriginFreeFramePlan EnvelopePlan { get; }
}
