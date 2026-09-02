namespace Atelia.MultiSegmentStateStoreProbe.Model;

internal sealed class ObjectVersion {
    internal ObjectVersion(
        uint objectId,
        ObjectVersionKind kind,
        int payloadBytes,
        LogicalObjectState resultState,
        LogicalObjectState? expectedParentState,
        RelativeFrameTicket? deltaParent) {
        ArgumentOutOfRangeException.ThrowIfZero(objectId);
        if (!Enum.IsDefined(kind)) {
            throw new ArgumentOutOfRangeException(nameof(kind));
        }

        ArgumentOutOfRangeException.ThrowIfNegative(payloadBytes);
        switch (kind) {
            case ObjectVersionKind.Base:
                if (expectedParentState is not null || deltaParent is not null) {
                    throw new ArgumentException(
                        "A Base ObjectVersion cannot carry a direct parent.");
                }

                if (payloadBytes != resultState.BasePayloadBytes) {
                    throw new ArgumentException(
                        "A Base payload size must equal its resulting Base size.",
                        nameof(payloadBytes));
                }

                break;
            case ObjectVersionKind.Delta:
                if (expectedParentState is null || deltaParent is null) {
                    throw new ArgumentException(
                        "A Delta ObjectVersion requires an exact parent reference and expected state.");
                }

                if (payloadBytes == 0) {
                    throw new ArgumentException(
                        "A Delta payload must be positive.",
                        nameof(payloadBytes));
                }

                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(kind));
        }

        ObjectId = objectId;
        Kind = kind;
        PayloadBytes = payloadBytes;
        ResultState = resultState;
        ExpectedParentState = expectedParentState;
        DeltaParent = deltaParent;
    }

    public uint ObjectId { get; }

    public ObjectVersionKind Kind { get; }

    public int PayloadBytes { get; }

    public LogicalObjectState ResultState { get; }

    public LogicalObjectState? ExpectedParentState { get; }

    public RelativeFrameTicket? DeltaParent { get; }
}
