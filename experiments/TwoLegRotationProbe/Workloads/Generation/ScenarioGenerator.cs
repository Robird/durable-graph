using Atelia.TwoLegRotationProbe.Workloads;

namespace Atelia.TwoLegRotationProbe.Workloads.Generation;

internal static class ScenarioGenerator {
    public const string GeneratorId = "two-leg-rotation-probe/scenario";
    public const int GeneratorVersion = 1;

    public static GeneratedScenario Generate(ScenarioDefinition definition) {
        ArgumentNullException.ThrowIfNull(definition);

        StableRandom random = new(definition.Seed);
        Dictionary<uint, IObjectBehavior> liveObjects = [];
        List<SaveStep> steps = new(definition.StepCount);
        ulong nextObjectId = 1;

        List<WorkloadChange> initialChanges = [];
        for (int index = 0; index < definition.InitialPopulation; index++) {
            AddCreatedObject(
                definition,
                random,
                stepIndex: 0,
                ref nextObjectId,
                liveObjects,
                initialChanges);
        }

        steps.Add(new SaveStep(initialChanges));

        for (int stepIndex = 1; stepIndex < definition.StepCount; stepIndex++) {
            RandomStream lifecycleRandom = random.Fork(RandomDomain.Lifecycle, stepIndex);
            LifecyclePlan plan = FixedCountLifecycle.Plan(
                definition,
                liveObjects.Keys,
                lifecycleRandom);
            List<WorkloadChange> changes = [];

            foreach (uint objectId in plan.RemoveObjectIds) {
                if (!liveObjects.Remove(objectId)) {
                    throw new InvalidOperationException(
                        $"Lifecycle selected non-live object {objectId} for removal.");
                }

                changes.Add(new RemoveObject(objectId));
            }

            foreach (uint objectId in plan.UpdateObjectIds) {
                if (!liveObjects.TryGetValue(objectId, out IObjectBehavior? behavior)) {
                    throw new InvalidOperationException(
                        $"Lifecycle selected non-live object {objectId} for update.");
                }

                RandomStream updateRandom = random.Fork(
                    RandomDomain.ObjectUpdate,
                    stepIndex,
                    objectId);
                changes.Add(behavior.GenerateUpdate(objectId, updateRandom));
            }

            for (int index = 0; index < plan.CreateCount; index++) {
                AddCreatedObject(
                    definition,
                    random,
                    stepIndex,
                    ref nextObjectId,
                    liveObjects,
                    changes);
            }

            if (changes.Count == 0) {
                throw new InvalidOperationException(
                    $"Scenario '{definition.Name}' requested empty save step {stepIndex}.");
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
        ValidateFinalState(liveObjects, replayed);
        return new GeneratedScenario(definition, trace);
    }

    private static void AddCreatedObject(
        ScenarioDefinition definition,
        StableRandom random,
        int stepIndex,
        ref ulong nextObjectId,
        IDictionary<uint, IObjectBehavior> liveObjects,
        ICollection<WorkloadChange> changes) {
        uint objectId = checked((uint)nextObjectId);
        nextObjectId = checked(nextObjectId + 1);

        RandomStream kindRandom = random.Fork(
            RandomDomain.ObjectCreate,
            stepIndex,
            objectId,
            lane: 0);
        RandomStream initializationRandom = random.Fork(
            RandomDomain.ObjectCreate,
            stepIndex,
            objectId,
            lane: 1);
        int totalWeight = checked(definition.FieldObjectWeight + definition.ListObjectWeight);
        int selection = kindRandom.NextInt(totalWeight);
        IObjectBehavior behavior = selection < definition.FieldObjectWeight
            ? FieldObjectBehavior.Create(definition.FieldBehavior, initializationRandom)
            : ListObjectBehavior.Create(definition.ListBehavior, initializationRandom);

        liveObjects.Add(objectId, behavior);
        changes.Add(new CreateObject(objectId, behavior.CurrentBasePayloadBytes));
    }

    private static void ValidateFinalState(
        IReadOnlyDictionary<uint, IObjectBehavior> generated,
        IReadOnlyDictionary<uint, LogicalObjectState> replayed) {
        if (generated.Count != replayed.Count) {
            throw new InvalidOperationException(
                "Generated behavior state and replayed workload state have different live counts.");
        }

        foreach ((uint objectId, IObjectBehavior behavior) in generated) {
            if (!replayed.TryGetValue(objectId, out LogicalObjectState replayedState)) {
                throw new InvalidOperationException(
                    $"Generated live object {objectId} is missing from replayed state.");
            }

            if (replayedState.BasePayloadBytes != behavior.CurrentBasePayloadBytes) {
                throw new InvalidOperationException(
                    $"Object {objectId} generated Base size {behavior.CurrentBasePayloadBytes}, " +
                    $"but replay produced {replayedState.BasePayloadBytes}.");
            }
        }
    }
}
