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
        SemanticMetadataPayloadBytes = 0;
        TailMetadataBytes = 0;
        _externalReferences = Array.AsReadOnly(
            externalReferences?.ToArray() ?? []);
    }

    public OriginFreeFramePlan(OriginFreeRevisionFramePlan revisionFrame) {
        RevisionFrame = revisionFrame ??
            throw new ArgumentNullException(nameof(revisionFrame));
        SyntheticPayloadBytes = revisionFrame.SyntheticPayloadBytes;
        SemanticMetadataPayloadBytes = revisionFrame.SemanticMetadataPayloadBytes;
        TailMetadataBytes = revisionFrame.TailMetadataBytes;
        _externalReferences = Array.AsReadOnly(
            revisionFrame.ExternalReferences.ToArray());
    }

    public long SyntheticPayloadBytes { get; }

    public int SemanticMetadataPayloadBytes { get; }

    public int TailMetadataBytes { get; }

    public IReadOnlyList<AbsoluteFrameAddress> ExternalReferences =>
        _externalReferences;

    public OriginFreeRevisionFramePlan? RevisionFrame { get; }
}
