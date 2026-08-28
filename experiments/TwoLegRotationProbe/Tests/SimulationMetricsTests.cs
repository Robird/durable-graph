using Atelia.TwoLegRotationProbe.Model;
using Atelia.TwoLegRotationProbe.Simulation;
using Atelia.TwoLegRotationProbe.Workloads;
using Atelia.TwoLegRotationProbe.Workloads.Generation;

namespace Atelia.TwoLegRotationProbe.Tests;

public sealed class SimulationMetricsTests {
    [Fact]
    public void Generated_mixed_trace_produces_deterministic_object_payload_only_metrics() {
        GeneratedScenario generated = ScenarioGenerator.Generate(CreateMixedDefinition());

        SimulationRun firstBase = WorkloadSimulator.Run(
            generated.Trace,
            BaselinePolicy.AlwaysBase);
        SimulationRun firstDelta = WorkloadSimulator.Run(
            generated.Trace,
            BaselinePolicy.AlwaysDeltaWhenLegal);
        SimulationRun secondBase = WorkloadSimulator.Run(
            generated.Trace,
            BaselinePolicy.AlwaysBase);
        SimulationRun secondDelta = WorkloadSimulator.Run(
            generated.Trace,
            BaselinePolicy.AlwaysDeltaWhenLegal);

        Assert.Same(generated.Trace, firstBase.SourceTrace);
        Assert.Same(generated.Trace, firstDelta.SourceTrace);
        AssertObservationSequencesEqual(firstBase.Observations, secondBase.Observations);
        AssertObservationSequencesEqual(firstDelta.Observations, secondDelta.Observations);
        Assert.Equal(
            "0@4/124:98+0:124/128:R3/3/1:98/98/0/124|" +
            "1@132/148:121+0:148/152:R3/3/1:121/121/0/148|" +
            "2@284/148:123+0:148/152:R3/3/1:123/123/0/148",
            Describe(firstBase));
        Assert.Equal(
            "0@4/124:98+0:124/128:R3/3/1:98/98/0/124|" +
            "1@132/100:42+34:100/104:R3/5/2:141/174/33/224|" +
            "2@236/124:49+51:124/128:R3/6/3:171/274/103/348",
            Describe(firstDelta));

        AssertRunEnvelope(firstBase);
        AssertRunEnvelope(firstDelta);
        AssertExactState(
            WorkloadReplayer.Replay(generated.Trace),
            PhysicalStateOracle.Materialize(firstBase));
        AssertExactState(
            WorkloadReplayer.Replay(generated.Trace),
            PhysicalStateOracle.Materialize(firstDelta));
    }

    [Fact]
    public void Remove_only_frame_is_written_but_not_added_to_reconstruction_reads() {
        WorkloadTrace trace = new(
            "remove-only-metrics",
            "handwritten",
            1,
            0,
            [
                new SaveStep([
                    new CreateObject(1, 10),
                    new CreateObject(2, 20),
                ]),
                new SaveStep([new UpdateObject(1, 8, 2)]),
                new SaveStep([new RemoveObject(2)]),
            ]);

        SimulationRun run = WorkloadSimulator.Run(
            trace,
            BaselinePolicy.AlwaysDeltaWhenLegal);

        RevisionObservation removal = run.Observations[2];
        Assert.Equal(0, removal.ObjectVersionCount);
        Assert.Equal(0, removal.ObjectPayloadBytesWritten);
        Assert.Equal(24, removal.ObjectPayloadOnlyRbfFrameLengthBytes);
        Assert.Equal(28, removal.ObjectPayloadOnlyRbfAppendBytes);
        Assert.Equal(2, removal.PostSaveReconstruction.UniqueFrameCount);
        Assert.Equal(2, removal.PostSaveReconstruction.RequiredObjectVersionCount);
        Assert.Equal(12, removal.PostSaveReconstruction.RequiredObjectPayloadBytes);
        Assert.Equal(32, removal.PostSaveReconstruction.ObjectPayloadBytesInUniqueFrames);
        Assert.Equal(20, removal.PostSaveReconstruction.CoReadObjectPayloadBytes);
        Assert.Equal(84, removal.PostSaveReconstruction.ObjectPayloadOnlyRbfFrameBytesRead);
        Assert.NotEqual(
            removal.Address.FrameTicket.LengthBytes + 84,
            removal.PostSaveReconstruction.ObjectPayloadOnlyRbfFrameBytesRead);
        AssertRunEnvelope(run);
    }

