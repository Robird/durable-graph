using ModelState = Atelia.MultiSegmentStateStoreProbe.Model.LogicalObjectState;
using WorkloadState = Atelia.MultiSegmentStateStoreProbe.Workloads.LogicalObjectState;

namespace Atelia.MultiSegmentStateStoreProbe.Normalization;

/// <summary>
/// Explicit boundary between workload state and persisted-model state. The workload has
/// no synthetic value channel, so normalized workload writes use zero for that probe-only
/// model field.
/// </summary>
internal static class WorkloadStateMapper {
    public static WorkloadState ToWorkload(ModelState state) => new(
        state.BasePayloadBytes,
        state.LogicalVersionOrdinal);

    public static ModelState ToModel(WorkloadState state) => new(
        value: 0,
        state.BasePayloadBytes,
        state.LogicalVersionOrdinal);
}
