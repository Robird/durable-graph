using Atelia.MultiSegmentStateStoreProbe.Workloads;

namespace Atelia.MultiSegmentStateStoreProbe.Tests;

public sealed class WorkloadCoreTests {
    [Fact]
    public void Replay_applies_Create_Update_Remove_and_advances_ordinal() {
        WorkloadTrace trace = Trace(
            new SaveStep([new CreateObject(2, 20), new CreateObject(1, 10)]),
            new SaveStep([new UpdateObject(1, 100, 90)]),
            new SaveStep([new RemoveObject(2)]));

        KeyValuePair<uint, LogicalObjectState> remaining = Assert.Single(
            WorkloadReplayer.Replay(trace));

        Assert.Equal(1U, remaining.Key);
        Assert.Equal(new LogicalObjectState(100, 2), remaining.Value);
        Assert.Equal([1U, 2U], trace.Steps[0].Changes.Select(static x => x.ObjectId));
    }

    [Fact]
    public void Replay_rejects_ObjectId_reuse_after_Remove() {
        WorkloadTrace trace = Trace(
            new SaveStep([new CreateObject(1, 10)]),
            new SaveStep([new RemoveObject(1)]),
            new SaveStep([new CreateObject(1, 20)]));

        Assert.Throws<InvalidDataException>(() => WorkloadReplayer.Replay(trace));
    }

    [Fact]
    public void Update_growth_requires_Delta_to_cover_new_bytes() {
        WorkloadTrace trace = Trace(
            new SaveStep([new CreateObject(1, 10)]),
            new SaveStep([new UpdateObject(1, 100, 89)]));

        Assert.Throws<InvalidDataException>(() => WorkloadReplayer.Replay(trace));
    }

    [Fact]
    public void Replay_rejects_Update_and_Remove_of_nonlive_objects() {
        Assert.Throws<InvalidDataException>(() => WorkloadReplayer.Replay(
            Trace(new SaveStep([new UpdateObject(1, 10, 1)]))));
        Assert.Throws<InvalidDataException>(() => WorkloadReplayer.Replay(
            Trace(new SaveStep([new RemoveObject(1)]))));
    }

    [Fact]
    public void Trace_and_SaveStep_freeze_caller_collections() {
        List<WorkloadChange> changes = [new CreateObject(1, 10)];
        SaveStep step = new(changes);
        List<SaveStep> steps = [step];
        WorkloadTrace trace = new("freeze", "tests", 1, 1, steps);

        changes[0] = new CreateObject(2, 20);
        steps[0] = new SaveStep([new CreateObject(3, 30)]);

        CreateObject frozen = Assert.IsType<CreateObject>(Assert.Single(trace.Steps[0].Changes));
        Assert.Equal(1U, frozen.ObjectId);
    }

    private static WorkloadTrace Trace(params SaveStep[] steps) =>
        new("test", "tests", 1, 42, steps);
}
