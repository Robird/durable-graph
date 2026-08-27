namespace Atelia.DurableGraph;

/// <summary>
/// Indicates that a schema key was reused for a different schema shape.
/// </summary>
public sealed class SchemaConflictException : InvalidOperationException {
    public SchemaConflictException(
        DurableSchema registeredSchema,
        DurableSchema conflictingSchema)
        : base(CreateMessage(registeredSchema, conflictingSchema)) {
        RegisteredSchema = registeredSchema;
        ConflictingSchema = conflictingSchema;
    }

    public string SchemaId => RegisteredSchema.SchemaId;

    public int Version => RegisteredSchema.Version;

    public DurableSchema RegisteredSchema { get; }

    public DurableSchema ConflictingSchema { get; }

    private static string CreateMessage(
        DurableSchema registeredSchema,
        DurableSchema conflictingSchema) {
        ArgumentNullException.ThrowIfNull(registeredSchema);
        ArgumentNullException.ThrowIfNull(conflictingSchema);
        return $"Schema '{registeredSchema.SchemaId}' version " +
            $"{registeredSchema.Version} is already registered with a different shape.";
    }
}
