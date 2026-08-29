using Atelia.TwoLegRotationProbe.Encoding;
using Atelia.TwoLegRotationProbe.Model;
using Atelia.TwoLegRotationProbe.Planning;
using Atelia.TwoLegRotationProbe.Rotation;
using Atelia.TwoLegRotationProbe.Simulation;
using Atelia.TwoLegRotationProbe.Workloads;

namespace Atelia.TwoLegRotationProbe.Tests;

public sealed class ContinuousMultiRotationTests {
    private const uint FirstObjectId = 10;
    private const uint SecondObjectId = 20;
    private const uint ThirdObjectId = 30;

    [Fact]
    public void Caller_script_crosses_two_file_rotations_with_exact_stay_certificates() {
        ScriptSource source = CreateSource();
        ProbeRevisionCursor cursor = new(
            new FileScope(source.Current.FileNumber),
            source.PublishedRevisionAddress,
            source.Current.TailOffsetBytes);
        List<CandidateRawObservation> selectedObservations = [];

        NormalizedSaveFacts firstFacts = SaveStepNormalizer.Normalize(
            source.Store,
            cursor.FileScope.CurrentFileNumber,
            cursor.PublishedRevisionAddress,
            new SaveStep([new CreateObject(SecondObjectId, 20)]));
        AssertFactsSource(cursor, firstFacts);
        ExplicitCandidatePairEvaluation firstPair =
            ExplicitCandidatePairEvaluator.Evaluate(
                source.Store,
                firstFacts,
                new StayBSaveDecision([], []),
                new RotateCSaveDecision([], []));
        FeasibleCandidate<StayBRevisionPlan> firstStay = GetStay(firstPair);
        AssertSelected(firstPair, firstStay);
        AssertCandidateSourceTail(cursor, firstStay.Plan.Revision, isRotate: false);

        RbfFileStore firstCertificateReplayStore = source.Store.ForkForProbe();
        StoreSnapshot beforeFirstCertificate = CaptureStore(source.Store);
        CanPrepareAndRotateProven firstProof = Assert.IsType<CanPrepareAndRotateProven>(
            CanPrepareAndRotateCertificatePlanner.TryCreate(
                source.Store,
                cursor,
                firstStay));
        AssertStoreSnapshot(beforeFirstCertificate, CaptureStore(source.Store));
        Assert.Same(cursor, firstProof.Certificate.InitialCursor);
        Assert.Same(firstStay, firstProof.Certificate.InitialStayB);
        AssertCertificateReplay(
            firstCertificateReplayStore,
            firstProof.Certificate,
            firstFacts.PostLiveStates,
            expectedPreviousFileNumber: 2,
            expectedCurrentFileNumber: 3);

        cursor = ExplicitProbeRevisionApplier.ApplyStayB(
            source.Store,
            cursor,
            firstProof.Certificate.InitialStayB);
        selectedObservations.Add(firstStay.Observation);
        AssertAppliedStep(
            source.Store,
            cursor,
            firstFacts,
            firstStay.Observation,
            expectedPreviousFileNumber: 1,
            expectedCurrentFileNumber: 2);
        Assert.Equal(2, source.Store.FileCount);

        NormalizedSaveFacts secondFacts = SaveStepNormalizer.Normalize(
            source.Store,
            cursor.FileScope.CurrentFileNumber,
            cursor.PublishedRevisionAddress,
            new SaveStep([new UpdateObject(SecondObjectId, 22, 2)]));
        AssertFactsSource(cursor, secondFacts);
        ExplicitCandidatePairEvaluation secondPair =
            ExplicitCandidatePairEvaluator.Evaluate(
                source.Store,
                secondFacts,
                new StayBSaveDecision(
                    [new UpdateWriteDecision(SecondObjectId, UpdateWriteMode.Delta)],
                    []),
                new RotateCSaveDecision(
                    [new UpdateWriteDecision(SecondObjectId, UpdateWriteMode.Delta)],
                    []));
        FeasibleCandidate<RotateCRevisionPlan> secondRotate = GetRotate(secondPair);
        AssertSelected(secondPair, secondRotate);
        AssertCandidateSourceTail(cursor, secondRotate.Plan.Revision, isRotate: true);

        cursor = ExplicitProbeRevisionApplier.ApplyRotateC(
            source.Store,
            cursor,
            secondRotate);
        selectedObservations.Add(secondRotate.Observation);
        AssertAppliedStep(
            source.Store,
            cursor,
            secondFacts,
            secondRotate.Observation,
            expectedPreviousFileNumber: 2,
            expectedCurrentFileNumber: 3);
        Assert.Equal(3, source.Store.FileCount);

        NormalizedSaveFacts thirdFacts = SaveStepNormalizer.Normalize(
            source.Store,
            cursor.FileScope.CurrentFileNumber,
            cursor.PublishedRevisionAddress,
            new SaveStep([new UpdateObject(FirstObjectId, 11, 1)]));
        AssertFactsSource(cursor, thirdFacts);
        ExplicitCandidatePairEvaluation thirdPair =
            ExplicitCandidatePairEvaluator.Evaluate(
                source.Store,
                thirdFacts,
                new StayBSaveDecision(
                    [new UpdateWriteDecision(FirstObjectId, UpdateWriteMode.Delta)],
                    [SecondObjectId]),
                new RotateCSaveDecision(
                    [new UpdateWriteDecision(FirstObjectId, UpdateWriteMode.Delta)],
                    []));
        FeasibleCandidate<StayBRevisionPlan> thirdStay = GetStay(thirdPair);
        AssertSelected(thirdPair, thirdStay);
        AssertCandidateSourceTail(cursor, thirdStay.Plan.Revision, isRotate: false);

        RbfFileStore thirdCertificateReplayStore = source.Store.ForkForProbe();
        StoreSnapshot beforeThirdCertificate = CaptureStore(source.Store);
        CanPrepareAndRotateProven thirdProof = Assert.IsType<CanPrepareAndRotateProven>(
            CanPrepareAndRotateCertificatePlanner.TryCreate(
                source.Store,
                cursor,
                thirdStay));
        AssertStoreSnapshot(beforeThirdCertificate, CaptureStore(source.Store));
        Assert.Same(cursor, thirdProof.Certificate.InitialCursor);
        Assert.Same(thirdStay, thirdProof.Certificate.InitialStayB);
        AssertCertificateReplay(
            thirdCertificateReplayStore,
            thirdProof.Certificate,
            thirdFacts.PostLiveStates,
            expectedPreviousFileNumber: 3,
            expectedCurrentFileNumber: 4);

        cursor = ExplicitProbeRevisionApplier.ApplyStayB(
            source.Store,
            cursor,
            thirdProof.Certificate.InitialStayB);
        selectedObservations.Add(thirdStay.Observation);
        AssertAppliedStep(
            source.Store,
            cursor,
            thirdFacts,
            thirdStay.Observation,
            expectedPreviousFileNumber: 2,
            expectedCurrentFileNumber: 3);
        Assert.Equal(3, source.Store.FileCount);

        NormalizedSaveFacts fourthFacts = SaveStepNormalizer.Normalize(
            source.Store,
            cursor.FileScope.CurrentFileNumber,
            cursor.PublishedRevisionAddress,
            new SaveStep([
                new RemoveObject(FirstObjectId),
                new CreateObject(ThirdObjectId, 30),
            ]));
        AssertFactsSource(cursor, fourthFacts);
        ExplicitCandidatePairEvaluation fourthPair =
            ExplicitCandidatePairEvaluator.Evaluate(
                source.Store,
                fourthFacts,
                new StayBSaveDecision([], []),
                new RotateCSaveDecision([], []));
        FeasibleCandidate<RotateCRevisionPlan> fourthRotate = GetRotate(fourthPair);
        AssertSelected(fourthPair, fourthRotate);
        AssertCandidateSourceTail(cursor, fourthRotate.Plan.Revision, isRotate: true);
        Assert.Empty(fourthRotate.Plan.Decision.BContainedUpdateDecisions);
        Assert.Empty(fourthRotate.Plan.Decision.BContainedNoChangeBaseObjectIds);
        ObjectVersionDictionary finalDictionary = Assert.IsType<ObjectVersionDictionary>(
            fourthRotate.Plan.Revision.Frame.ObjectVersionDictionary);
        Assert.Equal(ObjectVersionDictionaryKind.Base, finalDictionary.Kind);
        ObjectVersionDictionaryBinding secondBinding = finalDictionary.Entries[SecondObjectId];
        Assert.Equal(ObjectVersionDictionaryBindingKind.External, secondBinding.Kind);
        Assert.Equal(
            new RelativeFrameTicket(
                IsPreviousFile: true,
                fourthFacts.ParentLive[SecondObjectId].HeadAddress.FrameTicket),
            secondBinding.ExternalFrameTicket);

        cursor = ExplicitProbeRevisionApplier.ApplyRotateC(
            source.Store,
            cursor,
            fourthRotate);
        selectedObservations.Add(fourthRotate.Observation);
        AssertAppliedStep(
            source.Store,
            cursor,
            fourthFacts,
            fourthRotate.Observation,
            expectedPreviousFileNumber: 3,
            expectedCurrentFileNumber: 4);
        Assert.Equal(4, source.Store.FileCount);
        AssertExactState(
            new Dictionary<uint, LogicalObjectState> {
                [SecondObjectId] = new(22, 2),
                [ThirdObjectId] = new(30, 1),
            },
            fourthFacts.PostLiveStates);

        Assert.Equal(
            [
                CandidateTarget.StayB,
                CandidateTarget.RotateC,
                CandidateTarget.StayB,
                CandidateTarget.RotateC,
            ],
            selectedObservations.Select(static observation => observation.Target));
        Assert.Equal(
            [FirstObjectId],
            selectedObservations[0]
                .PostLiveReconstruction
                .PreviousFileDependentObjectIds);
        Assert.Equal(
            [SecondObjectId],
            selectedObservations[1]
                .PostLiveReconstruction
                .PreviousFileDependentObjectIds);
        Assert.Empty(
            selectedObservations[2]
                .PostLiveReconstruction
                .PreviousFileDependentObjectIds);
        Assert.Equal(
            [SecondObjectId],
            selectedObservations[3]
                .PostLiveReconstruction
                .PreviousFileDependentObjectIds);
    }

