using Atelia.TwoLegRotationProbe.Encoding;
using Atelia.TwoLegRotationProbe.Evaluation;
using Atelia.TwoLegRotationProbe.Model;
using Atelia.TwoLegRotationProbe.Planning;
using Atelia.TwoLegRotationProbe.Policies;
using Atelia.TwoLegRotationProbe.Simulation;
using Atelia.TwoLegRotationProbe.Workloads;

namespace Atelia.TwoLegRotationProbe.Tests;

public sealed class EvaluatorV1SessionTests {
    private const uint FirstObjectId = 10;
    private const uint InsertObjectId = 404;
    private const int LargePayloadBytes = 140_000_000;
    private const int HugePayloadBytes =
        RbfV040Layout.MaxPayloadAndTailMetaLengthBytes - 20;

    [Fact]
    public void Accepted_workload_and_direct_settlement_are_both_realized_and_charged() {
        SourceFixture source = CreateSource((FirstObjectId, 10));
        StoreSnapshot original = CaptureStore(source.Store);
        EvaluatorV1Session session = new(
            source.Store,
            source.Cursor,
            totalWorkloadStepCount: 1);

        ExplicitCandidatePairEvaluation pair = CreateInsertPair(session);
        FeasibleCandidate<StayBRevisionPlan> stay =
            Assert.IsType<FeasibleCandidate<StayBRevisionPlan>>(
                pair.StayBAttempt);
        Assert.IsType<AppliedStayBPolicyStep>(
            session.ApplySelectedWorkloadCommit(pair, CandidateTarget.StayB));
        long workloadColdReadBytes = FinalColdHeadReadMeasurer.Measure(
            session.Store,
            session.Cursor.PublishedRevisionAddress).UniqueFrameBytes;

        AdmittedEvaluatorRun admitted = Assert.IsType<AdmittedEvaluatorRun>(
            session.Complete());

        AssertStoreSnapshot(original, CaptureStore(source.Store));
        Assert.Equal(EvaluatorRunPhase.TerminalSettlement, admitted.Position.Phase);
        Assert.Equal(1, admitted.Position.CompletedWorkloadStepCount);
        Assert.Equal(1, admitted.Position.TotalWorkloadStepCount);
        Assert.Empty(admitted.Settlement.MigratedObjectIds);
        Assert.Equal(2, admitted.Metrics.RealizedCommitCount);
        Assert.Equal(1, admitted.Metrics.WorkloadColdReadSampleCount);
        Assert.Equal(
            workloadColdReadBytes,
            admitted.Metrics.TotalWorkloadColdReadBytes);
        Assert.Equal(11, admitted.Metrics.TotalWorkloadLogicalBasePayloadBytes);
        long workloadWrite = stay.Observation.Layout.AppendLengthBytes;
        long physicalWrite = TotalTailBytes(session.Store) -
            TotalTailBytes(source.Store);
        long settlementWrite = physicalWrite - workloadWrite;
        Assert.True(settlementWrite > workloadWrite);
        Assert.Equal(
            workloadWrite + settlementWrite,
            admitted.Metrics.TotalPhysicalWriteBytes);
        Assert.Equal(workloadWrite, admitted.Metrics.WorkloadPhysicalWriteBytes);
        Assert.Equal(
            settlementWrite,
            admitted.Metrics.TerminalSettlementPhysicalWriteBytes);
        Assert.Equal(1, admitted.Metrics.TotalWorkloadDeltaReferencePayloadBytes);
        Assert.Equal(1, admitted.Metrics.TotalWorkloadBaseReferencePayloadBytes);
        Assert.Equal(
            Math.Max(workloadWrite, settlementWrite),
            admitted.Metrics.PeakCommitWriteBytes);
        Assert.Equal(
            workloadWrite,
            admitted.Metrics.PeakWorkloadCommitWriteBytes);
        Assert.Equal(3, session.Store.FileCount);
        Assert.Equal(2U, admitted.FinalCursor.FileScope.PreviousFileNumber);
        Assert.Equal(3U, admitted.FinalCursor.FileScope.CurrentFileNumber);
        AssertClosedOverFinalScope(admitted, oldPreviousFileNumber: 1);
    }

