using Atelia.TwoLegRotationProbe.Encoding;
using Atelia.TwoLegRotationProbe.Evaluation;
using Atelia.TwoLegRotationProbe.Model;
using Atelia.TwoLegRotationProbe.Planning;
using Atelia.TwoLegRotationProbe.Policies;
using Atelia.TwoLegRotationProbe.Workloads;

namespace Atelia.TwoLegRotationProbe.Tests;

public sealed class EvaluatorRawMetricTests {
    private const uint FirstObjectId = 10;
    private const uint SecondObjectId = 20;

    [Fact]
    public void Stay_then_Rotate_measures_physical_writes_Current_tail_and_cold_head_union() {
        SourceFixture source = CreateSource((FirstObjectId, 100));
        EvaluatorRawMetricAccumulator metrics = new(source.Store, source.Cursor);

        NormalizedSaveFacts stayFacts = SaveStepNormalizer.Normalize(
            source.Store,
            source.Current.FileNumber,
            source.Cursor.PublishedRevisionAddress,
            new SaveStep([new CreateObject(SecondObjectId, 40)]));
        ExplicitCandidatePairEvaluation stayPair =
            ExplicitCandidatePairEvaluator.Evaluate(
                source.Store,
                stayFacts,
                new StayBSaveDecision([], []),
                new RotateCSaveDecision([], []));
        FeasibleCandidate<StayBRevisionPlan> stayCandidate =
            Assert.IsType<FeasibleCandidate<StayBRevisionPlan>>(
                stayPair.StayBAttempt);

        metrics.BeginCommit(source.Store, source.Cursor);
        AppliedStayBPolicyStep appliedStay = Assert.IsType<AppliedStayBPolicyStep>(
            ExplicitRotationPolicyStepHarness.TryApplySelected(
                source.Store,
                source.Cursor,
                stayPair,
                CandidateTarget.StayB));
        metrics.ObserveAcceptedRevision(source.Store, appliedStay.ResultCursor);
        metrics.EndWorkloadCommit(
            source.Store,
            appliedStay.ResultCursor,
            stayFacts);
        long expectedStayColdReadBytes = FinalColdHeadReadMeasurer.Measure(
            source.Store,
            appliedStay.ResultCursor.PublishedRevisionAddress).UniqueFrameBytes;

        NormalizedSaveFacts rotateFacts = SaveStepNormalizer.Normalize(
            source.Store,
            appliedStay.ResultCursor.FileScope.CurrentFileNumber,
            appliedStay.ResultCursor.PublishedRevisionAddress,
            new SaveStep([
                new UpdateObject(SecondObjectId, 40, DeltaPayloadBytes: 1),
            ]));
        UpdateWriteDecision updateDelta = new(
            SecondObjectId,
            UpdateWriteMode.Delta);
        ExplicitCandidatePairEvaluation rotatePair =
            ExplicitCandidatePairEvaluator.Evaluate(
                source.Store,
                rotateFacts,
                new StayBSaveDecision([updateDelta], []),
                new RotateCSaveDecision([updateDelta], []));
        FeasibleCandidate<RotateCRevisionPlan> rotateCandidate =
            Assert.IsType<FeasibleCandidate<RotateCRevisionPlan>>(
                rotatePair.RotateCAttempt);

        metrics.BeginCommit(source.Store, appliedStay.ResultCursor);
        AppliedRotateCPolicyStep appliedRotate = Assert.IsType<AppliedRotateCPolicyStep>(
            ExplicitRotationPolicyStepHarness.TryApplySelected(
                source.Store,
                appliedStay.ResultCursor,
                rotatePair,
                CandidateTarget.RotateC));
        metrics.ObserveAcceptedRevision(source.Store, appliedRotate.ResultCursor);
        metrics.EndWorkloadCommit(
            source.Store,
            appliedRotate.ResultCursor,
            rotateFacts);
        long expectedRotateColdReadBytes = FinalColdHeadReadMeasurer.Measure(
            source.Store,
            appliedRotate.ResultCursor.PublishedRevisionAddress).UniqueFrameBytes;

        EvaluatorRawMetrics observed = metrics.Complete(
            source.Store,
            appliedRotate.ResultCursor);
        long stayWriteBytes = stayCandidate.Observation.Layout.AppendLengthBytes;
        long rotateWriteBytes = checked(
            RbfV040Layout.InitialTailOffsetBytes +
            rotateCandidate.Observation.Layout.AppendLengthBytes);
        long expectedMaxCurrentTail = new[] {
            source.InitialCurrentTailOffsetBytes,
            appliedStay.ResultCursor.CurrentFileTailOffsetBytes,
            appliedRotate.ResultCursor.CurrentFileTailOffsetBytes,
        }.Max();

        Assert.Equal(2, observed.RealizedCommitCount);
        Assert.Equal(
            stayWriteBytes + rotateWriteBytes,
            observed.TotalPhysicalWriteBytes);
        Assert.Equal(observed.TotalPhysicalWriteBytes,
            observed.WorkloadPhysicalWriteBytes);
        Assert.Equal(0, observed.TerminalSettlementPhysicalWriteBytes);
        Assert.Equal(41, observed.TotalWorkloadDeltaReferencePayloadBytes);
        Assert.Equal(80, observed.TotalWorkloadBaseReferencePayloadBytes);
        Assert.Equal(
            Math.Max(stayWriteBytes, rotateWriteBytes),
            observed.PeakWorkloadCommitWriteBytes);
        Assert.Equal(
            observed.PeakWorkloadCommitWriteBytes,
            observed.PeakCommitWriteBytes);
        Assert.Equal(expectedMaxCurrentTail, observed.MaxCurrentFileTailBytes);
        Assert.Equal(2, observed.WorkloadColdReadSampleCount);
        Assert.Equal(
            expectedStayColdReadBytes + expectedRotateColdReadBytes,
            observed.TotalWorkloadColdReadBytes);
        Assert.Equal(280, observed.TotalWorkloadLogicalBasePayloadBytes);
        Assert.Equal([0, 1], observed.WorkloadColdReadSamples.Select(
            static sample => sample.WorkloadSaveOrdinal));

        AbsoluteFrameAddress stayAddress = stayCandidate.Plan.Revision.Address;
        AbsoluteFrameAddress rotateAddress = rotateCandidate.Plan.Revision.Address;
        Assert.Equal(
            [rotateAddress],
            observed.TerminalColdHeadRead.DictionaryFrameAddresses);
        Assert.Equal(
            CanonicalAddresses([stayAddress, rotateAddress]),
            observed.TerminalColdHeadRead.ObjectReconstructionFrameAddresses);
        Assert.Equal(
            CanonicalAddresses([stayAddress, rotateAddress]),
            observed.TerminalColdHeadRead.UniqueFrameAddresses);
        long expectedColdReadBytes = checked(
            (long)source.Store.ReadLayout(stayAddress).FrameLengthBytes +
            source.Store.ReadLayout(rotateAddress).FrameLengthBytes);
        Assert.Equal(expectedColdReadBytes, observed.TerminalColdHeadReadBytes);
        Assert.Equal(
            source.Store.ReadLayout(rotateAddress).FrameLengthBytes,
            observed.TerminalColdHeadRead.DictionaryFrameBytes);
        Assert.Equal(
            expectedColdReadBytes,
            observed.TerminalColdHeadRead.ObjectReconstructionFrameBytes);
    }

