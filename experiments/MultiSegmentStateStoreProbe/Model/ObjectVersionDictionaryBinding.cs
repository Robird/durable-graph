namespace Atelia.MultiSegmentStateStoreProbe.Model;

internal readonly record struct ObjectVersionDictionaryBinding {
    private ObjectVersionDictionaryBinding(
        ObjectVersionDictionaryBindingKind kind,
        RelativeFrameTicket? externalReference) {
        Kind = kind;
        ExternalReference = externalReference;
    }

    public ObjectVersionDictionaryBindingKind Kind { get; }

    public RelativeFrameTicket? ExternalReference { get; }

    public static ObjectVersionDictionaryBinding BindSelf() =>
        new(ObjectVersionDictionaryBindingKind.BindSelf, null);

    public static ObjectVersionDictionaryBinding External(
        RelativeFrameTicket reference) =>
        new(ObjectVersionDictionaryBindingKind.External, reference);

    public static ObjectVersionDictionaryBinding Remove() =>
        new(ObjectVersionDictionaryBindingKind.Remove, null);
}
