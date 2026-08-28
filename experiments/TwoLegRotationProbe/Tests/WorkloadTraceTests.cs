using Atelia.TwoLegRotationProbe.Workloads;

namespace Atelia.TwoLegRotationProbe.Tests;

public sealed class WorkloadTraceTests {
    [Fact]
    public void Replay_applies_create_update_and_remove() {
        WorkloadTrace trace = Trace(
            new SaveStep([new CreateObject(1, 10), new CreateObject(2, 20)]),
            new SaveStep([new UpdateObject(1, 100, 90)]),
            new SaveStep([new RemoveObject(2)]));

        IReadOnlyDictionary<uint, LogicalObjectState> live = WorkloadReplayer.Replay(trace);

        KeyValuePair<uint, LogicalObjectState> remaining = Assert.Single(live);
        Assert.Equal(1U, remaining.Key);
        Assert.Equal(new LogicalObjectState(100, 2), remaining.Value);
    }

    [Fact]
    public void Same_size_update_advances_the_version_ordinal() {
        WorkloadTrace trace = Trace(
            new SaveStep([new CreateObject(7, 100)]),
            new SaveStep([new UpdateObject(7, 100, 1)]));

        LogicalObjectState state = WorkloadReplayer.Replay(trace)[7];

        Assert.Equal(100, state.BasePayloadBytes);
        Assert.Equal(2, state.VersionOrdinal);
    }

    [Fact]
    public void Growth_requires_delta_to_contain_at_least_the_new_bytes() {
        WorkloadTrace trace = Trace(
            new SaveStep([new CreateObject(1, 10)]),
            new SaveStep([new UpdateObject(1, 100, 89)]));

        Assert.Throws<InvalidDataException>(() => WorkloadReplayer.Replay(trace));
    }

    [Theory]
    [InlineData(-1, 1)]
    [InlineData(10, 0)]
    [InlineData(10, -1)]
    public void Update_rejects_invalid_sizes(int resultBaseBytes, int deltaBytes) {
        WorkloadTrace trace = Trace(
            new SaveStep([new CreateObject(1, 10)]),
            new SaveStep([new UpdateObject(1, resultBaseBytes, deltaBytes)]));

        Assert.Throws<InvalidDataException>(() => WorkloadReplayer.Replay(trace));
    }

    [Fact]
    public void Create_rejects_negative_size() {
        WorkloadTrace trace = Trace(new SaveStep([new CreateObject(1, -1)]));

        Assert.Throws<InvalidDataException>(() => WorkloadReplayer.Replay(trace));
    }

    [Fact]
    public void Save_step_rejects_empty_or_duplicate_changes() {
        Assert.Throws<ArgumentException>(() => new SaveStep([]));
        Assert.Throws<ArgumentException>(() => new SaveStep([
            new CreateObject(1, 10),
            new UpdateObject(1, 10, 1),
        ]));
    }

    [Fact]
    public void Replay_rejects_update_and_remove_of_missing_objects() {
        WorkloadTrace update = Trace(new SaveStep([new UpdateObject(1, 10, 1)]));
        WorkloadTrace remove = Trace(new SaveStep([new RemoveObject(1)]));

        Assert.Throws<InvalidDataException>(() => WorkloadReplayer.Replay(update));
        Assert.Throws<InvalidDataException>(() => WorkloadReplayer.Replay(remove));
    }

    [Fact]
    public void Replay_rejects_object_id_reuse_after_removal() {
        WorkloadTrace trace = Trace(
            new SaveStep([new CreateObject(1, 10)]),
            new SaveStep([new RemoveObject(1)]),
            new SaveStep([new CreateObject(1, 20)]));

        Assert.Throws<InvalidDataException>(() => WorkloadReplayer.Replay(trace));
    }

    [Fact]
    public void Save_step_canonicalizes_changes_by_object_id() {
        SaveStep step = new([
            new CreateObject(30, 1),
            new CreateObject(10, 1),
            new CreateObject(20, 1),
        ]);

        Assert.Equal([10U, 20U, 30U], step.Changes.Select(static change => change.ObjectId));
    }

    [Fact]
    public void Trace_and_step_defensively_copy_input_collections() {
        List<WorkloadChange> changes = [new CreateObject(1, 10)];
        SaveStep step = new(changes);
        List<SaveStep> steps = [step];
        WorkloadTrace trace = new("defensive-copy", "tests", 1, 42, steps);

        changes[0] = new CreateObject(2, 20);
        steps[0] = new SaveStep([new CreateObject(3, 30)]);

        CreateObject frozen = Assert.IsType<CreateObject>(Assert.Single(trace.Steps[0].Changes));
        Assert.Equal(1U, frozen.ObjectId);

        IList<SaveStep> exposedSteps = Assert.IsAssignableFrom<IList<SaveStep>>(trace.Steps);
        Assert.True(exposedSteps.IsReadOnly);
        Assert.Throws<NotSupportedException>(() => exposedSteps.Add(step));
    }

    [Theory]
    [InlineData(null, "generator", 1)]
    [InlineData("", "generator", 1)]
    [InlineData("scenario", null, 1)]
    [InlineData("scenario", "", 1)]
    [InlineData("scenario", "generator", 0)]
    public void Trace_rejects_invalid_identity(
        string? scenarioName,
        string? generatorId,
        int generatorVersion) {
        Assert.ThrowsAny<ArgumentException>(() => new WorkloadTrace(
            scenarioName!,
            generatorId!,
            generatorVersion,
            0,
            [new SaveStep([new CreateObject(1, 1)])]));
    }

    [Fact]
    public void Trace_rejects_empty_steps() {
        Assert.Throws<ArgumentException>(() => new WorkloadTrace(
            "empty",
            "tests",
            1,
            0,
            []));
    }

    [Fact]
    public void Replay_result_is_read_only() {
        IReadOnlyDictionary<uint, LogicalObjectState> result = WorkloadReplayer.Replay(
            Trace(new SaveStep([new CreateObject(1, 10)])));
        IDictionary<uint, LogicalObjectState> dictionary =
            Assert.IsAssignableFrom<IDictionary<uint, LogicalObjectState>>(result);

        Assert.True(dictionary.IsReadOnly);
        Assert.Throws<NotSupportedException>(() => dictionary.Add(2, new LogicalObjectState(20, 1)));
    }

    [Fact]
    public void One_frozen_trace_can_be_replayed_repeatedly() {
        WorkloadTrace trace = Trace(
            new SaveStep([new CreateObject(1, 10)]),
            new SaveStep([new UpdateObject(1, 20, 10)]));

        IReadOnlyDictionary<uint, LogicalObjectState> first = WorkloadReplayer.Replay(trace);
        IReadOnlyDictionary<uint, LogicalObjectState> second = WorkloadReplayer.Replay(trace);

        Assert.Equal(first.OrderBy(static pair => pair.Key), second.OrderBy(static pair => pair.Key));
        Assert.Equal(new LogicalObjectState(20, 2), first[1]);
    }

    private static WorkloadTrace Trace(params SaveStep[] steps) {
        return new WorkloadTrace("test", "tests", 1, 42, steps);
    }
}
