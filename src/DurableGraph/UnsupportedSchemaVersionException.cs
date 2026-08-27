namespace Atelia.DurableGraph;

/// <summary>
/// Indicates that a serializer cannot read the stored version of its schema.
/// </summary>
public sealed class UnsupportedSchemaVersionException : InvalidOperationException {
    public UnsupportedSchemaVersionException(
        string schemaId,
        int storedVersion,
        int currentVersion)
        : base(CreateMessage(schemaId, storedVersion, currentVersion)) {
        SchemaId = schemaId;
        StoredVersion = storedVersion;
        CurrentVersion = currentVersion;
    }

    public string SchemaId { get; }

    public int StoredVersion { get; }

    public int CurrentVersion { get; }

    private static string CreateMessage(
        string schemaId,
        int storedVersion,
        int currentVersion) {
        ArgumentException.ThrowIfNullOrWhiteSpace(schemaId);

        if (storedVersion <= 0) {
            throw new ArgumentOutOfRangeException(
                nameof(storedVersion),
                storedVersion,
                "Stored schema versions must be positive.");
        }

        if (currentVersion <= 0) {
            throw new ArgumentOutOfRangeException(
                nameof(currentVersion),
                currentVersion,
                "Current schema versions must be positive.");
        }

        return $"Schema '{schemaId}' version {storedVersion} cannot be read " +
            $"by the current version {currentVersion} serializer.";
    }
}
