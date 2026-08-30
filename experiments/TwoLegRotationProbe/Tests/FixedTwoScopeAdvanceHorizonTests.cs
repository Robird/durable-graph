using Atelia.TwoLegRotationProbe.Benchmarking;
using Atelia.TwoLegRotationProbe.Evaluation;
using Atelia.TwoLegRotationProbe.Model;
using Atelia.TwoLegRotationProbe.Planning;
using Atelia.TwoLegRotationProbe.Simulation;
using Atelia.TwoLegRotationProbe.Workloads;

namespace Atelia.TwoLegRotationProbe.Tests;

public sealed class FixedTwoScopeAdvanceHorizonTests {
    [Fact]
    public void Handwritten_pair_reaches_scope_three_four_after_two_scope_advances() {
        RunAndAssertWitness();
    }

    private static void RunAndAssertWitness() {
        BenchmarkV1BatchDefinition corpus = BenchmarkV1Corpus.Create();
        BenchmarkV1CaseDefinition controlDefinition = FindCase(
            corpus,
            BenchmarkV1Corpus.DebtZeroThenRotateNoMigrationCaseId);
        BenchmarkV1CaseDefinition pacedDefinition = FindCase(
            corpus,
            BenchmarkV1Corpus.DebtZeroThenRotatePacedCaseId);
        Assert.Same(controlDefinition.Trace, pacedDefinition.Trace);

        BenchmarkV1CaseExecution controlSegment1Execution =
            BenchmarkV1Runner.ExecuteCase(controlDefinition);
        AdmittedEvaluatorRun controlSegment1 = Assert.IsType<AdmittedEvaluatorRun>(
            controlSegment1Execution.Outcome);
        AssertRaw(
            controlSegment1,
            new RawVector(5, 856, 680, 680, 832));
        AssertScope(
            controlSegment1.FinalCursor,
            previousFileNumber: 2,
            currentFileNumber: 3);
        AssertDirectSettlement(controlSegment1.Settlement);

        EvaluatorV1Session controlSegment2Session = new(
            controlSegment1Execution.Store,
            controlSegment1.FinalCursor,
            totalWorkloadStepCount: 0);
        AdmittedEvaluatorRun controlSegment2 = Assert.IsType<AdmittedEvaluatorRun>(
            controlSegment2Session.Complete());
        AssertRaw(
            controlSegment2,
            new RawVector(1, 88, 88, 680, 752));
        AssertScope(
            controlSegment2.FinalCursor,
            previousFileNumber: 3,
            currentFileNumber: 4);
        AssertDirectSettlement(controlSegment2.Settlement);

        RawVector controlCombined = ConcatenateSegments(
            controlSegment1,
            controlSegment2);
        Assert.Equal(new RawVector(6, 944, 680, 680, 752), controlCombined);
        Assert.NotEqual(
            controlSegment1.Metrics.FinalColdHeadReadBytes +
                controlSegment2.Metrics.FinalColdHeadReadBytes,
            controlCombined.FinalColdHeadReadBytes);
        AssertPhysicalGrowthEqualsWrites(
            controlDefinition.Trace,
            controlSegment2Session.Store,
            controlCombined.TotalPhysicalWriteBytes);
        AssertLogicalState(
            controlSegment2Session.Store,
            controlSegment2.FinalCursor,
            controlDefinition.Trace);
        Assert.Equal(
            [10U, 20U, 30U],
            GetPreviousDebtObjectIds(
                controlSegment2Session.Store,
                controlSegment2.FinalCursor));

        BenchmarkV1CaseExecution pacedExecution =
            BenchmarkV1Runner.ExecuteCase(pacedDefinition);
        AdmittedEvaluatorRun paced = Assert.IsType<AdmittedEvaluatorRun>(
            pacedExecution.Outcome);
        AssertRaw(
            paced,
            new RawVector(5, 1536, 696, 804, 756));
        AssertScope(
            paced.FinalCursor,
            previousFileNumber: 3,
            currentFileNumber: 4);
        AssertDirectSettlement(paced.Settlement);

        RawVector pacedCombined = ConcatenateSegments(paced);
        Assert.Equal(new RawVector(5, 1536, 696, 804, 756), pacedCombined);
        AssertPhysicalGrowthEqualsWrites(
            pacedDefinition.Trace,
            pacedExecution.Store,
            pacedCombined.TotalPhysicalWriteBytes);
        AssertLogicalState(
            pacedExecution.Store,
            paced.FinalCursor,
            pacedDefinition.Trace);
        Assert.Equal(
            [1004U],
            GetPreviousDebtObjectIds(pacedExecution.Store, paced.FinalCursor));
    }

