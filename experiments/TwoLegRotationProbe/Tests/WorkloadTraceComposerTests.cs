using Atelia.TwoLegRotationProbe.Workloads;

namespace Atelia.TwoLegRotationProbe.Tests;

public sealed class WorkloadTraceComposerTests {
    [Fact]
    public void Aligned_channels_merge_an_active_lane_with_a_silent_cold_lane() {
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
            seed: 1,
            [WorkloadChannel.FromTrace("active", active), cold]);

        Assert.Equal(WorkloadTraceComposer.GeneratorId, composite.GeneratorId);
        Assert.Equal([1U, 2U], composite.Steps[0].Changes.Select(static x => x.ObjectId));
        Assert.Equal([1U], composite.Steps[1].Changes.Select(static x => x.ObjectId));
        Assert.Equal(
            new LogicalObjectState(BasePayloadBytes: 20, LogicalVersionOrdinal: 1),
            WorkloadReplayer.Replay(composite)[2]);
    }
}