    [Fact]
    public void Multi_step_settlement_is_one_Commit_and_exposes_the_full_burst() {
        SourceFixture source = CreateLargeSource();
        StoreSnapshot original = CaptureStore(source.Store);
        EvaluatorV1Session session = new(
            source.Store,
            source.Cursor,
            totalWorkloadStepCount: 0);

        AdmittedEvaluatorRun admitted = Assert.IsType<AdmittedEvaluatorRun>(
            session.Complete());

        AssertStoreSnapshot(original, CaptureStore(source.Store));
        Assert.Equal(
            [101U, 202U],
            admitted.Settlement.MigratedObjectIds);
        Assert.Equal(1, admitted.Metrics.RealizedCommitCount);
        Assert.Equal(0, admitted.Metrics.WorkloadColdReadSampleCount);
        Assert.Equal(0, admitted.Metrics.TotalWorkloadColdReadBytes);
        Assert.Equal(0, admitted.Metrics.TotalWorkloadLogicalBasePayloadBytes);
        Assert.True(admitted.Metrics.TerminalColdHeadReadBytes > 0);
        long expectedWrite = TotalTailBytes(session.Store) -
            TotalTailBytes(source.Store);
        Assert.Equal(expectedWrite, admitted.Metrics.TotalPhysicalWriteBytes);
        Assert.Equal(0, admitted.Metrics.WorkloadPhysicalWriteBytes);
        Assert.Equal(
            expectedWrite,
            admitted.Metrics.TerminalSettlementPhysicalWriteBytes);
        Assert.Equal(0, admitted.Metrics.TotalWorkloadDeltaReferencePayloadBytes);
        Assert.Equal(0, admitted.Metrics.TotalWorkloadBaseReferencePayloadBytes);
        Assert.Equal(expectedWrite, admitted.Metrics.PeakCommitWriteBytes);
        Assert.Equal(0, admitted.Metrics.PeakWorkloadCommitWriteBytes);
        long lastPreparationTail = session.Store.GetFile(2).TailOffsetBytes;
        Assert.Equal(
            Math.Max(
                lastPreparationTail,
                admitted.FinalCursor.CurrentFileTailOffsetBytes),
            admitted.Metrics.MaxCurrentFileTailBytes);
        AssertClosedOverFinalScope(admitted, oldPreviousFileNumber: 1);
    }

    [Fact]
    public void Payload_references_are_policy_independent_across_Delta_and_forced_Base() {
        SourceFixture source = CreateSource((FirstObjectId, 100));
        List<AdmittedEvaluatorRun> runs = [];

        foreach (CandidateTarget target in new[] {
            CandidateTarget.StayB,
            CandidateTarget.RotateC,
        }) {
            EvaluatorV1Session session = new(
                source.Store,
                source.Cursor,
                totalWorkloadStepCount: 1);
            NormalizedSaveFacts facts = SaveStepNormalizer.Normalize(
                session.Store,
                session.Cursor.FileScope.CurrentFileNumber,
                session.Cursor.PublishedRevisionAddress,
                new SaveStep([
                    new UpdateObject(
                        FirstObjectId,
                        ResultBasePayloadBytes: 100,
                        DeltaPayloadBytes: 7),
                    new CreateObject(InsertObjectId, BasePayloadBytes: 13),
                ]));
            UpdateWriteDecision delta = new(
                FirstObjectId,
                UpdateWriteMode.Delta);
            ExplicitCandidatePairEvaluation pair =
                ExplicitCandidatePairEvaluator.Evaluate(
                    session.Store,
                    facts,
                    new StayBSaveDecision([delta], []),
                    new RotateCSaveDecision([], []));

            RotationPolicyStepAttempt applied =
                session.ApplySelectedWorkloadCommit(pair, target);
            if (target == CandidateTarget.StayB) {
                AppliedStayBPolicyStep stay =
                    Assert.IsType<AppliedStayBPolicyStep>(applied);
                Assert.Equal(
                    ObjectVersionKind.Delta,
                    stay.Selected.Plan.Revision.Frame.ObjectVersions[
                        FirstObjectId].Kind);
            } else {
                AppliedRotateCPolicyStep rotate =
                    Assert.IsType<AppliedRotateCPolicyStep>(applied);
                Assert.Equal(
                    ObjectVersionKind.Base,
                    rotate.Selected.Plan.Revision.Frame.ObjectVersions[
                        FirstObjectId].Kind);
            }

            runs.Add(Assert.IsType<AdmittedEvaluatorRun>(session.Complete()));
        }

        Assert.All(runs, admitted => {
            Assert.Equal(20,
                admitted.Metrics.TotalWorkloadDeltaReferencePayloadBytes);
            Assert.Equal(113,
                admitted.Metrics.TotalWorkloadBaseReferencePayloadBytes);
            Assert.Equal(113,
                admitted.Metrics.TotalWorkloadLogicalBasePayloadBytes);
        });
    }

