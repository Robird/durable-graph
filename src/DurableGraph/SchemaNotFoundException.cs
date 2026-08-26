namespace Atelia.DurableGraph;

/// <summary>
/// Indicates that an exact schema identity and version is not registered.
/// </summary>
public sealed class SchemaNotFoundException : KeyNotFoundException {
    internal SchemaNotFoundException(string schemaId, int version)
        : base($"Schema '{schemaId}' version {version} is not registered.") {
        SchemaId = schemaId;
        Version = version;
    }

    public string SchemaId { get; }

    public int Version { get; }
}
