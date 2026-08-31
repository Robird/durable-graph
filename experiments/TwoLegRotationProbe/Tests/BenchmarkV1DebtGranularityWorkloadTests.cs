using Atelia.TwoLegRotationProbe.Arena;
using Atelia.TwoLegRotationProbe.Baselines;
using Atelia.TwoLegRotationProbe.Benchmarking;
using Atelia.TwoLegRotationProbe.Evaluation;
using Atelia.TwoLegRotationProbe.Model;
using Atelia.TwoLegRotationProbe.Simulation;
using Atelia.TwoLegRotationProbe.Workloads;

namespace Atelia.TwoLegRotationProbe.Tests;

public sealed class BenchmarkV1DebtGranularityWorkloadTests {
    private static readonly BenchmarkV1BatchDefinition Corpus =
        BenchmarkV1Corpus.Create(BenchmarkV1Baselines.All);

    private static readonly WorkloadTrace SingleLarge = FindTrace(
        "previous-debt-granularity-single-large");

    private static readonly WorkloadTrace ThreeSmall = FindTrace(
        "previous-debt-granularity-three-small");

    [Fact]
    public void Matched_pair_preserves_operations_and_reconverges_final_versions() {
        Assert.Equal(5, SingleLarge.Steps.Count);
        Assert.Equal(5, ThreeSmall.Steps.Count);
        Assert.Equal([4, 3, 1, 1, 3], StepOperationCounts(SingleLarge));
        Assert.Equal([4, 1, 1, 3, 3], StepOperationCounts(ThreeSmall));
        Assert.Same(SingleLarge.Steps[0], ThreeSmall.Steps[0]);
        Assert.Same(SingleLarge.Steps[1], ThreeSmall.Steps[3]);
        Assert.Same(SingleLarge.Steps[2], ThreeSmall.Steps[2]);
        Assert.Same(SingleLarge.Steps[3], ThreeSmall.Steps[1]);
        Assert.Same(SingleLarge.Steps[4], ThreeSmall.Steps[4]);
        Assert.Equal(OperationMultiset(SingleLarge), OperationMultiset(ThreeSmall));
        AssertTraceCases(
            "previous-debt-granularity-single-large",
            "dbe729ca29f27f601f4390e0b63ebfdfaa25b3f070a7de70fd5314dea0655ab8");
        AssertTraceCases(
            "previous-debt-granularity-three-small",
            "9f2cb14e217f8d2cd09307e0df0a5bbbe03c864e79db49c89c64d967c784f67a");

        BenchmarkV1BootstrappedSource singleSource =
            BenchmarkV1SourceBootstrap.Create(SingleLarge);
        BenchmarkV1BootstrappedSource threeSource =
            BenchmarkV1SourceBootstrap.Create(ThreeSmall);
        Assert.Equal(
            singleSource.PreviousRevisionAddress,
            threeSource.PreviousRevisionAddress);
        Assert.Equal(
            singleSource.Cursor.FileScope.PreviousFileNumber,
            threeSource.Cursor.FileScope.PreviousFileNumber);
        Assert.Equal(
            singleSource.Cursor.FileScope.CurrentFileNumber,
            threeSource.Cursor.FileScope.CurrentFileNumber);
        Assert.Equal(
            singleSource.Cursor.PublishedRevisionAddress,
            threeSource.Cursor.PublishedRevisionAddress);
        Assert.Equal(
            singleSource.Cursor.CurrentFileTailOffsetBytes,
            threeSource.Cursor.CurrentFileTailOffsetBytes);
        Assert.Equal(FileTails(singleSource.Store), FileTails(threeSource.Store));
        Assert.Equal(
            singleSource.Store.ReadLayout(singleSource.PreviousRevisionAddress),
            threeSource.Store.ReadLayout(threeSource.PreviousRevisionAddress));

        IReadOnlyDictionary<uint, LogicalObjectState> singleFinal =
            WorkloadReplayer.Replay(SingleLarge);
        IReadOnlyDictionary<uint, LogicalObjectState> threeFinal =
            WorkloadReplayer.Replay(ThreeSmall);
        Assert.Equal(singleFinal, threeFinal);
        Assert.Equal([10U, 20U, 30U, 40U, 1001U], singleFinal.Keys.Order());
        Assert.All(new[] { 10U, 20U, 30U }, objectId => {
            Assert.Equal(100, singleFinal[objectId].BasePayloadBytes);
            Assert.Equal(3, singleFinal[objectId].LogicalVersionOrdinal);
        });
        Assert.Equal(300, singleFinal[40].BasePayloadBytes);
        Assert.Equal(2, singleFinal[40].LogicalVersionOrdinal);
        Assert.Equal(1, singleFinal[1001].BasePayloadBytes);
        Assert.Equal(1, singleFinal[1001].LogicalVersionOrdinal);
    }

