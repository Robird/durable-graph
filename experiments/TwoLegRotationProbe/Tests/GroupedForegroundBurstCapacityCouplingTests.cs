using Atelia.TwoLegRotationProbe.Encoding;
using Atelia.TwoLegRotationProbe.Model;
using Atelia.TwoLegRotationProbe.Planning;
using Atelia.TwoLegRotationProbe.Policies;
using Atelia.TwoLegRotationProbe.Workloads;

namespace Atelia.TwoLegRotationProbe.Tests;

public sealed class GroupedForegroundBurstCapacityCouplingTests {
    private const uint DebtObjectId = 10;
    private const uint FirstUpdateObjectId = 20;
    private const uint SecondUpdateObjectId = 30;
    private const uint ThirdUpdateObjectId = 40;
    private const int DebtPayloadBytes = 10;
    private const int UpdateBasePayloadBytes = 89_478_460;
    private const int DeltaPayloadBytes = 1;
    private const int MaximumForegroundSlackBytes = 32;

    [Fact]
    public void Optional_migration_can_push_one_grouped_foreground_Frame_over_its_envelope() {
        BurstFixture source = CreateSource();
        StoreSnapshot beforeEvaluation = CaptureStore(source.Store);

        NormalizedSaveFacts facts = SaveStepNormalizer.Normalize(
            source.Store,
            source.Current.FileNumber,
            source.PublishedRevisionAddress,
            new SaveStep([
                new UpdateObject(
                    FirstUpdateObjectId,
                    UpdateBasePayloadBytes,
                    DeltaPayloadBytes),
                new UpdateObject(
                    SecondUpdateObjectId,
                    UpdateBasePayloadBytes,
                    DeltaPayloadBytes),
                new UpdateObject(
                    ThirdUpdateObjectId,
                    UpdateBasePayloadBytes,
                    DeltaPayloadBytes),
            ]));
        Assert.Equal(
            [FirstUpdateObjectId, SecondUpdateObjectId, ThirdUpdateObjectId],
            facts.Updates.Select(static update => update.ObjectId));
        Assert.All(facts.Updates, update => Assert.Equal(
            source.Current.FileNumber,
            update.Source.BaseAddress.FileNumber));
        NormalizedNoChangeFact debt = Assert.Single(facts.NoChanges);
        Assert.Equal(DebtObjectId, debt.ObjectId);
        Assert.Equal(
            source.Previous.FileNumber,
            debt.Source.BaseAddress.FileNumber);
        UpdateWriteDecision[] stayBaseUpdates = CreateUpdateDecisions(
            UpdateWriteMode.Base);
        UpdateWriteDecision[] rotateDeltaUpdates = CreateUpdateDecisions(
            UpdateWriteMode.Delta);
        RotateCSaveDecision rotateDecision = new(rotateDeltaUpdates, []);

        ExplicitCandidatePairEvaluation foregroundOnly =
            ExplicitCandidatePairEvaluator.Evaluate(
                source.Store,
                facts,
                new StayBSaveDecision(stayBaseUpdates, []),
                rotateDecision);
        ExplicitCandidatePairEvaluation withMigration =
            ExplicitCandidatePairEvaluator.Evaluate(
                source.Store,
                facts,
                new StayBSaveDecision(stayBaseUpdates, [DebtObjectId]),
                rotateDecision);

        Assert.Same(facts, foregroundOnly.Facts);
        Assert.Same(facts, withMigration.Facts);
        Assert.Same(rotateDecision, foregroundOnly.RotateCDecision);
        Assert.Same(rotateDecision, withMigration.RotateCDecision);

        FeasibleCandidate<StayBRevisionPlan> feasibleStay =
            Assert.IsType<FeasibleCandidate<StayBRevisionPlan>>(
                foregroundOnly.StayBAttempt);
        Assert.Equal(0, feasibleStay.Observation.MaintenanceDomainRecordBytes);
        long foregroundPayloadAndTailMetaBytes = checked(
            (long)feasibleStay.Observation.Layout.PayloadLengthBytes +
            feasibleStay.Observation.Layout.TailMetaLengthBytes);
        long foregroundSlackBytes = checked(
            RbfV040Layout.MaxPayloadAndTailMetaLengthBytes -
            foregroundPayloadAndTailMetaBytes);
        Assert.InRange(
            foregroundPayloadAndTailMetaBytes,
            0,
            RbfV040Layout.MaxPayloadAndTailMetaLengthBytes);
        Assert.InRange(foregroundSlackBytes, 0, MaximumForegroundSlackBytes);

        CapacityRejectedCandidate<StayBRevisionPlan> rejectedStay =
            Assert.IsType<CapacityRejectedCandidate<StayBRevisionPlan>>(
                withMigration.StayBAttempt);
        Assert.Equal(
            RevisionCandidateCapacityLimit.PayloadAndTailMetaLength,
            rejectedStay.Rejection.Limit);
        Assert.True(
            rejectedStay.Rejection.AttemptedValue >
            rejectedStay.Rejection.MaximumValue);
        Assert.Equal(
            RbfV040Layout.MaxPayloadAndTailMetaLengthBytes,
            rejectedStay.Rejection.MaximumValue);

        _ = Assert.IsType<FeasibleCandidate<RotateCRevisionPlan>>(
            foregroundOnly.RotateCAttempt);
        _ = Assert.IsType<FeasibleCandidate<RotateCRevisionPlan>>(
            withMigration.RotateCAttempt);
        AssertStoreSnapshot(beforeEvaluation, CaptureStore(source.Store));

        SelectedPolicyCandidateCapacityRejected rejectedOutcome =
            Assert.IsType<SelectedPolicyCandidateCapacityRejected>(
                ExplicitRotationPolicyStepHarness.TryApplySelected(
                    source.Store,
                    source.Cursor,
                    withMigration,
                    CandidateTarget.StayB));
        Assert.Same(withMigration, rejectedOutcome.Evaluation);
        Assert.Equal(CandidateTarget.StayB, rejectedOutcome.SelectedTarget);
        Assert.Equal(rejectedStay.Rejection, rejectedOutcome.Rejection);
        AssertStoreSnapshot(beforeEvaluation, CaptureStore(source.Store));

        int previousFrameCountBeforeApply = source.Previous.FrameCount;
        long previousTailBeforeApply = source.Previous.TailOffsetBytes;
        int currentFrameCountBeforeApply = source.Current.FrameCount;
        AppliedStayBPolicyStep applied = Assert.IsType<AppliedStayBPolicyStep>(
            ExplicitRotationPolicyStepHarness.TryApplySelected(
                source.Store,
                source.Cursor,
                foregroundOnly,
                CandidateTarget.StayB));

        Assert.Same(foregroundOnly, applied.Evaluation);
        Assert.Same(feasibleStay, applied.Selected);
        Assert.Same(feasibleStay, applied.CompletionCertificate.InitialStayB);
        Assert.Empty(applied.CompletionCertificate.MaintenanceStayBSteps);
        Assert.Equal(2, source.Store.FileCount);
        Assert.Equal(previousFrameCountBeforeApply, source.Previous.FrameCount);
        Assert.Equal(previousTailBeforeApply, source.Previous.TailOffsetBytes);
        Assert.Equal(currentFrameCountBeforeApply + 1, source.Current.FrameCount);
        Assert.Equal(
            feasibleStay.Plan.Revision.Address,
            applied.ResultCursor.PublishedRevisionAddress);
        Assert.Equal(
            feasibleStay.Plan.Revision.Estimate.RbfLayout.TailOffsetAfterBytes,
            source.Current.TailOffsetBytes);
        Assert.Equal(
            feasibleStay.Plan.Revision.Estimate.RbfLayout.TailOffsetAfterBytes,
            applied.ResultCursor.CurrentFileTailOffsetBytes);
        Assert.Equal(source.Cursor.FileScope.CurrentFileNumber,
            applied.ResultCursor.FileScope.CurrentFileNumber);
        Assert.Equal(source.Cursor.FileScope.PreviousFileNumber,
            applied.ResultCursor.FileScope.PreviousFileNumber);
    }

