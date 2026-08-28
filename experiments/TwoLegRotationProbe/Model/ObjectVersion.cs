namespace Atelia.TwoLegRotationProbe.Model;

internal sealed class ObjectVersion {
    private readonly RelativeFrameTicket? _parentFrameTicket;

    internal ObjectVersion(RelativeFrameTicket? parentFrameTicket) {
        _parentFrameTicket = parentFrameTicket;
    }

    public RelativeFrameTicket? ParentFrameTicket => _parentFrameTicket;
}
