namespace Atelia.DurableGraph.StateStore.Serialization;

/// <summary>
/// Owns a prepared raw Base body that can be reused after representation selection.
/// The body excludes any typed StateStore header, Storage record envelope and ObjectId.
/// This container carries no type authority and does not validate the body format or its Schema.
/// </summary>
public sealed class PreparedBaseBody {
    private readonly byte[] _body;

    /// <summary>
    /// Copies the body. The input must remain stable during this call;
    /// subsequent changes to its backing storage cannot change this result.
    /// </summary>
    public PreparedBaseBody(ReadOnlySpan<byte> body) {
        _body = body.ToArray();
    }

    public ReadOnlySpan<byte> Body => _body;
}
