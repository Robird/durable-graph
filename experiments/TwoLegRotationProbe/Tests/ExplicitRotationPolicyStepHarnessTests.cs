using Atelia.TwoLegRotationProbe.Encoding;
using Atelia.TwoLegRotationProbe.Model;
using Atelia.TwoLegRotationProbe.Planning;
using Atelia.TwoLegRotationProbe.Policies;
using Atelia.TwoLegRotationProbe.Workloads;

namespace Atelia.TwoLegRotationProbe.Tests;

public sealed class ExplicitRotationPolicyStepHarnessTests {
    private const uint DebtObjectId = 10;
    private const uint InsertObjectId = 20;
    private const int LargeDebtPayloadBytes = 140_000_000;
    private const int HugeDebtPayloadBytes =
        RbfV040Layout.MaxPayloadAndTailMetaLengthBytes - 20;

    [Fact]
    public void Selected_Stay_applies_only_the_exact_initial_candidate() {
        StepFixture source = CreateFixture(debtPayloadBytes: 10);
        FeasibleCandidate<StayBRevisionPlan> selected =
            Assert.IsType<FeasibleCandidate<StayBRevisionPlan>>(
                source.Evaluation.StayBAttempt);
        int currentFrameCountBefore = source.Current.FrameCount;

        AppliedStayBPolicyStep applied = Assert.IsType<AppliedStayBPolicyStep>(
            ExplicitRotationPolicyStepHarness.TryApplySelected(
                source.Store,
                source.Cursor,
                source.Evaluation,
                CandidateTarget.StayB));

        Assert.Same(source.Evaluation, applied.Evaluation);
        Assert.Same(selected, applied.Selected);
        Assert.Same(selected, applied.CompletionCertificate.InitialStayB);
        Assert.Equal(
            selected.Plan.Revision.Address,
            applied.ResultCursor.PublishedRevisionAddress);
        Assert.Equal(currentFrameCountBefore + 1, source.Current.FrameCount);
        Assert.Equal(2, source.Store.FileCount);
    }

    [Fact]
    public void Selected_Rotate_applies_the_exact_pair_candidate_to_fresh_C() {
        StepFixture source = CreateFixture(debtPayloadBytes: 10);
        FeasibleCandidate<RotateCRevisionPlan> selected =
            Assert.IsType<FeasibleCandidate<RotateCRevisionPlan>>(
                source.Evaluation.RotateCAttempt);
        int currentFrameCountBefore = source.Current.FrameCount;

        AppliedRotateCPolicyStep applied = Assert.IsType<AppliedRotateCPolicyStep>(
            ExplicitRotationPolicyStepHarness.TryApplySelected(
                source.Store,
                source.Cursor,
                source.Evaluation,
                CandidateTarget.RotateC));

        Assert.Same(source.Evaluation, applied.Evaluation);
        Assert.Same(selected, applied.Selected);
        Assert.Equal(
            selected.Plan.Revision.Address,
            applied.ResultCursor.PublishedRevisionAddress);
        Assert.Equal(currentFrameCountBefore, source.Current.FrameCount);
        Assert.Equal(3, source.Store.FileCount);
        Assert.Equal(3U, applied.ResultCursor.FileScope.CurrentFileNumber);
    }

    [Fact]
    public void Selected_capacity_rejection_does_not_fallback_or_mutate() {
        StepFixture source = CreateFixture(HugeDebtPayloadBytes);
        Assert.IsType<FeasibleCandidate<StayBRevisionPlan>>(
            source.Evaluation.StayBAttempt);
        CapacityRejectedCandidate<RotateCRevisionPlan> selected =
            Assert.IsType<CapacityRejectedCandidate<RotateCRevisionPlan>>(
                source.Evaluation.RotateCAttempt);
        StoreSnapshot before = CaptureStore(source.Store);

        SelectedPolicyCandidateCapacityRejected rejected =
            Assert.IsType<SelectedPolicyCandidateCapacityRejected>(
                ExplicitRotationPolicyStepHarness.TryApplySelected(
                    source.Store,
                    source.Cursor,
                    source.Evaluation,
                    CandidateTarget.RotateC));

        Assert.Same(source.Evaluation, rejected.Evaluation);
        Assert.Equal(CandidateTarget.RotateC, rejected.SelectedTarget);
        Assert.Equal(selected.Rejection, rejected.Rejection);
        AssertStoreSnapshot(before, CaptureStore(source.Store));
    }

