using Atelia.TwoLegRotationProbe.Encoding;
using Atelia.TwoLegRotationProbe.Model;
using Atelia.TwoLegRotationProbe.Planning;
using Atelia.TwoLegRotationProbe.Rotation;
using Atelia.TwoLegRotationProbe.Simulation;
using Atelia.TwoLegRotationProbe.Workloads;

namespace Atelia.TwoLegRotationProbe.Tests;

public sealed class ProbeCandidateApplyTests {
    private const uint ANoChangeId = 10;
    private const uint AUpdateId = 20;
    private const uint RemoveId = 30;
    private const uint BBaseUpdateId = 40;
    private const uint BDeltaUpdateId = 41;
    private const uint BExternalId = 50;
    private const uint BRelocateId = 51;
    private const uint InsertId = 70;

    [Fact]
    public void Stay_apply_appends_exactly_one_B_frame_and_returns_runtime_state() {
        ApplyFixture source = CreateSource();
        PairInputs inputs = CreatePairInputs(source);
        FeasibleCandidate<StayBRevisionPlan> selected = GetStay(inputs.Pair);
        ProbeRevisionCursor cursor = CreateCursor(source);
        StoreSnapshot before = CaptureStore(source.Store);

        ProbeRevisionCursor result = ExplicitProbeRevisionApplier.ApplyStayB(
            source.Store,
            cursor,
            selected);

        StoreSnapshot after = CaptureStore(source.Store);
        Assert.Equal(selected.Plan.Revision.Address, result.PublishedRevisionAddress);
        Assert.Equal(source.Previous.FileNumber, result.FileScope.PreviousFileNumber);
        Assert.Equal(source.Current.FileNumber, result.FileScope.CurrentFileNumber);
        Assert.Equal(
            selected.Plan.Revision.Estimate.RbfLayout.TailOffsetAfterBytes,
            result.CurrentFileTailOffsetBytes);
        Assert.Equal(before.FileCount, after.FileCount);
        Assert.Equal(before.Files[0], after.Files[0]);
        Assert.Equal(before.Files[1].FrameCount + 1, after.Files[1].FrameCount);
        Assert.Equal(result.CurrentFileTailOffsetBytes, after.Files[1].TailOffsetBytes);
        Assert.Same(
            selected.Plan.Revision.Frame,
            source.Store.ReadFrame(result.PublishedRevisionAddress));
        Assert.Equal(
            selected.Plan.Revision.Estimate.RbfLayout,
            source.Store.ReadLayout(result.PublishedRevisionAddress));
        AssertRuntimeState(
            source.Store,
            result.PublishedRevisionAddress,
            inputs.Facts.PostLiveStates);
    }

    [Fact]
    public void Rotate_apply_rolls_A_B_to_B_C_and_result_can_be_normalized() {
        ApplyFixture source = CreateSource();
        PairInputs inputs = CreatePairInputs(source);
        FeasibleCandidate<RotateCRevisionPlan> selected = GetRotate(inputs.Pair);
        ProbeRevisionCursor cursor = CreateCursor(source);
        StoreSnapshot before = CaptureStore(source.Store);

        ProbeRevisionCursor result = ExplicitProbeRevisionApplier.ApplyRotateC(
            source.Store,
            cursor,
            selected);

        StoreSnapshot after = CaptureStore(source.Store);
        Assert.Equal(selected.Plan.Revision.Address, result.PublishedRevisionAddress);
        Assert.Equal(source.Current.FileNumber, result.FileScope.PreviousFileNumber);
        Assert.Equal(source.Current.FileNumber + 1, result.FileScope.CurrentFileNumber);
        Assert.Equal(before.FileCount + 1, after.FileCount);
        Assert.Equal(before.Files, after.Files.Take(2));
        FileSnapshot next = after.Files[2];
        Assert.Equal(1, next.FrameCount);
        Assert.Equal(result.CurrentFileTailOffsetBytes, next.TailOffsetBytes);
        Assert.Same(
            selected.Plan.Revision.Frame,
            source.Store.ReadFrame(result.PublishedRevisionAddress));
        Assert.Equal(
            selected.Plan.Revision.Estimate.RbfLayout,
            source.Store.ReadLayout(result.PublishedRevisionAddress));

        IReadOnlyDictionary<uint, AbsoluteFrameAddress> bindings =
            ObjectVersionDictionaryReader.MaterializeLive(
                source.Store,
                result.PublishedRevisionAddress).Bindings;
        AssertExactState(
            inputs.Facts.PostLiveStates,
            PhysicalStateOracle.Materialize(source.Store, bindings));
        foreach ((uint objectId, AbsoluteFrameAddress address) in bindings) {
            ObjectReconstructionInspection reconstruction =
                PhysicalStateOracle.InspectObjectReconstruction(
                    source.Store,
                    objectId,
                    address);
            Assert.All(
                reconstruction.ReconstructionFrameAddresses,
                frameAddress => Assert.Contains(
                    frameAddress.FileNumber,
                    new[] {
                        result.FileScope.PreviousFileNumber!.Value,
                        result.FileScope.CurrentFileNumber,
                    }));
        }

        NormalizedSaveFacts maintenance = SaveStepNormalizer.NormalizeMaintenanceOnly(
            source.Store,
            result.FileScope.CurrentFileNumber,
            result.PublishedRevisionAddress);
        Assert.Equal(source.Current.FileNumber, maintenance.PreviousFileNumber);
        Assert.Equal(source.Current.FileNumber + 1, maintenance.CurrentFileNumber);
        AssertExactState(inputs.Facts.PostLiveStates, maintenance.ParentLiveStates);
        AssertExactState(inputs.Facts.PostLiveStates, maintenance.PostLiveStates);
    }

