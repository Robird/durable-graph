using Atelia.TwoLegRotationProbe.Model;
using Atelia.TwoLegRotationProbe.Simulation;
using Atelia.TwoLegRotationProbe.Workloads;

namespace Atelia.TwoLegRotationProbe.Tests;

public sealed class SimulationTests {
    [Theory]
    [InlineData(BaselinePolicy.AlwaysBase)]
    [InlineData(BaselinePolicy.AlwaysDeltaWhenLegal)]
    [InlineData(BaselinePolicy.ObjectPayloadReadAmplification3)]
    internal void Golden_trace_compiles_one_frame_per_save_and_materializes_exact_state(
        BaselinePolicy policy) {
        WorkloadTrace trace = CreateGoldenTrace();

        SimulationRun run = WorkloadSimulator.Run(trace, policy);

        Assert.Same(trace, run.SourceTrace);
        Assert.Equal(policy, run.Policy);
        Assert.Equal(5, run.RevisionAddresses.Count);
        Assert.Equal(5, run.FileStore.GetFile(run.CurrentFileNumber).FrameCount);
        Assert.Empty(run.FileStore.ReadFrame(run.RevisionAddresses[2]).ObjectVersions);
        Assert.Equal(
            run.RevisionAddresses[4],
            Assert.Single(run.StateMap).Value);
        Assert.False(run.StateMap.ContainsKey(2));
        AssertExactState(
            WorkloadReplayer.Replay(trace),
            PhysicalStateOracle.Materialize(run));
        AssertRuntimeOvdChain(run);
        PhysicalStateOracle.ValidateLineage(run);

        IDictionary<uint, AbsoluteFrameAddress> stateMap =
            Assert.IsAssignableFrom<IDictionary<uint, AbsoluteFrameAddress>>(run.StateMap);
        Assert.True(stateMap.IsReadOnly);
    }

    [Theory]
    [InlineData(
        BaselinePolicy.AlwaysBase,
        ObjectVersionKind.Base,
        60,
        120,
        120,
        60,
        120,
        120)]
    [InlineData(
        BaselinePolicy.AlwaysDeltaWhenLegal,
        ObjectVersionKind.Delta,
        7,
        60,
        200,
        107,
        167,
        367)]
    internal void Baseline_policy_is_fixed_and_preserves_object_lineage(
        BaselinePolicy policy,
        ObjectVersionKind expectedUpdateKind,
        int firstPayloadBytes,
        int secondPayloadBytes,
        int thirdPayloadBytes,
        long firstReconstructionPayloadBytes,
        long secondReconstructionPayloadBytes,
        long thirdReconstructionPayloadBytes) {
        SimulationRun run = WorkloadSimulator.Run(CreateGoldenTrace(), policy);

        ObjectVersion firstUpdate = ReadObjectVersion(run, revisionIndex: 1, objectId: 1);
        ObjectVersion secondUpdate = ReadObjectVersion(run, revisionIndex: 3, objectId: 1);
        ObjectVersion thirdUpdate = ReadObjectVersion(run, revisionIndex: 4, objectId: 1);

        Assert.Equal(expectedUpdateKind, firstUpdate.Kind);
        Assert.Equal(expectedUpdateKind, secondUpdate.Kind);
        Assert.Equal(expectedUpdateKind, thirdUpdate.Kind);
        Assert.Equal(firstPayloadBytes, firstUpdate.PayloadBytes);
        Assert.Equal(secondPayloadBytes, secondUpdate.PayloadBytes);
        Assert.Equal(thirdPayloadBytes, thirdUpdate.PayloadBytes);
        Assert.Equal(
            firstReconstructionPayloadBytes,
            firstUpdate.ReconstructionObjectPayloadBytes);
        Assert.Equal(
            secondReconstructionPayloadBytes,
            secondUpdate.ReconstructionObjectPayloadBytes);
        Assert.Equal(
            thirdReconstructionPayloadBytes,
            thirdUpdate.ReconstructionObjectPayloadBytes);
        if (policy == BaselinePolicy.AlwaysBase) {
            Assert.Null(firstUpdate.DeltaParentFrameTicket);
            Assert.Null(secondUpdate.DeltaParentFrameTicket);
            Assert.Null(thirdUpdate.DeltaParentFrameTicket);
        } else {
            Assert.Equal(
                new RelativeFrameTicket(false, run.RevisionAddresses[0].FrameTicket),
                firstUpdate.DeltaParentFrameTicket);
            Assert.Equal(
                new RelativeFrameTicket(false, run.RevisionAddresses[1].FrameTicket),
                secondUpdate.DeltaParentFrameTicket);
            Assert.Equal(
                new RelativeFrameTicket(false, run.RevisionAddresses[3].FrameTicket),
                thirdUpdate.DeltaParentFrameTicket);
        }
        Assert.Equal(4, thirdUpdate.LogicalVersionOrdinal);

        if (policy == BaselinePolicy.AlwaysDeltaWhenLegal) {
            Assert.Equal(100, firstUpdate.ExpectedParentBasePayloadBytes);
            Assert.Equal(60, secondUpdate.ExpectedParentBasePayloadBytes);
            Assert.Equal(120, thirdUpdate.ExpectedParentBasePayloadBytes);
        } else {
            Assert.Null(firstUpdate.ExpectedParentBasePayloadBytes);
            Assert.Null(secondUpdate.ExpectedParentBasePayloadBytes);
            Assert.Null(thirdUpdate.ExpectedParentBasePayloadBytes);
        }
    }

