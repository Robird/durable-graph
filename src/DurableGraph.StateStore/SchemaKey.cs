namespace Atelia.DurableGraph.StateStore;

/// <summary>Identifies an exact schema definition within one SchemaStore.</summary>
public readonly record struct SchemaKey {
    public SchemaKey(string schemaId, int version) {
        ArgumentException.ThrowIfNullOrWhiteSpace(schemaId);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(version);
        SchemaId = schemaId;
        Version = version;
    }

    public string SchemaId { get; }
    public int Version { get; }

    internal void Validate() {
        ArgumentException.ThrowIfNullOrWhiteSpace(SchemaId);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(Version);
    }
}
