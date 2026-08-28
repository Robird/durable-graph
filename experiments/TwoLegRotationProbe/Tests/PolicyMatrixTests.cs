using Atelia.TwoLegRotationProbe.Model;
using Atelia.TwoLegRotationProbe.Simulation;
using Atelia.TwoLegRotationProbe.Workloads;
using Atelia.TwoLegRotationProbe.Workloads.Generation;

namespace Atelia.TwoLegRotationProbe.Tests;

public sealed class PolicyMatrixTests {
    [Fact]
    public void Object_payload_read_amplification_policy_has_an_exact_ratio_three_boundary() {
        Assert.Equal(3, ObjectPayloadReadAmplificationPolicy.MaxReadAmplificationRatio);
        Assert.False(ObjectPayloadReadAmplificationPolicy.ShouldWriteBase(100, 50, 100));
        Assert.False(ObjectPayloadReadAmplificationPolicy.ShouldWriteBase(100, 50, 149));
        Assert.True(ObjectPayloadReadAmplificationPolicy.ShouldWriteBase(100, 50, 150));
        Assert.True(ObjectPayloadReadAmplificationPolicy.ShouldWriteBase(100, 100, 0));
        Assert.True(ObjectPayloadReadAmplificationPolicy.ShouldWriteBase(40, 50, 0));
        Assert.Throws<ArgumentOutOfRangeException>(
            () => ObjectPayloadReadAmplificationPolicy.ShouldWriteBase(-1, 1, 0));
        Assert.Throws<ArgumentOutOfRangeException>(
            () => ObjectPayloadReadAmplificationPolicy.ShouldWriteBase(1, 0, 0));
        Assert.Throws<ArgumentOutOfRangeException>(
            () => ObjectPayloadReadAmplificationPolicy.ShouldWriteBase(1, 1, -1));
    }

    [Fact]
    public void Local_threshold_crossing_writes_delta_base_delta_and_resets_cost() {
        WorkloadTrace trace = new(
            "local-threshold-crossing",
            "handwritten",
            1,
            0,
            [
                new SaveStep([new CreateObject(1, 100)]),
                new SaveStep([new UpdateObject(1, 100, 50)]),
                new SaveStep([new UpdateObject(1, 100, 50)]),
                new SaveStep([new UpdateObject(1, 100, 50)]),
            ]);

        SimulationRun run = WorkloadSimulator.Run(
            trace,
            BaselinePolicy.ObjectPayloadReadAmplification3);

        Assert.Equal(
            [ObjectVersionKind.Base, ObjectVersionKind.Delta, ObjectVersionKind.Base, ObjectVersionKind.Delta],
            ReadSingleVersionPerRevision(run).Select(static version => version.Kind));
        Assert.Equal(
            [100L, 150L, 100L, 150L],
            ReadSingleVersionPerRevision(run)
                .Select(static version => version.ReconstructionObjectPayloadBytes));
        Assert.Equal(2, run.Observations[^1].PostSaveReconstruction.RequiredObjectVersionCount);
        Assert.Equal(150, run.Observations[^1].PostSaveReconstruction.RequiredObjectPayloadBytes);
        AssertExactState(WorkloadReplayer.Replay(trace), PhysicalStateOracle.Materialize(run));
        PhysicalStateOracle.ValidateLineage(run);
    }

    [Fact]
    public void Direct_base_size_rule_covers_growth_shrink_and_equal_size() {
        WorkloadTrace trace = new(
            "direct-base-size-directions",
            "handwritten",
            1,
            0,
            [
                new SaveStep([
                    new CreateObject(1, 10),
                    new CreateObject(2, 100),
                    new CreateObject(3, 20),
                ]),
                new SaveStep([
                    new UpdateObject(1, 30, 30),
                    new UpdateObject(2, 40, 50),
                    new UpdateObject(3, 20, 25),
                ]),
            ]);

        SimulationRun local = WorkloadSimulator.Run(
            trace,
            BaselinePolicy.ObjectPayloadReadAmplification3);
        SimulationRun delta = WorkloadSimulator.Run(
            trace,
            BaselinePolicy.AlwaysDeltaWhenLegal);

        Assert.All(
            local.FileStore.ReadFrame(local.RevisionAddresses[1]).ObjectVersions.Values,
            static version => Assert.Equal(ObjectVersionKind.Base, version.Kind));
        Assert.All(
            delta.FileStore.ReadFrame(delta.RevisionAddresses[1]).ObjectVersions.Values,
            static version => Assert.Equal(ObjectVersionKind.Delta, version.Kind));
        AssertExactState(WorkloadReplayer.Replay(trace), PhysicalStateOracle.Materialize(local));
        AssertExactState(WorkloadReplayer.Replay(trace), PhysicalStateOracle.Materialize(delta));
    }

