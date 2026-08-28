namespace Atelia.TwoLegRotationProbe.Workloads;

internal abstract record WorkloadChange(uint ObjectId);

internal sealed record CreateObject(
    uint ObjectId,
    int BasePayloadBytes) : WorkloadChange(ObjectId);

internal sealed record UpdateObject(
    uint ObjectId,
    int ResultBasePayloadBytes,
    int DeltaPayloadBytes) : WorkloadChange(ObjectId);

internal sealed record RemoveObject(uint ObjectId) : WorkloadChange(ObjectId);
