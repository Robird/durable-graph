using System.Reflection;
using Atelia.TwoLegRotationProbe.Arena;
using Atelia.TwoLegRotationProbe.Baselines;
using Atelia.TwoLegRotationProbe.Benchmarking;
using Atelia.TwoLegRotationProbe.Evaluation;
using Atelia.TwoLegRotationProbe.Model;
using Atelia.TwoLegRotationProbe.Simulation;
using Atelia.TwoLegRotationProbe.Workloads;

namespace Atelia.TwoLegRotationProbe.Tests;

public sealed class BenchmarkV1HorizonPhaseWorkloadTests {
    private static readonly BenchmarkV1BatchDefinition Corpus =
        BenchmarkV1Corpus.Create(BenchmarkV1Baselines.All);

    private static readonly WorkloadTrace Short = FindTrace(
        "debt-zero-before-rotate");

    private static readonly WorkloadTrace Long = FindTrace(
        "debt-zero-then-rotate");

    [Fact]
    public void Short_horizon_is_the_exact_long_prefix_and_horizon_is_not_public() {
        Assert.Equal(4, Short.Steps.Count);
        Assert.Equal(5, Long.Steps.Count);
        Assert.All(Enumerable.Range(0, Short.Steps.Count), index =>
            Assert.Same(Short.Steps[index], Long.Steps[index]));
        AssertTraceCases(
            "debt-zero-before-rotate",
            evaluatedWorkloadStepCount: 3,
            expectedHash:
                "fcc62106479d509ea7716e20b4030e07d60436c8804a0c97c42562ee08d0f7ef");
        AssertTraceCases(
            "debt-zero-then-rotate",
            evaluatedWorkloadStepCount: 4,
            expectedHash:
                "cd064c19d4cd94f0a25536481fa0c901ca77e7187b0ff9350b95a2b804d286e2");

        AssertHorizonIsNotPublic();

        foreach (StrategyBindingV1 strategy in BenchmarkV1Baselines.All) {
            CapturedPrefix shortPrefix = Capture(Short, strategy);
            CapturedPrefix longPrefix = Capture(Long, strategy);
            Assert.Equal(shortPrefix.Views, longPrefix.Views.Take(3));
            Assert.Equal(shortPrefix.Selections, longPrefix.Selections.Take(3));
        }
    }

    [Fact]
    public void Short_horizon_admits_and_settles_before_first_natural_rotate() {
        foreach (StrategyBindingV1 strategy in BenchmarkV1Baselines.All) {
            bool noMigration = strategy.Identity == BenchmarkV1Baselines
                .DebtZeroThenRotateDeltaNoMigration.Identity;
            string caseId = ShortCaseId(strategy);
            HorizonRun run = Run(caseId);
            RawVector expected = noMigration
                ? new RawVector(4, 808, 676, 676, 788)
                : new RawVector(4, 832, 356, 804, 812);

            Assert.Equal(expected, run.Vector);
            Assert.Equal(
                [
                    StrategyTargetV1.StayB,
                    StrategyTargetV1.StayB,
                    StrategyTargetV1.StayB,
                ],
                run.Product.WorkloadCommits.Select(static receipt =>
                    receipt.SelectedTarget));
            CapturedPrefix captured = Capture(Short, strategy);
            Assert.Equal(
                noMigration ? ["[]", "[]", "[]"] : ["[10]", "[20]", "[30]"],
                captured.Migrations);
            Assert.Equal(2U, run.Admitted.FinalCursor.FileScope.PreviousFileNumber);
            Assert.Equal(3U, run.Admitted.FinalCursor.FileScope.CurrentFileNumber);
            Assert.Equal(EvaluatorRunPhase.TerminalSettlement, run.Admitted.Position.Phase);
            Assert.Empty(run.Admitted.Settlement.MigratedObjectIds);
            Assert.Equal(0, run.Admitted.Settlement.MaintenanceRevisionCount);
            Assert.Equal(1, run.Admitted.Settlement.RealizedRevisionCount);
            Assert.Equal(3, run.Admitted.Position.CompletedWorkloadStepCount);
            Assert.Equal(3, run.Admitted.Position.TotalWorkloadStepCount);
            Assert.Equal(
                WorkloadReplayer.Replay(Short),
                run.PhysicalState);
        }
    }