    [Theory]
    [InlineData(BaselinePolicy.AlwaysBase)]
    [InlineData(BaselinePolicy.AlwaysDeltaWhenLegal)]
    [InlineData(BaselinePolicy.ObjectPayloadReadAmplification3)]
    internal void Object_payload_that_cannot_fit_one_rbf_frame_fails_closed(
        BaselinePolicy policy) {
        WorkloadTrace trace = new(
            "object-payload-overflow",
            "handwritten",
            1,
            0,
            [
                new SaveStep([
                    new CreateObject(1, 150_000_000),
                    new CreateObject(2, 150_000_000),
                ]),
            ]);

        InvalidDataException exception = Assert.Throws<InvalidDataException>(
            () => WorkloadSimulator.Run(trace, policy));

        Assert.Contains("Object payload length", exception.Message);
    }

    [Fact]
    public void Reconstruction_metrics_reject_a_frame_outside_object_payload_only_scope() {
        RbfFileStore store = new();
        RbfFile file = store.CreateFile();
        FrameBuilder frameBuilder = new();
        ObjectVersionBuilder version = frameBuilder.Add(1);
        version.PayloadBytes = 10;
        version.ReconstructionObjectPayloadBytes = 10;
        version.ResultBasePayloadBytes = 10;
        FrameTicket ticket = file.Append(frameBuilder.Build());
        Dictionary<uint, AbsoluteFrameAddress> stateMap = new() {
            [1] = new AbsoluteFrameAddress(file.FileNumber, ticket),
        };

        InvalidDataException exception = Assert.Throws<InvalidDataException>(
            () => PhysicalStateOracle.MeasureReconstruction(store, stateMap));

        Assert.Contains("ObjectPayloadOnly", exception.Message);
    }

    private static ScenarioDefinition CreateMixedDefinition() => new(
        "metrics-mixed",
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

    private static void AssertRunEnvelope(SimulationRun run) {
        Assert.Equal(run.SourceTrace.Steps.Count, run.Observations.Count);
        Assert.Equal(run.RevisionAddresses, run.Observations.Select(static item => item.Address));
        Assert.All(run.Observations, static observation => {
            Assert.Equal(AccountingScope.ObjectPayloadOnly, observation.AccountingScope);
            Assert.False(observation.IncludesObjectVersionHeaders);
            Assert.False(observation.IncludesObjectVersionDict);
            Assert.False(observation.IncludesTailMetaIndex);
            Assert.Equal(0, observation.ObjectPayloadOnlyLayout.TailMetaLengthBytes);
            Assert.Equal(
                observation.ObjectPayloadBytesWritten,
                observation.ObjectPayloadOnlyLayout.PayloadLengthBytes);
        });

        long expectedFileLength = checked(
            RbfV040Layout.HeaderFenceBytes +
            run.Observations.Sum(static observation =>
                (long)observation.ObjectPayloadOnlyRbfAppendBytes));
        Assert.Equal(
            expectedFileLength,
            run.FileStore.GetFile(run.CurrentFileNumber).TailOffsetBytes);

        IList<RevisionObservation> observations =
            Assert.IsAssignableFrom<IList<RevisionObservation>>(run.Observations);
        Assert.True(observations.IsReadOnly);
    }

    private static void AssertObservationSequencesEqual(
        IReadOnlyList<RevisionObservation> expected,
        IReadOnlyList<RevisionObservation> actual) {
        Assert.Equal(expected.Count, actual.Count);
        for (int index = 0; index < expected.Count; index++) {
            Assert.Equal(expected[index], actual[index]);
        }
    }

    private static void AssertExactState(
        IReadOnlyDictionary<uint, LogicalObjectState> expected,
        IReadOnlyDictionary<uint, LogicalObjectState> actual) {
        Assert.Equal(expected.Count, actual.Count);
        foreach ((uint objectId, LogicalObjectState expectedState) in expected) {
            Assert.True(actual.TryGetValue(objectId, out LogicalObjectState actualState));
            Assert.Equal(expectedState, actualState);
        }
    }

    private static string Describe(SimulationRun run) => string.Join(
        "|",
        run.Observations.Select(static observation => {
            PostSaveReconstructionMetrics read = observation.PostSaveReconstruction;
            return $"{observation.StepIndex}@{observation.Address.FrameTicket.OffsetBytes}/" +
                $"{observation.Address.FrameTicket.LengthBytes}:" +
                $"{observation.BaseObjectPayloadBytes}+{observation.DeltaObjectPayloadBytes}:" +
                $"{observation.ObjectPayloadOnlyRbfFrameLengthBytes}/" +
                $"{observation.ObjectPayloadOnlyRbfAppendBytes}:" +
                $"R{read.LiveObjectCount}/{read.RequiredObjectVersionCount}/" +
                $"{read.UniqueFrameCount}:{read.RequiredObjectPayloadBytes}/" +
                $"{read.ObjectPayloadBytesInUniqueFrames}/" +
                $"{read.CoReadObjectPayloadBytes}/" +
                $"{read.ObjectPayloadOnlyRbfFrameBytesRead}";
        }));
}
