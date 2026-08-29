using Atelia.TwoLegRotationProbe.Encoding;
using Atelia.TwoLegRotationProbe.Model;
using Atelia.TwoLegRotationProbe.Planning;
using Atelia.TwoLegRotationProbe.Rotation;
using Atelia.TwoLegRotationProbe.Simulation;
using Atelia.TwoLegRotationProbe.Workloads;

namespace Atelia.TwoLegRotationProbe.Tests;

public sealed class PairedCandidateEvaluationTests {
    private const uint ANoChangeId = 10;
    private const uint AUpdateId = 20;
    private const uint RemoveId = 30;
    private const uint BBaseUpdateId = 40;
    private const uint BDeltaUpdateId = 41;
    private const uint BExternalId = 50;
    private const uint BRelocateId = 51;
    private const uint InsertId = 70;
    private const int HugeADebtPayloadBytes =
        RbfV040Layout.MaxPayloadAndTailMetaLengthBytes - 20;

    [Fact]
    public void Both_candidates_succeed_from_the_same_inputs_with_raw_observations() {
        PairFixture source = CreateStrongMixedSource();
        NormalizedSaveFacts facts = NormalizeMixed(source, CreateMixedSaveStep());
        StayBSaveDecision stayDecision = CreateStayDecision();
        RotateCSaveDecision rotateDecision = CreateRotateDecision();
        StoreSnapshot before = CaptureStore(source.Store);

        ExplicitCandidatePairEvaluation pair = ExplicitCandidatePairEvaluator.Evaluate(
            source.Store,
            facts,
            stayDecision,
            rotateDecision);

        AssertStoreSnapshot(before, CaptureStore(source.Store));
        Assert.Same(facts, pair.Facts);
        Assert.Same(stayDecision, pair.StayBDecision);
        Assert.Same(rotateDecision, pair.RotateCDecision);
        FeasibleCandidate<StayBRevisionPlan> stay =
            Assert.IsType<FeasibleCandidate<StayBRevisionPlan>>(pair.StayBAttempt);
        FeasibleCandidate<RotateCRevisionPlan> rotate =
            Assert.IsType<FeasibleCandidate<RotateCRevisionPlan>>(pair.RotateCAttempt);
        AssertFeasibleIdentity(facts, stayDecision, stay);
        AssertFeasibleIdentity(facts, rotateDecision, rotate);

        Assert.Equal(CandidateTarget.StayB, stay.Observation.Target);
        Assert.Equal(CandidateTarget.RotateC, rotate.Observation.Target);
        Assert.Same(stay.Plan.Revision, stay.Observation.Candidate);
        Assert.Same(rotate.Plan.Revision, rotate.Observation.Candidate);
        Assert.Same(stay.Plan.Revision.Estimate, stay.Observation.Estimate);
        Assert.Same(rotate.Plan.Revision.Estimate, rotate.Observation.Estimate);
        Assert.Equal(stay.Plan.Revision.Estimate.RbfLayout, stay.Observation.Layout);
        Assert.Equal(rotate.Plan.Revision.Estimate.RbfLayout, rotate.Observation.Layout);

        Assert.Equal(
            SumRecordBytes(stay.Plan.Revision, [AUpdateId, BBaseUpdateId,
                BDeltaUpdateId, InsertId]),
            stay.Observation.ForegroundDomainRecordBytes);
        Assert.Equal(
            SumRecordBytes(stay.Plan.Revision, [ANoChangeId]),
            stay.Observation.MaintenanceDomainRecordBytes);
        Assert.Equal(
            SumRecordBytes(rotate.Plan.Revision, [AUpdateId, BBaseUpdateId,
                BDeltaUpdateId, InsertId]),
            rotate.Observation.ForegroundDomainRecordBytes);
        Assert.Equal(
            SumRecordBytes(rotate.Plan.Revision, [ANoChangeId, BRelocateId]),
            rotate.Observation.MaintenanceDomainRecordBytes);

        CandidateReconstructionObservation stayRead =
            stay.Observation.PostLiveReconstruction;
        CandidateReconstructionObservation rotateRead =
            rotate.Observation.PostLiveReconstruction;
        Assert.Equal(source.Current.FileNumber, stayRead.ResultScope.CurrentFileNumber);
        Assert.Equal(source.Previous.FileNumber, stayRead.ResultScope.PreviousFileNumber);
        Assert.Equal(
            CanonicalAddresses([
                source.PreviousRevisionAddress,
                source.BLocalRevisionAddress,
                source.PublishedRevisionAddress,
                stay.Plan.Revision.Address,
            ]),
            stayRead.UniqueFrameAddresses);
        Assert.Equal([AUpdateId], stayRead.PreviousFileDependentObjectIds);
        Assert.Equal(30, stayRead.PreviousFileDependentBasePayloadBytes);
        Assert.Equal(1, stayRead.PreviousFileUniqueFrameCount);
        Assert.Equal(
            source.Store.ReadLayout(source.PreviousRevisionAddress).FrameLengthBytes,
            stayRead.PreviousFileFrameBytes);

        Assert.Equal(source.Current.FileNumber + 1,
            rotateRead.ResultScope.CurrentFileNumber);
        Assert.Equal(source.Current.FileNumber, rotateRead.ResultScope.PreviousFileNumber);
        Assert.Equal(
            CanonicalAddresses([
                source.BLocalRevisionAddress,
                rotate.Plan.Revision.Address,
            ]),
            rotateRead.UniqueFrameAddresses);
        Assert.Equal(
            [BDeltaUpdateId, BExternalId],
            rotateRead.PreviousFileDependentObjectIds);
        Assert.Equal(93, rotateRead.PreviousFileDependentBasePayloadBytes);
        Assert.Equal(1, rotateRead.PreviousFileUniqueFrameCount);
        Assert.Equal(
            source.Store.ReadLayout(source.BLocalRevisionAddress).FrameLengthBytes,
            rotateRead.PreviousFileFrameBytes);

        Assert.Equal(facts.PostLiveStates.Count, stayRead.Metrics.LiveObjectCount);
        Assert.Equal(facts.PostLiveStates.Count, rotateRead.Metrics.LiveObjectCount);
        Assert.Equal(10, stayRead.Metrics.RequiredObjectVersionCount);
        Assert.Equal(8, rotateRead.Metrics.RequiredObjectVersionCount);
        Assert.Equal(237, stayRead.Metrics.RequiredObjectPayloadBytes);
        Assert.Equal(237, rotateRead.Metrics.RequiredObjectPayloadBytes);
        Assert.Equal(319, stayRead.Metrics.ObjectPayloadBytesInUniqueFrames);
        Assert.Equal(328, rotateRead.Metrics.ObjectPayloadBytesInUniqueFrames);
    }