    [Fact]
    public void Terminal_settlement_RejectedUnproven_is_deterministic_and_non_mutating() {
        SourceFixture source = CreateLargeSource();
        ExhaustCurrentRelativeStart(source.Current);
        ProbeRevisionCursor cursor = new(
            new FileScope(source.Current.FileNumber),
            source.Cursor.PublishedRevisionAddress,
            source.Current.TailOffsetBytes);

        EvaluatorRunRejectedUnproven first = CompleteRejected(source.Store, cursor);
        EvaluatorRunRejectedUnproven second = CompleteRejected(source.Store, cursor);

        Assert.Equal(first, second);
        Assert.Equal(EvaluatorRunPhase.TerminalSettlement, first.Position.Phase);
        Assert.Equal(
            CanPrepareAndRotateRejectionStage.PreparatoryMigration,
            first.Rejection.Stage);
        Assert.Equal(0, first.Rejection.CompletedMigrationCount);
        Assert.Equal(101U, first.Rejection.BlockingObjectId);
        Assert.Equal(
            RevisionCandidateCapacityLimit.TargetFrameStartRelative,
            first.Rejection.Capacity.Limit);
    }

    [Fact]
    public void Selected_capacity_rejection_is_a_run_outcome_without_fallback_or_metrics() {
        SourceFixture source = CreateSource((FirstObjectId, HugePayloadBytes));
        EvaluatorV1Session session = new(
            source.Store,
            source.Cursor,
            totalWorkloadStepCount: 1);
        ExplicitCandidatePairEvaluation pair = CreateInsertPair(session);
        Assert.IsType<FeasibleCandidate<StayBRevisionPlan>>(pair.StayBAttempt);
        CapacityRejectedCandidate<RotateCRevisionPlan> rotateRejected =
            Assert.IsType<CapacityRejectedCandidate<RotateCRevisionPlan>>(
                pair.RotateCAttempt);
        StoreSnapshot before = CaptureStore(session.Store);

        Assert.IsType<SelectedPolicyCandidateCapacityRejected>(
            session.ApplySelectedWorkloadCommit(pair, CandidateTarget.RotateC));
        EvaluatorRunCapacityRejected outcome =
            Assert.IsType<EvaluatorRunCapacityRejected>(session.Complete());

        AssertStoreSnapshot(before, CaptureStore(session.Store));
        Assert.Equal(EvaluatorRunPhase.Workload, outcome.Position.Phase);
        Assert.Equal(0, outcome.Position.CompletedWorkloadStepCount);
        Assert.Equal(1, outcome.Position.TotalWorkloadStepCount);
        Assert.Equal(CandidateTarget.RotateC, outcome.SelectedTarget);
        Assert.Equal(rotateRejected.Rejection, outcome.Rejection);
        Assert.Equal(2, session.Store.FileCount);
    }

