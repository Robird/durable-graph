using Atelia.TwoLegRotationProbe.Workloads;
using Atelia.TwoLegRotationProbe.Workloads.Generation;

namespace Atelia.TwoLegRotationProbe.Tests;

public sealed class WorkloadGenerationTests {
    [Fact]
    public void Same_definition_and_seed_generate_the_exact_same_trace() {
        ScenarioDefinition definition = Definition(seed: 12345);

        WorkloadTrace first = ScenarioGenerator.Generate(definition).Trace;
        WorkloadTrace second = ScenarioGenerator.Generate(definition).Trace;

        Assert.Equal(first.ScenarioName, second.ScenarioName);
        Assert.Equal(first.GeneratorId, second.GeneratorId);
        Assert.Equal(first.GeneratorVersion, second.GeneratorVersion);
        Assert.Equal(first.Seed, second.Seed);
        Assert.Equal(first.Steps.Count, second.Steps.Count);
        for (int index = 0; index < first.Steps.Count; index++) {
            Assert.Equal(first.Steps[index].Changes, second.Steps[index].Changes);
        }
    }

    [Fact]
    public void Generator_version_guards_a_complete_mixed_trace_golden() {
        ScenarioDefinition definition = Definition(
            seed: 12345,
            stepCount: 3,
            initialPopulation: 3,
            createPerLaterStep: 1,
            maxUpdatePerLaterStep: 2,
            maxRemovePerLaterStep: 1);

        WorkloadTrace trace = ScenarioGenerator.Generate(definition).Trace;

        Assert.Equal("two-leg-rotation-probe/scenario", ScenarioGenerator.GeneratorId);
        Assert.Equal(1, ScenarioGenerator.GeneratorVersion);
        Assert.Equal(
            "0:C1:20,C2:33,C3:45|1:U1:17:9,R2,U3:62:25,C4:42|" +
            "2:U1:7:2,R3,U4:67:49,C5:49",
            DescribeTrace(trace));
    }

    [Fact]
    public void Generated_scenario_preserves_parameters_outside_the_policy_trace() {
        ScenarioDefinition firstDefinition = Definition(
            seed: 77,
            fieldObjectWeight: 1,
            listObjectWeight: 1);
        ScenarioDefinition secondDefinition = Definition(
            seed: 77,
            fieldObjectWeight: 2,
            listObjectWeight: 1);

        GeneratedScenario first = ScenarioGenerator.Generate(firstDefinition);
        GeneratedScenario second = ScenarioGenerator.Generate(secondDefinition);

        Assert.NotEqual(first.Definition, second.Definition);
        Assert.Equal(first.Trace.ScenarioName, second.Trace.ScenarioName);
        Assert.Equal(first.Trace.Seed, second.Trace.Seed);
    }

    [Fact]
    public void Different_seeds_diverge_for_the_same_parameters() {
        WorkloadTrace first = ScenarioGenerator.Generate(Definition(seed: 1)).Trace;
        WorkloadTrace second = ScenarioGenerator.Generate(Definition(seed: 2)).Trace;

        Assert.False(Flatten(first).SequenceEqual(Flatten(second)));
    }