    [Fact]
    public void Equal_total_previous_debt_with_different_granularity_changes_cadence() {
        foreach (StrategyBindingV1 strategy in BenchmarkV1Baselines.All) {
            CapturedTrace single = Capture(SingleLarge, strategy);
            CapturedTrace three = Capture(ThreeSmall, strategy);
            bool noMigration = strategy.Identity == BenchmarkV1Baselines
                .DebtZeroThenRotateDeltaNoMigration.Identity;
            bool paced = strategy.Identity == BenchmarkV1Baselines
                .DebtZeroThenRotateDeltaPacedOneDebtByObjectId.Identity;

            if (noMigration) {
                AssertCadence(single, StayFour, [[], [], [], []]);
                AssertCadence(three, StayFour, [[], [], [], []]);
            } else if (paced) {
                AssertCadence(single, StayFour, [[40], [10], [20], []]);
                AssertCadence(three, StayFour, [[10], [20], [40], []]);
            } else {
                AssertCadence(
                    single,
                    [
                        StrategyTargetV1.StayB,
                        StrategyTargetV1.StayB,
                        StrategyTargetV1.RotateC,
                        StrategyTargetV1.StayB,
                    ],
                    [[], [40], [], []]);
                AssertCadence(
                    three,
                    [
                        StrategyTargetV1.StayB,
                        StrategyTargetV1.StayB,
                        StrategyTargetV1.StayB,
                        StrategyTargetV1.RotateC,
                    ],
                    [[], [10], [], []]);
                AssertPivot(single.Views[1], [(40U, 300)]);
                AssertPivot(
                    three.Views[1],
                    [(10U, 100), (20U, 100), (30U, 100)]);
                PayloadSnapshot singlePayload = PayloadSnapshot.Create(
                    single.Views[1]);
                PayloadSnapshot threePayload = PayloadSnapshot.Create(
                    three.Views[1]);
                Assert.Equal(
                    singlePayload.SourceHeadPayloadBytes,
                    threePayload.SourceHeadPayloadBytes);
                Assert.Equal(
                    singlePayload.ResultBasePayloadBytes,
                    threePayload.ResultBasePayloadBytes);
                Assert.Equal(
                    singlePayload.DeltaPayloadBytes,
                    threePayload.DeltaPayloadBytes);
                Assert.Equal(
                    [100L, 100L, 100L, 300L],
                    singlePayload.SourceHeadPayloadBytes);
                Assert.Equal(
                    [1, 100, 100, 100, 300],
                    singlePayload.ResultBasePayloadBytes);
                Assert.Empty(singlePayload.DeltaPayloadBytes);
            }
        }
    }

