namespace Atelia.TwoLegRotationProbe.Model;

internal readonly record struct ObjectVersionDictionaryBinding {
    internal ObjectVersionDictionaryBinding(
        ObjectVersionDictionaryBindingKind kind,
        RelativeFrameTicket? externalFrameTicket) {
        Kind = kind;
        ExternalFrameTicket = externalFrameTicket;
    }

    public ObjectVersionDictionaryBindingKind Kind { get; }

    public RelativeFrameTicket? ExternalFrameTicket { get; }

    public static ObjectVersionDictionaryBinding BindSelf() =>
        new(ObjectVersionDictionaryBindingKind.Self, null);

    public static ObjectVersionDictionaryBinding BindExternal(
        RelativeFrameTicket frameTicket) =>
        new(ObjectVersionDictionaryBindingKind.External, frameTicket);

    public static ObjectVersionDictionaryBinding Remove() =>
        new(ObjectVersionDictionaryBindingKind.Remove, null);
}