    [Fact]
    public void Wrong_caller_PublishedRevision_fails_without_mutation() {
        ApplyFixture source = CreateSource();
        PairInputs inputs = CreatePairInputs(source);
        ProbeRevisionCursor wrongCursor = new(
            new FileScope(source.Current.FileNumber),
            source.BLocalRevisionAddress,
            source.Current.TailOffsetBytes);
        StoreSnapshot before = CaptureStore(source.Store);

        Assert.Throws<InvalidDataException>(() =>
            ExplicitProbeRevisionApplier.ApplyStayB(
                source.Store,
                wrongCursor,
                GetStay(inputs.Pair)));

        AssertStoreSnapshot(before, CaptureStore(source.Store));
    }

    [Fact]
    public void Stale_B_tail_and_reapply_fail_without_further_mutation() {
        ApplyFixture staleSource = CreateSource();
        PairInputs staleInputs = CreatePairInputs(staleSource);
        ProbeRevisionCursor staleCursor = CreateCursor(staleSource);
        _ = staleSource.Current.Append(new FrameBuilder().Build());
        StoreSnapshot afterCompetingAppend = CaptureStore(staleSource.Store);

        Assert.Throws<InvalidDataException>(() =>
            ExplicitProbeRevisionApplier.ApplyStayB(
                staleSource.Store,
                staleCursor,
                GetStay(staleInputs.Pair)));
        AssertStoreSnapshot(afterCompetingAppend, CaptureStore(staleSource.Store));

        ApplyFixture appliedSource = CreateSource();
        PairInputs appliedInputs = CreatePairInputs(appliedSource);
        ProbeRevisionCursor originalCursor = CreateCursor(appliedSource);
        FeasibleCandidate<StayBRevisionPlan> selected = GetStay(appliedInputs.Pair);
        _ = ExplicitProbeRevisionApplier.ApplyStayB(
            appliedSource.Store,
            originalCursor,
            selected);
        StoreSnapshot afterFirstApply = CaptureStore(appliedSource.Store);

        Assert.Throws<InvalidDataException>(() =>
            ExplicitProbeRevisionApplier.ApplyStayB(
                appliedSource.Store,
                originalCursor,
                selected));
        AssertStoreSnapshot(afterFirstApply, CaptureStore(appliedSource.Store));
    }

    [Fact]
    public void Preexisting_C_rejects_Rotate_without_further_mutation() {
        ApplyFixture source = CreateSource();
        PairInputs inputs = CreatePairInputs(source);
        ProbeRevisionCursor cursor = CreateCursor(source);
        _ = source.Store.CreateFile();
        StoreSnapshot before = CaptureStore(source.Store);

        Assert.Throws<InvalidDataException>(() =>
            ExplicitProbeRevisionApplier.ApplyRotateC(
                source.Store,
                cursor,
                GetRotate(inputs.Pair)));

        AssertStoreSnapshot(before, CaptureStore(source.Store));
    }

