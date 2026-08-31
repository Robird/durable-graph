using Atelia.TwoLegRotationProbe.Benchmarking;
using Atelia.TwoLegRotationProbe.Evaluation;
using Atelia.TwoLegRotationProbe.Model;
using Atelia.TwoLegRotationProbe.Planning;
using Atelia.TwoLegRotationProbe.Policies;
using Atelia.TwoLegRotationProbe.Simulation;
using Atelia.TwoLegRotationProbe.Workloads;

namespace Atelia.TwoLegRotationProbe.Tests;

public sealed partial class RotationPolicyComparisonTests {
    [Fact]
    public void Anchor_normalized_source_layout_changes_intermediate_closure_not_fixed_horizon_raw_vectors() {
        WorkloadTrace trace = CreateSourceLayoutFixedHorizonTrace();
        AnchoredInteractionFixture shared = CreateAnchoredInteractionFixture(
            AnchoredInteractionPacking.Shared);
        AnchoredInteractionFixture split = CreateAnchoredInteractionFixture(
            AnchoredInteractionPacking.Split);

        FixedHorizonSourceDiagnostic sharedSource = InspectFixedHorizonSource(
            shared,
            trace);
        FixedHorizonSourceDiagnostic splitSource = InspectFixedHorizonSource(
            split,
            trace);
        FixedHorizonDebtObject[] expectedDebtObjects = [
            new(1, 100),
            new(2, 200),
            new(3, 300),
            new(10, 100),
            new(20, 200),
            new(30, 300),
        ];
        Assert.Equal(expectedDebtObjects, sharedSource.DebtObjects);
        Assert.Equal(sharedSource.DebtObjects, splitSource.DebtObjects);
        Assert.Equal(new FixedHorizonFrameCost(1, 1276), sharedSource.PreviousFrames);
        Assert.Equal(new FixedHorizonFrameCost(2, 1308), splitSource.PreviousFrames);
        Assert.Equal(shared.ColdPayloadAddress, shared.ChangedPayloadAddress);
        Assert.NotEqual(split.ColdPayloadAddress, split.ChangedPayloadAddress);

        FixedHorizonCellResult sharedNoMigration = RunFixedHorizonCell(
            shared,
            trace,
            BenchmarkV1SelectionProfiles.DebtZeroThenRotateDeltaNoMigration.Identity);
        FixedHorizonCellResult sharedPaced = RunFixedHorizonCell(
            shared,
            trace,
            BenchmarkV1SelectionProfiles
                .DebtZeroThenRotateDeltaPacedOneDebtByObjectId.Identity);
        FixedHorizonCellResult splitNoMigration = RunFixedHorizonCell(
            split,
            trace,
            BenchmarkV1SelectionProfiles.DebtZeroThenRotateDeltaNoMigration.Identity);
        FixedHorizonCellResult splitPaced = RunFixedHorizonCell(
            split,
            trace,
            BenchmarkV1SelectionProfiles
                .DebtZeroThenRotateDeltaPacedOneDebtByObjectId.Identity);

        FixedHorizonRawVector noMigrationSegment1 = new(5, 1476, 1288, 1288, 1320);
        FixedHorizonRawVector noMigrationSegment2 = new(1, 80, 80, 1288, 1352);
        FixedHorizonRawVector noMigrationCombined = new(6, 1556, 1288, 1288, 1352);
        FixedHorizonRawVector pacedSegment1 = new(5, 1496, 580, 956, 1472);
        FixedHorizonRawVector pacedSegment2 = new(1, 788, 788, 788, 1352);
        FixedHorizonRawVector pacedCombined = new(6, 2284, 788, 956, 1352);
        AssertCellRaw(
            sharedNoMigration,
            noMigrationSegment1,
            noMigrationSegment2,
            noMigrationCombined);
        AssertCellRaw(
            splitNoMigration,
            noMigrationSegment1,
            noMigrationSegment2,
            noMigrationCombined);
        AssertCellRaw(
            sharedPaced,
            pacedSegment1,
            pacedSegment2,
            pacedCombined);
        AssertCellRaw(
            splitPaced,
            pacedSegment1,
            pacedSegment2,
            pacedCombined);

        FixedHorizonFrameCost sharedFrame = new(1, 1276);
        FixedHorizonFrameCost splitFrames = new(2, 1308);
        FixedHorizonFrameCost splitChangedFrame = new(1, 656);
        Assert.Equal(
            [sharedFrame, sharedFrame, sharedFrame, sharedFrame],
            sharedNoMigration.PreviousFramesAfterWorkloadSteps);
        Assert.Equal(
            [sharedFrame, sharedFrame, sharedFrame, sharedFrame],
            sharedPaced.PreviousFramesAfterWorkloadSteps);
        Assert.Equal(
            [splitFrames, splitFrames, splitFrames, splitFrames],
            splitNoMigration.PreviousFramesAfterWorkloadSteps);
        Assert.Equal(
            [splitFrames, splitFrames, splitChangedFrame, splitChangedFrame],
            splitPaced.PreviousFramesAfterWorkloadSteps);

        uint[] noMigrationDebt = [1, 2, 3, 10, 20, 30];
        uint[] pacedDebt = [20, 30];
        Assert.Equal(noMigrationDebt, sharedNoMigration.FinalPreviousDebtObjectIds);
        Assert.Equal(noMigrationDebt, splitNoMigration.FinalPreviousDebtObjectIds);
        Assert.Equal(pacedDebt, sharedPaced.FinalPreviousDebtObjectIds);
        Assert.Equal(pacedDebt, splitPaced.FinalPreviousDebtObjectIds);
    }