    [Fact]
    public void One_outer_Commit_groups_multiple_realized_Revisions_into_one_peak() {
        SourceFixture source = CreateSource(
            (FirstObjectId, 10),
            (SecondObjectId, 20));
        EvaluatorRawMetricAccumulator metrics = new(source.Store, source.Cursor);

        metrics.BeginCommit(source.Store, source.Cursor);
        FeasibleCandidate<StayBRevisionPlan> firstMigration = CreateMigration(
            source.Store,
            source.Cursor,
            FirstObjectId);
        ProbeRevisionCursor afterFirst = ExplicitProbeRevisionApplier.ApplyStayB(
            source.Store,
            source.Cursor,
            firstMigration);
        metrics.ObserveAcceptedRevision(source.Store, afterFirst);

        FeasibleCandidate<StayBRevisionPlan> secondMigration = CreateMigration(
            source.Store,
            afterFirst,
            SecondObjectId);
        ProbeRevisionCursor afterSecond = ExplicitProbeRevisionApplier.ApplyStayB(
            source.Store,
            afterFirst,
            secondMigration);
        metrics.ObserveAcceptedRevision(source.Store, afterSecond);
        metrics.EndWorkloadCommit(
            source.Store,
            afterSecond,
            secondMigration.Plan.Facts);

        EvaluatorRawMetrics observed = metrics.Complete(source.Store, afterSecond);
        long expectedWriteBytes = checked(
            (long)firstMigration.Observation.Layout.AppendLengthBytes +
            secondMigration.Observation.Layout.AppendLengthBytes);
        Assert.Equal(1, observed.RealizedCommitCount);
        Assert.Equal(expectedWriteBytes, observed.TotalPhysicalWriteBytes);
        Assert.Equal(expectedWriteBytes, observed.WorkloadPhysicalWriteBytes);
        Assert.Equal(0, observed.TerminalSettlementPhysicalWriteBytes);
        Assert.Equal(0, observed.TotalWorkloadDeltaReferencePayloadBytes);
        Assert.Equal(0, observed.TotalWorkloadBaseReferencePayloadBytes);
        Assert.Equal(expectedWriteBytes, observed.PeakCommitWriteBytes);
        Assert.Equal(expectedWriteBytes, observed.PeakWorkloadCommitWriteBytes);
        Assert.Equal(
            afterSecond.CurrentFileTailOffsetBytes,
            observed.MaxCurrentFileTailBytes);
        Assert.Equal(1, observed.WorkloadColdReadSampleCount);
        Assert.Equal(
            FinalColdHeadReadMeasurer.Measure(
                source.Store,
                afterSecond.PublishedRevisionAddress).UniqueFrameBytes,
            observed.TotalWorkloadColdReadBytes);
        Assert.Equal(30, observed.TotalWorkloadLogicalBasePayloadBytes);
    }