    [Fact]
    public void Granularity_pair_remains_distinguishable_after_common_reconvergence() {
        DebtGranularityRun noMigrationSingle = AssertRun(
            BenchmarkV1Corpus.PreviousDebtGranularitySingleLargeNoMigrationCaseId,
            new RawVector(5, 1800, 672, 1168, 708),
            previousFileNumber: 2,
            currentFileNumber: 3);
        DebtGranularityRun noMigrationThree = AssertRun(
            BenchmarkV1Corpus.PreviousDebtGranularityThreeSmallNoMigrationCaseId,
            new RawVector(5, 1800, 672, 1168, 708),
            previousFileNumber: 2,
            currentFileNumber: 3);
        Assert.Equal(noMigrationSingle.Vector, noMigrationThree.Vector);

        DebtGranularityRun pacedSingle = AssertRun(
            BenchmarkV1Corpus.PreviousDebtGranularitySingleLargePacedCaseId,
            new RawVector(5, 1812, 672, 1688, 1788),
            previousFileNumber: 2,
            currentFileNumber: 3);
        DebtGranularityRun pacedThree = AssertRun(
            BenchmarkV1Corpus.PreviousDebtGranularityThreeSmallPacedCaseId,
            new RawVector(5, 1812, 676, 1688, 1788),
            previousFileNumber: 2,
            currentFileNumber: 3);
        Assert.Equal(
            pacedSingle.Vector.PeakCommitWriteBytes + 4,
            pacedThree.Vector.PeakCommitWriteBytes);
        Assert.Equal(
            pacedSingle.Vector with {
                PeakCommitWriteBytes = pacedThree.Vector.PeakCommitWriteBytes,
            },
            pacedThree.Vector);

        DebtGranularityRun adaptive35Single = AssertRun(
            BenchmarkV1Corpus
                .PreviousDebtGranularitySingleLargeAdaptiveR3B5PercentCaseId,
            new RawVector(5, 1504, 368, 752, 772),
            previousFileNumber: 3,
            currentFileNumber: 4);
        DebtGranularityRun adaptive35Three = AssertRun(
            BenchmarkV1Corpus
                .PreviousDebtGranularityThreeSmallAdaptiveR3B5PercentCaseId,
            new RawVector(5, 1596, 372, 892, 728),
            previousFileNumber: 3,
            currentFileNumber: 4);
        DebtGranularityRun adaptive44Single = AssertRun(
            BenchmarkV1Corpus
                .PreviousDebtGranularitySingleLargeAdaptiveR4B4PercentCaseId,
            adaptive35Single.Vector,
            previousFileNumber: 3,
            currentFileNumber: 4);
        DebtGranularityRun adaptive44Three = AssertRun(
            BenchmarkV1Corpus
                .PreviousDebtGranularityThreeSmallAdaptiveR4B4PercentCaseId,
            adaptive35Three.Vector,
            previousFileNumber: 3,
            currentFileNumber: 4);
        Assert.Equal(adaptive35Single.Vector, adaptive44Single.Vector);
        Assert.Equal(adaptive35Three.Vector, adaptive44Three.Vector);
        AssertPreSettlementScope(adaptive35Single.Product, 2, 3);
        AssertPreSettlementScope(adaptive35Three.Product, 2, 3);
        AssertPreSettlementScope(adaptive44Single.Product, 2, 3);
        AssertPreSettlementScope(adaptive44Three.Product, 2, 3);

        AssertAllFinalStatesEqual([
            noMigrationSingle,
            noMigrationThree,
            pacedSingle,
            pacedThree,
            adaptive35Single,
            adaptive35Three,
            adaptive44Single,
            adaptive44Three,
        ]);

        // The paced 4-byte P delta is current-layout fallout, not a policy benefit.
        // Adaptive granularity remains a W/P/F versus R trade after both traces
        // reconverge to the same logical versions and pre-settlement scope.
        Assert.True(adaptive35Single.Vector.TotalPhysicalWriteBytes <
            adaptive35Three.Vector.TotalPhysicalWriteBytes);
        Assert.True(adaptive35Single.Vector.PeakCommitWriteBytes <
            adaptive35Three.Vector.PeakCommitWriteBytes);
        Assert.True(adaptive35Single.Vector.MaxCurrentFileTailBytes <
            adaptive35Three.Vector.MaxCurrentFileTailBytes);
        Assert.True(adaptive35Single.Vector.FinalColdHeadReadBytes >
            adaptive35Three.Vector.FinalColdHeadReadBytes);
    }

    private static StrategyTargetV1[] StayFour => [
        StrategyTargetV1.StayB,
        StrategyTargetV1.StayB,
        StrategyTargetV1.StayB,
        StrategyTargetV1.StayB,
    ];