    [Fact]
    public void Selected_Stay_with_nonempty_completion_applies_no_maintenance_or_final_rotate() {
        StepFixture source = CreateLargeFixture(exhaustCurrentTail: false);
        FeasibleCandidate<StayBRevisionPlan> selected =
            Assert.IsType<FeasibleCandidate<StayBRevisionPlan>>(
                source.Evaluation.StayBAttempt);
        int currentFrameCountBefore = source.Current.FrameCount;

        AppliedStayBPolicyStep applied = Assert.IsType<AppliedStayBPolicyStep>(
            ExplicitRotationPolicyStepHarness.TryApplySelected(
                source.Store,
                source.Cursor,
                source.Evaluation,
                CandidateTarget.StayB));

        Assert.Same(source.Evaluation, applied.Evaluation);
        Assert.Same(selected, applied.Selected);
        Assert.Same(selected, applied.CompletionCertificate.InitialStayB);
        Assert.Equal(2, applied.CompletionCertificate.MaintenanceStayBSteps.Count);
        Assert.Equal(2, source.Store.FileCount);
        Assert.Equal(currentFrameCountBefore + 1, source.Current.FrameCount);
        Assert.Equal(selected.Plan.Revision.Address,
            applied.ResultCursor.PublishedRevisionAddress);
        Assert.Equal(
            selected.Plan.Revision.Estimate.RbfLayout.TailOffsetAfterBytes,
            applied.ResultCursor.CurrentFileTailOffsetBytes);
        Assert.Equal(source.Cursor.FileScope.CurrentFileNumber,
            applied.ResultCursor.FileScope.CurrentFileNumber);
        Assert.Equal(source.Cursor.FileScope.PreviousFileNumber,
            applied.ResultCursor.FileScope.PreviousFileNumber);
    }

    [Fact]
    public void Stay_completion_RejectedUnproven_retains_selection_and_does_not_mutate() {
        StepFixture source = CreateLargeFixture(exhaustCurrentTail: true);
        FeasibleCandidate<StayBRevisionPlan> selected =
            Assert.IsType<FeasibleCandidate<StayBRevisionPlan>>(
                source.Evaluation.StayBAttempt);
        CanPrepareAndRotateRejectedUnproven expected =
            Assert.IsType<CanPrepareAndRotateRejectedUnproven>(
                CanPrepareAndRotateCertificatePlanner.TryCreate(
                    source.Store,
                    source.Cursor,
                    selected));
        StoreSnapshot before = CaptureStore(source.Store);

        StayBPolicyCompletionRejectedUnproven rejected =
            Assert.IsType<StayBPolicyCompletionRejectedUnproven>(
                ExplicitRotationPolicyStepHarness.TryApplySelected(
                    source.Store,
                    source.Cursor,
                    source.Evaluation,
                    CandidateTarget.StayB));

        Assert.Same(source.Evaluation, rejected.Evaluation);
        Assert.Same(selected, rejected.Selected);
        Assert.Equal(expected.Rejection, rejected.Rejection);
        Assert.Equal(
            CanPrepareAndRotateRejectionStage.PreparatoryMigration,
            rejected.Rejection.Stage);
        Assert.Equal(
            RevisionCandidateCapacityLimit.TargetFrameStartRelative,
            rejected.Rejection.Capacity.Limit);
        AssertStoreSnapshot(before, CaptureStore(source.Store));
    }

    [Fact]
    public void Selected_Stay_capacity_rejection_does_not_fallback_to_feasible_Rotate() {
        StepFixture source = CreateFixture(
            debtPayloadBytes: 10,
            exhaustCurrentRelativeStart: true);
        CapacityRejectedCandidate<StayBRevisionPlan> selected =
            Assert.IsType<CapacityRejectedCandidate<StayBRevisionPlan>>(
                source.Evaluation.StayBAttempt);
        Assert.IsType<FeasibleCandidate<RotateCRevisionPlan>>(
            source.Evaluation.RotateCAttempt);
        StoreSnapshot before = CaptureStore(source.Store);

        SelectedPolicyCandidateCapacityRejected rejected =
            Assert.IsType<SelectedPolicyCandidateCapacityRejected>(
                ExplicitRotationPolicyStepHarness.TryApplySelected(
                    source.Store,
                    source.Cursor,
                    source.Evaluation,
                    CandidateTarget.StayB));

        Assert.Same(source.Evaluation, rejected.Evaluation);
        Assert.Equal(CandidateTarget.StayB, rejected.SelectedTarget);
        Assert.Equal(selected.Rejection, rejected.Rejection);
        Assert.Equal(
            RevisionCandidateCapacityLimit.TargetFrameStartRelative,
            rejected.Rejection.Limit);
        AssertStoreSnapshot(before, CaptureStore(source.Store));
    }