    [Fact]
    public void Unrealized_Commit_cancels_without_creating_a_metric_sample() {
        SourceFixture source = CreateSource((FirstObjectId, 10));
        EvaluatorRawMetricAccumulator metrics = new(source.Store, source.Cursor);

        metrics.BeginCommit(source.Store, source.Cursor);
        metrics.CancelUnrealizedCommit(source.Store, source.Cursor);
        EvaluatorRawMetrics observed = metrics.Complete(source.Store, source.Cursor);

        Assert.Equal(0, observed.RealizedCommitCount);
        Assert.Equal(0, observed.TotalPhysicalWriteBytes);
        Assert.Equal(0, observed.WorkloadPhysicalWriteBytes);
        Assert.Equal(0, observed.TerminalSettlementPhysicalWriteBytes);
        Assert.Equal(0, observed.TotalWorkloadDeltaReferencePayloadBytes);
        Assert.Equal(0, observed.TotalWorkloadBaseReferencePayloadBytes);
        Assert.Equal(0, observed.PeakCommitWriteBytes);
        Assert.Equal(0, observed.PeakWorkloadCommitWriteBytes);
        Assert.Equal(
            source.InitialCurrentTailOffsetBytes,
            observed.MaxCurrentFileTailBytes);
        Assert.Equal(0, observed.WorkloadColdReadSampleCount);
        Assert.Equal(0, observed.TotalWorkloadColdReadBytes);
        Assert.Equal(0, observed.TotalWorkloadLogicalBasePayloadBytes);
        Assert.True(observed.TerminalColdHeadReadBytes > 0);
    }