    [Fact]
    public void Stay_capacity_rejection_does_not_skip_Rotate_evaluation() {
        PairFixture source = CreateStrongMixedSource();
        ExhaustCurrentRelativeStart(source.Current);
        NormalizedSaveFacts facts = NormalizeMixed(source, CreateMixedSaveStep());
        StoreSnapshot before = CaptureStore(source.Store);

        ExplicitCandidatePairEvaluation pair = ExplicitCandidatePairEvaluator.Evaluate(
            source.Store,
            facts,
            CreateStayDecision(),
            CreateRotateDecision());

        CapacityRejectedCandidate<StayBRevisionPlan> stay =
            Assert.IsType<CapacityRejectedCandidate<StayBRevisionPlan>>(
                pair.StayBAttempt);
        Assert.Equal(
            RevisionCandidateCapacityLimit.TargetFrameStartRelative,
            stay.Rejection.Limit);
        FeasibleCandidate<RotateCRevisionPlan> rotate =
            Assert.IsType<FeasibleCandidate<RotateCRevisionPlan>>(
                pair.RotateCAttempt);
        Assert.Equal(CandidateTarget.RotateC, rotate.Observation.Target);
        AssertStoreSnapshot(before, CaptureStore(source.Store));
    }

    [Fact]
    public void Rotate_capacity_rejection_does_not_hide_Stay_success() {
        PairFixture source = CreateHugeADebtSource();
        NormalizedSaveFacts facts = NormalizeHuge(source);
        StoreSnapshot before = CaptureStore(source.Store);

        ExplicitCandidatePairEvaluation pair = ExplicitCandidatePairEvaluator.Evaluate(
            source.Store,
            facts,
            new StayBSaveDecision([], []),
            new RotateCSaveDecision([], []));

        FeasibleCandidate<StayBRevisionPlan> stay =
            Assert.IsType<FeasibleCandidate<StayBRevisionPlan>>(pair.StayBAttempt);
        Assert.Equal(CandidateTarget.StayB, stay.Observation.Target);
        CandidateReconstructionObservation stayRead =
            stay.Observation.PostLiveReconstruction;
        RbfFrameLayoutEstimate storedPreviousLayout =
            source.Store.ReadLayout(source.PreviousRevisionAddress);
        Assert.True(
            storedPreviousLayout.FrameLengthBytes > RbfV040Layout.MinFrameLengthBytes);
        Assert.Equal(
            CanonicalAddresses([
                source.PreviousRevisionAddress,
                stay.Plan.Revision.Address,
            ]),
            stayRead.UniqueFrameAddresses);
        Assert.Equal([ANoChangeId], stayRead.PreviousFileDependentObjectIds);
        Assert.Equal(HugeADebtPayloadBytes,
            stayRead.PreviousFileDependentBasePayloadBytes);
        Assert.Equal(storedPreviousLayout.FrameLengthBytes,
            stayRead.PreviousFileFrameBytes);
        Assert.Equal(
            (long)HugeADebtPayloadBytes + 1,
            stayRead.Metrics.RequiredObjectPayloadBytes);
        Assert.Equal(
            (long)HugeADebtPayloadBytes + 1,
            stayRead.Metrics.ObjectPayloadBytesInUniqueFrames);
        Assert.Equal(
            (long)storedPreviousLayout.FrameLengthBytes +
                stay.Plan.Revision.Estimate.RbfLayout.FrameLengthBytes,
            stayRead.Metrics.ModeledRbfFrameBytesRead);
        CapacityRejectedCandidate<RotateCRevisionPlan> rotate =
            Assert.IsType<CapacityRejectedCandidate<RotateCRevisionPlan>>(
                pair.RotateCAttempt);
        Assert.Equal(
            RevisionCandidateCapacityLimit.PayloadAndTailMetaLength,
            rotate.Rejection.Limit);
        AssertStoreSnapshot(before, CaptureStore(source.Store));
    }