    private static BurstFixture CreateSource() {
        RbfFileStore store = new();
        RbfFile previous = store.CreateFile();
        ObjectVersionDictionaryBuilder previousDictionary = new();
        previousDictionary.BindSelf(DebtObjectId);
        FrameBuilder previousBuilder = new() {
            ObjectVersionDictionary = previousDictionary,
        };
        AddBase(previousBuilder, DebtObjectId, DebtPayloadBytes);
        AbsoluteFrameAddress debtAddress = AppendExact(previous, previousBuilder);

        RbfFile current = store.CreateFile();
        FileScope currentScope = new(current.FileNumber);
        ObjectVersionDictionaryBuilder firstDictionary = new();
        firstDictionary.BindExternal(
            DebtObjectId,
            currentScope.Relativize(debtAddress));
        firstDictionary.BindSelf(FirstUpdateObjectId);
        FrameBuilder firstBuilder = new() {
            ObjectVersionDictionary = firstDictionary,
        };
        AddBase(firstBuilder, FirstUpdateObjectId, UpdateBasePayloadBytes);
        AbsoluteFrameAddress firstAddress = AppendExact(current, firstBuilder);

        AbsoluteFrameAddress secondAddress = AppendBLocalBaseRevision(
            current,
            currentScope,
            firstAddress,
            SecondUpdateObjectId);
        AbsoluteFrameAddress thirdAddress = AppendBLocalBaseRevision(
            current,
            currentScope,
            secondAddress,
            ThirdUpdateObjectId);

        FrameBuilder publishedBuilder = new() {
            ObjectVersionDictionary = new ObjectVersionDictionaryBuilder {
                Kind = ObjectVersionDictionaryKind.Delta,
                ParentRevisionFrameTicket = currentScope.Relativize(thirdAddress),
            },
        };
        AbsoluteFrameAddress published = AppendExact(current, publishedBuilder);
        ProbeRevisionCursor cursor = new(
            currentScope,
            published,
            current.TailOffsetBytes);
        return new BurstFixture(
            store,
            previous,
            current,
            published,
            cursor);
    }

