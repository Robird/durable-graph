using Atelia.TwoLegRotationProbe.Arena;
using Atelia.TwoLegRotationProbe.Baselines;
using Atelia.TwoLegRotationProbe.Benchmarking;
using Atelia.TwoLegRotationProbe.Encoding;
using Atelia.TwoLegRotationProbe.Evaluation;
using Atelia.TwoLegRotationProbe.Model;
using Atelia.TwoLegRotationProbe.Simulation;
using Atelia.TwoLegRotationProbe.Workloads;

namespace Atelia.TwoLegRotationProbe.Tests;

public sealed class BenchmarkV1InsertBurstPartitionWorkloadTests {
    private static readonly BenchmarkV1BatchDefinition Corpus =
        BenchmarkV1Corpus.Create(BenchmarkV1Baselines.All);

    private static readonly WorkloadTrace ThreeOne = FindTrace(
        "insert-burst-three-one");

    private static readonly WorkloadTrace TwoTwo = FindTrace(
        "insert-burst-two-two");

    [Fact]
    public void Pair_shares_prefix_operations_horizon_and_final_state() {
        Assert.Equal(4, ThreeOne.Steps.Count);
        Assert.Equal(4, TwoTwo.Steps.Count);
        Assert.Same(ThreeOne.Steps[0], TwoTwo.Steps[0]);
        Assert.Same(ThreeOne.Steps[1], TwoTwo.Steps[1]);
        Assert.Same(ThreeOne.Steps[2].Changes[0], TwoTwo.Steps[2].Changes[0]);
        Assert.Same(ThreeOne.Steps[2].Changes[1], TwoTwo.Steps[2].Changes[1]);
        Assert.Same(ThreeOne.Steps[2].Changes[2], TwoTwo.Steps[3].Changes[0]);
        Assert.Same(ThreeOne.Steps[3].Changes[0], TwoTwo.Steps[3].Changes[1]);
        Assert.Equal(OperationMultiset(ThreeOne), OperationMultiset(TwoTwo));
        AssertTraceCases(
            "insert-burst-three-one",
            expectedHash:
                "c1dd88f6b7a863e01e53eb61eeb7c8e318496c63527594c856fdaa1cd645ec7e");
        AssertTraceCases(
            "insert-burst-two-two",
            expectedHash:
                "67ffc1d30452ac97ac39bb6a498208492987abb6fcb17d97f982887aea041571");

        IReadOnlyDictionary<uint, LogicalObjectState> threeFinal =
            WorkloadReplayer.Replay(ThreeOne);
        IReadOnlyDictionary<uint, LogicalObjectState> twoFinal =
            WorkloadReplayer.Replay(TwoTwo);
        Assert.Equal(threeFinal, twoFinal);
        Assert.Equal([10U, 100U, 101U, 102U, 103U], threeFinal.Keys.Order());
        Assert.Equal(2, threeFinal[10].LogicalVersionOrdinal);
        Assert.Equal(10, threeFinal[10].BasePayloadBytes);
        Assert.All(new[] { 100U, 101U, 102U, 103U }, objectId => {
            Assert.Equal(1, threeFinal[objectId].LogicalVersionOrdinal);
            Assert.Equal(300, threeFinal[objectId].BasePayloadBytes);
        });
    }

