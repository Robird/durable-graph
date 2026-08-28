using System.Collections.ObjectModel;
using Atelia.TwoLegRotationProbe.Model;
using Atelia.TwoLegRotationProbe.Workloads;

namespace Atelia.TwoLegRotationProbe.Simulation;

internal sealed class ObjectReconstructionInspection {
    public ObjectReconstructionInspection(
        LogicalObjectState state,
        AbsoluteFrameAddress headAddress,
        AbsoluteFrameAddress baseAddress,
        IEnumerable<AbsoluteFrameAddress> reconstructionFrameAddresses) {
        ArgumentNullException.ThrowIfNull(reconstructionFrameAddresses);

        State = state;
        HeadAddress = headAddress;
        BaseAddress = baseAddress;
        ReconstructionFrameAddresses = Array.AsReadOnly(
            reconstructionFrameAddresses.ToArray());
    }

    public LogicalObjectState State { get; }

    public AbsoluteFrameAddress HeadAddress { get; }

    public AbsoluteFrameAddress BaseAddress { get; }

    public ReadOnlyCollection<AbsoluteFrameAddress> ReconstructionFrameAddresses { get; }
}
