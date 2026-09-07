namespace Atelia.DurableGraph.StateStore;

/// <summary>
/// Locates a Schema definition by its <c>(SchemaId, Version)</c> within one SchemaStore.
/// Exactness still requires equality of the complete definition.
/// </summary>
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