    [Fact]
    public void Inconsistent_source_accounting_escapes_instead_of_becoming_capacity() {
        PairFixture source = CreateMalformedSourceAccounting();
        NormalizedSaveFacts facts = NormalizeHuge(source);
        Frame sourceFrame = source.Store.ReadFrame(source.PreviousRevisionAddress);
        RbfFrameLayoutEstimate stored =
            source.Store.ReadLayout(source.PreviousRevisionAddress);
        RbfFrameLayoutEstimate exact = ProvisionalRevisionV0Estimator.Estimate(
            sourceFrame,
            stored.FrameStartOffsetBytes).RbfLayout;
        Assert.NotEqual(exact, stored);
        StoreSnapshot before = CaptureStore(source.Store);

        Assert.Throws<InvalidDataException>(() =>
            ExplicitCandidatePairEvaluator.Evaluate(
                source.Store,
                facts,
                new StayBSaveDecision([], []),
                new RotateCSaveDecision([], [])));

        AssertStoreSnapshot(before, CaptureStore(source.Store));
    }

    [Fact]
    public void Both_capacity_rejections_are_reported_independently() {
        PairFixture source = CreateHugeADebtSource();
        NormalizedSaveFacts facts = NormalizeHuge(source);
        ExhaustCurrentRelativeStart(source.Current);
        StoreSnapshot before = CaptureStore(source.Store);

        ExplicitCandidatePairEvaluation pair = ExplicitCandidatePairEvaluator.Evaluate(
            source.Store,
            facts,
            new StayBSaveDecision([], []),
            new RotateCSaveDecision([], []));

        CapacityRejectedCandidate<StayBRevisionPlan> stay =
            Assert.IsType<CapacityRejectedCandidate<StayBRevisionPlan>>(
                pair.StayBAttempt);
        CapacityRejectedCandidate<RotateCRevisionPlan> rotate =
            Assert.IsType<CapacityRejectedCandidate<RotateCRevisionPlan>>(
                pair.RotateCAttempt);
        Assert.Equal(
            RevisionCandidateCapacityLimit.TargetFrameStartRelative,
            stay.Rejection.Limit);
        Assert.Equal(
            RevisionCandidateCapacityLimit.PayloadAndTailMetaLength,
            rotate.Rejection.Limit);
        Assert.NotEqual(stay.Rejection.Limit, rotate.Rejection.Limit);
        AssertStoreSnapshot(before, CaptureStore(source.Store));
    }

