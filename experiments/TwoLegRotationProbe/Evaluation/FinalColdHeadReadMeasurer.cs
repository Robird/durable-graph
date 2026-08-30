using Atelia.TwoLegRotationProbe.Model;
using Atelia.TwoLegRotationProbe.Simulation;

namespace Atelia.TwoLegRotationProbe.Evaluation;

internal static class FinalColdHeadReadMeasurer {
    public static FinalColdHeadReadObservation Measure(
        RbfFileStore store,
        AbsoluteFrameAddress publishedRevisionAddress) {
        ArgumentNullException.ThrowIfNull(store);

        ObjectVersionDictionaryMaterializationInspection materialization =
            ObjectVersionDictionaryReader.MaterializeLive(
                store,
                publishedRevisionAddress);
        List<AbsoluteFrameAddress> objectReconstructionFrames = [];
        foreach ((uint objectId, AbsoluteFrameAddress headAddress) in
            materialization.Bindings.OrderBy(static pair => pair.Key)) {
            ObjectReconstructionInspection reconstruction =
                PhysicalStateOracle.InspectObjectReconstruction(
                    store,
                    objectId,
                    headAddress);
            objectReconstructionFrames.AddRange(
                reconstruction.ReconstructionFrameAddresses);
        }

        return new FinalColdHeadReadObservation(
            materialization.DictionaryRevisionAddresses,
            objectReconstructionFrames,
            address => store.ReadLayout(address).FrameLengthBytes);
    }
}
