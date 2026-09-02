using Atelia.MultiSegmentStateStoreProbe.Model;
using Atelia.MultiSegmentStateStoreProbe.Reconstruction;
using Atelia.MultiSegmentStateStoreProbe.Storage;

namespace Atelia.MultiSegmentStateStoreProbe.Evaluation;

internal static class ColdHeadReadMeasurer {
    public static ColdHeadReadObservation Measure(
        InMemorySegmentStore store,
        AbsoluteFrameAddress publishedHead) {
        ArgumentNullException.ThrowIfNull(store);
        MaterializedCurrentState current = CurrentStateMaterializer.Materialize(
            store,
            publishedHead);
        long liveBaseBytes = 0;
        foreach (LogicalObjectState state in current.States.Values) {
            liveBaseBytes = checked(liveBaseBytes + state.BasePayloadBytes);
        }

        return new ColdHeadReadObservation(
            current.OvdRequiredFrameAddresses,
            current.ObjectReconstructionPaths.Values.SelectMany(static path => path),
            liveBaseBytes,
            address => {
                _ = store.Read(address);
                return address.FrameTicket.LengthBytes;
            });
    }
}
