using System.Collections.ObjectModel;
using Atelia.TwoLegRotationProbe.Model;
using Atelia.TwoLegRotationProbe.Workloads;

namespace Atelia.TwoLegRotationProbe.Simulation;

internal sealed class ObjectLineageInspection {
    public ObjectLineageInspection(
        uint objectId,
        LogicalObjectState headState,
        AbsoluteFrameAddress headAddress,
        AbsoluteFrameAddress rootAddress,
        IEnumerable<AbsoluteFrameAddress> objectVersionLineageAddresses,
        IEnumerable<ObjectVersionDictionaryLookupInspection> baseParentLookups) {
        ArgumentNullException.ThrowIfNull(objectVersionLineageAddresses);
        ArgumentNullException.ThrowIfNull(baseParentLookups);

        ObjectId = objectId;
        HeadState = headState;
        HeadAddress = headAddress;
        RootAddress = rootAddress;
        ObjectVersionLineageAddresses = Array.AsReadOnly(
            objectVersionLineageAddresses.ToArray());
        BaseParentLookups = Array.AsReadOnly(baseParentLookups.ToArray());
    }

    public uint ObjectId { get; }

    public LogicalObjectState HeadState { get; }

    public AbsoluteFrameAddress HeadAddress { get; }

    public AbsoluteFrameAddress RootAddress { get; }

    public ReadOnlyCollection<AbsoluteFrameAddress> ObjectVersionLineageAddresses { get; }

    public ReadOnlyCollection<ObjectVersionDictionaryLookupInspection> BaseParentLookups { get; }
}
