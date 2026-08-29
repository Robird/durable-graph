namespace Atelia.TwoLegRotationProbe.Model;

internal sealed class ObjectVersionDictionaryBuilder {
    public ObjectVersionDictionaryKind Kind { get; set; } = ObjectVersionDictionaryKind.Base;

    public RelativeFrameTicket? ParentRevisionFrameTicket { get; set; }

    public Dictionary<uint, ObjectVersionDictionaryBinding> Entries { get; } = [];

    public void BindSelf(uint objectId) =>
        Entries.Add(objectId, ObjectVersionDictionaryBinding.BindSelf());

    public void BindExternal(uint objectId, RelativeFrameTicket frameTicket) =>
        Entries.Add(objectId, ObjectVersionDictionaryBinding.BindExternal(frameTicket));

    public void Remove(uint objectId) =>
        Entries.Add(objectId, ObjectVersionDictionaryBinding.Remove());

    public ObjectVersionDictionary Build() => new(
        Kind,
        ParentRevisionFrameTicket,
        Entries);
}
