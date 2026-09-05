namespace Atelia.DurableGraph;

/// <summary>
/// Assigns a stable schema identity and version to a durable CLR type.
/// </summary>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
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

    /// <summary>
    /// Generates schema metadata and exact version history without the prototype boxed serializer.
    /// All durable types in an inheritance chain must explicitly select this mode.
    /// </summary>
    public bool SchemaOnly { get; set; }
}
