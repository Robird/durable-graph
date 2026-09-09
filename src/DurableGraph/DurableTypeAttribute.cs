namespace Atelia.DurableGraph;

/// <summary>
/// Assigns a stable schema identity and version to a durable CLR type.
/// For enums, the schema records one underlying integer value; names, aliases,
/// and flags declarations are not part of the persistent layout.
/// </summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Struct | AttributeTargets.Enum, AllowMultiple = false, Inherited = false)]
public sealed class DurableTypeAttribute : Attribute {
    public DurableTypeAttribute(string schemaId, int version) {
        ArgumentException.ThrowIfNullOrWhiteSpace(schemaId);

        if (version <= 0) {
            throw new ArgumentOutOfRangeException(
                nameof(version),
                version,
                "Schema versions must be positive.");
        }

        SchemaId = schemaId;
        Version = version;
    }

    /// <summary>
    /// Gets the stable schema identity. An exact schema version is identified by
    /// the <see cref="SchemaId"/> and <see cref="Version"/> pair.
    /// </summary>
    public string SchemaId { get; }

    public int Version { get; }

}