    private static void AssertRuntimeOvdChain(SimulationRun run) {
        for (int index = 0; index < run.RevisionAddresses.Count; index++) {
            AbsoluteFrameAddress revisionAddress = run.RevisionAddresses[index];
            Frame frame = run.FileStore.ReadFrame(revisionAddress);
            ObjectVersionDictionary dictionary = Assert.IsType<ObjectVersionDictionary>(
                frame.ObjectVersionDictionary);

            if (index == 0) {
                Assert.Equal(ObjectVersionDictionaryKind.Base, dictionary.Kind);
                Assert.Null(dictionary.ParentRevisionFrameTicket);
            } else {
                Assert.Equal(ObjectVersionDictionaryKind.Delta, dictionary.Kind);
                Assert.Equal(
                    new RelativeFrameTicket(false, run.RevisionAddresses[index - 1].FrameTicket),
                    dictionary.ParentRevisionFrameTicket);
            }

            SaveStep step = run.SourceTrace.Steps[index];
            Assert.Equal(
                step.Changes.Select(static change => change.ObjectId).Order(),
                dictionary.Entries.Keys.Order());
            foreach (WorkloadChange change in step.Changes) {
                ObjectVersionDictionaryBinding binding = dictionary.Entries[change.ObjectId];
                Assert.Equal(
                    change is RemoveObject
                        ? ObjectVersionDictionaryBindingKind.Remove
                        : ObjectVersionDictionaryBindingKind.Self,
                    binding.Kind);
            }

            ObjectVersionDictionaryMaterializationInspection materialized =
                ObjectVersionDictionaryReader.MaterializeLive(run.FileStore, revisionAddress);
            foreach ((uint objectId, AbsoluteFrameAddress expectedAddress) in
                materialized.Bindings) {
                ObjectVersionDictionaryLookupInspection lookup =
                    ObjectVersionDictionaryReader.LookupLive(
                        run.FileStore,
                        revisionAddress,
                        objectId);
                Assert.Equal(ObjectVersionDictionaryLookupDisposition.Found, lookup.Disposition);
                Assert.Equal(expectedAddress, lookup.ResolvedObjectVersionAddress);
            }

            foreach (RemoveObject removed in step.Changes.OfType<RemoveObject>()) {
                Assert.Equal(
                    ObjectVersionDictionaryLookupDisposition.Removed,
                    ObjectVersionDictionaryReader.LookupLive(
                        run.FileStore,
                        revisionAddress,
                        removed.ObjectId).Disposition);
            }

            if (index == run.RevisionAddresses.Count - 1) {
                Assert.Equal(run.StateMap, materialized.Bindings);
            }
        }
    }

