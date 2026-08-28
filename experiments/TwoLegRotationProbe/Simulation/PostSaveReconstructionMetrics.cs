namespace Atelia.TwoLegRotationProbe.Simulation;

internal readonly record struct PostSaveReconstructionMetrics {
    public PostSaveReconstructionMetrics(
        int liveObjectCount,
        int requiredObjectVersionCount,
        int uniqueFrameCount,
        long requiredObjectPayloadBytes,
        long objectPayloadBytesInUniqueFrames,
        long objectPayloadOnlyRbfFrameBytesRead) {
        ArgumentOutOfRangeException.ThrowIfNegative(liveObjectCount);
        ArgumentOutOfRangeException.ThrowIfNegative(requiredObjectVersionCount);
        ArgumentOutOfRangeException.ThrowIfNegative(uniqueFrameCount);
        ArgumentOutOfRangeException.ThrowIfNegative(requiredObjectPayloadBytes);
        ArgumentOutOfRangeException.ThrowIfNegative(objectPayloadBytesInUniqueFrames);
        ArgumentOutOfRangeException.ThrowIfNegative(objectPayloadOnlyRbfFrameBytesRead);
        if (objectPayloadBytesInUniqueFrames < requiredObjectPayloadBytes) {
            throw new ArgumentException(
                "Payload in the read frames cannot be smaller than required payload.",
                nameof(objectPayloadBytesInUniqueFrames));
        }

        LiveObjectCount = liveObjectCount;
        RequiredObjectVersionCount = requiredObjectVersionCount;
        UniqueFrameCount = uniqueFrameCount;
        RequiredObjectPayloadBytes = requiredObjectPayloadBytes;
        ObjectPayloadBytesInUniqueFrames = objectPayloadBytesInUniqueFrames;
        ObjectPayloadOnlyRbfFrameBytesRead = objectPayloadOnlyRbfFrameBytesRead;
    }

    public int LiveObjectCount { get; }

    public int RequiredObjectVersionCount { get; }

    public int UniqueFrameCount { get; }

    public long RequiredObjectPayloadBytes { get; }

    public long ObjectPayloadBytesInUniqueFrames { get; }

    public long CoReadObjectPayloadBytes =>
        ObjectPayloadBytesInUniqueFrames - RequiredObjectPayloadBytes;

    public long ObjectPayloadOnlyRbfFrameBytesRead { get; }
}