    [Fact]
    public void Fixed_count_lifecycle_generates_legal_create_update_and_remove_changes() {
        ScenarioDefinition definition = Definition(
            seed: 987,
            stepCount: 5,
            initialPopulation: 8,
            createPerLaterStep: 2,
            maxUpdatePerLaterStep: 3,
            maxRemovePerLaterStep: 2);

        WorkloadTrace trace = ScenarioGenerator.Generate(definition).Trace;
        IReadOnlyDictionary<uint, LogicalObjectState> finalState = WorkloadReplayer.Replay(trace);
        WorkloadChange[] changes = Flatten(trace).ToArray();
        uint[] createdIds = changes
            .OfType<CreateObject>()
            .Select(static change => change.ObjectId)
            .ToArray();
        HashSet<uint> liveBeforeStep = [];

        Assert.Equal(definition.StepCount, trace.Steps.Count);
        Assert.Contains(changes, static change => change is CreateObject);
        Assert.Contains(changes, static change => change is UpdateObject);
        Assert.Contains(changes, static change => change is RemoveObject);
        Assert.Equal(
            Enumerable.Range(1, createdIds.Length).Select(static id => (uint)id),
            createdIds);

        for (int stepIndex = 0; stepIndex < trace.Steps.Count; stepIndex++) {
            SaveStep step = trace.Steps[stepIndex];
            RemoveObject[] removes = step.Changes.OfType<RemoveObject>().ToArray();
            UpdateObject[] updates = step.Changes.OfType<UpdateObject>().ToArray();
            CreateObject[] creates = step.Changes.OfType<CreateObject>().ToArray();

            if (stepIndex == 0) {
                Assert.Empty(removes);
                Assert.Empty(updates);
                Assert.Equal(definition.InitialPopulation, creates.Length);
            } else {
                int expectedRemoveCount = Math.Min(
                    definition.MaxRemovePerLaterStep,
                    liveBeforeStep.Count);
                int expectedUpdateCount = Math.Min(
                    definition.MaxUpdatePerLaterStep,
                    liveBeforeStep.Count - expectedRemoveCount);
                Assert.Equal(expectedRemoveCount, removes.Length);
                Assert.Equal(expectedUpdateCount, updates.Length);
                Assert.Equal(definition.CreatePerLaterStep, creates.Length);
                Assert.All(removes, change => Assert.Contains(change.ObjectId, liveBeforeStep));
                Assert.All(updates, change => Assert.Contains(change.ObjectId, liveBeforeStep));
                Assert.Empty(
                    removes.Select(static change => change.ObjectId)
                        .Intersect(updates.Select(static change => change.ObjectId)));
            }

            foreach (RemoveObject remove in removes) {
                Assert.True(liveBeforeStep.Remove(remove.ObjectId));
            }

            foreach (CreateObject create in creates) {
                Assert.True(liveBeforeStep.Add(create.ObjectId));
            }
        }

        Assert.Equal(
            definition.InitialPopulation
                + (definition.StepCount - 1)
                    * (definition.CreatePerLaterStep - definition.MaxRemovePerLaterStep),
            finalState.Count);
    }

    [Fact]
    public void Generated_trace_freezes_generator_identity_and_replays() {
        ScenarioDefinition definition = Definition(seed: 321);

        GeneratedScenario generated = ScenarioGenerator.Generate(definition);
        WorkloadTrace trace = generated.Trace;
        IReadOnlyDictionary<uint, LogicalObjectState> finalState = WorkloadReplayer.Replay(trace);

        Assert.Same(definition, generated.Definition);
        Assert.Equal(definition.Name, trace.ScenarioName);
        Assert.Equal(ScenarioGenerator.GeneratorId, trace.GeneratorId);
        Assert.Equal(ScenarioGenerator.GeneratorVersion, trace.GeneratorVersion);
        Assert.Equal(definition.Seed, trace.Seed);
        Assert.NotEmpty(finalState);
    }

    [Fact]
    public void Field_updates_cover_growth_shrink_and_equal_size_with_consistent_delta() {
        FieldBehaviorParameters parameters = new(
            componentCount: 1,
            initialComponentBytesMinInclusive: 10,
            initialComponentBytesMaxExclusive: 11,
            replacementComponentBytesMinInclusive: 5,
            replacementComponentBytesMaxExclusive: 16,
            maxReplacedComponentsPerUpdate: 1,
            deltaOperationOverheadBytes: 2,
            deltaComponentOverheadBytes: 1);
        bool sawGrowth = false;
        bool sawShrink = false;
        bool sawEqual = false;

        for (ulong seed = 1; seed <= 1_000 && !(sawGrowth && sawShrink && sawEqual); seed++) {
            StableRandom random = new(seed);
            FieldObjectBehavior behavior = FieldObjectBehavior.Create(
                parameters,
                random.Fork(RandomDomain.ObjectCreate, 0, 1));
            int before = behavior.CurrentBasePayloadBytes;

            UpdateObject update = behavior.GenerateUpdate(
                1,
                random.Fork(RandomDomain.ObjectUpdate, 1, 1));

            Assert.Equal(update.ResultBasePayloadBytes + 3, update.DeltaPayloadBytes);
            Assert.True(
                update.DeltaPayloadBytes
                    >= Math.Max(0, update.ResultBasePayloadBytes - before));
            sawGrowth |= update.ResultBasePayloadBytes > before;
            sawShrink |= update.ResultBasePayloadBytes < before;
            sawEqual |= update.ResultBasePayloadBytes == before;
        }

        Assert.True(sawGrowth);
        Assert.True(sawShrink);
        Assert.True(sawEqual);
    }