    [Fact]
    public void Caller_decision_errors_escape_instead_of_becoming_outcomes() {
        PairFixture source = CreateStrongMixedSource();
        NormalizedSaveFacts facts = NormalizeMixed(source, CreateMixedSaveStep());
        StoreSnapshot before = CaptureStore(source.Store);
        StayBSaveDecision missingStayUpdate = new(
            [
                new UpdateWriteDecision(AUpdateId, UpdateWriteMode.Delta),
                new UpdateWriteDecision(BBaseUpdateId, UpdateWriteMode.Base),
            ],
            [ANoChangeId]);

        Assert.Throws<ArgumentException>(() =>
            ExplicitCandidatePairEvaluator.Evaluate(
                source.Store,
                facts,
                missingStayUpdate,
                CreateRotateDecision()));

        RotateCSaveDecision missingRotateUpdate = new(
            [new UpdateWriteDecision(BBaseUpdateId, UpdateWriteMode.Base)],
            [BRelocateId]);
        Assert.Throws<ArgumentException>(() =>
            ExplicitCandidatePairEvaluator.Evaluate(
                source.Store,
                facts,
                CreateStayDecision(),
                missingRotateUpdate));
        AssertStoreSnapshot(before, CaptureStore(source.Store));
    }

    [Fact]
    public void Stale_source_error_escapes_instead_of_becoming_an_outcome() {
        PairFixture source = CreateStrongMixedSource();
        NormalizedSaveFacts facts = NormalizeMixed(source, CreateMixedSaveStep());
        _ = source.Store.CreateFile();
        StoreSnapshot before = CaptureStore(source.Store);

        Assert.Throws<InvalidDataException>(() =>
            ExplicitCandidatePairEvaluator.Evaluate(
                source.Store,
                facts,
                CreateStayDecision(),
                CreateRotateDecision()));

        AssertStoreSnapshot(before, CaptureStore(source.Store));
    }

    [Fact]
    public void Equivalent_input_and_decision_orders_produce_equivalent_pairs() {
        PairFixture source = CreateStrongMixedSource();
        NormalizedSaveFacts firstFacts = NormalizeMixed(source, CreateMixedSaveStep());
        NormalizedSaveFacts secondFacts = NormalizeMixed(
            source,
            new SaveStep([
                new UpdateObject(BDeltaUpdateId, 43, 2),
                new CreateObject(InsertId, 7),
                new UpdateObject(AUpdateId, 30, 5),
                new RemoveObject(RemoveId),
                new UpdateObject(BBaseUpdateId, 44, 4),
            ]));
        StayBSaveDecision firstStay = CreateStayDecision();
        StayBSaveDecision secondStay = new(
            [
                new UpdateWriteDecision(BDeltaUpdateId, UpdateWriteMode.Delta),
                new UpdateWriteDecision(BBaseUpdateId, UpdateWriteMode.Base),
                new UpdateWriteDecision(AUpdateId, UpdateWriteMode.Delta),
            ],
            [ANoChangeId]);
        RotateCSaveDecision firstRotate = CreateRotateDecision();
        RotateCSaveDecision secondRotate = new(
            [
                new UpdateWriteDecision(BDeltaUpdateId, UpdateWriteMode.Delta),
                new UpdateWriteDecision(BBaseUpdateId, UpdateWriteMode.Base),
            ],
            [BRelocateId]);
        StoreSnapshot before = CaptureStore(source.Store);

        ExplicitCandidatePairEvaluation first = ExplicitCandidatePairEvaluator.Evaluate(
            source.Store, firstFacts, firstStay, firstRotate);
        ExplicitCandidatePairEvaluation second = ExplicitCandidatePairEvaluator.Evaluate(
            source.Store, secondFacts, secondStay, secondRotate);

        Assert.Same(firstFacts, first.Facts);
        Assert.Same(secondFacts, second.Facts);
        Assert.Equal(DescribePair(first), DescribePair(second));
        AssertStoreSnapshot(before, CaptureStore(source.Store));
    }