    [Fact]
    public void Delta_apply_rejects_a_parent_state_that_does_not_match_its_precondition() {
        RbfFileStore store = new();
        RbfFile file = store.CreateFile();
        FrameTicket parentTicket = AppendBase(file, objectId: 1, resultBasePayloadBytes: 100);
        FrameBuilder deltaFrame = new();
        ObjectVersionBuilder delta = deltaFrame.Add(1);
        ConfigureDelta(
            delta,
            expectedParentBasePayloadBytes: 99,
            resultBasePayloadBytes: 60,
            payloadBytes: 7,
            reconstructionObjectPayloadBytes: 107,
            logicalVersionOrdinal: 2,
            parentFrameTicket: parentTicket);
        FrameTicket headTicket = file.Append(deltaFrame.Build());
        Dictionary<uint, AbsoluteFrameAddress> stateMap = new() {
            [1] = new AbsoluteFrameAddress(file.FileNumber, headTicket),
        };

        InvalidDataException exception = Assert.Throws<InvalidDataException>(
            () => PhysicalStateOracle.Materialize(store, stateMap));

        Assert.Contains("expected a 99-byte parent", exception.Message);
    }

    [Fact]
    public void Delta_apply_rejects_a_tampered_reconstruction_payload_size() {
        RbfFileStore store = new();
        RbfFile file = store.CreateFile();
        FrameTicket parentTicket = AppendBase(file, objectId: 1, resultBasePayloadBytes: 100);
        FrameBuilder deltaFrame = new();
        ConfigureDelta(
            deltaFrame.Add(1),
            expectedParentBasePayloadBytes: 100,
            resultBasePayloadBytes: 60,
            payloadBytes: 7,
            reconstructionObjectPayloadBytes: 106,
            logicalVersionOrdinal: 2,
            parentFrameTicket: parentTicket);
        FrameTicket headTicket = file.Append(deltaFrame.Build());
        Dictionary<uint, AbsoluteFrameAddress> stateMap = new() {
            [1] = new AbsoluteFrameAddress(file.FileNumber, headTicket),
        };

        InvalidDataException exception = Assert.Throws<InvalidDataException>(
            () => PhysicalStateOracle.Materialize(store, stateMap));

        Assert.Contains("parent and payload require 107 bytes", exception.Message);
    }

    [Fact]
    public void Base_materialization_does_not_read_its_corrupt_prior_snapshot_anchor() {
        RbfFileStore store = new();
        RbfFile previous = store.CreateFile();
        AbsoluteFrameAddress corruptPriorSnapshot = new(
            previous.FileNumber,
            previous.Append(new FrameBuilder().Build()));
        RbfFile current = store.CreateFile();
        ObjectVersionDictionaryBuilder dictionary = new() {
            ParentRevisionFrameTicket = new RelativeFrameTicket(
                IsPreviousFile: true,
                corruptPriorSnapshot.FrameTicket),
        };
        dictionary.BindSelf(1);
        FrameBuilder frame = new() { ObjectVersionDictionary = dictionary };
        ObjectVersionBuilder rebased = frame.Add(1);
        rebased.Kind = ObjectVersionKind.Base;
        rebased.PayloadBytes = 100;
        rebased.ReconstructionObjectPayloadBytes = 100;
        rebased.ResultBasePayloadBytes = 100;
        rebased.LogicalVersionOrdinal = 2;
        FrameTicket headTicket = current.Append(frame.Build());
        Dictionary<uint, AbsoluteFrameAddress> stateMap = new() {
            [1] = new AbsoluteFrameAddress(current.FileNumber, headTicket),
        };

        IReadOnlyDictionary<uint, LogicalObjectState> materialized =
            PhysicalStateOracle.Materialize(store, stateMap);

        Assert.Equal(new LogicalObjectState(100, 2), Assert.Single(materialized).Value);
        Assert.Throws<InvalidDataException>(
            () => PhysicalStateOracle.ValidateLineage(store, stateMap));
    }