    [Fact]
    public void Pair_keeps_each_profile_selection_and_cadence_equal() {
        foreach (StrategyBindingV1 strategy in BenchmarkV1Baselines.All) {
            CapturedTrace three = Capture(ThreeOne, strategy);
            CapturedTrace two = Capture(TwoTwo, strategy);
            bool noMigration = strategy.Identity == BenchmarkV1Baselines
                .DebtZeroThenRotateDeltaNoMigration.Identity;
            bool paced = strategy.Identity == BenchmarkV1Baselines
                .DebtZeroThenRotateDeltaPacedOneDebtByObjectId.Identity;

            Assert.Equal(three.Views[0], two.Views[0]);
            Assert.Equal(three.Selections, two.Selections);
            Assert.Equal(
                noMigration
                    ? [(10L, 10L), (910L, 10L), (1210L, 10L)]
                    : paced
                        ? [(10L, 10L), (910L, 10L), (1210L, 0L)]
                        : [(10L, 10L), (910L, 0L), (1210L, 10L)],
                three.Views.Select(static view => (view.G, view.E)));
            Assert.Equal(
                noMigration
                    ? [(10L, 10L), (610L, 10L), (1210L, 10L)]
                    : paced
                        ? [(10L, 10L), (610L, 10L), (1210L, 0L)]
                        : [(10L, 10L), (610L, 0L), (1210L, 10L)],
                two.Views.Select(static view => (view.G, view.E)));
            Assert.Equal(
                noMigration
                    ? [
                        StrategyTargetV1.StayB,
                        StrategyTargetV1.StayB,
                        StrategyTargetV1.StayB,
                    ]
                    : paced
                        ? [
                            StrategyTargetV1.StayB,
                            StrategyTargetV1.StayB,
                            StrategyTargetV1.RotateC,
                        ]
                        : [
                            StrategyTargetV1.StayB,
                            StrategyTargetV1.RotateC,
                            StrategyTargetV1.RotateC,
                        ],
                three.Targets);
            Assert.Equal(three.Targets, two.Targets);
            Assert.Equal(
                noMigration
                    ? ["[]", "[]", "[]"]
                    : paced
                        ? ["[]", "[10]", "[]"]
                        : ["[]", "[]", "[10]"],
                three.StayMigrationDiagnostics);
            Assert.Equal(
                three.StayMigrationDiagnostics,
                two.StayMigrationDiagnostics);

            BurstRun threeRun = Run(ThreeOneCaseId(strategy));
            BurstRun twoRun = Run(TwoTwoCaseId(strategy));
            Assert.Equal(
                three.Targets,
                threeRun.Product.WorkloadCommits.Select(static receipt =>
                    receipt.SelectedTarget));
            Assert.Equal(
                two.Targets,
                twoRun.Product.WorkloadCommits.Select(static receipt =>
                    receipt.SelectedTarget));
            AssertAllWorkloadFramesSmall(threeRun.Product);
            AssertAllWorkloadFramesSmall(twoRun.Product);

            if (!noMigration && !paced) {
                // The final [10] belongs to the unselected Stay alternative.
                Assert.Equal(StrategyTargetV1.RotateC,
                    threeRun.Product.WorkloadCommits[2].SelectedTarget);
                Assert.Equal("[10]", three.StayMigrationDiagnostics[2]);
            }
        }
    }