    [Fact]
    public void List_empty_state_deterministically_falls_back_to_insert() {
        ListBehaviorParameters parameters = ListParameters(initialItemCount: 0);
        StableRandom random = new(17);
        ListObjectBehavior behavior = ListObjectBehavior.Create(
            parameters,
            random.Fork(RandomDomain.ObjectCreate, 0, 1));

        UpdateObject update = behavior.GenerateUpdate(
            1,
            random.Fork(RandomDomain.ObjectUpdate, 1, 1));

        Assert.Equal(1, behavior.CurrentItemCount);
        Assert.Equal(5, update.ResultBasePayloadBytes);
        Assert.Equal(7, update.DeltaPayloadBytes);
    }

    [Fact]
    public void List_updates_cover_insert_remove_and_replace_with_consistent_delta() {
        ListBehaviorParameters parameters = ListParameters(initialItemCount: 1);
        bool sawInsert = false;
        bool sawRemove = false;
        bool sawReplace = false;

        for (ulong seed = 1; seed <= 1_000 && !(sawInsert && sawRemove && sawReplace); seed++) {
            StableRandom random = new(seed);
            ListObjectBehavior behavior = ListObjectBehavior.Create(
                parameters,
                random.Fork(RandomDomain.ObjectCreate, 0, 1));
            UpdateObject update = behavior.GenerateUpdate(
                1,
                random.Fork(RandomDomain.ObjectUpdate, 1, 1));

            switch (behavior.CurrentItemCount) {
                case 0:
                    sawRemove = true;
                    Assert.Equal(0, update.ResultBasePayloadBytes);
                    Assert.Equal(2, update.DeltaPayloadBytes);
                    break;
                case 1:
                    sawReplace = true;
                    Assert.Equal(7, update.ResultBasePayloadBytes);
                    Assert.Equal(9, update.DeltaPayloadBytes);
                    break;
                case 2:
                    sawInsert = true;
                    Assert.Equal(15, update.ResultBasePayloadBytes);
                    Assert.Equal(7, update.DeltaPayloadBytes);
                    break;
                default:
                    Assert.Fail($"Unexpected item count {behavior.CurrentItemCount}.");
                    break;
            }
        }

        Assert.True(sawInsert);
        Assert.True(sawRemove);
        Assert.True(sawReplace);
    }

    [Fact]
    public void List_mutation_weights_can_select_a_single_operation_kind() {
        ListBehaviorParameters parameters = new(
            initialItemCount: 1,
            initialItemBytesMinInclusive: 10,
            initialItemBytesMaxExclusive: 11,
            insertedItemBytesMinInclusive: 5,
            insertedItemBytesMaxExclusive: 6,
            replacementItemBytesMinInclusive: 7,
            replacementItemBytesMaxExclusive: 8,
            deltaOperationOverheadBytes: 2,
            insertWeight: 1,
            removeWeight: 0,
            replaceWeight: 0);
        StableRandom random = new(17);
        ListObjectBehavior behavior = ListObjectBehavior.Create(
            parameters,
            random.Fork(RandomDomain.ObjectCreate, 0, 1));

        UpdateObject update = behavior.GenerateUpdate(
            1,
            random.Fork(RandomDomain.ObjectUpdate, 1, 1));

        Assert.Equal(2, behavior.CurrentItemCount);
        Assert.Equal(15, update.ResultBasePayloadBytes);
        Assert.Equal(7, update.DeltaPayloadBytes);
    }

