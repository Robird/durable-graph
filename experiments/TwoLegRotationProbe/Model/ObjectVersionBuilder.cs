namespace Atelia.TwoLegRotationProbe.Model;

internal sealed class ObjectVersionBuilder {
    public ObjectVersionKind Kind { get; set; } = ObjectVersionKind.Base;

    public int PayloadBytes { get; set; }

    public int ResultBasePayloadBytes { get; set; }

    public int? ExpectedParentBasePayloadBytes { get; set; }

    public int VersionOrdinal { get; set; } = 1;

    public RelativeFrameTicket? ParentFrameTicket { get; set; }

    public ObjectVersion Build() => new(
        Kind,
        PayloadBytes,
        ResultBasePayloadBytes,
        ExpectedParentBasePayloadBytes,
        VersionOrdinal,
        ParentFrameTicket);
}