    [Fact]
    public void Three_one_partition_exposes_bounded_peak_write_cost() {
        BurstRun noMigrationThree = AssertRun(
            BenchmarkV1Corpus.InsertBurstThreeOneNoMigrationCaseId,
            new RawVector(4, 1432, 960, 1400, 1360),
            2,
            3);
        BurstRun noMigrationTwo = AssertRun(
            BenchmarkV1Corpus.InsertBurstTwoTwoNoMigrationCaseId,
            new RawVector(4, 1432, 652, 1400, 1360),
            2,
            3);
        Assert.Equal(
            noMigrationThree.Vector with {
                PeakCommitWriteBytes = noMigrationTwo.Vector.PeakCommitWriteBytes,
            },
            noMigrationTwo.Vector);
        Assert.Equal(
            noMigrationTwo.Vector.PeakCommitWriteBytes + 308,
            noMigrationThree.Vector.PeakCommitWriteBytes);

        BurstRun pacedThree = AssertRun(
            BenchmarkV1Corpus.InsertBurstThreeOnePacedCaseId,
            new RawVector(4, 2380, 984, 1072, 1332),
            3,
            4);
        BurstRun pacedTwo = AssertRun(
            BenchmarkV1Corpus.InsertBurstTwoTwoPacedCaseId,
            new RawVector(4, 2072, 680, 764, 1332),
            3,
            4);
        AssertDelta(pacedTwo.Vector, pacedThree.Vector, 308, 304, 308, 0);

        BurstRun adaptive35Three = AssertRun(
            BenchmarkV1Corpus.InsertBurstThreeOneAdaptiveR3B5PercentCaseId,
            new RawVector(4, 2368, 972, 972, 1332),
            4,
            5);
        BurstRun adaptive35Two = AssertRun(
            BenchmarkV1Corpus.InsertBurstTwoTwoAdaptiveR3B5PercentCaseId,
            new RawVector(4, 2060, 680, 680, 1332),
            4,
            5);
        BurstRun adaptive44Three = AssertRun(
            BenchmarkV1Corpus.InsertBurstThreeOneAdaptiveR4B4PercentCaseId,
            adaptive35Three.Vector,
            4,
            5);
        BurstRun adaptive44Two = AssertRun(
            BenchmarkV1Corpus.InsertBurstTwoTwoAdaptiveR4B4PercentCaseId,
            adaptive35Two.Vector,
            4,
            5);
        Assert.Equal(adaptive35Three.Vector, adaptive44Three.Vector);
        Assert.Equal(adaptive35Two.Vector, adaptive44Two.Vector);
        AssertDelta(
            adaptive35Two.Vector,
            adaptive35Three.Vector,
            308,
            292,
            292,
            0);

        AssertAllFinalStatesEqual([
            noMigrationThree,
            noMigrationTwo,
            pacedThree,
            pacedTwo,
            adaptive35Three,
            adaptive35Two,
            adaptive44Three,
            adaptive44Two,
        ]);
    }

    private static CapturedTrace Capture(
        WorkloadTrace trace,
        StrategyBindingV1 strategy) {
        BenchmarkV1BootstrappedSource source = BenchmarkV1SourceBootstrap.Create(trace);
        StrategyRunContextV1 context = new(
            source.Store,
            source.Cursor,
            trace,
            firstWorkloadTraceStepIndex: 1,
            totalWorkloadStepCount: 3);
        List<ViewSnapshot> views = [];
        List<string> selections = [];
        List<StrategyTargetV1> targets = [];
        List<string> migrations = [];
        while (context.CurrentStep is StrategyStepViewV1 view) {
            StrategySelectionV1 selection = BenchmarkV1Baselines.Select(
                strategy.Identity,
                view);
            views.Add(ViewSnapshot.Create(view));
            selections.Add(ProjectSelection(selection));
            targets.Add(selection.Target);
            migrations.Add(
                $"[{string.Join(',', selection.Stay.UnchangedMigrationObjectIds)}]");
            StrategyCommitStatusV1 status = context.Commit(selection);
            Assert.True(status is StrategyCommitStatusV1.AppliedStayB or
                StrategyCommitStatusV1.AppliedRotateC);
        }

        return new CapturedTrace(views, selections, targets, migrations);
    }

    private static string ProjectSelection(StrategySelectionV1 selection) =>
        $"{selection.Target}:" +
        string.Join(',', selection.Stay.UpdateDecisions) + ":" +
        string.Join(',', selection.Stay.UnchangedMigrationObjectIds) + ":" +
        string.Join(',', selection.Rotate.BContainedUpdateDecisions) + ":" +
        string.Join(',', selection.Rotate.BContainedNoChangeBaseObjectIds);

    private static BurstRun AssertRun(
        string caseId,
        RawVector expected,
        uint previousFileNumber,
        uint currentFileNumber) {
        BurstRun run = Run(caseId);
        Assert.Equal(expected, run.Vector);
        Assert.Equal(EvaluatorRunPhase.TerminalSettlement, run.Admitted.Position.Phase);
        Assert.Equal(3, run.Admitted.Position.CompletedWorkloadStepCount);
        Assert.Equal(3, run.Admitted.Position.TotalWorkloadStepCount);
        Assert.Equal(previousFileNumber,
            run.Admitted.FinalCursor.FileScope.PreviousFileNumber);
        Assert.Equal(currentFileNumber,
            run.Admitted.FinalCursor.FileScope.CurrentFileNumber);
        Assert.Empty(run.Admitted.Settlement.MigratedObjectIds);
        Assert.Equal(0, run.Admitted.Settlement.MaintenanceRevisionCount);
        Assert.Equal(1, run.Admitted.Settlement.RealizedRevisionCount);
        AssertAllWorkloadFramesSmall(run.Product);
        return run;
    }