    [Fact]
    public void One_more_save_crosses_the_rotation_phase_without_cross_horizon_ranking() {
        foreach (StrategyBindingV1 strategy in BenchmarkV1Baselines.All) {
            bool noMigration = strategy.Identity == BenchmarkV1Baselines
                .DebtZeroThenRotateDeltaNoMigration.Identity;
            HorizonRun shortRun = Run(ShortCaseId(strategy));
            HorizonRun longRun = Run(LongCaseId(strategy));
            Assert.Empty(longRun.Admitted.Settlement.MigratedObjectIds);
            Assert.Equal(0, longRun.Admitted.Settlement.MaintenanceRevisionCount);
            Assert.Equal(1, longRun.Admitted.Settlement.RealizedRevisionCount);

            if (noMigration) {
                AssertDelta(shortRun.Vector, longRun.Vector, 1, 48, 4, 4, 44);
                Assert.Equal(
                    [
                        StrategyTargetV1.StayB,
                        StrategyTargetV1.StayB,
                        StrategyTargetV1.StayB,
                        StrategyTargetV1.StayB,
                    ],
                    longRun.Product.WorkloadCommits.Select(static receipt =>
                        receipt.SelectedTarget));
                Assert.Equal([44L, 44L, 44L, 676L], CommitWrites(Short, shortRun));
                Assert.Equal(
                    [44L, 44L, 44L, 44L, 680L],
                    CommitWrites(Long, longRun));
                Assert.Equal(
                    shortRun.Admitted.FinalCursor.FileScope.PreviousFileNumber,
                    longRun.Admitted.FinalCursor.FileScope.PreviousFileNumber);
                Assert.Equal(
                    shortRun.Admitted.FinalCursor.FileScope.CurrentFileNumber,
                    longRun.Admitted.FinalCursor.FileScope.CurrentFileNumber);
            } else {
                AssertDelta(shortRun.Vector, longRun.Vector, 1, 704, 340, 0, -56);
                Assert.Equal(
                    [
                        StrategyTargetV1.StayB,
                        StrategyTargetV1.StayB,
                        StrategyTargetV1.StayB,
                        StrategyTargetV1.RotateC,
                    ],
                    longRun.Product.WorkloadCommits.Select(static receipt =>
                        receipt.SelectedTarget));
                Assert.Equal(
                    [152L, 256L, 356L, 68L],
                    CommitWrites(Short, shortRun));
                Assert.Equal(
                    [152L, 256L, 356L, 76L, 696L],
                    CommitWrites(Long, longRun));
                Assert.Equal(2U,
                    longRun.Product.WorkloadCommits[^1].Result.PreviousFileNumber);
                Assert.Equal(3U,
                    longRun.Product.WorkloadCommits[^1].Result.CurrentFileNumber);
                Assert.Equal(3U,
                    longRun.Admitted.FinalCursor.FileScope.PreviousFileNumber);
                Assert.Equal(4U,
                    longRun.Admitted.FinalCursor.FileScope.CurrentFileNumber);
            }
        }
    }

    private static CapturedPrefix Capture(
        WorkloadTrace trace,
        StrategyBindingV1 strategy) {
        BenchmarkV1BootstrappedSource source = BenchmarkV1SourceBootstrap.Create(trace);
        StrategyRunContextV1 context = new(
            source.Store,
            source.Cursor,
            trace,
            firstWorkloadTraceStepIndex: 1,
            totalWorkloadStepCount: trace.Steps.Count - 1);
        List<string> views = [];
        List<string> selections = [];
        List<string> migrations = [];
        while (context.CurrentStep is StrategyStepViewV1 view) {
            StrategySelectionV1 selection = BenchmarkV1Baselines.Select(
                strategy.Identity,
                view);
            views.Add(ProjectView(view));
            selections.Add(ProjectSelection(selection));
            migrations.Add(
                $"[{string.Join(',', selection.Stay.UnchangedMigrationObjectIds)}]");
            StrategyCommitStatusV1 status = context.Commit(selection);
            Assert.True(status is StrategyCommitStatusV1.AppliedStayB or
                StrategyCommitStatusV1.AppliedRotateC);
        }

        return new CapturedPrefix(views, selections, migrations);
    }