    [Fact]
    public void Capacity_rejection_after_an_accepted_prefix_reports_the_next_workload_position() {
        SourceFixture source = CreateLargeSource();
        EvaluatorV1Session session = new(
            source.Store,
            source.Cursor,
            totalWorkloadStepCount: 2);
        ExplicitCandidatePairEvaluation firstPair = CreateInsertPair(
            session,
            InsertObjectId);
        Assert.IsType<AppliedStayBPolicyStep>(
            session.ApplySelectedWorkloadCommit(
                firstPair,
                CandidateTarget.StayB));
        ExplicitCandidatePairEvaluation secondPair = CreateInsertPair(
            session,
            InsertObjectId + 1);
        CapacityRejectedCandidate<RotateCRevisionPlan> rejectedCandidate =
            Assert.IsType<CapacityRejectedCandidate<RotateCRevisionPlan>>(
                secondPair.RotateCAttempt);
        StoreSnapshot beforeRejectedCommit = CaptureStore(session.Store);

        Assert.IsType<SelectedPolicyCandidateCapacityRejected>(
            session.ApplySelectedWorkloadCommit(
                secondPair,
                CandidateTarget.RotateC));
        EvaluatorRunCapacityRejected outcome =
            Assert.IsType<EvaluatorRunCapacityRejected>(session.Complete());

        AssertStoreSnapshot(beforeRejectedCommit, CaptureStore(session.Store));
        Assert.Equal(1, outcome.Position.CompletedWorkloadStepCount);
        Assert.Equal(2, outcome.Position.TotalWorkloadStepCount);
        Assert.Equal(CandidateTarget.RotateC, outcome.SelectedTarget);
        Assert.Equal(rejectedCandidate.Rejection, outcome.Rejection);
    }

    [Fact]
    public void Selected_Stay_completion_rejection_is_not_applied_or_retyped_as_capacity() {
        SourceFixture source = CreateLargeSource();
        AdvanceTailTo(
            source.Current,
            RbfV040Layout.MaxDurableGraphRelativeFrameStartOffsetBytes);
        ProbeRevisionCursor cursor = new(
            source.Cursor.FileScope,
            source.Cursor.PublishedRevisionAddress,
            source.Current.TailOffsetBytes);
        EvaluatorV1Session session = new(
            source.Store,
            cursor,
            totalWorkloadStepCount: 1);
        ExplicitCandidatePairEvaluation pair = CreateInsertPair(session);
        Assert.IsType<FeasibleCandidate<StayBRevisionPlan>>(pair.StayBAttempt);
        StoreSnapshot before = CaptureStore(session.Store);

        StayBPolicyCompletionRejectedUnproven rejected =
            Assert.IsType<StayBPolicyCompletionRejectedUnproven>(
                session.ApplySelectedWorkloadCommit(
                    pair,
                    CandidateTarget.StayB));
        EvaluatorRunRejectedUnproven outcome =
            Assert.IsType<EvaluatorRunRejectedUnproven>(session.Complete());

        AssertStoreSnapshot(before, CaptureStore(session.Store));
        Assert.Equal(EvaluatorRunPhase.Workload, outcome.Position.Phase);
        Assert.Equal(rejected.Rejection, outcome.Rejection);
        Assert.Equal(
            CanPrepareAndRotateRejectionStage.PreparatoryMigration,
            outcome.Rejection.Stage);
        Assert.Equal(
            RevisionCandidateCapacityLimit.TargetFrameStartRelative,
            outcome.Rejection.Capacity.Limit);
    }

    [Fact]
    public void Completing_before_all_workload_steps_returns_Incomplete_without_settlement() {
        SourceFixture source = CreateSource((FirstObjectId, 10));
        EvaluatorV1Session session = new(
            source.Store,
            source.Cursor,
            totalWorkloadStepCount: 1);
        StoreSnapshot before = CaptureStore(session.Store);

        EvaluatorRunIncomplete incomplete =
            Assert.IsType<EvaluatorRunIncomplete>(session.Complete());

        AssertStoreSnapshot(before, CaptureStore(session.Store));
        Assert.Equal(EvaluatorRunPhase.Workload, incomplete.Position.Phase);
        Assert.Equal(0, incomplete.Position.CompletedWorkloadStepCount);
        Assert.Equal(1, incomplete.Position.TotalWorkloadStepCount);
    }