    private static BurstRun Run(string caseId) {
        BenchmarkV1CaseDefinition definition = Corpus.Cases.Single(
            benchmarkCase => benchmarkCase.ManifestCase.CaseId == caseId);
        BenchmarkV1CaseExecution execution = BenchmarkV1Runner.ExecuteCase(definition);
        AdmittedEvaluatorRun admitted = Assert.IsType<AdmittedEvaluatorRun>(
            execution.Outcome);
        Assert.Equal(StrategyRunTerminationV1.Admitted, execution.Product.Termination);
        IReadOnlyDictionary<uint, LogicalObjectState> physical =
            PhysicalStateOracle.Materialize(
                execution.Store,
                ObjectVersionDictionaryReader.MaterializeLive(
                    execution.Store,
                    admitted.FinalCursor.PublishedRevisionAddress).Bindings);
        Assert.Equal(WorkloadReplayer.Replay(definition.Trace), physical);
        return new BurstRun(
            execution.Product,
            admitted,
            physical,
            new RawVector(
                admitted.Metrics.RealizedCommitCount,
                admitted.Metrics.TotalPhysicalWriteBytes,
                admitted.Metrics.PeakCommitWriteBytes,
                admitted.Metrics.MaxCurrentFileTailBytes,
                admitted.Metrics.FinalColdHeadReadBytes));
    }

    private static void AssertAllWorkloadFramesSmall(StrategyRunProductV1 product) {
        int maximum = product.WorkloadCommits.Max(static receipt =>
            receipt.Result.PublishedRevisionLengthBytes);
        Assert.True(maximum < 2048);
        Assert.True(maximum < RbfV040Layout.MaxPayloadAndTailMetaLengthBytes);
    }

    private static void AssertDelta(
        RawVector smaller,
        RawVector larger,
        long totalPhysicalWriteBytes,
        long peakCommitWriteBytes,
        long maxCurrentFileTailBytes,
        long finalColdHeadReadBytes) {
        Assert.Equal(
            totalPhysicalWriteBytes,
            larger.TotalPhysicalWriteBytes - smaller.TotalPhysicalWriteBytes);
        Assert.Equal(
            peakCommitWriteBytes,
            larger.PeakCommitWriteBytes - smaller.PeakCommitWriteBytes);
        Assert.Equal(
            maxCurrentFileTailBytes,
            larger.MaxCurrentFileTailBytes - smaller.MaxCurrentFileTailBytes);
        Assert.Equal(
            finalColdHeadReadBytes,
            larger.FinalColdHeadReadBytes - smaller.FinalColdHeadReadBytes);
    }

    private static void AssertAllFinalStatesEqual(IReadOnlyList<BurstRun> runs) {
        Assert.NotEmpty(runs);
        Assert.All(runs.Skip(1), run =>
            Assert.Equal(runs[0].PhysicalState, run.PhysicalState));
    }

    private static string[] OperationMultiset(WorkloadTrace trace) => trace.Steps
        .Skip(1)
        .SelectMany(static step => step.Changes)
        .Select(static change => change switch {
            UpdateObject update =>
                $"U:{update.ObjectId}:{update.ResultBasePayloadBytes}:" +
                $"{update.DeltaPayloadBytes}",
            CreateObject create => $"C:{create.ObjectId}:{create.BasePayloadBytes}",
            _ => throw new InvalidDataException(
                $"Unexpected burst operation '{change.GetType().Name}'."),
        })
        .Order(StringComparer.Ordinal)
        .ToArray();