    private static ScriptSource CreateSource() {
        RbfFileStore store = new();
        RbfFile previous = store.CreateFile();
        ObjectVersionDictionaryBuilder previousDictionary = new();
        previousDictionary.BindSelf(FirstObjectId);
        FrameBuilder previousBuilder = new() {
            ObjectVersionDictionary = previousDictionary,
        };
        AddBase(previousBuilder, FirstObjectId, payloadBytes: 10, ordinal: 1);
        AbsoluteFrameAddress previousRevision = AppendExact(previous, previousBuilder);

        RbfFile current = store.CreateFile();
        FrameBuilder publishedBuilder = new() {
            ObjectVersionDictionary = new ObjectVersionDictionaryBuilder {
                Kind = ObjectVersionDictionaryKind.Delta,
                ParentRevisionFrameTicket = new RelativeFrameTicket(
                    IsPreviousFile: true,
                    previousRevision.FrameTicket),
            },
        };
        AbsoluteFrameAddress published = AppendExact(current, publishedBuilder);
        return new ScriptSource(store, current, published);
    }

    private static void AssertFactsSource(
        ProbeRevisionCursor source,
        NormalizedSaveFacts facts) {
        Assert.Equal(source.FileScope.PreviousFileNumber, facts.PreviousFileNumber);
        Assert.Equal(source.FileScope.CurrentFileNumber, facts.CurrentFileNumber);
        Assert.Equal(source.PublishedRevisionAddress, facts.PublishedRevisionAddress);
    }

