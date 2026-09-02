using Atelia.MultiSegmentStateStoreProbe.Workloads;
using Atelia.MultiSegmentStateStoreProbe.Workloads.Generation;

namespace Atelia.MultiSegmentStateStoreProbe.Tests;

public sealed class WorkloadGenerationTests {
    [Fact]
    public void StableRandom_matches_frozen_golden_vector() {
        RandomStream stream = new StableRandom(0x0123456789ABCDEF).Fork(
            RandomDomain.ObjectUpdate,
            stepIndex: 17,
            objectId: 42,
            lane: 3);

        ulong[] actual = [
            stream.NextUInt64(),
            stream.NextUInt64(),
            stream.NextUInt64(),
            stream.NextUInt64(),
        ];
        Assert.Equal(new ulong[] {
            0xC25EB298C9C92B1D,
            0x0CBD7892DF68F6C4,
            0xE152C9FD20D62927,
            0x7F4C9BA064649338,
        }, actual);
    }

    [Fact]
    public void Same_definition_and_seed_generate_the_same_trace() {
        ScenarioDefinition definition = Definition(seed: 12345);

        WorkloadTrace first = ScenarioGenerator.Generate(definition).Trace;
        WorkloadTrace second = ScenarioGenerator.Generate(definition).Trace;

        Assert.Equal(Describe(first), Describe(second));
        Assert.Equal("multi-segment-state-store-probe/scenario", first.GeneratorId);
        Assert.Equal(ScenarioGenerator.GeneratorVersion, first.GeneratorVersion);
    }

    [Fact]
    public void Generator_version_guards_a_complete_mixed_trace_golden() {
        WorkloadTrace trace = ScenarioGenerator.Generate(Definition(
            seed: 12345,
            stepCount: 3,
            initialPopulation: 3,
            createPerLaterStep: 1,
            maxUpdatePerLaterStep: 2,
            maxRemovePerLaterStep: 1)).Trace;

        Assert.Equal(
            "0:C1:20,C2:33,C3:45|1:U1:17:9,R2,U3:62:25,C4:42|" +
            "2:U1:7:2,R3,U4:67:49,C5:49",
            Describe(trace));
        _ = WorkloadReplayer.Replay(trace);
    }

    [Fact]
    public void Generated_lifecycle_is_legal_and_ObjectIds_are_never_reused() {
        ScenarioDefinition definition = Definition(
            seed: 987,
            stepCount: 8,
            initialPopulation: 6,
            createPerLaterStep: 2,
            maxUpdatePerLaterStep: 3,
            maxRemovePerLaterStep: 1);

        WorkloadTrace trace = ScenarioGenerator.Generate(definition).Trace;
        HashSet<uint> created = [];
        foreach (CreateObject create in trace.Steps.SelectMany(static step => step.Changes)
            .OfType<CreateObject>()) {
            Assert.True(created.Add(create.ObjectId));
        }

        _ = WorkloadReplayer.Replay(trace);
    }

    [Fact]
    public void Field_and_List_behaviors_generate_consistent_Deltas() {
        FieldObjectBehavior field = FieldObjectBehavior.Create(
            FieldParameters(),
            new StableRandom(5).Fork(RandomDomain.ObjectCreate, 0, 1));
        int fieldBefore = field.CurrentBasePayloadBytes;
        UpdateObject fieldUpdate = field.GenerateUpdate(
            1,
            new StableRandom(5).Fork(RandomDomain.ObjectUpdate, 1, 1));
        Assert.True(fieldUpdate.DeltaPayloadBytes >=
            Math.Max(0, fieldUpdate.ResultBasePayloadBytes - fieldBefore));

        ListObjectBehavior list = ListObjectBehavior.Create(
            ListParameters(),
            new StableRandom(7).Fork(RandomDomain.ObjectCreate, 0, 2));
        int listBefore = list.CurrentBasePayloadBytes;
        UpdateObject listUpdate = list.GenerateUpdate(
            2,
            new StableRandom(7).Fork(RandomDomain.ObjectUpdate, 1, 2));
        Assert.True(listUpdate.DeltaPayloadBytes >=
            Math.Max(0, listUpdate.ResultBasePayloadBytes - listBefore));
    }

    private static ScenarioDefinition Definition(
        ulong seed,
        int stepCount = 6,
        int initialPopulation = 10,
        int createPerLaterStep = 2,
        int maxUpdatePerLaterStep = 3,
        int maxRemovePerLaterStep = 1) => new(
        "mixed",
        seed,
        stepCount,
        initialPopulation,
        createPerLaterStep,
        maxUpdatePerLaterStep,
        maxRemovePerLaterStep,
        fieldObjectWeight: 1,
        listObjectWeight: 1,
        FieldParameters(),
        ListParameters());

    private static FieldBehaviorParameters FieldParameters() => new(
        componentCount: 4,
        initialComponentBytesMinInclusive: 4,
        initialComponentBytesMaxExclusive: 20,
        replacementComponentBytesMinInclusive: 1,
        replacementComponentBytesMaxExclusive: 32,
        maxReplacedComponentsPerUpdate: 2,
        deltaOperationOverheadBytes: 2,
        deltaComponentOverheadBytes: 1);

    private static ListBehaviorParameters ListParameters() => new(
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
        replaceWeight: 1);

    private static string Describe(WorkloadTrace trace) => string.Join(
        "|",
        trace.Steps.Select(static (step, index) =>
            $"{index}:{string.Join(',', step.Changes.Select(Describe))}"));

    private static string Describe(WorkloadChange change) => change switch {
        CreateObject create => $"C{create.ObjectId}:{create.BasePayloadBytes}",
        UpdateObject update =>
            $"U{update.ObjectId}:{update.ResultBasePayloadBytes}:{update.DeltaPayloadBytes}",
        RemoveObject remove => $"R{remove.ObjectId}",
        _ => throw new InvalidOperationException(),
    };
}
