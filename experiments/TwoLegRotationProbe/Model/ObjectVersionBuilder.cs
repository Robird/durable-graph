namespace Atelia.TwoLegRotationProbe.Model;

internal sealed class ObjectVersionBuilder {
    public RelativeFrameTicket? ParentFrameTicket { get; set; }

    public ObjectVersion Build() => new(ParentFrameTicket);
}