    [Fact]
    public void Delta_apply_rejects_a_same_file_forward_parent() {
        RbfFileStore store = new();
        RbfFile file = store.CreateFile();
        FrameBuilder deltaFrame = new();
        ConfigureDelta(
            deltaFrame.Add(1),
            expectedParentBasePayloadBytes: 100,
            resultBasePayloadBytes: 60,
            payloadBytes: 7,
            reconstructionObjectPayloadBytes: 107,
            logicalVersionOrdinal: 2,
            parentFrameTicket: new FrameTicket(32, 24));
        FrameTicket headTicket = file.Append(deltaFrame.Build());
        AppendBase(file, objectId: 1, resultBasePayloadBytes: 100);
        Dictionary<uint, AbsoluteFrameAddress> stateMap = new() {
            [1] = new AbsoluteFrameAddress(file.FileNumber, headTicket),
        };

        Assert.Throws<InvalidDataException>(() => PhysicalStateOracle.Materialize(store, stateMap));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Delta_apply_rejects_a_missing_parent_frame_or_object(bool missingFrame) {
        RbfFileStore store = new();
        RbfFile file = store.CreateFile();
        FrameBuilder unrelatedFrame = new();
        AppendConfiguredBase(unrelatedFrame.Add(missingFrame ? 1U : 2U), 100);
        FrameTicket availableParent = file.Append(unrelatedFrame.Build());

        FrameBuilder deltaFrame = new();
        ObjectVersionBuilder delta = deltaFrame.Add(1);
        ConfigureDelta(
            delta,
            expectedParentBasePayloadBytes: 100,
            resultBasePayloadBytes: 60,
            payloadBytes: 7,
            reconstructionObjectPayloadBytes: 107,
            logicalVersionOrdinal: 2,
            parentFrameTicket: missingFrame
                ? new FrameTicket(
                    availableParent.OffsetBytes,
                    availableParent.LengthBytes + RbfV040Layout.AlignmentBytes)
                : availableParent);
        FrameTicket headTicket = file.Append(deltaFrame.Build());
        Dictionary<uint, AbsoluteFrameAddress> stateMap = new() {
            [1] = new AbsoluteFrameAddress(file.FileNumber, headTicket),
        };

        Assert.Throws<InvalidDataException>(() => PhysicalStateOracle.Materialize(store, stateMap));
    }

    [Fact]
    public void Same_frozen_trace_produces_isolated_repeatable_runs() {
        WorkloadTrace trace = CreateGoldenTrace();

        SimulationRun firstBase = WorkloadSimulator.Run(trace, BaselinePolicy.AlwaysBase);
        SimulationRun firstDelta = WorkloadSimulator.Run(trace, BaselinePolicy.AlwaysDeltaWhenLegal);
        SimulationRun secondDelta = WorkloadSimulator.Run(trace, BaselinePolicy.AlwaysDeltaWhenLegal);
        SimulationRun secondBase = WorkloadSimulator.Run(trace, BaselinePolicy.AlwaysBase);

        Assert.Same(trace, firstBase.SourceTrace);
        Assert.Same(trace, firstDelta.SourceTrace);
        Assert.NotSame(firstBase.FileStore, secondBase.FileStore);
        Assert.NotSame(firstDelta.FileStore, secondDelta.FileStore);
        Assert.NotSame(firstBase.StateMap, secondBase.StateMap);
        Assert.Equal(Describe(firstBase), Describe(secondBase));
        Assert.Equal(Describe(firstDelta), Describe(secondDelta));
    }

    [Theory]
    [InlineData(BaselinePolicy.AlwaysBase)]
    [InlineData(BaselinePolicy.AlwaysDeltaWhenLegal)]
    [InlineData(BaselinePolicy.ObjectPayloadReadAmplification3)]
    internal void Invalid_trace_is_rejected_even_when_policy_would_ignore_its_delta(
        BaselinePolicy policy) {
        WorkloadTrace invalidGrowth = new(
            "invalid-growth",
            "handwritten",
            1,
            0,
            [
                new SaveStep([new CreateObject(1, 10)]),
                new SaveStep([new UpdateObject(1, 100, 89)]),
            ]);
        WorkloadTrace reusedId = new(
            "reused-id",
            "handwritten",
            1,
            0,
            [
                new SaveStep([new CreateObject(1, 10)]),
                new SaveStep([new RemoveObject(1)]),
                new SaveStep([new CreateObject(1, 20)]),
            ]);

        Assert.Throws<InvalidDataException>(() => WorkloadSimulator.Run(invalidGrowth, policy));
        Assert.Throws<InvalidDataException>(() => WorkloadSimulator.Run(reusedId, policy));

        SimulationRun retry = WorkloadSimulator.Run(CreateGoldenTrace(), policy);
        Assert.Equal(5, retry.RevisionAddresses.Count);
    }

    private static WorkloadTrace CreateGoldenTrace() => new(
        "physical-baseline-golden",
        "handwritten",
        1,
        0xBADC0FFEEUL,
        [
            new SaveStep([
                new CreateObject(1, 100),
                new CreateObject(2, 10),
            ]),
            new SaveStep([new UpdateObject(1, 60, 7)]),
            new SaveStep([new RemoveObject(2)]),
            new SaveStep([new UpdateObject(1, 120, 60)]),
            new SaveStep([new UpdateObject(1, 120, 200)]),
        ]);

    private static ObjectVersion ReadObjectVersion(
        SimulationRun run,
        int revisionIndex,
        uint objectId) =>
        run.FileStore
            .ReadFrame(run.RevisionAddresses[revisionIndex])
            .ObjectVersions[objectId];

    private static FrameTicket AppendBase(
        RbfFile file,
        uint objectId,
        int resultBasePayloadBytes) {
        FrameBuilder frame = new();
        AppendConfiguredBase(frame.Add(objectId), resultBasePayloadBytes);
        return file.Append(frame.Build());
    }

    private static void AppendConfiguredBase(
        ObjectVersionBuilder builder,
        int resultBasePayloadBytes) {
        builder.Kind = ObjectVersionKind.Base;
        builder.PayloadBytes = resultBasePayloadBytes;
        builder.ReconstructionObjectPayloadBytes = resultBasePayloadBytes;
        builder.ResultBasePayloadBytes = resultBasePayloadBytes;
        builder.LogicalVersionOrdinal = 1;
    }

    private static void ConfigureDelta(
        ObjectVersionBuilder builder,
        int expectedParentBasePayloadBytes,
        int resultBasePayloadBytes,
        int payloadBytes,
        long reconstructionObjectPayloadBytes,
        int logicalVersionOrdinal,
        FrameTicket parentFrameTicket) {
        builder.Kind = ObjectVersionKind.Delta;
        builder.PayloadBytes = payloadBytes;
        builder.ReconstructionObjectPayloadBytes = reconstructionObjectPayloadBytes;
        builder.ResultBasePayloadBytes = resultBasePayloadBytes;
        builder.ExpectedParentBasePayloadBytes = expectedParentBasePayloadBytes;
        builder.LogicalVersionOrdinal = logicalVersionOrdinal;
        builder.DeltaParentFrameTicket = new RelativeFrameTicket(false, parentFrameTicket);
    }

    private static string Describe(SimulationRun run) {
        List<string> descriptions = [];
        foreach (AbsoluteFrameAddress address in run.RevisionAddresses) {
            Frame frame = run.FileStore.ReadFrame(address);
            descriptions.Add(string.Join(
                ";",
                frame.ObjectVersions
                    .OrderBy(static pair => pair.Key)
                    .Select(static pair =>
                        $"{pair.Key}:{pair.Value.Kind}:{pair.Value.PayloadBytes}:" +
                        $"{pair.Value.ReconstructionObjectPayloadBytes}:" +
                        $"{pair.Value.ResultBasePayloadBytes}:" +
                        $"{pair.Value.ExpectedParentBasePayloadBytes}:" +
                        $"{pair.Value.LogicalVersionOrdinal}:{pair.Value.DeltaParentFrameTicket}")));
        }

        descriptions.Add(string.Join(
            ";",
            run.StateMap
                .OrderBy(static pair => pair.Key)
                .Select(static pair => $"{pair.Key}:{pair.Value}")));
        return string.Join("|", descriptions);
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
}