    private static PairFixture CreateStrongMixedSource() {
        RbfFileStore store = new();
        RbfFile previous = store.CreateFile();
        ObjectVersionDictionaryBuilder aDictionary = new();
        aDictionary.BindSelf(ANoChangeId);
        aDictionary.BindSelf(AUpdateId);
        aDictionary.BindSelf(RemoveId);
        FrameBuilder aBuilder = new() { ObjectVersionDictionary = aDictionary };
        AddBase(aBuilder, ANoChangeId, 10, 1);
        AddBase(aBuilder, AUpdateId, 20, 1);
        AddBase(aBuilder, RemoveId, 30, 1);
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
        AddDelta(publishedBuilder, ANoChangeId, Previous(a.FrameTicket), 10, 12, 2, 12, 2);
        AddDelta(publishedBuilder, AUpdateId, Previous(a.FrameTicket), 20, 25, 5, 25, 2);
        AbsoluteFrameAddress published = AppendExact(current, publishedBuilder);
        return new PairFixture(store, previous, current, a, bLocal, published);
    }

    private static PairFixture CreateHugeADebtSource() {
        RbfFileStore store = new();
        RbfFile previous = store.CreateFile();
        ObjectVersionDictionaryBuilder aDictionary = new();
        aDictionary.BindSelf(ANoChangeId);
        FrameBuilder aBuilder = new() { ObjectVersionDictionary = aDictionary };
        AddBase(
            aBuilder,
            ANoChangeId,
            HugeADebtPayloadBytes,
            1);
        AbsoluteFrameAddress a = AppendExact(previous, aBuilder);

        RbfFile current = store.CreateFile();
        FrameBuilder publishedBuilder = new() {
            ObjectVersionDictionary = new ObjectVersionDictionaryBuilder {
                Kind = ObjectVersionDictionaryKind.Delta,
                ParentRevisionFrameTicket = Previous(a.FrameTicket),
            },
        };
        AbsoluteFrameAddress published = AppendExact(current, publishedBuilder);
        return new PairFixture(store, previous, current, a, published, published);
    }

    private static PairFixture CreateMalformedSourceAccounting() {
        RbfFileStore store = new();
        RbfFile previous = store.CreateFile();
        ObjectVersionDictionaryBuilder aDictionary = new();
        aDictionary.BindSelf(ANoChangeId);
        FrameBuilder aBuilder = new() { ObjectVersionDictionary = aDictionary };
        AddBase(aBuilder, ANoChangeId, 10, 1);
        AbsoluteFrameAddress a = new(
            previous.FileNumber,
            previous.Append(aBuilder.Build()));

        RbfFile current = store.CreateFile();
        FrameBuilder publishedBuilder = new() {
            ObjectVersionDictionary = new ObjectVersionDictionaryBuilder {
                Kind = ObjectVersionDictionaryKind.Delta,
                ParentRevisionFrameTicket = Previous(a.FrameTicket),
            },
        };
        AbsoluteFrameAddress published = AppendExact(current, publishedBuilder);
        return new PairFixture(store, previous, current, a, published, published);
    }

    private static NormalizedSaveFacts NormalizeMixed(
        PairFixture source,
        SaveStep step) => SaveStepNormalizer.Normalize(
        source.Store,
        source.Current.FileNumber,
        source.PublishedRevisionAddress,
        step);

    private static NormalizedSaveFacts NormalizeHuge(PairFixture source) =>
        NormalizeMixed(source, new SaveStep([new CreateObject(InsertId, 1)]));

    private static SaveStep CreateMixedSaveStep() => new([
        new RemoveObject(RemoveId),
        new UpdateObject(BDeltaUpdateId, 43, 2),
        new CreateObject(InsertId, 7),
        new UpdateObject(AUpdateId, 30, 5),
        new UpdateObject(BBaseUpdateId, 44, 4),
    ]);

    private static StayBSaveDecision CreateStayDecision() => new(
        [
            new UpdateWriteDecision(AUpdateId, UpdateWriteMode.Delta),
            new UpdateWriteDecision(BBaseUpdateId, UpdateWriteMode.Base),
            new UpdateWriteDecision(BDeltaUpdateId, UpdateWriteMode.Delta),
        ],
        [ANoChangeId]);