    private static void AssertCandidateSourceTail(
        ProbeRevisionCursor source,
        PlannedRevisionV0 candidate,
        bool isRotate) {
        if (isRotate) {
            Assert.Equal(
                checked(source.FileScope.CurrentFileNumber + 1),
                candidate.FileNumber);
            Assert.Equal(
                RbfV040Layout.InitialTailOffsetBytes,
                candidate.Estimate.RbfLayout.FrameStartOffsetBytes);
        } else {
            Assert.Equal(source.FileScope.CurrentFileNumber, candidate.FileNumber);
            Assert.Equal(
                source.CurrentFileTailOffsetBytes,
                candidate.Estimate.RbfLayout.FrameStartOffsetBytes);
        }
    }

    private static void AssertSelected(
        ExplicitCandidatePairEvaluation pair,
        FeasibleCandidate<StayBRevisionPlan> selected) {
        Assert.Same(pair.Facts, selected.Plan.Facts);
        Assert.Same(pair.Facts, selected.Observation.Facts);
        Assert.Same(selected.Plan.Revision, selected.Observation.Candidate);
        Assert.Equal(CandidateTarget.StayB, selected.Observation.Target);
    }

    private static void AssertSelected(
        ExplicitCandidatePairEvaluation pair,
        FeasibleCandidate<RotateCRevisionPlan> selected) {
        Assert.Same(pair.Facts, selected.Plan.Facts);
        Assert.Same(pair.Facts, selected.Observation.Facts);
        Assert.Same(selected.Plan.Revision, selected.Observation.Candidate);
        Assert.Equal(CandidateTarget.RotateC, selected.Observation.Target);
    }

