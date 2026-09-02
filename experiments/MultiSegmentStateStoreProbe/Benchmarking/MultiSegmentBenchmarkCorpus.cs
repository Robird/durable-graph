using Atelia.MultiSegmentStateStoreProbe.Workloads;
using Atelia.MultiSegmentStateStoreProbe.Workloads.Generation;

namespace Atelia.MultiSegmentStateStoreProbe.Benchmarking;

internal static class MultiSegmentBenchmarkCorpus {
    public static WorkloadTrace CreateDeterministicMixedTrace() =>
        ScenarioGenerator.Generate(new ScenarioDefinition(
            name: "multi-segment-g4-mixed",
            seed: 12345,
            stepCount: 8,
            initialPopulation: 8,
            createPerLaterStep: 1,
            maxUpdatePerLaterStep: 4,
            maxRemovePerLaterStep: 1,
            fieldObjectWeight: 1,
            listObjectWeight: 1,
            fieldBehavior: new FieldBehaviorParameters(
                componentCount: 4,
                initialComponentBytesMinInclusive: 4,
                initialComponentBytesMaxExclusive: 20,
                replacementComponentBytesMinInclusive: 1,
                replacementComponentBytesMaxExclusive: 32,
                maxReplacedComponentsPerUpdate: 2,
                deltaOperationOverheadBytes: 2,
                deltaComponentOverheadBytes: 1),
            listBehavior: new ListBehaviorParameters(
                initialItemCount: 2,
                initialItemBytesMinInclusive: 10,
                initialItemBytesMaxExclusive: 11,
                insertedItemBytesMinInclusive: 5,
                insertedItemBytesMaxExclusive: 6,
                replacementItemBytesMinInclusive: 7,
                replacementItemBytesMaxExclusive: 8,
                deltaOperationOverheadBytes: 2,
                insertWeight: 1,
                removeWeight: 1,
                replaceWeight: 1))).Trace;
}