    private static CapturedTrace Capture(
        WorkloadTrace trace,
        StrategyBindingV1 strategy) {
        BenchmarkV1BootstrappedSource source = BenchmarkV1SourceBootstrap.Create(trace);
        StrategyRunContextV1 context = new(
            source.Store,
            source.Cursor,
            trace,
            firstWorkloadTraceStepIndex: 1,
            totalWorkloadStepCount: 4);
        List<StrategyStepViewV1> views = [];
        List<StrategyTargetV1> targets = [];
        List<uint[]> migrations = [];
        while (context.CurrentStep is StrategyStepViewV1 view) {
            StrategySelectionV1 selection = BenchmarkV1Baselines.Select(
                strategy.Identity,
                view);
            views.Add(view);
            targets.Add(selection.Target);
            migrations.Add(selection.Stay.UnchangedMigrationObjectIds.ToArray());
            StrategyCommitStatusV1 status = context.Commit(selection);
            Assert.True(status is StrategyCommitStatusV1.AppliedStayB or
                StrategyCommitStatusV1.AppliedRotateC);
        }

        return new CapturedTrace(
            views.ToArray(),
            targets.ToArray(),
            migrations.ToArray());
    }

    private static void AssertCadence(
        CapturedTrace captured,
        IReadOnlyList<StrategyTargetV1> expectedTargets,
        IReadOnlyList<uint[]> expectedMigrations) {
        Assert.Equal(expectedTargets, captured.Targets);
        Assert.Equal(expectedMigrations, captured.Migrations);
    }

    private static void AssertPivot(
        StrategyStepViewV1 view,
        IReadOnlyList<(uint ObjectId, int BasePayloadBytes)> expectedDebt) {
        Assert.Equal(601, view.PostLiveGraphBasePayloadBytes);
        Assert.Equal(300, view.ADependentEvacuationBasePayloadBytes);
        Assert.True(view.HasParentPreviousDebt);
        Assert.Equal(
            expectedDebt,
            view.Objects
                .Where(static fact => fact.SourceIsPreviousDependent is true)
                .Select(static fact => (
                    fact.ObjectId,
                    fact.ResultBasePayloadBytes ?? throw new InvalidDataException())));
    }

    private static DebtGranularityRun AssertRun(
        string caseId,
        RawVector expected,
        uint previousFileNumber,
        uint currentFileNumber) {
        BenchmarkV1CaseDefinition definition = Corpus.Cases.Single(
            candidate => candidate.ManifestCase.CaseId == caseId);
        BenchmarkV1CaseExecution execution = BenchmarkV1Runner.ExecuteCase(definition);
        AdmittedEvaluatorRun admitted = Assert.IsType<AdmittedEvaluatorRun>(
            execution.Outcome);
        Assert.Equal(StrategyRunTerminationV1.Admitted, execution.Product.Termination);
        Assert.Equal([0, 1, 2, 3], execution.Product.WorkloadCommits.Select(
            static receipt => receipt.WorkloadStepOrdinal));
        RawVector actual = new(
            admitted.Metrics.RealizedCommitCount,
            admitted.Metrics.TotalPhysicalWriteBytes,
            admitted.Metrics.PeakCommitWriteBytes,
            admitted.Metrics.MaxCurrentFileTailBytes,
            admitted.Metrics.FinalColdHeadReadBytes);
        Assert.Equal(expected, actual);
        Assert.Equal(EvaluatorRunPhase.TerminalSettlement, admitted.Position.Phase);
        Assert.Equal(4, admitted.Position.CompletedWorkloadStepCount);
        Assert.Equal(4, admitted.Position.TotalWorkloadStepCount);
        Assert.Equal(previousFileNumber,
            admitted.FinalCursor.FileScope.PreviousFileNumber);
        Assert.Equal(currentFileNumber,
            admitted.FinalCursor.FileScope.CurrentFileNumber);
        Assert.Empty(admitted.Settlement.MigratedObjectIds);
        Assert.Equal(0, admitted.Settlement.MaintenanceRevisionCount);
        Assert.Equal(1, admitted.Settlement.RealizedRevisionCount);
        IReadOnlyDictionary<uint, LogicalObjectState> physical =
            PhysicalStateOracle.Materialize(
                execution.Store,
                ObjectVersionDictionaryReader.MaterializeLive(
                    execution.Store,
                    admitted.FinalCursor.PublishedRevisionAddress).Bindings);
        Assert.Equal(WorkloadReplayer.Replay(definition.Trace), physical);
        return new DebtGranularityRun(execution.Product, physical, actual);
    }