    private static FixedHorizonCellResult RunFixedHorizonCell(
        AnchoredInteractionFixture fixture,
        WorkloadTrace trace,
        BenchmarkComponentIdentityV1 selectionProfile) {
        PolicySource source = fixture.Source;
        long sourceTailBytes = FixedHorizonTotalTailBytes(source.Store);
        EvaluatorV1Session first = new(
            source.Store,
            CreateCursor(source),
            totalWorkloadStepCount: trace.Steps.Count - 1);
        List<FixedHorizonFrameCost> previousFramesAfterWorkloadSteps = [];

        for (int stepIndex = 1; stepIndex < trace.Steps.Count; stepIndex++) {
            NormalizedSaveFacts facts = SaveStepNormalizer.Normalize(
                first.Store,
                first.Cursor.FileScope.CurrentFileNumber,
                first.Cursor.PublishedRevisionAddress,
                trace.Steps[stepIndex]);
            BenchmarkV1StepSelection selection =
                BenchmarkV1SelectionProfileSelector.Select(
                selectionProfile,
                facts);
            Assert.Equal(CandidateTarget.StayB, selection.Target);

            ExplicitCandidatePairEvaluation pair =
                ExplicitCandidatePairEvaluator.Evaluate(
                    first.Store,
                    facts,
                    selection.StayB,
                    selection.RotateC);
            RotationPolicyStepAttempt attempt = first.ApplySelectedWorkloadCommit(
                pair,
                selection.Target);
            _ = Assert.IsType<AppliedStayBPolicyStep>(attempt);
            previousFramesAfterWorkloadSteps.Add(InspectFixedHorizonPreviousFrames(
                first.Store,
                first.Cursor));
        }

        AdmittedEvaluatorRun firstClosed = Assert.IsType<AdmittedEvaluatorRun>(
            first.Complete());
        AssertFixedHorizonScope(firstClosed.FinalCursor, 2, 3);
        AssertDirectFixedHorizonSettlement(firstClosed.Settlement);

        EvaluatorV1Session second = new(
            first.Store,
            firstClosed.FinalCursor,
            totalWorkloadStepCount: 0);
        AdmittedEvaluatorRun secondClosed = Assert.IsType<AdmittedEvaluatorRun>(
            second.Complete());
        AssertFixedHorizonScope(secondClosed.FinalCursor, 3, 4);
        AssertDirectFixedHorizonSettlement(secondClosed.Settlement);

        FixedHorizonRawVector firstRaw = ProjectFixedHorizonRaw(firstClosed);
        FixedHorizonRawVector secondRaw = ProjectFixedHorizonRaw(secondClosed);
        FixedHorizonRawVector combined = ConcatenateFixedHorizonSegments(
            firstClosed,
            secondClosed);
        Assert.Equal(6, combined.RealizedCommitCount);
        Assert.Equal(
            combined.TotalPhysicalWriteBytes,
            checked(FixedHorizonTotalTailBytes(second.Store) - sourceTailBytes));
        AssertRuntimeStateAndClosure(
            second.Store,
            secondClosed.FinalCursor,
            WorkloadReplayer.Replay(trace));

        return new FixedHorizonCellResult(
            firstRaw,
            secondRaw,
            combined,
            previousFramesAfterWorkloadSteps,
            GetFixedHorizonPreviousDebtObjectIds(
                second.Store,
                secondClosed.FinalCursor));
    }