    [Fact]
    public void Same_addresses_but_different_source_state_fail_without_mutation() {
        ApplyFixture healthy = CreateSource(sourceOrdinalOffset: 0);
        PairInputs inputs = CreatePairInputs(healthy);
        FeasibleCandidate<StayBRevisionPlan> selected = GetStay(inputs.Pair);
        ApplyFixture wrongState = CreateSource(sourceOrdinalOffset: 1);
        Assert.Equal(healthy.PreviousRevisionAddress, wrongState.PreviousRevisionAddress);
        Assert.Equal(healthy.BLocalRevisionAddress, wrongState.BLocalRevisionAddress);
        Assert.Equal(healthy.PublishedRevisionAddress, wrongState.PublishedRevisionAddress);
        Assert.Equal(healthy.Current.TailOffsetBytes, wrongState.Current.TailOffsetBytes);
        StoreSnapshot before = CaptureStore(wrongState.Store);

        Assert.Throws<InvalidDataException>(() =>
            ExplicitProbeRevisionApplier.ApplyStayB(
                wrongState.Store,
                CreateCursor(wrongState),
                selected));

        AssertStoreSnapshot(before, CaptureStore(wrongState.Store));
    }

    [Fact]
    public void Forged_target_and_candidate_start_fail_without_mutation() {
        ApplyFixture source = CreateSource();
        PairInputs inputs = CreatePairInputs(source);
        FeasibleCandidate<StayBRevisionPlan> stay = GetStay(inputs.Pair);
        FeasibleCandidate<RotateCRevisionPlan> rotate = GetRotate(inputs.Pair);
        CandidateRawObservation wrongTargetObservation = new(
            inputs.Facts,
            CandidateTarget.StayB,
            rotate.Plan.Revision,
            rotate.Observation.ForegroundDomainRecordBytes,
            rotate.Observation.MaintenanceDomainRecordBytes,
            rotate.Observation.PostLiveReconstruction);
        StayBRevisionPlan wrongTargetPlan = new(
            inputs.Facts,
            inputs.StayDecision,
            rotate.Plan.Revision);
        FeasibleCandidate<StayBRevisionPlan> wrongTarget = new(
            wrongTargetPlan,
            wrongTargetObservation);
        StoreSnapshot beforeWrongTarget = CaptureStore(source.Store);

        Assert.Throws<InvalidDataException>(() =>
            ExplicitProbeRevisionApplier.ApplyStayB(
                source.Store,
                CreateCursor(source),
                wrongTarget));
        AssertStoreSnapshot(beforeWrongTarget, CaptureStore(source.Store));

        PlannedRevisionV0 wrongStartRevision = new(
            source.Current.FileNumber,
            stay.Plan.Revision.Frame,
            source.Current.TailOffsetBytes + RbfV040Layout.AlignmentBytes);
        StayBRevisionPlan wrongStartPlan = new(
            inputs.Facts,
            inputs.StayDecision,
            wrongStartRevision);
        CandidateRawObservation wrongStartObservation =
            CandidateRawObservationBuilder.Create(
                source.Store,
                inputs.Facts,
                CandidateTarget.StayB,
                wrongStartRevision);
        FeasibleCandidate<StayBRevisionPlan> wrongStart = new(
            wrongStartPlan,
            wrongStartObservation);
        StoreSnapshot beforeWrongStart = CaptureStore(source.Store);

        Assert.Throws<InvalidDataException>(() =>
            ExplicitProbeRevisionApplier.ApplyStayB(
                source.Store,
                CreateCursor(source),
                wrongStart));
        AssertStoreSnapshot(beforeWrongStart, CaptureStore(source.Store));
    }

    [Fact]
    public void Feasible_selection_requires_exact_plan_candidate_identity() {
        ApplyFixture source = CreateSource();
        PairInputs inputs = CreatePairInputs(source);
        FeasibleCandidate<StayBRevisionPlan> stay = GetStay(inputs.Pair);
        FeasibleCandidate<RotateCRevisionPlan> rotate = GetRotate(inputs.Pair);
        FeasibleCandidate<StayBRevisionPlan> mismatched = new(
            stay.Plan,
            rotate.Observation);
        StoreSnapshot before = CaptureStore(source.Store);

        Assert.Throws<ArgumentException>(() =>
            ExplicitProbeRevisionApplier.ApplyStayB(
                source.Store,
                CreateCursor(source),
                mismatched));

        AssertStoreSnapshot(before, CaptureStore(source.Store));
    }

