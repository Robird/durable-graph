using Atelia.MultiSegmentStateStoreProbe.Workloads;

namespace Atelia.MultiSegmentStateStoreProbe.Tests;

public sealed class WorkloadTraceComposerTests {
    [Fact]
    public void Aligned_channels_merge_active_and_silent_cold_lanes() {
        WorkloadTrace active = new(
            "active",
            "tests",
            1,
            1,
            [
                new SaveStep([new CreateObject(1, 10)]),
                new SaveStep([new UpdateObject(1, 11, 2)]),
            ]);
        WorkloadChannel cold = new(
            "cold",
            [
                [new CreateObject(2, 20)],
                [],
            ]);

        WorkloadTrace composite = WorkloadTraceComposer.Compose(
            "active-plus-cold",
            1,
            [WorkloadChannel.FromTrace("active", active), cold]);

        Assert.Equal(WorkloadTraceComposer.GeneratorId, composite.GeneratorId);
        Assert.Equal([1U, 2U], composite.Steps[0].Changes.Select(static x => x.ObjectId));
        Assert.Equal([1U], composite.Steps[1].Changes.Select(static x => x.ObjectId));
        Assert.Equal(new LogicalObjectState(20, 1), WorkloadReplayer.Replay(composite)[2]);
    }

    [Fact]
    public void Composer_rejects_cross_channel_ObjectId_ownership() {
        WorkloadChannel first = new("first", [[new CreateObject(1, 10)]]);
        WorkloadChannel second = new("second", [[new CreateObject(1, 20)]]);

        Assert.Throws<ArgumentException>(() => WorkloadTraceComposer.Compose(
            "overlap",
            1,
            [first, second]));
    }
}