    [Fact]
    public void Raw_metric_checked_sums_fail_on_overflow() {
        SourceFixture source = CreateSource((FirstObjectId, 10));
        FinalColdHeadReadObservation coldRead = FinalColdHeadReadMeasurer.Measure(
            source.Store,
            source.Cursor.PublishedRevisionAddress);

        Assert.Throws<OverflowException>(() => new EvaluatorRawMetrics(
            realizedCommitCount: 2,
            workloadPhysicalWriteBytes: long.MaxValue,
            terminalSettlementPhysicalWriteBytes: 1,
            totalWorkloadDeltaReferencePayloadBytes: 0,
            totalWorkloadBaseReferencePayloadBytes: 0,
            peakWorkloadCommitWriteBytes: 1,
            maxCurrentFileTailBytes: source.Cursor.CurrentFileTailOffsetBytes,
            workloadColdReadSamples: [],
            terminalColdHeadRead: coldRead));

        Assert.Throws<OverflowException>(() => new EvaluatorRawMetrics(
            realizedCommitCount: 2,
            workloadPhysicalWriteBytes: 2,
            terminalSettlementPhysicalWriteBytes: 0,
            totalWorkloadDeltaReferencePayloadBytes: 0,
            totalWorkloadBaseReferencePayloadBytes: 0,
            peakWorkloadCommitWriteBytes: 1,
            maxCurrentFileTailBytes: source.Cursor.CurrentFileTailOffsetBytes,
            workloadColdReadSamples: [
                new WorkloadColdReadSample(0, coldRead, long.MaxValue),
                new WorkloadColdReadSample(1, coldRead, 1),
            ],
            terminalColdHeadRead: coldRead));
    }

    [Fact]
    public void Raw_metric_rejects_impossible_peak_accounting() {
        SourceFixture source = CreateSource((FirstObjectId, 10));
        FinalColdHeadReadObservation coldRead = FinalColdHeadReadMeasurer.Measure(
            source.Store,
            source.Cursor.PublishedRevisionAddress);
        WorkloadColdReadSample[] samples = [
            new WorkloadColdReadSample(0, coldRead, 10),
        ];

        Assert.Throws<ArgumentException>(() => new EvaluatorRawMetrics(
            realizedCommitCount: 1,
            workloadPhysicalWriteBytes: 10,
            terminalSettlementPhysicalWriteBytes: 0,
            totalWorkloadDeltaReferencePayloadBytes: 0,
            totalWorkloadBaseReferencePayloadBytes: 0,
            peakWorkloadCommitWriteBytes: 11,
            maxCurrentFileTailBytes: source.Cursor.CurrentFileTailOffsetBytes,
            workloadColdReadSamples: samples,
            terminalColdHeadRead: coldRead));

        Assert.Throws<ArgumentException>(() => new EvaluatorRawMetrics(
            realizedCommitCount: 1,
            workloadPhysicalWriteBytes: 10,
            terminalSettlementPhysicalWriteBytes: 0,
            totalWorkloadDeltaReferencePayloadBytes: 0,
            totalWorkloadBaseReferencePayloadBytes: 0,
            peakWorkloadCommitWriteBytes: 0,
            maxCurrentFileTailBytes: source.Cursor.CurrentFileTailOffsetBytes,
            workloadColdReadSamples: samples,
            terminalColdHeadRead: coldRead));

        Assert.Throws<ArgumentException>(() => new EvaluatorRawMetrics(
            realizedCommitCount: 0,
            workloadPhysicalWriteBytes: 0,
            terminalSettlementPhysicalWriteBytes: 10,
            totalWorkloadDeltaReferencePayloadBytes: 0,
            totalWorkloadBaseReferencePayloadBytes: 0,
            peakWorkloadCommitWriteBytes: 0,
            maxCurrentFileTailBytes: source.Cursor.CurrentFileTailOffsetBytes,
            workloadColdReadSamples: [],
            terminalColdHeadRead: coldRead));
    }

    [Fact]
    public void Removing_the_last_live_object_records_positive_cold_bytes_over_zero_live_bytes() {
        SourceFixture source = CreateSource((FirstObjectId, 10));
        EvaluatorRawMetricAccumulator metrics = new(source.Store, source.Cursor);
        NormalizedSaveFacts facts = SaveStepNormalizer.Normalize(
            source.Store,
            source.Current.FileNumber,
            source.Cursor.PublishedRevisionAddress,
            new SaveStep([new RemoveObject(FirstObjectId)]));
        ExplicitCandidatePairEvaluation pair = ExplicitCandidatePairEvaluator.Evaluate(
            source.Store,
            facts,
            new StayBSaveDecision([], []),
            new RotateCSaveDecision([], []));

        metrics.BeginCommit(source.Store, source.Cursor);
        AppliedStayBPolicyStep applied = Assert.IsType<AppliedStayBPolicyStep>(
            ExplicitRotationPolicyStepHarness.TryApplySelected(
                source.Store,
                source.Cursor,
                pair,
                CandidateTarget.StayB));
        metrics.ObserveAcceptedRevision(source.Store, applied.ResultCursor);
        metrics.EndWorkloadCommit(
            source.Store,
            applied.ResultCursor,
            facts);

        EvaluatorRawMetrics observed = metrics.Complete(
            source.Store,
            applied.ResultCursor);

        Assert.Equal(1, observed.WorkloadColdReadSampleCount);
        Assert.Equal(0, observed.TotalWorkloadDeltaReferencePayloadBytes);
        Assert.Equal(0, observed.TotalWorkloadBaseReferencePayloadBytes);
        Assert.True(observed.TotalWorkloadColdReadBytes > 0);
        Assert.Equal(0, observed.TotalWorkloadLogicalBasePayloadBytes);
        Assert.Empty(observed.WorkloadColdReadSamples[0]
            .ColdRead.ObjectReconstructionFrameAddresses);
    }