    private static ApplyFixture CreateSource(int sourceOrdinalOffset = 0) {
        RbfFileStore store = new();
        RbfFile previous = store.CreateFile();
        ObjectVersionDictionaryBuilder aDictionary = new();
        aDictionary.BindSelf(ANoChangeId);
        aDictionary.BindSelf(AUpdateId);
        aDictionary.BindSelf(RemoveId);
        FrameBuilder aBuilder = new() { ObjectVersionDictionary = aDictionary };
        AddBase(aBuilder, ANoChangeId, 10, 1 + sourceOrdinalOffset);
        AddBase(aBuilder, AUpdateId, 20, 1 + sourceOrdinalOffset);
        AddBase(aBuilder, RemoveId, 30, 1 + sourceOrdinalOffset);
        AbsoluteFrameAddress a = AppendExact(previous, aBuilder);

        RbfFile current = store.CreateFile();
        ObjectVersionDictionaryBuilder bLocalDictionary = new() {
            Kind = ObjectVersionDictionaryKind.Delta,
            ParentRevisionFrameTicket = Previous(a.FrameTicket),
        };
        foreach (uint id in new[] { BBaseUpdateId, BDeltaUpdateId,
            BExternalId, BRelocateId }) {
            bLocalDictionary.BindSelf(id);
        }

        FrameBuilder bLocalBuilder = new() {
            ObjectVersionDictionary = bLocalDictionary,
        };
        AddBase(bLocalBuilder, BBaseUpdateId, 40, 1);
        AddBase(bLocalBuilder, BDeltaUpdateId, 41, 1);
        AddBase(bLocalBuilder, BExternalId, 50, 1);
        AddBase(bLocalBuilder, BRelocateId, 51, 1);
        AbsoluteFrameAddress bLocal = AppendExact(current, bLocalBuilder);

        ObjectVersionDictionaryBuilder publishedDictionary = new() {
            Kind = ObjectVersionDictionaryKind.Delta,
            ParentRevisionFrameTicket = Current(bLocal.FrameTicket),
        };
        publishedDictionary.BindSelf(ANoChangeId);
        publishedDictionary.BindSelf(AUpdateId);
        FrameBuilder publishedBuilder = new() {
            ObjectVersionDictionary = publishedDictionary,
        };
        AddDelta(
            publishedBuilder,
            ANoChangeId,
            Previous(a.FrameTicket),
            10,
            12,
            2,
            12,
            2 + sourceOrdinalOffset);
        AddDelta(
            publishedBuilder,
            AUpdateId,
            Previous(a.FrameTicket),
            20,
            25,
            5,
            25,
            2 + sourceOrdinalOffset);
        AbsoluteFrameAddress published = AppendExact(current, publishedBuilder);
        return new ApplyFixture(store, previous, current, a, bLocal, published);
    }

    private static PairInputs CreatePairInputs(ApplyFixture source) {
        NormalizedSaveFacts facts = SaveStepNormalizer.Normalize(
            source.Store,
            source.Current.FileNumber,
            source.PublishedRevisionAddress,
            new SaveStep([
                new RemoveObject(RemoveId),
                new UpdateObject(BDeltaUpdateId, 43, 2),
                new CreateObject(InsertId, 7),
                new UpdateObject(AUpdateId, 30, 5),
                new UpdateObject(BBaseUpdateId, 44, 4),
            ]));
        StayBSaveDecision stayDecision = new(
            [
                new UpdateWriteDecision(AUpdateId, UpdateWriteMode.Delta),
                new UpdateWriteDecision(BBaseUpdateId, UpdateWriteMode.Base),
                new UpdateWriteDecision(BDeltaUpdateId, UpdateWriteMode.Delta),
            ],
            [ANoChangeId]);
        RotateCSaveDecision rotateDecision = new(
            [
                new UpdateWriteDecision(BBaseUpdateId, UpdateWriteMode.Base),
                new UpdateWriteDecision(BDeltaUpdateId, UpdateWriteMode.Delta),
            ],
            [BRelocateId]);
        ExplicitCandidatePairEvaluation pair = ExplicitCandidatePairEvaluator.Evaluate(
            source.Store,
            facts,
            stayDecision,
            rotateDecision);
        return new PairInputs(facts, stayDecision, rotateDecision, pair);
    }

    private static ProbeRevisionCursor CreateCursor(ApplyFixture source) => new(
        new FileScope(source.Current.FileNumber),
        source.PublishedRevisionAddress,
        source.Current.TailOffsetBytes);

    private static FeasibleCandidate<StayBRevisionPlan> GetStay(
        ExplicitCandidatePairEvaluation pair) =>
        Assert.IsType<FeasibleCandidate<StayBRevisionPlan>>(pair.StayBAttempt);

