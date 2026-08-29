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
        IEnumerable<AbsoluteFrameAddress> headToRootFrameAddresses) {
        ArgumentNullException.ThrowIfNull(headToRootFrameAddresses);

        ObjectId = objectId;
        HeadState = headState;
        HeadAddress = headAddress;
        RootAddress = rootAddress;
        HeadToRootFrameAddresses = Array.AsReadOnly(
            headToRootFrameAddresses.ToArray());
    }

    public uint ObjectId { get; }

    public LogicalObjectState HeadState { get; }

    public AbsoluteFrameAddress HeadAddress { get; }

    public AbsoluteFrameAddress RootAddress { get; }

    public ReadOnlyCollection<AbsoluteFrameAddress> HeadToRootFrameAddresses { get; }
}