    private static RotateCSaveDecision CreateRotateDecision() => new(
        [
            new UpdateWriteDecision(BBaseUpdateId, UpdateWriteMode.Base),
            new UpdateWriteDecision(BDeltaUpdateId, UpdateWriteMode.Delta),
        ],
        [BRelocateId]);

    private static void ExhaustCurrentRelativeStart(RbfFile current) {
        while (current.TailOffsetBytes <=
            RbfV040Layout.MaxDurableGraphRelativeFrameStartOffsetBytes) {
            _ = current.Append(
                new FrameBuilder().Build(),
                RbfV040Layout.MaxPayloadAndTailMetaLengthBytes);
        }
    }

    private static void AssertFeasibleIdentity(
        NormalizedSaveFacts facts,
        StayBSaveDecision decision,
        FeasibleCandidate<StayBRevisionPlan> feasible) {
        Assert.Same(facts, feasible.Plan.Facts);
        Assert.Same(decision, feasible.Plan.Decision);
        Assert.Same(feasible.Plan.Revision, feasible.Observation.Candidate);
    }

    private static void AssertFeasibleIdentity(
        NormalizedSaveFacts facts,
        RotateCSaveDecision decision,
        FeasibleCandidate<RotateCRevisionPlan> feasible) {
        Assert.Same(facts, feasible.Plan.Facts);
        Assert.Same(decision, feasible.Plan.Decision);
        Assert.Same(feasible.Plan.Revision, feasible.Observation.Candidate);
    }

    private static int SumRecordBytes(
        PlannedRevisionV0 revision,
        IEnumerable<uint> objectIds) {
        HashSet<uint> selected = objectIds.ToHashSet();
        return revision.Estimate.DomainRecords
            .Where(record => selected.Contains(record.ObjectId))
            .Sum(static record => record.FullRecordBytes);
    }

    private static AbsoluteFrameAddress[] CanonicalAddresses(
        IEnumerable<AbsoluteFrameAddress> addresses) => addresses
        .OrderBy(static address => address.FileNumber)
        .ThenBy(static address => address.FrameTicket.OffsetBytes)
        .ThenBy(static address => address.FrameTicket.LengthBytes)
        .ToArray();

    private static string[] DescribePair(ExplicitCandidatePairEvaluation pair) {
        FeasibleCandidate<StayBRevisionPlan> stay =
            Assert.IsType<FeasibleCandidate<StayBRevisionPlan>>(pair.StayBAttempt);
        FeasibleCandidate<RotateCRevisionPlan> rotate =
            Assert.IsType<FeasibleCandidate<RotateCRevisionPlan>>(pair.RotateCAttempt);
        return [.. DescribeAttempt(stay.Observation), .. DescribeAttempt(rotate.Observation)];
    }

    private static IEnumerable<string> DescribeAttempt(CandidateRawObservation observation) {
        yield return $"{observation.Target}:{observation.Candidate.Address}:" +
            $"{observation.ForegroundDomainRecordBytes}:" +
            $"{observation.MaintenanceDomainRecordBytes}:{observation.Layout}";
        foreach ((uint id, ObjectVersion version) in observation.Candidate.Frame.ObjectVersions) {
            yield return $"V:{id}:{version.Kind}:{version.PayloadBytes}:" +
                $"{version.ResultBasePayloadBytes}:{version.DeltaParentFrameTicket}";
        }
        foreach (AbsoluteFrameAddress address in
            observation.PostLiveReconstruction.UniqueFrameAddresses) {
            yield return $"F:{address}";
        }
        foreach (uint id in observation.PostLiveReconstruction
            .PreviousFileDependentObjectIds) {
            yield return $"D:{id}";
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

    private sealed record PairFixture(
        RbfFileStore Store,
        RbfFile Previous,
        RbfFile Current,
        AbsoluteFrameAddress PreviousRevisionAddress,
        AbsoluteFrameAddress BLocalRevisionAddress,
        AbsoluteFrameAddress PublishedRevisionAddress);

    private sealed record StoreSnapshot(int FileCount, IReadOnlyList<FileSnapshot> Files);

    private sealed record FileSnapshot(uint FileNumber, int FrameCount, long TailOffsetBytes);
}
