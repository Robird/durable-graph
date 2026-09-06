namespace Atelia.DurableGraph.StateStore.Storage;

/// <summary>An immutable, complete opaque Base body for one local ObjectId.</summary>
public sealed class BaseObjectRecord {
    private readonly byte[] _body;

    /// <summary>Copies the body. The caller must keep the input stable during this call.</summary>
    public BaseObjectRecord(uint objectId, ReadOnlySpan<byte> body) {
        if (objectId == 0) {
            throw new ArgumentOutOfRangeException(nameof(objectId), objectId, "ObjectId 0 is reserved.");
        }

        ObjectId = objectId;
        _body = body.ToArray();
    }

    public uint ObjectId { get; }

    /// <summary>The complete body, including an explicitly supplied empty body.</summary>
    public ReadOnlySpan<byte> Body => _body;
}
