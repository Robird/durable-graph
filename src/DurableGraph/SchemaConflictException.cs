namespace Atelia.DurableGraph;

/// <summary>
/// Indicates that a schema key was reused for a different schema shape.
/// </summary>
public sealed class SchemaConflictException : InvalidOperationException {
    internal SchemaConflictException(
        DurableSchema registeredSchema,
        DurableSchema conflictingSchema)
        : base(
            $"Schema '{registeredSchema.SchemaId}' version " +
            $"{registeredSchema.Version} is already registered with a different shape.") {
        RegisteredSchema = registeredSchema;
        ConflictingSchema = conflictingSchema;
    }

    public string SchemaId => RegisteredSchema.SchemaId;

    public int Version => RegisteredSchema.Version;

    public DurableSchema RegisteredSchema { get; }

    public DurableSchema ConflictingSchema { get; }
}
