namespace Atelia.TwoLegRotationProbe.Model;

internal sealed class ObjectVersion {
    private readonly int? _parentId;

    internal ObjectVersion(int? parentId) {
        if (parentId < 0) {
            throw new ArgumentOutOfRangeException(nameof(parentId));
        }

        _parentId = parentId;
    }

    public int? ParentId => _parentId;
}
