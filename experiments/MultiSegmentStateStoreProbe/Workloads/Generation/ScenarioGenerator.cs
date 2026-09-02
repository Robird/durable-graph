using Atelia.MultiSegmentStateStoreProbe.Workloads;

namespace Atelia.MultiSegmentStateStoreProbe.Workloads.Generation;

internal sealed record GeneratedScenario(
    ScenarioDefinition Definition,
    WorkloadTrace Trace);

internal static class ScenarioGenerator {
    public const string GeneratorId = "multi-segment-state-store-probe/scenario";
    public const int GeneratorVersion = 1;

    public static GeneratedScenario Generate(ScenarioDefinition definition) {
        ArgumentNullException.ThrowIfNull(definition);
        StableRandom random = new(definition.Seed);
        Dictionary<uint, IObjectBehavior> live = [];
        List<SaveStep> steps = new(definition.StepCount);
        ulong nextObjectId = 1;

        List<WorkloadChange> initial = [];
        for (int index = 0; index < definition.InitialPopulation; index++) {
            AddCreatedObject(definition, random, 0, ref nextObjectId, live, initial);
        }

        steps.Add(new SaveStep(initial));
        for (int stepIndex = 1; stepIndex < definition.StepCount; stepIndex++) {
            LifecyclePlan plan = FixedCountLifecycle.Plan(
                definition,
                live.Keys,
                random.Fork(RandomDomain.Lifecycle, stepIndex));
            List<WorkloadChange> changes = [];
            foreach (uint objectId in plan.RemoveObjectIds) {
                if (!live.Remove(objectId)) {
                    throw new InvalidOperationException($"Lifecycle selected missing object {objectId}.");
                }

                changes.Add(new RemoveObject(objectId));
            }

            foreach (uint objectId in plan.UpdateObjectIds) {
                changes.Add(live[objectId].GenerateUpdate(
                    objectId,
                    random.Fork(RandomDomain.ObjectUpdate, stepIndex, objectId)));
            }

            for (int index = 0; index < plan.CreateCount; index++) {
                AddCreatedObject(
                    definition,
                    random,
                    stepIndex,
                    ref nextObjectId,
                    live,
                    changes);
            }

            if (changes.Count == 0) {
                throw new InvalidOperationException(
                    $"Scenario '{definition.Name}' requested empty Save step {stepIndex}.");
            }

            steps.Add(new SaveStep(changes));
        }

        WorkloadTrace trace = new(
            definition.Name,
            GeneratorId,
            GeneratorVersion,
            definition.Seed,
            steps);
        IReadOnlyDictionary<uint, LogicalObjectState> replayed = WorkloadReplayer.Replay(trace);
        if (replayed.Count != live.Count || live.Any(pair =>
            !replayed.TryGetValue(pair.Key, out LogicalObjectState state) ||
            state.BasePayloadBytes != pair.Value.CurrentBasePayloadBytes)) {
            throw new InvalidOperationException("Generated and replayed final states diverged.");
        }

        return new GeneratedScenario(definition, trace);
    }

    private static void AddCreatedObject(
        ScenarioDefinition definition,
        StableRandom random,
        int stepIndex,
        ref ulong nextObjectId,
        IDictionary<uint, IObjectBehavior> live,
        ICollection<WorkloadChange> changes) {
        uint objectId = checked((uint)nextObjectId);
        nextObjectId = checked(nextObjectId + 1);
        int totalWeight = checked(definition.FieldObjectWeight + definition.ListObjectWeight);
        int selection = random.Fork(
            RandomDomain.ObjectCreate,
            stepIndex,
            objectId,
            lane: 0).NextInt(totalWeight);
        RandomStream initialization = random.Fork(
            RandomDomain.ObjectCreate,
            stepIndex,
            objectId,
            lane: 1);
        IObjectBehavior behavior = selection < definition.FieldObjectWeight
            ? FieldObjectBehavior.Create(definition.FieldBehavior, initialization)
            : ListObjectBehavior.Create(definition.ListBehavior, initialization);
        live.Add(objectId, behavior);
        changes.Add(new CreateObject(objectId, behavior.CurrentBasePayloadBytes));
    }
}