    [Fact]
    public void Hot_one_cold_eight_shared_frame_exposes_three_distinct_raw_tradeoffs() {
        WorkloadTrace trace = CreateHotColdTrace();
        SimulationRun alwaysBase = WorkloadSimulator.Run(trace, BaselinePolicy.AlwaysBase);
        SimulationRun alwaysDelta = WorkloadSimulator.Run(trace, BaselinePolicy.AlwaysDeltaWhenLegal);
        SimulationRun local = WorkloadSimulator.Run(
            trace,
            BaselinePolicy.ObjectPayloadReadAmplification3);

        Assert.Equal(
            "AlwaysBase:W1500/F1700:FinalR900/1000/100/1048/9/2|" +
            "AlwaysDeltaWhenLegal:W1050/F1268:FinalR1050/1050/0/1236/15/7|" +
            "ObjectPayloadReadAmplification3:W1125/F1340:FinalR900/1000/100/1048/9/2",
            DescribeRawComparison([alwaysBase, alwaysDelta, local]));
        Assert.Equal(
            [ObjectVersionKind.Delta, ObjectVersionKind.Delta, ObjectVersionKind.Delta,
                ObjectVersionKind.Delta, ObjectVersionKind.Delta, ObjectVersionKind.Base],
            local.RevisionAddresses
                .Skip(1)
                .Select(address => local.FileStore.ReadFrame(address).ObjectVersions[1].Kind));
        Assert.All(
            new[] { alwaysBase, alwaysDelta, local },
            run => AssertExactState(
                WorkloadReplayer.Replay(trace),
                PhysicalStateOracle.Materialize(run)));
    }

    [Fact]
    public void Fixed_seed_mixed_matrix_is_repeatable_without_summing_read_snapshots() {
        WorkloadTrace trace = ScenarioGenerator.Generate(CreateMixedDefinition()).Trace;
        BaselinePolicy[] policies = [
            BaselinePolicy.AlwaysBase,
            BaselinePolicy.AlwaysDeltaWhenLegal,
            BaselinePolicy.ObjectPayloadReadAmplification3,
        ];
        SimulationRun[] first = policies
            .Select(policy => WorkloadSimulator.Run(trace, policy))
            .ToArray();
        SimulationRun[] second = policies
            .Select(policy => WorkloadSimulator.Run(trace, policy))
            .ToArray();

        Assert.Equal(
            "AlwaysBase:W342/F436:FinalR123/123/0/148/3/1|" +
            "AlwaysDeltaWhenLegal:W274/F364:FinalR171/274/103/348/6/3|" +
            "ObjectPayloadReadAmplification3:W279/F372:FinalR147/181/34/232/4/2",
            DescribeRawComparison(first));
        for (int index = 0; index < policies.Length; index++) {
            Assert.Same(trace, first[index].SourceTrace);
            Assert.Same(trace, second[index].SourceTrace);
            Assert.Equal(first[index].Observations, second[index].Observations);
            AssertExactState(
                WorkloadReplayer.Replay(trace),
                PhysicalStateOracle.Materialize(first[index]));
            AssertExactState(
                WorkloadReplayer.Replay(trace),
                PhysicalStateOracle.Materialize(second[index]));
        }
    }

    private static WorkloadTrace CreateHotColdTrace() {
        List<WorkloadChange> creates = [new CreateObject(1, 100)];
        for (uint objectId = 2; objectId <= 9; objectId++) {
            creates.Add(new CreateObject(objectId, 100));
        }

        List<SaveStep> steps = [new SaveStep(creates)];
        for (int index = 0; index < 6; index++) {
            steps.Add(new SaveStep([new UpdateObject(1, 100, 25)]));
        }

        return new WorkloadTrace(
            "hot-one-cold-eight-shared-frame",
            "handwritten",
            1,
            0,
            steps);
    }

    private static ScenarioDefinition CreateMixedDefinition() => new(
        "fixed-seed-field-list-mixed",
        seed: 12_345,
        stepCount: 3,
        initialPopulation: 3,
        createPerLaterStep: 1,
        maxUpdatePerLaterStep: 2,
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
            replaceWeight: 1));

    private static IEnumerable<ObjectVersion> ReadSingleVersionPerRevision(SimulationRun run) =>
        run.RevisionAddresses.Select(address =>
            Assert.Single(run.FileStore.ReadFrame(address).ObjectVersions).Value);

    private static string DescribeRawComparison(IEnumerable<SimulationRun> runs) => string.Join(
        "|",
        runs.Select(static run => {
            long writePayloadBytes = run.Observations.Sum(static item =>
                item.ObjectPayloadBytesWritten);
            long modeledFileBytes = run.FileStore.GetFile(run.CurrentFileNumber).TailOffsetBytes;
            PostSaveReconstructionMetrics finalRead =
                run.Observations[^1].PostSaveReconstruction;
            return $"{run.Policy}:W{writePayloadBytes}/F{modeledFileBytes}:" +
                $"FinalR{finalRead.RequiredObjectPayloadBytes}/" +
                $"{finalRead.ObjectPayloadBytesInUniqueFrames}/" +
                $"{finalRead.CoReadObjectPayloadBytes}/" +
                $"{finalRead.ObjectPayloadOnlyRbfFrameBytesRead}/" +
                $"{finalRead.RequiredObjectVersionCount}/{finalRead.UniqueFrameCount}";
        }));

    private static void AssertExactState(
        IReadOnlyDictionary<uint, LogicalObjectState> expected,
        IReadOnlyDictionary<uint, LogicalObjectState> actual) {
        Assert.Equal(expected.Count, actual.Count);
        foreach ((uint objectId, LogicalObjectState expectedState) in expected) {
            Assert.True(actual.TryGetValue(objectId, out LogicalObjectState actualState));
            Assert.Equal(expectedState, actualState);
        }
    }
}