    private static void AssertPreSettlementScope(
        StrategyRunProductV1 product,
        uint previousFileNumber,
        uint currentFileNumber) {
        StrategyCommitReceiptV1 last = product.WorkloadCommits[^1];
        Assert.Equal(previousFileNumber, last.Result.PreviousFileNumber);
        Assert.Equal(currentFileNumber, last.Result.CurrentFileNumber);
    }

    private static void AssertAllFinalStatesEqual(
        IReadOnlyList<DebtGranularityRun> runs) {
        Assert.NotEmpty(runs);
        Assert.All(runs.Skip(1), run =>
            Assert.Equal(runs[0].PhysicalState, run.PhysicalState));
    }

    private static int[] StepOperationCounts(WorkloadTrace trace) => trace.Steps
        .Select(static step => step.Changes.Count)
        .ToArray();

    private static string[] OperationMultiset(WorkloadTrace trace) => trace.Steps
        .Skip(1)
        .SelectMany(static step => step.Changes)
        .Select(static change => change switch {
            CreateObject create => $"C:{create.ObjectId}:{create.BasePayloadBytes}",
            UpdateObject update =>
                $"U:{update.ObjectId}:{update.ResultBasePayloadBytes}:" +
                $"{update.DeltaPayloadBytes}",
            _ => throw new InvalidDataException(
                $"Unexpected granularity operation '{change.GetType().Name}'."),
        })
        .Order(StringComparer.Ordinal)
        .ToArray();

    private static long[] FileTails(RbfFileStore store) => Enumerable
        .Range(1, store.FileCount)
        .Select(index => store.GetFile((uint)index).TailOffsetBytes)
        .ToArray();

    private static WorkloadTrace FindTrace(string traceId) {
        BenchmarkV1CaseDefinition[] cases = Corpus.Cases
            .Where(candidate => candidate.ManifestCase.TraceDefinition.Id == traceId)
            .ToArray();
        Assert.Equal(BenchmarkV1Baselines.All.Count, cases.Length);
        Assert.All(cases.Skip(1), benchmarkCase =>
            Assert.Same(cases[0].Trace, benchmarkCase.Trace));
        return cases[0].Trace;
    }

    private static void AssertTraceCases(string traceId, string expectedHash) {
        BenchmarkV1CaseDefinition[] cases = Corpus.Cases
            .Where(candidate => candidate.ManifestCase.TraceDefinition.Id == traceId)
            .ToArray();
        Assert.Equal(4, cases.Length);
        Assert.All(cases, benchmarkCase => {
            Assert.Equal(4, benchmarkCase.ManifestCase.EvaluatedWorkloadStepCount);
            Assert.Equal(expectedHash, benchmarkCase.ManifestCase.ResolvedTraceSha256);
        });
    }

    private sealed record CapturedTrace(
        IReadOnlyList<StrategyStepViewV1> Views,
        IReadOnlyList<StrategyTargetV1> Targets,
        IReadOnlyList<uint[]> Migrations);

    private sealed record DebtGranularityRun(
        StrategyRunProductV1 Product,
        IReadOnlyDictionary<uint, LogicalObjectState> PhysicalState,
        RawVector Vector);

    private sealed record PayloadSnapshot(
        IReadOnlyList<long> SourceHeadPayloadBytes,
        IReadOnlyList<int> ResultBasePayloadBytes,
        IReadOnlyList<int> DeltaPayloadBytes) {
        public static PayloadSnapshot Create(StrategyStepViewV1 view) => new(
            view.Objects
                .Where(static fact =>
                    fact.SourceHeadReconstructionPayloadBytes is not null)
                .Select(static fact =>
                    fact.SourceHeadReconstructionPayloadBytes!.Value)
                .Order()
                .ToArray(),
            view.Objects
                .Where(static fact => fact.ResultBasePayloadBytes is not null)
                .Select(static fact => fact.ResultBasePayloadBytes!.Value)
                .Order()
                .ToArray(),
            view.Objects
                .Where(static fact => fact.DeltaPayloadBytes is not null)
                .Select(static fact => fact.DeltaPayloadBytes!.Value)
                .Order()
                .ToArray());
    }

    private readonly record struct RawVector(
        int RealizedCommitCount,
        long TotalPhysicalWriteBytes,
        long PeakCommitWriteBytes,
        long MaxCurrentFileTailBytes,
        long FinalColdHeadReadBytes);
}