    [Fact]
    public void Explicit_stop_after_workload_reports_unsettled_terminal_phase() {
        SourceFixture source = CreateSource((FirstObjectId, 10));
        EvaluatorV1Session session = new(
            source.Store,
            source.Cursor,
            totalWorkloadStepCount: 1);
        ExplicitCandidatePairEvaluation pair = CreateInsertPair(session);
        Assert.IsType<AppliedStayBPolicyStep>(
            session.ApplySelectedWorkloadCommit(pair, CandidateTarget.StayB));
        int fileCountBeforeStop = session.Store.FileCount;
        long tailBeforeStop = session.Cursor.CurrentFileTailOffsetBytes;

        EvaluatorRunIncomplete incomplete = session.StopIncomplete();

        Assert.Equal(EvaluatorRunPhase.TerminalSettlement, incomplete.Position.Phase);
        Assert.Equal(1, incomplete.Position.CompletedWorkloadStepCount);
        Assert.Equal(1, incomplete.Position.TotalWorkloadStepCount);
        Assert.Equal(fileCountBeforeStop, session.Store.FileCount);
        Assert.Equal(tailBeforeStop, session.Cursor.CurrentFileTailOffsetBytes);
        Assert.Equal(2, session.Store.FileCount);
    }

    [Fact]
    public void Empty_live_graph_still_settles_and_reads_its_final_OVD_Frame() {
        SourceFixture source = CreateSource();
        EvaluatorV1Session session = new(
            source.Store,
            source.Cursor,
            totalWorkloadStepCount: 0);

        AdmittedEvaluatorRun admitted = Assert.IsType<AdmittedEvaluatorRun>(
            session.Complete());

        Assert.Empty(admitted.Settlement.MigratedObjectIds);
        Assert.Empty(
            admitted.Metrics.TerminalColdHeadRead
                .ObjectReconstructionFrameAddresses);
        AbsoluteFrameAddress finalAddress =
            admitted.FinalCursor.PublishedRevisionAddress;
        Assert.Equal(
            [finalAddress],
            admitted.Metrics.TerminalColdHeadRead.DictionaryFrameAddresses);
        Assert.Equal(
            [finalAddress],
            admitted.Metrics.TerminalColdHeadRead.UniqueFrameAddresses);
        Assert.True(admitted.Metrics.TerminalColdHeadReadBytes > 0);
    }

    [Fact]
    public void Workload_Rotate_is_followed_by_an_unconditional_terminal_Rotate() {
        SourceFixture source = CreateSource((FirstObjectId, 10));
        EvaluatorV1Session session = new(
            source.Store,
            source.Cursor,
            totalWorkloadStepCount: 1);
        ExplicitCandidatePairEvaluation pair = CreateInsertPair(session);
        FeasibleCandidate<RotateCRevisionPlan> workloadRotate =
            Assert.IsType<FeasibleCandidate<RotateCRevisionPlan>>(
                pair.RotateCAttempt);

        AppliedRotateCPolicyStep applied = Assert.IsType<AppliedRotateCPolicyStep>(
            session.ApplySelectedWorkloadCommit(
                pair,
                CandidateTarget.RotateC));
        Assert.Equal(3U, applied.ResultCursor.FileScope.CurrentFileNumber);
        AdmittedEvaluatorRun admitted = Assert.IsType<AdmittedEvaluatorRun>(
            session.Complete());

        Assert.Empty(admitted.Settlement.MigratedObjectIds);
        Assert.Equal(3U, admitted.FinalCursor.FileScope.PreviousFileNumber);
        Assert.Equal(4U, admitted.FinalCursor.FileScope.CurrentFileNumber);
        Assert.Equal(2, admitted.Metrics.RealizedCommitCount);
        long firstRotateWrite = checked(
            RbfV040Layout.InitialTailOffsetBytes +
            workloadRotate.Observation.Layout.AppendLengthBytes);
        long totalWrite = TotalTailBytes(session.Store) -
            TotalTailBytes(source.Store);
        long terminalRotateWrite = totalWrite - firstRotateWrite;
        Assert.Equal(totalWrite, admitted.Metrics.TotalPhysicalWriteBytes);
        Assert.Equal(
            Math.Max(firstRotateWrite, terminalRotateWrite),
            admitted.Metrics.PeakCommitWriteBytes);
        Assert.Equal(
            firstRotateWrite,
            admitted.Metrics.PeakWorkloadCommitWriteBytes);
        AssertClosedOverFinalScope(admitted, oldPreviousFileNumber: 2);
    }

