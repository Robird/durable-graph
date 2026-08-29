namespace Atelia.TwoLegRotationProbe.Model;

internal sealed class ObjectVersionBuilder {
    public ObjectVersionKind Kind { get; set; } = ObjectVersionKind.Base;

    public int PayloadBytes { get; set; }

    public long? ReconstructionObjectPayloadBytes { get; set; }

    public int ResultBasePayloadBytes { get; set; }

    public int? ExpectedParentBasePayloadBytes { get; set; }

    public int LogicalVersionOrdinal { get; set; } = 1;

    public RelativeFrameTicket? ParentFrameTicket { get; set; }

    public ObjectVersion Build() => new(
        Kind,
        PayloadBytes,
        ReconstructionObjectPayloadBytes
            ?? throw new InvalidOperationException(
                $"{nameof(ReconstructionObjectPayloadBytes)} must be set before Build."),
        ResultBasePayloadBytes,
        ExpectedParentBasePayloadBytes,
        LogicalVersionOrdinal,
        ParentFrameTicket);
}
