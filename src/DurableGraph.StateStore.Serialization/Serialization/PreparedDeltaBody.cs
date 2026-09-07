namespace Atelia.DurableGraph.StateStore.Serialization;

/// <summary>
/// Owns the change decision and encoded body produced by one Delta preparation.
/// The body can be reused after representation selection without re-encoding.
/// </summary>
/// <remarks>
/// The producer supplies a consistent decision and body for the same frozen
/// prior/current pair. This container does not validate the body format, Schema,
/// or prior identity. An unchanged result can contain a nonempty zero bitmap.
/// </remarks>
public sealed class PreparedDeltaBody {
    private readonly byte[] _body;

    /// <summary>
    /// Copies the body. The input must remain stable during this call;
    /// subsequent changes to its backing storage cannot change this result.
    /// </summary>
    public PreparedDeltaBody(bool hasChanges, ReadOnlySpan<byte> body) {
        HasChanges = hasChanges;
        _body = body.ToArray();
    }

    public bool HasChanges { get; }
    public ReadOnlySpan<byte> Body => _body;
}
