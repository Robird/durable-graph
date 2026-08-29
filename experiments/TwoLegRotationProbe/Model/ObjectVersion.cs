namespace Atelia.TwoLegRotationProbe.Model;

internal sealed class ObjectVersion {
    internal ObjectVersion(
        ObjectVersionKind kind,
        int payloadBytes,
        long reconstructionObjectPayloadBytes,
        int resultBasePayloadBytes,
        int? expectedParentBasePayloadBytes,
        int logicalVersionOrdinal,
        RelativeFrameTicket? parentFrameTicket) {
        if (!Enum.IsDefined(kind)) {
            throw new ArgumentOutOfRangeException(nameof(kind));
        }

        ArgumentOutOfRangeException.ThrowIfNegative(payloadBytes);
        ArgumentOutOfRangeException.ThrowIfNegative(reconstructionObjectPayloadBytes);
        ArgumentOutOfRangeException.ThrowIfNegative(resultBasePayloadBytes);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(logicalVersionOrdinal);

        if (parentFrameTicket is null) {
            if (kind != ObjectVersionKind.Base) {
                throw new ArgumentException(
                    "An object version without a parent must be a Base version.",
                    nameof(kind));
            }

            if (logicalVersionOrdinal != 1) {
                throw new ArgumentException(
                    "An object version without a parent must have logical version ordinal 1.",
                    nameof(logicalVersionOrdinal));
            }
        }

        switch (kind) {
            case ObjectVersionKind.Base:
                if (expectedParentBasePayloadBytes is not null) {
                    throw new ArgumentException(
                        "A Base version cannot declare an expected parent base size.",
                        nameof(expectedParentBasePayloadBytes));
                }

                if (payloadBytes != resultBasePayloadBytes) {
                    throw new ArgumentException(
                        "A Base version payload size must equal its resulting base size.",
                        nameof(payloadBytes));
                }

                if (reconstructionObjectPayloadBytes != payloadBytes) {
                    throw new ArgumentException(
                        "A Base version reconstruction payload size must equal its payload size.",
                        nameof(reconstructionObjectPayloadBytes));
                }

                break;
            case ObjectVersionKind.Delta:
                if (parentFrameTicket is null) {
                    throw new ArgumentException(
                        "A Delta version must have a parent.",
                        nameof(parentFrameTicket));
                }

                if (expectedParentBasePayloadBytes is null) {
                    throw new ArgumentException(
                        "A Delta version must declare its expected parent base size.",
                        nameof(expectedParentBasePayloadBytes));
                }

                ArgumentOutOfRangeException.ThrowIfNegative(
                    expectedParentBasePayloadBytes.Value,
                    nameof(expectedParentBasePayloadBytes));
                if (payloadBytes == 0) {
                    throw new ArgumentException(
                        "A Delta version must have a positive payload size.",
                        nameof(payloadBytes));
                }

                if (reconstructionObjectPayloadBytes < payloadBytes) {
                    throw new ArgumentException(
                        "A Delta version reconstruction payload size cannot be smaller than its payload size.",
                        nameof(reconstructionObjectPayloadBytes));
                }

                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(kind));
        }

        Kind = kind;
        PayloadBytes = payloadBytes;
        ReconstructionObjectPayloadBytes = reconstructionObjectPayloadBytes;
        ResultBasePayloadBytes = resultBasePayloadBytes;
        ExpectedParentBasePayloadBytes = expectedParentBasePayloadBytes;
        LogicalVersionOrdinal = logicalVersionOrdinal;
        ParentFrameTicket = parentFrameTicket;
    }

    public ObjectVersionKind Kind { get; }

    public int PayloadBytes { get; }

    public long ReconstructionObjectPayloadBytes { get; }

    public int ResultBasePayloadBytes { get; }

    public int? ExpectedParentBasePayloadBytes { get; }

    public int LogicalVersionOrdinal { get; }

    public RelativeFrameTicket? ParentFrameTicket { get; }
}