    private static void AssertHorizonIsNotPublic() {
        const BindingFlags declaredPublicInstance =
            BindingFlags.Instance |
            BindingFlags.Public |
            BindingFlags.DeclaredOnly;
        string[] forbiddenNameFragments = [
            "Horizon",
            "Remaining",
            "StepCount",
            "StepIndex",
            "Trace",
        ];
        MemberInfo[] members = typeof(StrategyRunContextV1)
            .GetMembers(declaredPublicInstance);

        Assert.DoesNotContain(members, member => forbiddenNameFragments.Any(
            fragment => member.Name.Contains(
                fragment,
                StringComparison.OrdinalIgnoreCase)));
        Assert.DoesNotContain(
            members.OfType<PropertyInfo>(),
            static property => property.PropertyType == typeof(WorkloadTrace));
        Assert.DoesNotContain(
            members.OfType<MethodInfo>(),
            static method => method.ReturnType == typeof(WorkloadTrace) ||
                method.GetParameters().Any(parameter =>
                    parameter.ParameterType == typeof(WorkloadTrace)));
    }

    private static string ProjectView(StrategyStepViewV1 view) =>
        $"G{view.PostLiveGraphBasePayloadBytes}:" +
        $"E{view.ADependentEvacuationBasePayloadBytes}:" +
        $"D{view.HasParentPreviousDebt}:" +
        string.Join(';', view.Objects.Select(fact =>
            $"{fact.ObjectId}/{fact.Kind}/{fact.SourceIsPreviousDependent}/" +
            $"{fact.SourceHeadReconstructionPayloadBytes}/" +
            $"{fact.ResultBasePayloadBytes}/{fact.DeltaPayloadBytes}"));

    private static string ProjectSelection(StrategySelectionV1 selection) =>
        $"{selection.Target}:" +
        string.Join(',', selection.Stay.UpdateDecisions) + ":" +
        string.Join(',', selection.Stay.UnchangedMigrationObjectIds) + ":" +
        string.Join(',', selection.Rotate.BContainedUpdateDecisions) + ":" +
        string.Join(',', selection.Rotate.BContainedNoChangeBaseObjectIds);

    private static HorizonRun Run(string caseId) {
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
        return new HorizonRun(
            execution.Product,
            admitted,
            physical,
            new RawVector(
                admitted.Metrics.RealizedCommitCount,
                admitted.Metrics.TotalPhysicalWriteBytes,
                admitted.Metrics.PeakCommitWriteBytes,
                admitted.Metrics.MaxCurrentFileTailBytes,
                admitted.Metrics.TerminalColdHeadReadBytes));
    }

    private static long[] CommitWrites(WorkloadTrace trace, HorizonRun run) {
        BenchmarkV1BootstrappedSource source = BenchmarkV1SourceBootstrap.Create(trace);
        uint currentFile = source.Cursor.FileScope.CurrentFileNumber;
        long currentTail = source.Cursor.CurrentFileTailOffsetBytes;
        List<long> writes = [];
        foreach (StrategyCommitReceiptV1 receipt in run.Product.WorkloadCommits) {
            StrategyRevisionCheckpointV1 checkpoint = receipt.Result;
            writes.Add(checkpoint.CurrentFileNumber == currentFile
                ? checkpoint.CurrentFileTailOffsetBytes - currentTail
                : checkpoint.CurrentFileTailOffsetBytes);
            currentFile = checkpoint.CurrentFileNumber;
            currentTail = checkpoint.CurrentFileTailOffsetBytes;
        }

        StrategyRevisionCheckpointV1 final = run.Product.FinalCheckpoint;
        writes.Add(final.CurrentFileNumber == currentFile
            ? final.CurrentFileTailOffsetBytes - currentTail
            : final.CurrentFileTailOffsetBytes);
        return writes.ToArray();
    }