    [Fact]
    public void Failed_candidate_size_does_not_commit_behavior_state() {
        FieldBehaviorParameters parameters = new(
            componentCount: 1,
            initialComponentBytesMinInclusive: 10,
            initialComponentBytesMaxExclusive: 11,
            replacementComponentBytesMinInclusive: int.MaxValue - 1,
            replacementComponentBytesMaxExclusive: int.MaxValue,
            maxReplacedComponentsPerUpdate: 1,
            deltaOperationOverheadBytes: 2,
            deltaComponentOverheadBytes: 0);
        StableRandom random = new(5);
        FieldObjectBehavior behavior = FieldObjectBehavior.Create(
            parameters,
            random.Fork(RandomDomain.ObjectCreate, 0, 1));

        Assert.Throws<OverflowException>(() => behavior.GenerateUpdate(
            1,
            random.Fork(RandomDomain.ObjectUpdate, 1, 1)));
        Assert.Equal(10, behavior.CurrentBasePayloadBytes);
    }

    [Fact]
    public void Late_delta_failure_does_not_commit_the_typed_candidate_before_retry() {
        FailOnceBehavior behavior = new(initialValue: 10);
        StableRandom random = new(5);

        Assert.Throws<OverflowException>(() => behavior.GenerateUpdate(
            1,
            random.Fork(RandomDomain.ObjectUpdate, 1, 1)));
        Assert.Equal(10, behavior.CurrentValue);
        Assert.Equal(10, behavior.CurrentBasePayloadBytes);

        UpdateObject retry = behavior.GenerateUpdate(
            1,
            random.Fork(RandomDomain.ObjectUpdate, 1, 1));

        Assert.Equal(11, retry.ResultBasePayloadBytes);
        Assert.Equal(11, behavior.CurrentValue);
    }