    private static string ThreeOneCaseId(StrategyBindingV1 strategy) =>
        SelectCaseId(
            strategy,
            BenchmarkV1Corpus.InsertBurstThreeOneNoMigrationCaseId,
            BenchmarkV1Corpus.InsertBurstThreeOnePacedCaseId,
            BenchmarkV1Corpus.InsertBurstThreeOneAdaptiveR3B5PercentCaseId,
            BenchmarkV1Corpus.InsertBurstThreeOneAdaptiveR4B4PercentCaseId);

    private static string TwoTwoCaseId(StrategyBindingV1 strategy) =>
        SelectCaseId(
            strategy,
            BenchmarkV1Corpus.InsertBurstTwoTwoNoMigrationCaseId,
            BenchmarkV1Corpus.InsertBurstTwoTwoPacedCaseId,
            BenchmarkV1Corpus.InsertBurstTwoTwoAdaptiveR3B5PercentCaseId,
            BenchmarkV1Corpus.InsertBurstTwoTwoAdaptiveR4B4PercentCaseId);

    private static string SelectCaseId(
        StrategyBindingV1 strategy,
        string noMigration,
        string paced,
        string adaptive35,
        string adaptive44) => strategy.Identity == BenchmarkV1Baselines
            .DebtZeroThenRotateDeltaNoMigration.Identity
            ? noMigration
            : strategy.Identity == BenchmarkV1Baselines
                .DebtZeroThenRotateDeltaPacedOneDebtByObjectId.Identity
                ? paced
                : strategy.Identity == BenchmarkV1Baselines
                    .ReadAmplificationBaseBudgetR3B5Percent.Identity
                    ? adaptive35
                    : adaptive44;

    private static WorkloadTrace FindTrace(string traceId) {
        BenchmarkV1CaseDefinition[] cases = Corpus.Cases
            .Where(benchmarkCase =>
                benchmarkCase.ManifestCase.TraceDefinition.Id == traceId)
            .ToArray();
        Assert.Equal(4, cases.Length);
        Assert.All(cases.Skip(1), benchmarkCase =>
            Assert.Same(cases[0].Trace, benchmarkCase.Trace));
        return cases[0].Trace;
    }

    private static void AssertTraceCases(string traceId, string expectedHash) {
        BenchmarkV1CaseDefinition[] cases = Corpus.Cases
            .Where(benchmarkCase =>
                benchmarkCase.ManifestCase.TraceDefinition.Id == traceId)
            .ToArray();
        Assert.Equal(4, cases.Length);
        Assert.All(cases, benchmarkCase => {
            Assert.Equal(3, benchmarkCase.ManifestCase.EvaluatedWorkloadStepCount);
            Assert.Equal(expectedHash, benchmarkCase.ManifestCase.ResolvedTraceSha256);
        });
    }

    private sealed record CapturedTrace(
        IReadOnlyList<ViewSnapshot> Views,
        IReadOnlyList<string> Selections,
        IReadOnlyList<StrategyTargetV1> Targets,
        IReadOnlyList<string> StayMigrationDiagnostics);

    private sealed record BurstRun(
        StrategyRunProductV1 Product,
        AdmittedEvaluatorRun Admitted,
        IReadOnlyDictionary<uint, LogicalObjectState> PhysicalState,
        RawVector Vector);

    private sealed record ViewSnapshot(long G, long E, string Objects) {
        public static ViewSnapshot Create(StrategyStepViewV1 view) => new(
            view.PostLiveGraphBasePayloadBytes,
            view.ADependentEvacuationBasePayloadBytes,
            string.Join(';', view.Objects.Select(fact =>
                $"{fact.ObjectId}/{fact.Kind}/{fact.SourceIsPreviousDependent}/" +
                $"{fact.SourceHeadReconstructionPayloadBytes}/" +
                $"{fact.ResultBasePayloadBytes}/{fact.DeltaPayloadBytes}")));
    }

    private readonly record struct RawVector(
        int RealizedCommitCount,
        long TotalPhysicalWriteBytes,
        long PeakCommitWriteBytes,
        long MaxCurrentFileTailBytes,
        long FinalColdHeadReadBytes);
}
