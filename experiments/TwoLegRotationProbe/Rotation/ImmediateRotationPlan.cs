using System.Collections.ObjectModel;
using Atelia.TwoLegRotationProbe.Model;

namespace Atelia.TwoLegRotationProbe.Rotation;

/// <summary>
/// Constructive witness that the supplied A/B state can be rotated immediately into B/C
/// using at most one B relay revision and one C evacuation revision.
/// </summary>
internal sealed class ImmediateRotationPlan {
    private readonly ReadOnlyCollection<uint> _evacuationObjectIds;
    private readonly ReadOnlyCollection<uint> _relayObjectIds;
    private readonly ReadOnlyDictionary<uint, AbsoluteFrameAddress> _projectedStateMap;

    internal ImmediateRotationPlan(
        uint previousFileNumber,
        uint currentFileNumber,
        uint nextFileNumber,
        AbsoluteFrameAddress publishedRevisionAddress,
        IEnumerable<uint> evacuationObjectIds,
        IEnumerable<uint> relayObjectIds,
        PlannedRevisionV0? relayRevision,
        PlannedRevisionV0 evacuationRevision,
        IReadOnlyDictionary<uint, AbsoluteFrameAddress> projectedStateMap) {
        ArgumentNullException.ThrowIfNull(evacuationObjectIds);
        ArgumentNullException.ThrowIfNull(relayObjectIds);
        ArgumentNullException.ThrowIfNull(evacuationRevision);
        ArgumentNullException.ThrowIfNull(projectedStateMap);

        PreviousFileNumber = previousFileNumber;
        CurrentFileNumber = currentFileNumber;
        NextFileNumber = nextFileNumber;
        PublishedRevisionAddress = publishedRevisionAddress;
        _evacuationObjectIds = Array.AsReadOnly(evacuationObjectIds.ToArray());
        _relayObjectIds = Array.AsReadOnly(relayObjectIds.ToArray());
        RelayRevision = relayRevision;
        EvacuationRevision = evacuationRevision;
        _projectedStateMap = new ReadOnlyDictionary<uint, AbsoluteFrameAddress>(
            new Dictionary<uint, AbsoluteFrameAddress>(projectedStateMap));
    }

    public uint PreviousFileNumber { get; }

    public uint CurrentFileNumber { get; }

    public uint NextFileNumber { get; }

    public AbsoluteFrameAddress PublishedRevisionAddress { get; }

    public IReadOnlyList<uint> EvacuationObjectIds => _evacuationObjectIds;

    public IReadOnlyList<uint> RelayObjectIds => _relayObjectIds;

    public PlannedRevisionV0? RelayRevision { get; }

    public PlannedRevisionV0 EvacuationRevision { get; }

    public IReadOnlyDictionary<uint, AbsoluteFrameAddress> ProjectedStateMap =>
        _projectedStateMap;
}