    [Fact]
    public void Scenario_fails_instead_of_fabricating_an_empty_requested_step() {
        ScenarioDefinition definition = Definition(
            seed: 1,
            stepCount: 2,
            initialPopulation: 1,
            createPerLaterStep: 0,
            maxUpdatePerLaterStep: 0,
            maxRemovePerLaterStep: 0);

        InvalidOperationException exception = Assert.Throws<InvalidOperationException>(
            () => ScenarioGenerator.Generate(definition));

        Assert.Contains("empty save step 1", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Generation_overflow_fails_without_returning_a_trace() {
        FieldBehaviorParameters oversizedField = new(
            componentCount: 2,
            initialComponentBytesMinInclusive: 1_500_000_000,
            initialComponentBytesMaxExclusive: 1_500_000_001,
            replacementComponentBytesMinInclusive: 1,
            replacementComponentBytesMaxExclusive: 2,
            maxReplacedComponentsPerUpdate: 1,
            deltaOperationOverheadBytes: 1,
            deltaComponentOverheadBytes: 0);
        ScenarioDefinition definition = new(
            "overflow",
            seed: 1,
            stepCount: 1,
            initialPopulation: 1,
            createPerLaterStep: 0,
            maxUpdatePerLaterStep: 0,
            maxRemovePerLaterStep: 0,
            fieldObjectWeight: 1,
            listObjectWeight: 0,
            oversizedField,
            ListParameters(initialItemCount: 0));

        Assert.Throws<OverflowException>(() => ScenarioGenerator.Generate(definition));
    }

    [Fact]
    public void Definitions_reject_invalid_counts_weights_and_behavior_ranges() {
        Assert.Throws<ArgumentOutOfRangeException>(() => Definition(stepCount: 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => Definition(initialPopulation: 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => Definition(createPerLaterStep: -1));
        Assert.Throws<ArgumentOutOfRangeException>(() => new ScenarioDefinition(
            "weights",
            1,
            1,
            1,
            0,
            0,
            0,
            0,
            0,
            FieldParameters(),
            ListParameters(initialItemCount: 0)));
        Assert.Throws<ArgumentOutOfRangeException>(() => new FieldBehaviorParameters(
            componentCount: 0,
            initialComponentBytesMinInclusive: 1,
            initialComponentBytesMaxExclusive: 2,
            replacementComponentBytesMinInclusive: 1,
            replacementComponentBytesMaxExclusive: 2,
            maxReplacedComponentsPerUpdate: 1,
            deltaOperationOverheadBytes: 1,
            deltaComponentOverheadBytes: 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => new ListBehaviorParameters(
            initialItemCount: -1,
            initialItemBytesMinInclusive: 1,
            initialItemBytesMaxExclusive: 2,
            insertedItemBytesMinInclusive: 1,
            insertedItemBytesMaxExclusive: 2,
            replacementItemBytesMinInclusive: 1,
            replacementItemBytesMaxExclusive: 2,
            deltaOperationOverheadBytes: 1,
            insertWeight: 0,
            removeWeight: 1,
            replaceWeight: 1));
    }

    private static ScenarioDefinition Definition(
        ulong seed = 42,
        int stepCount = 6,
        int initialPopulation = 10,
        int createPerLaterStep = 2,
        int maxUpdatePerLaterStep = 3,
        int maxRemovePerLaterStep = 1,
        int fieldObjectWeight = 1,
        int listObjectWeight = 1) {
        return new ScenarioDefinition(
            "mixed",
            seed,
            stepCount,
            initialPopulation,
            createPerLaterStep,
            maxUpdatePerLaterStep,
            maxRemovePerLaterStep,
            fieldObjectWeight,
            listObjectWeight,
            FieldParameters(),
            ListParameters(initialItemCount: 2));
    }

    private static FieldBehaviorParameters FieldParameters() {
        return new FieldBehaviorParameters(
            componentCount: 4,
            initialComponentBytesMinInclusive: 4,
            initialComponentBytesMaxExclusive: 20,
            replacementComponentBytesMinInclusive: 1,
            replacementComponentBytesMaxExclusive: 32,
            maxReplacedComponentsPerUpdate: 2,
            deltaOperationOverheadBytes: 2,
            deltaComponentOverheadBytes: 1);
    }

    private static ListBehaviorParameters ListParameters(int initialItemCount) {
        return new ListBehaviorParameters(
            initialItemCount,
            initialItemBytesMinInclusive: 10,
            initialItemBytesMaxExclusive: 11,
            insertedItemBytesMinInclusive: 5,
            insertedItemBytesMaxExclusive: 6,
            replacementItemBytesMinInclusive: 7,
            replacementItemBytesMaxExclusive: 8,
            deltaOperationOverheadBytes: 2,
            insertWeight: 1,
            removeWeight: 1,
            replaceWeight: 1);
    }

    private static IEnumerable<WorkloadChange> Flatten(WorkloadTrace trace) {
        return trace.Steps.SelectMany(static step => step.Changes);
    }

    private static string DescribeTrace(WorkloadTrace trace) {
        return string.Join(
            "|",
            trace.Steps.Select(static (step, index) =>
                $"{index}:{string.Join(',', step.Changes.Select(DescribeChange))}"));
    }

    private static string DescribeChange(WorkloadChange change) {
        return change switch {
            CreateObject create => $"C{create.ObjectId}:{create.BasePayloadBytes}",
            UpdateObject update =>
                $"U{update.ObjectId}:{update.ResultBasePayloadBytes}:{update.DeltaPayloadBytes}",
            RemoveObject remove => $"R{remove.ObjectId}",
            _ => throw new InvalidOperationException(
                $"Unsupported workload change '{change.GetType().FullName}'."),
        };
    }

    private sealed class FailOnceBehavior : ObjectBehavior<int[], int> {
        private bool _fail = true;

        public FailOnceBehavior(int initialValue)
            : base([initialValue], initialValue) {
        }

        public int CurrentValue => CurrentState[0];

        protected override int[] CloneStateForUpdate(int[] current) {
            return (int[])current.Clone();
        }

        protected override ObjectBehaviorCandidate<int[], int> ProposeUpdate(
            int[] current,
            RandomStream random) {
            current[0] = checked(current[0] + 1);
            return new ObjectBehaviorCandidate<int[], int>(current, current[0]);
        }

        protected override long MeasureBase(int[] state) => state[0];

        protected override long MeasureDelta(
            int[] before,
            int mutation,
            int[] candidate) {
            if (_fail) {
                _fail = false;
                throw new OverflowException("Injected late Delta failure.");
            }

            return 1;
        }
    }
}
