namespace Atelia.MultiSegmentStateStoreProbe.Workloads;

internal readonly record struct LogicalObjectState(
    int BasePayloadBytes,
    int LogicalVersionOrdinal);