    private static FeasibleCandidate<RotateCRevisionPlan> GetRotate(
        ExplicitCandidatePairEvaluation pair) =>
        Assert.IsType<FeasibleCandidate<RotateCRevisionPlan>>(pair.RotateCAttempt);

    private static void AssertRuntimeState(
        RbfFileStore store,
        AbsoluteFrameAddress revisionAddress,
        IReadOnlyDictionary<uint, LogicalObjectState> expected) {
        IReadOnlyDictionary<uint, AbsoluteFrameAddress> bindings =
            ObjectVersionDictionaryReader.MaterializeLive(
                store,
                revisionAddress).Bindings;
        AssertExactState(expected, PhysicalStateOracle.Materialize(store, bindings));
    }

    private static void AssertExactState(
        IReadOnlyDictionary<uint, LogicalObjectState> expected,
        IReadOnlyDictionary<uint, LogicalObjectState> actual) {
        Assert.Equal(expected.Count, actual.Count);
        foreach ((uint objectId, LogicalObjectState state) in expected) {
            Assert.True(actual.TryGetValue(objectId, out LogicalObjectState actualState));
            Assert.Equal(state, actualState);
        }
    }

    private static void AddBase(
        FrameBuilder frame,
        uint objectId,
        int payloadBytes,
        int ordinal) {
        ObjectVersionBuilder version = frame.Add(objectId);
        version.Kind = ObjectVersionKind.Base;
        version.PayloadBytes = payloadBytes;
        version.ReconstructionObjectPayloadBytes = payloadBytes;
        version.ResultBasePayloadBytes = payloadBytes;
        version.LogicalVersionOrdinal = ordinal;
    }

    private static void AddDelta(
        FrameBuilder frame,
        uint objectId,
        RelativeFrameTicket parent,
        int parentBytes,
        int resultBytes,
        int deltaBytes,
        long reconstructionBytes,
        int ordinal) {
        ObjectVersionBuilder version = frame.Add(objectId);
        version.Kind = ObjectVersionKind.Delta;
        version.PayloadBytes = deltaBytes;
        version.ReconstructionObjectPayloadBytes = reconstructionBytes;
        version.ResultBasePayloadBytes = resultBytes;
        version.ExpectedParentBasePayloadBytes = parentBytes;
        version.LogicalVersionOrdinal = ordinal;
        version.DeltaParentFrameTicket = parent;
    }

    private static AbsoluteFrameAddress AppendExact(RbfFile file, FrameBuilder builder) {
        Frame frame = builder.Build();
        ProvisionalRevisionV0Estimate estimate =
            ProvisionalRevisionV0Estimator.Estimate(frame, file.TailOffsetBytes);
        FrameTicket ticket = file.Append(
            frame,
            estimate.RbfLayout.PayloadLengthBytes,
            estimate.RbfLayout.TailMetaLengthBytes);
        return new AbsoluteFrameAddress(file.FileNumber, ticket);
    }

    private static RelativeFrameTicket Previous(FrameTicket ticket) =>
        new(IsPreviousFile: true, ticket);

    private static RelativeFrameTicket Current(FrameTicket ticket) =>
        new(IsPreviousFile: false, ticket);

    private static StoreSnapshot CaptureStore(RbfFileStore store) => new(
        store.FileCount,
        Enumerable.Range(1, store.FileCount)
            .Select(index => store.GetFile((uint)index))
            .Select(static file => new FileSnapshot(
                file.FileNumber,
                file.FrameCount,
                file.TailOffsetBytes))
            .ToArray());

    private static void AssertStoreSnapshot(StoreSnapshot expected, StoreSnapshot actual) {
        Assert.Equal(expected.FileCount, actual.FileCount);
        Assert.Equal(expected.Files, actual.Files);
    }

    private sealed record ApplyFixture(
        RbfFileStore Store,
        RbfFile Previous,
        RbfFile Current,
        AbsoluteFrameAddress PreviousRevisionAddress,
        AbsoluteFrameAddress BLocalRevisionAddress,
        AbsoluteFrameAddress PublishedRevisionAddress);

    private sealed record PairInputs(
        NormalizedSaveFacts Facts,
        StayBSaveDecision StayDecision,
        RotateCSaveDecision RotateDecision,
        ExplicitCandidatePairEvaluation Pair);

    private sealed record StoreSnapshot(int FileCount, IReadOnlyList<FileSnapshot> Files);

    private sealed record FileSnapshot(uint FileNumber, int FrameCount, long TailOffsetBytes);
}