    private static FixedHorizonSourceDiagnostic InspectFixedHorizonSource(
        AnchoredInteractionFixture fixture,
        WorkloadTrace trace) {
        PolicySource source = fixture.Source;
        Assert.Equal(2, source.Store.FileCount);
        AssertFixedHorizonScope(CreateCursor(source), 1, 2);
        AssertExactState(
            source.InitialExpectedState,
            WorkloadReplayer.Replay(new WorkloadTrace(
                trace.ScenarioName,
                trace.GeneratorId,
                trace.GeneratorVersion,
                trace.Seed,
                [trace.Steps[0]])));

        NormalizedSaveFacts facts = SaveStepNormalizer.Normalize(
            source.Store,
            source.Current.FileNumber,
            source.PublishedRevisionAddress,
            trace.Steps[1]);
        AssertAnchoredInteractionSource(fixture, facts);

        FixedHorizonDebtObject[] debtObjects = facts.ParentLive.Values
            .Select(static fact => new FixedHorizonDebtObject(
                fact.ObjectId,
                fact.State.BasePayloadBytes))
            .OrderBy(static debt => debt.ObjectId)
            .ToArray();
        Assert.All(facts.ParentLive.Values, fact =>
            Assert.Equal(
                source.Current.FileNumber - 1,
                fact.BaseAddress.FileNumber));
        return new FixedHorizonSourceDiagnostic(
            debtObjects,
            InspectFixedHorizonPreviousFrames(source.Store, CreateCursor(source)));
    }

    private static FixedHorizonFrameCost InspectFixedHorizonPreviousFrames(
        RbfFileStore store,
        ProbeRevisionCursor cursor) {
        uint previousFileNumber = cursor.FileScope.PreviousFileNumber
            ?? throw new InvalidDataException("The witness requires a two-file scope.");
        NormalizedSaveFacts facts = SaveStepNormalizer.NormalizeMaintenanceOnly(
            store,
            cursor.FileScope.CurrentFileNumber,
            cursor.PublishedRevisionAddress);
        AbsoluteFrameAddress[] previousFrames = facts.ParentLive.Values
            .SelectMany(static fact => fact.ReconstructionFrameAddresses)
            .Where(address => address.FileNumber == previousFileNumber)
            .Distinct()
            .ToArray();
        return new FixedHorizonFrameCost(
            previousFrames.Length,
            previousFrames.Sum(address => store.ReadLayout(address).FrameLengthBytes));
    }

    private static WorkloadTrace CreateSourceLayoutFixedHorizonTrace() {
        WorkloadTrace workload = CreateChangedDebtTrace();
        return new WorkloadTrace(
            scenarioName: "anchor-normalized-source-layout-fixed-horizon",
            generatorId: "handwritten",
            generatorVersion: 1,
            seed: 0,
            [
                new SaveStep([
                    new CreateObject(1, 100),
                    new CreateObject(2, 200),
                    new CreateObject(3, 300),
                    new CreateObject(10, 100),
                    new CreateObject(20, 200),
                    new CreateObject(30, 300),
                ]),
                .. workload.Steps,
            ]);
    }

