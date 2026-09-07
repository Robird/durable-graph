namespace Atelia.DurableGraph.StateStore.Serialization;

/// <summary>
/// Owns prepared bytes that can be reused after representation selection. Depending
/// on the call site, they are either a raw object body or a Base body that includes
/// its type header. Payload excludes the Storage record envelope and ObjectId; its
/// length alone is not the full Base record cost. This container carries no type
/// authority and does not validate the body format or its Schema.
/// </summary>
public sealed class PreparedBase {
    private readonly byte[] _payload;

    /// <summary>
    /// Copies the payload. The input must remain stable during this call;
    /// subsequent changes to its backing storage cannot change this result.
    /// </summary>
    public PreparedBase(ReadOnlySpan<byte> payload) {
        _payload = payload.ToArray();
    }

    public ReadOnlySpan<byte> Payload => _payload;
}
