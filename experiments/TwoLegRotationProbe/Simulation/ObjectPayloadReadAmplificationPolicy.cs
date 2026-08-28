namespace Atelia.TwoLegRotationProbe.Simulation;

internal static class ObjectPayloadReadAmplificationPolicy {
    public const long MaxReadAmplificationRatio = 3;

    public static bool ShouldWriteBase(
        int basePayloadBytes,
        int deltaPayloadBytes,
        long parentReconstructionObjectPayloadBytes) {
        ArgumentOutOfRangeException.ThrowIfNegative(basePayloadBytes);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(deltaPayloadBytes);
        ArgumentOutOfRangeException.ThrowIfNegative(parentReconstructionObjectPayloadBytes);

        if (basePayloadBytes <= deltaPayloadBytes) {
            return true;
        }

        long bytesSavedByDelta = (long)basePayloadBytes - deltaPayloadBytes;
        long maximumAcceptedSavings = checked(
            bytesSavedByDelta * MaxReadAmplificationRatio);
        return maximumAcceptedSavings <= parentReconstructionObjectPayloadBytes;
    }
}
