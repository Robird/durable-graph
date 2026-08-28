using Atelia.TwoLegRotationProbe.Workloads;

namespace Atelia.TwoLegRotationProbe.Workloads.Generation;

internal sealed record GeneratedScenario(
    ScenarioDefinition Definition,
    WorkloadTrace Trace);