    private static void AssertDelta(
        RawVector shorter,
        RawVector longer,
        int realizedCommitCount,
        long totalPhysicalWriteBytes,
        long peakCommitWriteBytes,
        long maxCurrentFileTailBytes,
        long finalColdHeadReadBytes) {
        Assert.Equal(
            realizedCommitCount,
            longer.RealizedCommitCount - shorter.RealizedCommitCount);
        Assert.Equal(
            totalPhysicalWriteBytes,
            longer.TotalPhysicalWriteBytes - shorter.TotalPhysicalWriteBytes);
        Assert.Equal(
            peakCommitWriteBytes,
            longer.PeakCommitWriteBytes - shorter.PeakCommitWriteBytes);
        Assert.Equal(
            maxCurrentFileTailBytes,
            longer.MaxCurrentFileTailBytes - shorter.MaxCurrentFileTailBytes);
        Assert.Equal(
            finalColdHeadReadBytes,
            longer.FinalColdHeadReadBytes - shorter.FinalColdHeadReadBytes);
    }

    private static string ShortCaseId(StrategyBindingV1 strategy) =>
        strategy.Identity == BenchmarkV1Baselines
            .DebtZeroThenRotateDeltaNoMigration.Identity
            ? BenchmarkV1Corpus.DebtZeroBeforeRotateNoMigrationCaseId
            : strategy.Identity == BenchmarkV1Baselines
                .DebtZeroThenRotateDeltaPacedOneDebtByObjectId.Identity
                ? BenchmarkV1Corpus.DebtZeroBeforeRotatePacedCaseId
                : strategy.Identity == BenchmarkV1Baselines
                    .ReadAmplificationBaseBudgetR3B5Percent.Identity
                    ? BenchmarkV1Corpus
                        .DebtZeroBeforeRotateAdaptiveR3B5PercentCaseId
                    : BenchmarkV1Corpus
                        .DebtZeroBeforeRotateAdaptiveR4B4PercentCaseId;

    private static string LongCaseId(StrategyBindingV1 strategy) =>
        strategy.Identity == BenchmarkV1Baselines
            .DebtZeroThenRotateDeltaNoMigration.Identity
            ? BenchmarkV1Corpus.DebtZeroThenRotateNoMigrationCaseId
            : strategy.Identity == BenchmarkV1Baselines
                .DebtZeroThenRotateDeltaPacedOneDebtByObjectId.Identity
                ? BenchmarkV1Corpus.DebtZeroThenRotatePacedCaseId
                : strategy.Identity == BenchmarkV1Baselines
                    .ReadAmplificationBaseBudgetR3B5Percent.Identity
                    ? BenchmarkV1Corpus.DebtZeroThenRotateAdaptiveR3B5PercentCaseId
                    : BenchmarkV1Corpus.DebtZeroThenRotateAdaptiveR4B4PercentCaseId;

    private static WorkloadTrace FindTrace(string traceId) {
        BenchmarkV1CaseDefinition[] cases = Corpus.Cases
            .Where(benchmarkCase =>
                benchmarkCase.ManifestCase.TraceDefinition.Id == traceId)
            .ToArray();
        Assert.Equal(BenchmarkV1Baselines.All.Count, cases.Length);
        Assert.All(cases.Skip(1), benchmarkCase =>
            Assert.Same(cases[0].Trace, benchmarkCase.Trace));
        return cases[0].Trace;
    }

    private static void AssertTraceCases(
        string traceId,
        int evaluatedWorkloadStepCount,
        string expectedHash) {
        BenchmarkV1CaseDefinition[] cases = Corpus.Cases
            .Where(benchmarkCase =>
                benchmarkCase.ManifestCase.TraceDefinition.Id == traceId)
            .ToArray();
        Assert.Equal(4, cases.Length);
        Assert.All(cases, benchmarkCase => {
            Assert.Equal(
                evaluatedWorkloadStepCount,
                benchmarkCase.ManifestCase.EvaluatedWorkloadStepCount);
            Assert.Equal(expectedHash, benchmarkCase.ManifestCase.ResolvedTraceSha256);
        });
    }

    private sealed record CapturedPrefix(
        IReadOnlyList<string> Views,
        IReadOnlyList<string> Selections,
        IReadOnlyList<string> Migrations);

    private sealed record HorizonRun(
        StrategyRunProductV1 Product,
        AdmittedEvaluatorRun Admitted,
        IReadOnlyDictionary<uint, LogicalObjectState> PhysicalState,
        RawVector Vector);

    private readonly record struct RawVector(
        int RealizedCommitCount,
        long TotalPhysicalWriteBytes,
        long PeakCommitWriteBytes,
        long MaxCurrentFileTailBytes,
        long FinalColdHeadReadBytes);
}