    [Fact]
    public void Stale_capacity_rejection_after_a_prefix_is_not_promoted_to_an_outcome() {
        SourceFixture source = CreateLargeSource();
        EvaluatorV1Session session = new(
            source.Store,
            source.Cursor,
            totalWorkloadStepCount: 2);
        ExplicitCandidatePairEvaluation stalePair = CreateInsertPair(session);
        Assert.IsType<CapacityRejectedCandidate<RotateCRevisionPlan>>(
            stalePair.RotateCAttempt);
        Assert.IsType<AppliedStayBPolicyStep>(
            session.ApplySelectedWorkloadCommit(
                stalePair,
                CandidateTarget.StayB));
        StoreSnapshot afterPrefix = CaptureStore(session.Store);

        Assert.Throws<InvalidDataException>(() =>
            session.ApplySelectedWorkloadCommit(
                stalePair,
                CandidateTarget.RotateC));

        AssertStoreSnapshot(afterPrefix, CaptureStore(session.Store));
        Assert.Equal(1, session.CompletedWorkloadStepCount);
    }

    private static EvaluatorRunRejectedUnproven CompleteRejected(
        RbfFileStore sourceStore,
        ProbeRevisionCursor cursor) {
        StoreSnapshot before = CaptureStore(sourceStore);
        EvaluatorV1Session session = new(
            sourceStore,
            cursor,
            totalWorkloadStepCount: 0);
        StoreSnapshot sessionBefore = CaptureStore(session.Store);

        EvaluatorRunRejectedUnproven outcome =
            Assert.IsType<EvaluatorRunRejectedUnproven>(session.Complete());

        AssertStoreSnapshot(before, CaptureStore(sourceStore));
        AssertStoreSnapshot(sessionBefore, CaptureStore(session.Store));
        return outcome;
    }

    private static ExplicitCandidatePairEvaluation CreateInsertPair(
        EvaluatorV1Session session,
        uint objectId = InsertObjectId) {
        NormalizedSaveFacts facts = SaveStepNormalizer.Normalize(
            session.Store,
            session.Cursor.FileScope.CurrentFileNumber,
            session.Cursor.PublishedRevisionAddress,
            new SaveStep([new CreateObject(objectId, 1)]));
        return ExplicitCandidatePairEvaluator.Evaluate(
            session.Store,
            facts,
            new StayBSaveDecision([], []),
            new RotateCSaveDecision([], []));
    }

    private static void AssertClosedOverFinalScope(
        AdmittedEvaluatorRun admitted,
        uint oldPreviousFileNumber) {
        Assert.DoesNotContain(
            admitted.Metrics.TerminalColdHeadRead.UniqueFrameAddresses,
            address => address.FileNumber == oldPreviousFileNumber);
        Assert.All(
            admitted.Metrics.TerminalColdHeadRead.UniqueFrameAddresses,
            address => Assert.Contains(
                address.FileNumber,
                new[] {
                    admitted.FinalCursor.FileScope.PreviousFileNumber!.Value,
                    admitted.FinalCursor.FileScope.CurrentFileNumber,
                }));
    }

    private static SourceFixture CreateSource(
        params (uint ObjectId, int PayloadBytes)[] objects) {
        RbfFileStore store = new();
        RbfFile previous = store.CreateFile();
        ObjectVersionDictionaryBuilder dictionary = new();
        FrameBuilder builder = new() { ObjectVersionDictionary = dictionary };
        foreach ((uint objectId, int payloadBytes) in objects) {
            dictionary.BindSelf(objectId);
            AddBase(builder, objectId, payloadBytes);
        }

        AbsoluteFrameAddress previousRevision = AppendExact(previous, builder);
        return CreatePublishedSource(store, previousRevision);
    }