    /// <summary>
    /// Concatenates already-accounted closed segments without remeasuring physical
    /// state. W/Commit count add, P/F take the maximum, and R belongs only to the
    /// final cold head.
    /// </summary>
    private static RawVector ConcatenateSegments(
        params AdmittedEvaluatorRun[] segments) {
        Assert.NotEmpty(segments);
        return new RawVector(
            RealizedCommitCount: segments.Sum(
                static segment => segment.Metrics.RealizedCommitCount),
            TotalPhysicalWriteBytes: segments.Sum(
                static segment => segment.Metrics.TotalPhysicalWriteBytes),
            PeakCommitWriteBytes: segments.Max(
                static segment => segment.Metrics.PeakCommitWriteBytes),
            MaxCurrentFileTailBytes: segments.Max(
                static segment => segment.Metrics.MaxCurrentFileTailBytes),
            FinalColdHeadReadBytes: segments[^1].Metrics.FinalColdHeadReadBytes);
    }

    private static void AssertRaw(
        AdmittedEvaluatorRun admitted,
        RawVector expected) {
        Assert.Equal(expected, ConcatenateSegments(admitted));
    }

    private static void AssertScope(
        ProbeRevisionCursor cursor,
        uint previousFileNumber,
        uint currentFileNumber) {
        Assert.Equal(previousFileNumber, cursor.FileScope.PreviousFileNumber);
        Assert.Equal(currentFileNumber, cursor.FileScope.CurrentFileNumber);
        Assert.Equal(currentFileNumber, cursor.PublishedRevisionAddress.FileNumber);
    }

    private static void AssertDirectSettlement(
        TerminalSettlementObservation settlement) {
        Assert.Empty(settlement.MigratedObjectIds);
        Assert.Equal(0, settlement.MaintenanceRevisionCount);
        Assert.Equal(1, settlement.RealizedRevisionCount);
    }

    private static void AssertPhysicalGrowthEqualsWrites(
        WorkloadTrace trace,
        RbfFileStore finalStore,
        long expectedWrites) {
        BenchmarkV1BootstrappedSource independentBootstrap =
            BenchmarkV1SourceBootstrap.Create(trace);
        long physicalGrowth = checked(
            TotalTailBytes(finalStore) - TotalTailBytes(independentBootstrap.Store));
        Assert.Equal(expectedWrites, physicalGrowth);
    }

    private static void AssertLogicalState(
        RbfFileStore store,
        ProbeRevisionCursor cursor,
        WorkloadTrace trace) {
        IReadOnlyDictionary<uint, LogicalObjectState> expected =
            WorkloadReplayer.Replay(trace);
        IReadOnlyDictionary<uint, LogicalObjectState> actual =
            PhysicalStateOracle.Materialize(
                store,
                ObjectVersionDictionaryReader.MaterializeLive(
                    store,
                    cursor.PublishedRevisionAddress).Bindings);

        Assert.Equal(expected.Count, actual.Count);
        foreach ((uint objectId, LogicalObjectState expectedState) in expected) {
            Assert.True(actual.TryGetValue(objectId, out LogicalObjectState actualState));
            Assert.Equal(expectedState, actualState);
        }
    }

    private static uint[] GetPreviousDebtObjectIds(
        RbfFileStore store,
        ProbeRevisionCursor cursor) {
        uint previousFileNumber = cursor.FileScope.PreviousFileNumber
            ?? throw new InvalidDataException(
                "The fixed-horizon witness requires a two-file final scope.");
        NormalizedSaveFacts facts = SaveStepNormalizer.NormalizeMaintenanceOnly(
            store,
            cursor.FileScope.CurrentFileNumber,
            cursor.PublishedRevisionAddress);
        return facts.NoChanges
            .Where(noChange =>
                noChange.Source.BaseAddress.FileNumber == previousFileNumber)
            .Select(static noChange => noChange.ObjectId)
            .Order()
            .ToArray();
    }

    private static BenchmarkV1CaseDefinition FindCase(
        BenchmarkV1BatchDefinition corpus,
        string caseId) => corpus.Cases.Single(
            benchmarkCase => benchmarkCase.ManifestCase.CaseId == caseId);

    private static long TotalTailBytes(RbfFileStore store) => Enumerable
        .Range(1, store.FileCount)
        .Sum(index => store.GetFile((uint)index).TailOffsetBytes);

    private readonly record struct RawVector(
        int RealizedCommitCount,
        long TotalPhysicalWriteBytes,
        long PeakCommitWriteBytes,
        long MaxCurrentFileTailBytes,
        long FinalColdHeadReadBytes);
}
