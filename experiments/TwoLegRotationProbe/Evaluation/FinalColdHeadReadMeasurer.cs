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
        long postLiveBasePayloadBytes = 0;
        foreach ((uint objectId, AbsoluteFrameAddress headAddress) in
            materialization.Bindings.OrderBy(static pair => pair.Key)) {
            ObjectReconstructionInspection reconstruction =
                PhysicalStateOracle.InspectObjectReconstruction(
                    store,
                    objectId,
                    headAddress);
            postLiveBasePayloadBytes = checked(
                postLiveBasePayloadBytes + reconstruction.State.BasePayloadBytes);
            objectReconstructionFrames.AddRange(
                reconstruction.ReconstructionFrameAddresses);
        }

        return new FinalColdHeadReadObservation(
            materialization.DictionaryRevisionAddresses,
            objectReconstructionFrames,
            postLiveBasePayloadBytes,
            address => store.ReadLayout(address).FrameLengthBytes);
    }
}
