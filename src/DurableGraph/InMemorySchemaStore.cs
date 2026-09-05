namespace Atelia.DurableGraph;

/// <summary>
/// Stores exact schema versions in memory for prototype experiments.
/// </summary>
/// <remarks>This type does not provide persistence or thread-safety.</remarks>
public sealed class InMemorySchemaStore {
    private readonly Dictionary<SchemaKey, DurableSchema> _schemas = new();

    /// <summary>
    /// Registers a schema and its ancestors, or returns the previously registered equivalent instance.
    /// </summary>
    /// <exception cref="SchemaConflictException">
    /// The same schema identity and version is already registered with a different shape.
    /// </exception>
    public DurableSchema Register(DurableSchema schema) {
        ArgumentNullException.ThrowIfNull(schema);

        List<DurableSchema> chain = new();
        for (DurableSchema? candidate = schema; candidate is not null; candidate = candidate.BaseSchema) {
            SchemaKey key = new(candidate.SchemaId, candidate.Version);
            if (_schemas.TryGetValue(key, out DurableSchema? registeredSchema) &&
                !registeredSchema.Equals(candidate)) {
                throw new SchemaConflictException(registeredSchema, candidate);
            }

            chain.Add(candidate);
        }

        // Validate the complete chain before publishing any new schema.
        for (int index = chain.Count - 1; index >= 0; index--) {
            DurableSchema candidate = chain[index];
            _schemas.TryAdd(new SchemaKey(candidate.SchemaId, candidate.Version), candidate);
        }

        return _schemas[new SchemaKey(schema.SchemaId, schema.Version)];
    }

    /// <summary>
    /// Gets the schema registered for an exact schema identity and version.
    /// </summary>
    /// <exception cref="SchemaNotFoundException">
    /// No schema is registered for the exact identity and version.
    /// </exception>
    public DurableSchema GetRequired(string schemaId, int version) {
        ArgumentException.ThrowIfNullOrWhiteSpace(schemaId);

        if (version <= 0) {
            throw new ArgumentOutOfRangeException(
                nameof(version),
                version,
                "Schema versions must be positive.");
        }

        SchemaKey key = new(schemaId, version);

        if (_schemas.TryGetValue(key, out DurableSchema? schema)) {
            return schema;
        }

        throw new SchemaNotFoundException(schemaId, version);
    }

    private readonly record struct SchemaKey(string SchemaId, int Version);
}
