namespace Atelia.MultiSegmentStateStoreProbe.Model;

/// <summary>
/// Size/value stand-in for one reconstructed domain object. It deliberately carries no
/// schema or serializer semantics.
/// </summary>
internal readonly record struct LogicalObjectState {
    public LogicalObjectState(
        int value,
        int basePayloadBytes,
        int logicalVersionOrdinal) {
        ArgumentOutOfRangeException.ThrowIfNegative(basePayloadBytes);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(logicalVersionOrdinal);
        Value = value;
        BasePayloadBytes = basePayloadBytes;
        LogicalVersionOrdinal = logicalVersionOrdinal;
    }

    public int Value { get; }

    public int BasePayloadBytes { get; }

    public int LogicalVersionOrdinal { get; }
}
