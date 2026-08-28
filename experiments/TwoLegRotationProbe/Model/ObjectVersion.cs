namespace Atelia.TwoLegRotationProbe.Model;

internal sealed class ObjectVersion {
    private readonly ParentId? _parentId;

    internal ObjectVersion(ParentId? parentId) {
        _parentId = parentId;
    }

    public ParentId? ParentId => _parentId;
}