    private static void AssertCellRaw(
        FixedHorizonCellResult cell,
        FixedHorizonRawVector expectedFirst,
        FixedHorizonRawVector expectedSecond,
        FixedHorizonRawVector expectedCombined) {
        Assert.Equal(expectedFirst, cell.FirstSegmentRaw);
        Assert.Equal(expectedSecond, cell.SecondSegmentRaw);
        Assert.Equal(expectedCombined, cell.CombinedRaw);
    }

    private static FixedHorizonRawVector ProjectFixedHorizonRaw(
        AdmittedEvaluatorRun admitted) => new(
            admitted.Metrics.RealizedCommitCount,
            admitted.Metrics.TotalPhysicalWriteBytes,
            admitted.Metrics.PeakCommitWriteBytes,
            admitted.Metrics.MaxCurrentFileTailBytes,
            admitted.Metrics.FinalColdHeadReadBytes);

    private static FixedHorizonRawVector ConcatenateFixedHorizonSegments(
        params AdmittedEvaluatorRun[] segments) => new(
            segments.Sum(static segment => segment.Metrics.RealizedCommitCount),
            segments.Sum(static segment => segment.Metrics.TotalPhysicalWriteBytes),
            segments.Max(static segment => segment.Metrics.PeakCommitWriteBytes),
            segments.Max(static segment => segment.Metrics.MaxCurrentFileTailBytes),
            segments[^1].Metrics.FinalColdHeadReadBytes);

    private static void AssertFixedHorizonScope(
        ProbeRevisionCursor cursor,
        uint previousFileNumber,
        uint currentFileNumber) {
        Assert.Equal(previousFileNumber, cursor.FileScope.PreviousFileNumber);
        Assert.Equal(currentFileNumber, cursor.FileScope.CurrentFileNumber);
        Assert.Equal(currentFileNumber, cursor.PublishedRevisionAddress.FileNumber);
    }

    private static void AssertDirectFixedHorizonSettlement(
        TerminalSettlementObservation settlement) {
        Assert.Empty(settlement.MigratedObjectIds);
        Assert.Equal(0, settlement.MaintenanceRevisionCount);
        Assert.Equal(1, settlement.RealizedRevisionCount);
    }

    private static uint[] GetFixedHorizonPreviousDebtObjectIds(
        RbfFileStore store,
        ProbeRevisionCursor cursor) {
        uint previousFileNumber = cursor.FileScope.PreviousFileNumber
            ?? throw new InvalidDataException("The witness requires a two-file scope.");
        return SaveStepNormalizer.NormalizeMaintenanceOnly(
                store,
                cursor.FileScope.CurrentFileNumber,
                cursor.PublishedRevisionAddress)
            .NoChanges
            .Where(fact => fact.Source.BaseAddress.FileNumber == previousFileNumber)
            .Select(static fact => fact.ObjectId)
            .Order()
            .ToArray();
    }

    private static long FixedHorizonTotalTailBytes(RbfFileStore store) => Enumerable
        .Range(1, store.FileCount)
        .Sum(index => store.GetFile((uint)index).TailOffsetBytes);

    private sealed record FixedHorizonSourceDiagnostic(
        IReadOnlyList<FixedHorizonDebtObject> DebtObjects,
        FixedHorizonFrameCost PreviousFrames);

    private sealed record FixedHorizonCellResult(
        FixedHorizonRawVector FirstSegmentRaw,
        FixedHorizonRawVector SecondSegmentRaw,
        FixedHorizonRawVector CombinedRaw,
        IReadOnlyList<FixedHorizonFrameCost> PreviousFramesAfterWorkloadSteps,
        uint[] FinalPreviousDebtObjectIds);

    private readonly record struct FixedHorizonDebtObject(
        uint ObjectId,
        int BasePayloadBytes);

    private readonly record struct FixedHorizonFrameCost(int Count, long Bytes);

    private readonly record struct FixedHorizonRawVector(
        int RealizedCommitCount,
        long TotalPhysicalWriteBytes,
        long PeakCommitWriteBytes,
        long MaxCurrentFileTailBytes,
        long FinalColdHeadReadBytes);
}
