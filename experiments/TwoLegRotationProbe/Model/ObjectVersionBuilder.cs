namespace Atelia.TwoLegRotationProbe.Model;

internal sealed class ObjectVersionBuilder {
    public ParentId? ParentId { get; set; }

    public ObjectVersion Build() => new(ParentId);
}
