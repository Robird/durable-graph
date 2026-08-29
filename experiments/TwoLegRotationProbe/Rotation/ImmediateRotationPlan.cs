using System.Collections.ObjectModel;
using Atelia.TwoLegRotationProbe.Model;

namespace Atelia.TwoLegRotationProbe.Rotation;

/// <summary>
/// Constructive witness that the published A/B state can be rotated immediately into B/C
/// using one C evacuation revision.
/// </summary>
internal sealed class ImmediateRotationPlan {
    private readonly ReadOnlyCollection<uint> _evacuationObjectIds;

    internal ImmediateRotationPlan(
        uint previousFileNumber,
        uint currentFileNumber,
        uint nextFileNumber,
        AbsoluteFrameAddress publishedRevisionAddress,
        PlannedRevisionV0 evacuationRevision) {
        ArgumentNullException.ThrowIfNull(evacuationRevision);

        PreviousFileNumber = previousFileNumber;
        CurrentFileNumber = currentFileNumber;
        NextFileNumber = nextFileNumber;
        PublishedRevisionAddress = publishedRevisionAddress;
        EvacuationRevision = evacuationRevision;
        _evacuationObjectIds = Array.AsReadOnly(
            evacuationRevision.Frame.ObjectVersions.Keys.Order().ToArray());
    }

    public uint PreviousFileNumber { get; }

    public uint CurrentFileNumber { get; }

    public uint NextFileNumber { get; }

    public AbsoluteFrameAddress PublishedRevisionAddress { get; }

    public IReadOnlyList<uint> EvacuationObjectIds => _evacuationObjectIds;

    public PlannedRevisionV0 EvacuationRevision { get; }
}
