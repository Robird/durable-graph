namespace Atelia.TwoLegRotationProbe.Model;

internal sealed class ObjectVersion {
    internal ObjectVersion(
        ObjectVersionKind kind,
        int payloadBytes,
        int resultBasePayloadBytes,
        int? expectedParentBasePayloadBytes,
        int versionOrdinal,
        RelativeFrameTicket? parentFrameTicket) {
        if (!Enum.IsDefined(kind)) {
            throw new ArgumentOutOfRangeException(nameof(kind));
        }

        ArgumentOutOfRangeException.ThrowIfNegative(payloadBytes);
        ArgumentOutOfRangeException.ThrowIfNegative(resultBasePayloadBytes);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(versionOrdinal);

        if (versionOrdinal == 1) {
            if (kind != ObjectVersionKind.Base) {
                throw new ArgumentException("The first object version must be a Base version.", nameof(kind));
            }

            if (parentFrameTicket is not null) {
                throw new ArgumentException("The first object version cannot have a parent.", nameof(parentFrameTicket));
            }
        } else if (parentFrameTicket is null) {
            throw new ArgumentException("An object version after the first must have a parent.", nameof(parentFrameTicket));
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

                break;
            case ObjectVersionKind.Delta:
                if (expectedParentBasePayloadBytes is null) {
                    throw new ArgumentException(
                        "A Delta version must declare its expected parent base size.",
                        nameof(expectedParentBasePayloadBytes));
                }

                ArgumentOutOfRangeException.ThrowIfNegative(
                    expectedParentBasePayloadBytes.Value,
                    nameof(expectedParentBasePayloadBytes));
                ArgumentOutOfRangeException.ThrowIfZero(payloadBytes);
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(kind));
        }

        Kind = kind;
        PayloadBytes = payloadBytes;
        ResultBasePayloadBytes = resultBasePayloadBytes;
        ExpectedParentBasePayloadBytes = expectedParentBasePayloadBytes;
        VersionOrdinal = versionOrdinal;
        ParentFrameTicket = parentFrameTicket;
    }

    public ObjectVersionKind Kind { get; }

    public int PayloadBytes { get; }

    public int ResultBasePayloadBytes { get; }

    public int? ExpectedParentBasePayloadBytes { get; }

    public int VersionOrdinal { get; }

    public RelativeFrameTicket? ParentFrameTicket { get; }
}
