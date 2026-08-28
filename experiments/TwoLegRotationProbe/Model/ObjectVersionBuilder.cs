namespace Atelia.TwoLegRotationProbe.Model;

internal sealed class ObjectVersionBuilder {
    public int? ParentId { get; set; }

    public ObjectVersion Build() => new(ParentId);
}