    private static SourceFixture CreateLargeSource() {
        RbfFileStore store = new();
        RbfFile previous = store.CreateFile();
        AbsoluteFrameAddress? previousRevision = null;
        foreach (uint objectId in new[] { 101U, 202U, 303U }) {
            ObjectVersionDictionaryBuilder dictionary = new();
            if (previousRevision is AbsoluteFrameAddress parent) {
                dictionary.Kind = ObjectVersionDictionaryKind.Delta;
                dictionary.ParentRevisionFrameTicket = new RelativeFrameTicket(
                    IsPreviousFile: false,
                    parent.FrameTicket);
            }

            dictionary.BindSelf(objectId);
            FrameBuilder builder = new() { ObjectVersionDictionary = dictionary };
            AddBase(builder, objectId, LargePayloadBytes);
            previousRevision = AppendExact(previous, builder);
        }

        return CreatePublishedSource(store, previousRevision!.Value);
    }

    private static SourceFixture CreatePublishedSource(
        RbfFileStore store,
        AbsoluteFrameAddress previousRevision) {
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
        ProbeRevisionCursor cursor = new(
            new FileScope(current.FileNumber),
            published,
            current.TailOffsetBytes);
        return new SourceFixture(store, current, cursor);
    }

    private static void ExhaustCurrentRelativeStart(RbfFile file) {
        while (file.TailOffsetBytes <=
            RbfV040Layout.MaxDurableGraphRelativeFrameStartOffsetBytes) {
            _ = file.Append(
                new FrameBuilder().Build(),
                RbfV040Layout.MaxPayloadAndTailMetaLengthBytes);
        }
    }

    private static void AdvanceTailTo(RbfFile file, long targetTailOffsetBytes) {
        while (file.TailOffsetBytes < targetTailOffsetBytes) {
            long remaining = targetTailOffsetBytes - file.TailOffsetBytes;
            int maximumAppendBytes = checked(
                RbfV040Layout.MaxFrameLengthBytes +
                RbfV040Layout.TrailingFenceBytes);
            int appendBytes = remaining > maximumAppendBytes
                ? maximumAppendBytes
                : checked((int)remaining);
            long after = remaining - appendBytes;
            int minimumAppendBytes = checked(
                RbfV040Layout.MinFrameLengthBytes +
                RbfV040Layout.TrailingFenceBytes);
            if (after > 0 && after < minimumAppendBytes) {
                appendBytes = checked(
                    appendBytes - (minimumAppendBytes - (int)after));
            }

            if (appendBytes < minimumAppendBytes ||
                (appendBytes & RbfV040Layout.AlignmentMask) != 0) {
                throw new InvalidOperationException(
                    $"Cannot advance file {file.FileNumber} exactly to " +
                    $"{targetTailOffsetBytes}.");
            }

            int payloadBytes = appendBytes - minimumAppendBytes;
            _ = file.Append(new FrameBuilder().Build(), payloadBytes);
        }

        Assert.Equal(targetTailOffsetBytes, file.TailOffsetBytes);
    }

    private static void AddBase(
        FrameBuilder frame,
        uint objectId,
        int payloadBytes) {
        ObjectVersionBuilder version = frame.Add(objectId);
        version.Kind = ObjectVersionKind.Base;
        version.PayloadBytes = payloadBytes;
        version.ReconstructionObjectPayloadBytes = payloadBytes;
        version.ResultBasePayloadBytes = payloadBytes;
        version.LogicalVersionOrdinal = 1;
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

    private static long TotalTailBytes(RbfFileStore store) =>
        Enumerable.Range(1, store.FileCount)
            .Sum(index => store.GetFile((uint)index).TailOffsetBytes);

    private static void AssertStoreSnapshot(
        StoreSnapshot expected,
        StoreSnapshot actual) {
        Assert.Equal(expected.FileCount, actual.FileCount);
        Assert.Equal(expected.Files, actual.Files);
    }

    private sealed record SourceFixture(
        RbfFileStore Store,
        RbfFile Current,
        ProbeRevisionCursor Cursor);

    private sealed record StoreSnapshot(
        int FileCount,
        IReadOnlyList<FileSnapshot> Files);

    private sealed record FileSnapshot(
        uint FileNumber,
        int FrameCount,
        long TailOffsetBytes);
}
