using System.Collections.ObjectModel;
using Atelia.MultiSegmentStateStoreProbe.Model;

namespace Atelia.MultiSegmentStateStoreProbe.Planning;

/// <summary>
/// Minimal G1 logical plan. External references remain absolute until rendered for a
/// selected Segment origin.
/// </summary>
internal sealed class OriginFreeFramePlan {
    private readonly ReadOnlyCollection<AbsoluteFrameAddress> _externalReferences;

    public OriginFreeFramePlan(
        int syntheticPayloadBytes,
        IEnumerable<AbsoluteFrameAddress>? externalReferences = null) {
        ArgumentOutOfRangeException.ThrowIfNegative(syntheticPayloadBytes);
        SyntheticPayloadBytes = syntheticPayloadBytes;
        _externalReferences = Array.AsReadOnly(
            externalReferences?.ToArray() ?? []);
    }

    public OriginFreeFramePlan(OriginFreeRevisionFramePlan revisionFrame) {
        RevisionFrame = revisionFrame ??
            throw new ArgumentNullException(nameof(revisionFrame));
        SyntheticPayloadBytes = revisionFrame.SyntheticPayloadBytes;
        _externalReferences = Array.AsReadOnly(
            revisionFrame.ExternalReferences.ToArray());
    }

    public int SyntheticPayloadBytes { get; }

    public IReadOnlyList<AbsoluteFrameAddress> ExternalReferences =>
        _externalReferences;

    public OriginFreeRevisionFramePlan? RevisionFrame { get; }
}
