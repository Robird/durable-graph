namespace Atelia.TwoLegRotationProbe.Workloads;

internal readonly record struct LogicalObjectState(
    int BasePayloadBytes,
    int LogicalVersionOrdinal);
