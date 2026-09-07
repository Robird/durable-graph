namespace Atelia.DurableGraph.StateStore;

/// <summary>An owned decoded Base type header and its raw body.</summary>
/// <remarks>The Schema key is meaningful only in the caller's repository SchemaStore.</remarks>
internal sealed class DecodedBaseObjectBody {
    private readonly byte[] _body;

    internal DecodedBaseObjectBody(ObjectStateKind kind, SchemaKey? schemaKey, ReadOnlySpan<byte> body) {
        Kind = kind;
        SchemaKey = schemaKey;
        _body = body.ToArray();
    }

    internal ObjectStateKind Kind { get; }
    internal SchemaKey? SchemaKey { get; }
    internal ReadOnlySpan<byte> Body => _body;
}