    [Fact]
    public void Empty_live_graph_still_reads_the_full_OVD_materialization_chain() {
        SourceFixture source = CreateSource();

        FinalColdHeadReadObservation observed = FinalColdHeadReadMeasurer.Measure(
            source.Store,
            source.Cursor.PublishedRevisionAddress);

        AbsoluteFrameAddress[] expectedAddresses = CanonicalAddresses([
            source.PreviousRevisionAddress,
            source.Cursor.PublishedRevisionAddress,
        ]);
        Assert.Equal(expectedAddresses, observed.DictionaryFrameAddresses);
        Assert.Empty(observed.ObjectReconstructionFrameAddresses);
        Assert.Equal(expectedAddresses, observed.UniqueFrameAddresses);
        long expectedBytes = expectedAddresses.Sum(address =>
            (long)source.Store.ReadLayout(address).FrameLengthBytes);
        Assert.Equal(expectedBytes, observed.DictionaryFrameBytes);
        Assert.Equal(0, observed.ObjectReconstructionFrameBytes);
        Assert.Equal(expectedBytes, observed.UniqueFrameBytes);
        Assert.True(observed.UniqueFrameBytes > 0);
    }

    private static FeasibleCandidate<StayBRevisionPlan> CreateMigration(
        RbfFileStore store,
        ProbeRevisionCursor cursor,
        uint objectId) {
        NormalizedSaveFacts facts = SaveStepNormalizer.NormalizeMaintenanceOnly(
            store,
            cursor.FileScope.CurrentFileNumber,
            cursor.PublishedRevisionAddress);
        StayBRevisionPlan plan = StayBRevisionPlanner.Create(
            store,
            facts,
            new StayBSaveDecision([], [objectId]));
        CandidateRawObservation observation = CandidateRawObservationBuilder.Create(
            store,
            facts,
            CandidateTarget.StayB,
            plan.Revision);
        return new FeasibleCandidate<StayBRevisionPlan>(plan, observation);
    }

    private static SourceFixture CreateSource(
        params (uint ObjectId, int PayloadBytes)[] objects) {
        RbfFileStore store = new();
        RbfFile previous = store.CreateFile();
        ObjectVersionDictionaryBuilder previousDictionary = new();
        FrameBuilder previousBuilder = new() {
            ObjectVersionDictionary = previousDictionary,
        };
        foreach ((uint objectId, int payloadBytes) in objects) {
            previousDictionary.BindSelf(objectId);
            AddBase(previousBuilder, objectId, payloadBytes);
        }

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
        ProbeRevisionCursor cursor = new(
            new FileScope(current.FileNumber),
            published,
            current.TailOffsetBytes);
        return new SourceFixture(
            store,
            previousRevision,
            current,
            cursor,
            current.TailOffsetBytes);
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

    private static AbsoluteFrameAddress[] CanonicalAddresses(
        IEnumerable<AbsoluteFrameAddress> addresses) => addresses
        .Distinct()
        .OrderBy(static address => address.FileNumber)
        .ThenBy(static address => address.FrameTicket.OffsetBytes)
        .ThenBy(static address => address.FrameTicket.LengthBytes)
        .ToArray();

    private sealed record SourceFixture(
        RbfFileStore Store,
        AbsoluteFrameAddress PreviousRevisionAddress,
        RbfFile Current,
        ProbeRevisionCursor Cursor,
        long InitialCurrentTailOffsetBytes);
}