    private static void AssertAppliedStep(
        RbfFileStore store,
        ProbeRevisionCursor result,
        NormalizedSaveFacts facts,
        CandidateRawObservation selectedObservation,
        uint expectedPreviousFileNumber,
        uint expectedCurrentFileNumber) {
        Assert.Equal(expectedPreviousFileNumber, result.FileScope.PreviousFileNumber);
        Assert.Equal(expectedCurrentFileNumber, result.FileScope.CurrentFileNumber);
        Assert.Equal(selectedObservation.Candidate.Address, result.PublishedRevisionAddress);
        Assert.Equal(
            selectedObservation.Layout.TailOffsetAfterBytes,
            result.CurrentFileTailOffsetBytes);
        Assert.Equal(
            expectedPreviousFileNumber,
            selectedObservation.PostLiveReconstruction.ResultScope.PreviousFileNumber);
        Assert.Equal(
            expectedCurrentFileNumber,
            selectedObservation.PostLiveReconstruction.ResultScope.CurrentFileNumber);
        AssertRuntimeStateAndClosure(store, result, facts.PostLiveStates);
    }

    private static void AssertCertificateReplay(
        RbfFileStore replayStore,
        CanPrepareAndRotateCertificate certificate,
        IReadOnlyDictionary<uint, LogicalObjectState> expectedState,
        uint expectedPreviousFileNumber,
        uint expectedCurrentFileNumber) {
        ProbeRevisionCursor replayCursor = ExplicitProbeRevisionApplier.ApplyStayB(
            replayStore,
            certificate.InitialCursor,
            certificate.InitialStayB);
        foreach (FeasibleCandidate<StayBRevisionPlan> maintenance in
            certificate.MaintenanceStayBSteps) {
            replayCursor = ExplicitProbeRevisionApplier.ApplyStayB(
                replayStore,
                replayCursor,
                maintenance);
        }

        replayCursor = ExplicitProbeRevisionApplier.ApplyRotateC(
            replayStore,
            replayCursor,
            certificate.FinalRotateC);
        Assert.Equal(expectedPreviousFileNumber, replayCursor.FileScope.PreviousFileNumber);
        Assert.Equal(expectedCurrentFileNumber, replayCursor.FileScope.CurrentFileNumber);
        AssertRuntimeStateAndClosure(replayStore, replayCursor, expectedState);
    }