    private static StepFixture CreateFixture(
        int debtPayloadBytes,
        bool exhaustCurrentRelativeStart = false) {
        RbfFileStore store = new();
        RbfFile previous = store.CreateFile();
        ObjectVersionDictionaryBuilder previousDictionary = new();
        previousDictionary.BindSelf(DebtObjectId);
        FrameBuilder previousBuilder = new() {
            ObjectVersionDictionary = previousDictionary,
        };
        AddBase(previousBuilder, DebtObjectId, debtPayloadBytes);
        AbsoluteFrameAddress previousRevision = AppendExact(
            previous,
            previousBuilder);

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
        if (exhaustCurrentRelativeStart) {
            ExhaustCurrentRelativeStart(current);
        }

        return CreateStepFixture(store, current, published);
    }

    private static StepFixture CreateLargeFixture(bool exhaustCurrentTail) {
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
            FrameBuilder builder = new() {
                ObjectVersionDictionary = dictionary,
            };
            AddBase(builder, objectId, LargeDebtPayloadBytes);
            previousRevision = AppendExact(previous, builder);
        }

        RbfFile current = store.CreateFile();
        FrameBuilder publishedBuilder = new() {
            ObjectVersionDictionary = new ObjectVersionDictionaryBuilder {
                Kind = ObjectVersionDictionaryKind.Delta,
                ParentRevisionFrameTicket = new RelativeFrameTicket(
                    IsPreviousFile: true,
                    previousRevision!.Value.FrameTicket),
            },
        };
        AbsoluteFrameAddress published = AppendExact(current, publishedBuilder);
        if (exhaustCurrentTail) {
            AdvanceTailTo(
                current,
                RbfV040Layout.MaxDurableGraphRelativeFrameStartOffsetBytes);
        }

        return CreateStepFixture(store, current, published);
    }

    private static StepFixture CreateStepFixture(
        RbfFileStore store,
        RbfFile current,
        AbsoluteFrameAddress published) {
        ProbeRevisionCursor cursor = new(
            new FileScope(current.FileNumber),
            published,
            current.TailOffsetBytes);
        NormalizedSaveFacts facts = SaveStepNormalizer.Normalize(
            store,
            current.FileNumber,
            published,
            new SaveStep([new CreateObject(InsertObjectId, 1)]));
        ExplicitCandidatePairEvaluation evaluation =
            ExplicitCandidatePairEvaluator.Evaluate(
                store,
                facts,
                new StayBSaveDecision([], []),
                new RotateCSaveDecision([], []));
        return new StepFixture(store, current, cursor, evaluation);
    }

    private static void AdvanceTailTo(
        RbfFile file,
        long targetTailOffsetBytes) {
        while (file.TailOffsetBytes < targetTailOffsetBytes) {
            long remaining = targetTailOffsetBytes - file.TailOffsetBytes;
            int maximumAppendBytes = checked(
                RbfV040Layout.MaxFrameLengthBytes +
                RbfV040Layout.TrailingFenceBytes);
            int appendBytes = remaining > maximumAppendBytes
                ? maximumAppendBytes
                : checked((int)remaining);
            long remainder = remaining - appendBytes;
            int minimumAppendBytes = checked(
                RbfV040Layout.MinFrameLengthBytes +
                RbfV040Layout.TrailingFenceBytes);
            if (remainder > 0 && remainder < minimumAppendBytes) {
                appendBytes = checked(
                    appendBytes - (minimumAppendBytes - (int)remainder));
            }

            int payloadBytes = appendBytes - minimumAppendBytes;
            _ = file.Append(new FrameBuilder().Build(), payloadBytes);
        }

        Assert.Equal(targetTailOffsetBytes, file.TailOffsetBytes);
    }

    private static void ExhaustCurrentRelativeStart(RbfFile current) {
        while (current.TailOffsetBytes <=
            RbfV040Layout.MaxDurableGraphRelativeFrameStartOffsetBytes) {
            _ = current.Append(
                new FrameBuilder().Build(),
                RbfV040Layout.MaxPayloadAndTailMetaLengthBytes);
        }
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

    private static void AssertStoreSnapshot(
        StoreSnapshot expected,
        StoreSnapshot actual) {
        Assert.Equal(expected.FileCount, actual.FileCount);
        Assert.Equal(expected.Files, actual.Files);
    }

    private sealed record StepFixture(
        RbfFileStore Store,
        RbfFile Current,
        ProbeRevisionCursor Cursor,
        ExplicitCandidatePairEvaluation Evaluation);

    private sealed record StoreSnapshot(
        int FileCount,
        IReadOnlyList<FileSnapshot> Files);

    private sealed record FileSnapshot(
        uint FileNumber,
        int FrameCount,
        long TailOffsetBytes);
}