    private static AbsoluteFrameAddress AppendBLocalBaseRevision(
        RbfFile current,
        FileScope currentScope,
        AbsoluteFrameAddress parent,
        uint objectId) {
        ObjectVersionDictionaryBuilder dictionary = new() {
            Kind = ObjectVersionDictionaryKind.Delta,
            ParentRevisionFrameTicket = currentScope.Relativize(parent),
        };
        dictionary.BindSelf(objectId);
        FrameBuilder builder = new() {
            ObjectVersionDictionary = dictionary,
        };
        AddBase(builder, objectId, UpdateBasePayloadBytes);
        return AppendExact(current, builder);
    }

    private static UpdateWriteDecision[] CreateUpdateDecisions(
        UpdateWriteMode mode) => [
        new(FirstUpdateObjectId, mode),
        new(SecondUpdateObjectId, mode),
        new(ThirdUpdateObjectId, mode),
    ];

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

    private sealed record BurstFixture(
        RbfFileStore Store,
        RbfFile Previous,
        RbfFile Current,
        AbsoluteFrameAddress PublishedRevisionAddress,
        ProbeRevisionCursor Cursor);

    private sealed record StoreSnapshot(
        int FileCount,
        IReadOnlyList<FileSnapshot> Files);

    private sealed record FileSnapshot(
        uint FileNumber,
        int FrameCount,
        long TailOffsetBytes);
}
