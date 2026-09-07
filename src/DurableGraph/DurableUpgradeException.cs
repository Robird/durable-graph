namespace Atelia.DurableGraph;

/// <summary>
/// Wraps a failure while upgrading a versioned state DTO to the next exact Schema version.
/// </summary>
public sealed class DurableUpgradeException : InvalidOperationException {
    public DurableUpgradeException(
        string schemaId,
        int fromVersion,
        int toVersion,
        Exception innerException)
        : base(
            CreateMessage(schemaId, fromVersion, toVersion),
            innerException ?? throw new ArgumentNullException(nameof(innerException))) {
        SchemaId = schemaId;
        FromVersion = fromVersion;
        ToVersion = toVersion;
    }

    public string SchemaId { get; }

    public int FromVersion { get; }

    public int ToVersion { get; }

    private static string CreateMessage(
        string schemaId,
        int fromVersion,
        int toVersion) {
        ArgumentException.ThrowIfNullOrWhiteSpace(schemaId);

        if (fromVersion <= 0) {
            throw new ArgumentOutOfRangeException(
                nameof(fromVersion),
                fromVersion,
                "Source schema versions must be positive.");
        }

        if (toVersion <= 0) {
            throw new ArgumentOutOfRangeException(
                nameof(toVersion),
                toVersion,
                "Target schema versions must be positive.");
        }

        return $"Upgrading schema '{schemaId}' from version {fromVersion} " +
            $"to version {toVersion} failed.";
    }
}
