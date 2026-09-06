namespace Atelia.DurableGraph.StateStore;

/// <summary>An owned Base type header and its uninterpreted body.</summary>
/// <remarks>The Schema key is meaningful only in the caller's repository SchemaStore.</remarks>
public sealed class BaseObjectPayload {
    private readonly byte[] _body;

    internal BaseObjectPayload(CapturedObjectKind kind, SchemaKey? schemaKey, ReadOnlySpan<byte> body) {
        Kind = kind;
        SchemaKey = schemaKey;
        _body = body.ToArray();
    }

    public CapturedObjectKind Kind { get; }
    public SchemaKey? SchemaKey { get; }
    public ReadOnlySpan<byte> Body => _body;
}