    private static void AssertRuntimeStateAndClosure(
        RbfFileStore store,
        ProbeRevisionCursor cursor,
        IReadOnlyDictionary<uint, LogicalObjectState> expectedState) {
        ObjectVersionDictionaryMaterializationInspection materialization =
            ObjectVersionDictionaryReader.MaterializeLive(
                store,
                cursor.PublishedRevisionAddress);
        AssertExactState(
            expectedState,
            PhysicalStateOracle.Materialize(store, materialization.Bindings));

        uint[] allowedFileNumbers = [
            cursor.FileScope.PreviousFileNumber!.Value,
            cursor.FileScope.CurrentFileNumber,
        ];
        Assert.All(
            materialization.DictionaryRevisionAddresses,
            address => Assert.Contains(address.FileNumber, allowedFileNumbers));
        foreach ((uint objectId, AbsoluteFrameAddress headAddress) in
            materialization.Bindings) {
            ObjectReconstructionInspection reconstruction =
                PhysicalStateOracle.InspectObjectReconstruction(
                    store,
                    objectId,
                    headAddress);
            Assert.All(
                reconstruction.ReconstructionFrameAddresses,
                address => Assert.Contains(address.FileNumber, allowedFileNumbers));
        }
    }

    private static void AssertExactState(
        IReadOnlyDictionary<uint, LogicalObjectState> expected,
        IReadOnlyDictionary<uint, LogicalObjectState> actual) {
        Assert.Equal(expected.Count, actual.Count);
        foreach ((uint objectId, LogicalObjectState expectedState) in expected) {
            Assert.True(
                actual.TryGetValue(objectId, out LogicalObjectState actualState));
            Assert.Equal(expectedState, actualState);
        }
    }

    private static FeasibleCandidate<StayBRevisionPlan> GetStay(
        ExplicitCandidatePairEvaluation pair) =>
        Assert.IsType<FeasibleCandidate<StayBRevisionPlan>>(pair.StayBAttempt);

    private static FeasibleCandidate<RotateCRevisionPlan> GetRotate(
        ExplicitCandidatePairEvaluation pair) =>
        Assert.IsType<FeasibleCandidate<RotateCRevisionPlan>>(pair.RotateCAttempt);

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

    private static AbsoluteFrameAddress AppendExact(
        RbfFile file,
        FrameBuilder builder) {
        Frame frame = builder.Build();
        ProvisionalRevisionV0Estimate estimate =
            ProvisionalRevisionV0Estimator.Estimate(frame, file.TailOffsetBytes);
        FrameTicket ticket = file.Append(
            frame,
            estimate.RbfLayout.PayloadLengthBytes,
            estimate.RbfLayout.TailMetaLengthBytes);
        return new AbsoluteFrameAddress(file.FileNumber, ticket);
    }

    private static StoreSnapshot CaptureStore(RbfFileStore store) => new(
        store.FileCount,
        Enumerable.Range(1, store.FileCount)
            .Select(index => store.GetFile((uint)index))
            .Select(static file => new FileSnapshot(
                file.FileNumber,
                file.FrameCount,
                file.TailOffsetBytes))
            .ToArray());

    private static void AssertStoreSnapshot(
        StoreSnapshot expected,
        StoreSnapshot actual) {
        Assert.Equal(expected.FileCount, actual.FileCount);
        Assert.Equal(expected.Files, actual.Files);
    }

    private sealed record ScriptSource(
        RbfFileStore Store,
        RbfFile Current,
        AbsoluteFrameAddress PublishedRevisionAddress);

    private sealed record StoreSnapshot(
        int FileCount,
        IReadOnlyList<FileSnapshot> Files);

    private sealed record FileSnapshot(
        uint FileNumber,
        int FrameCount,
        long TailOffsetBytes);
}
