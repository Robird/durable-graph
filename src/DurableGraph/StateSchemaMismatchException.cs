namespace Atelia.DurableGraph;

/// <summary>
/// Indicates that stored state belongs to a different schema identity than the
/// serializer requested for loading it.
/// </summary>
public sealed class StateSchemaMismatchException : InvalidOperationException {
    public StateSchemaMismatchException(
        DurableSchema storedSchema,
        DurableSchema expectedSchema)
        : base(CreateMessage(storedSchema, expectedSchema)) {
        StoredSchema = storedSchema;
        ExpectedSchema = expectedSchema;
    }

    public DurableSchema StoredSchema { get; }

    public DurableSchema ExpectedSchema { get; }

    private static string CreateMessage(
        DurableSchema storedSchema,
        DurableSchema expectedSchema) {
        ArgumentNullException.ThrowIfNull(storedSchema);
        ArgumentNullException.ThrowIfNull(expectedSchema);
        return $"Stored state uses schema '{storedSchema.SchemaId}' version " +
            $"{storedSchema.Version}, but the serializer targets schema identity " +
            $"'{expectedSchema.SchemaId}' (current version {expectedSchema.Version}).";
    }
}
