namespace Atelia.TwoLegRotationProbe.Simulation;

internal readonly record struct PostSaveReconstructionMetrics {
    public PostSaveReconstructionMetrics(
        AccountingScope accountingScope,
        int liveObjectCount,
        int requiredObjectVersionCount,
        int uniqueFrameCount,
        long requiredObjectPayloadBytes,
        long objectPayloadBytesInUniqueFrames,
        long modeledRbfFrameBytesRead) {
        if (!Enum.IsDefined(accountingScope)) {
            throw new ArgumentOutOfRangeException(nameof(accountingScope));
        }

        ArgumentOutOfRangeException.ThrowIfNegative(liveObjectCount);
        ArgumentOutOfRangeException.ThrowIfNegative(requiredObjectVersionCount);
        ArgumentOutOfRangeException.ThrowIfNegative(uniqueFrameCount);
        ArgumentOutOfRangeException.ThrowIfNegative(requiredObjectPayloadBytes);
        ArgumentOutOfRangeException.ThrowIfNegative(objectPayloadBytesInUniqueFrames);
        ArgumentOutOfRangeException.ThrowIfNegative(modeledRbfFrameBytesRead);
        if (objectPayloadBytesInUniqueFrames < requiredObjectPayloadBytes) {
            throw new ArgumentException(
                "Payload in the read frames cannot be smaller than required payload.",
                nameof(objectPayloadBytesInUniqueFrames));
        }

        AccountingScope = accountingScope;
        LiveObjectCount = liveObjectCount;
        RequiredObjectVersionCount = requiredObjectVersionCount;
        UniqueFrameCount = uniqueFrameCount;
        RequiredObjectPayloadBytes = requiredObjectPayloadBytes;
        ObjectPayloadBytesInUniqueFrames = objectPayloadBytesInUniqueFrames;
        ModeledRbfFrameBytesRead = modeledRbfFrameBytesRead;
    }

    public AccountingScope AccountingScope { get; }

    public int LiveObjectCount { get; }

    public int RequiredObjectVersionCount { get; }

    public int UniqueFrameCount { get; }

    public long RequiredObjectPayloadBytes { get; }

    public long ObjectPayloadBytesInUniqueFrames { get; }

    public long CoReadObjectPayloadBytes =>
        ObjectPayloadBytesInUniqueFrames - RequiredObjectPayloadBytes;

    public long ModeledRbfFrameBytesRead { get; }

    public long ObjectPayloadOnlyRbfFrameBytesRead =>
        AccountingScope == AccountingScope.ObjectPayloadOnly
            ? ModeledRbfFrameBytesRead
            : throw new InvalidOperationException(
                "ProvisionalRevisionV0 frame bytes cannot be read through an ObjectPayloadOnly property.");
}
