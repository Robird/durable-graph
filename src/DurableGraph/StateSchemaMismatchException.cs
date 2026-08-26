namespace Atelia.DurableGraph;

/// <summary>
/// Indicates that stored state belongs to a different schema identity or version
/// than the serializer requested for loading it.
/// </summary>
public sealed class StateSchemaMismatchException : InvalidOperationException {
    internal StateSchemaMismatchException(
        DurableSchema storedSchema,
        DurableSchema expectedSchema)
        : base(
            $"Stored state uses schema '{storedSchema.SchemaId}' version " +
            $"{storedSchema.Version}, but the serializer expects schema " +
            $"'{expectedSchema.SchemaId}' version {expectedSchema.Version}.") {
        StoredSchema = storedSchema;
        ExpectedSchema = expectedSchema;
    }

    public DurableSchema StoredSchema { get; }

    public DurableSchema ExpectedSchema { get; }
}
