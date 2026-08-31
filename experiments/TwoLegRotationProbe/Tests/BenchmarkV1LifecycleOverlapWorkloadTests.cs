using Atelia.TwoLegRotationProbe.Arena;
using Atelia.TwoLegRotationProbe.Baselines;
using Atelia.TwoLegRotationProbe.Benchmarking;
using Atelia.TwoLegRotationProbe.Evaluation;
using Atelia.TwoLegRotationProbe.Model;
using Atelia.TwoLegRotationProbe.Simulation;
using Atelia.TwoLegRotationProbe.Workloads;

namespace Atelia.TwoLegRotationProbe.Tests;

public sealed class BenchmarkV1LifecycleOverlapWorkloadTests {
    private static readonly BenchmarkV1BatchDefinition Corpus =
        BenchmarkV1Corpus.Create(BenchmarkV1Baselines.All);

    private static readonly WorkloadTrace Overlap = FindTrace(
        "lifecycle-transient-overlap");

    private static readonly WorkloadTrace Serial = FindTrace(
        "lifecycle-transient-serial");

    [Fact]
    public void Matched_pair_reorders_one_shared_transient_lifecycle_multiset() {
        Assert.Equal(5, Overlap.Steps.Count);
        Assert.Equal(5, Serial.Steps.Count);
        Assert.Same(Overlap.Steps[0], Serial.Steps[0]);
        Assert.Same(Overlap.Steps[1], Serial.Steps[1]);
        Assert.Same(Overlap.Steps[2], Serial.Steps[3]);
        Assert.Same(Overlap.Steps[3], Serial.Steps[2]);
        Assert.Same(Overlap.Steps[4], Serial.Steps[4]);
        Assert.Equal(
            OperationMultiset(Overlap),
            OperationMultiset(Serial));
        Assert.Equal(2, PeakTransientLiveCount(Overlap));
        Assert.Equal(1, PeakTransientLiveCount(Serial));

        AssertTraceCases(
            "lifecycle-transient-overlap",
            expectedHash:
                "eea0f08a47c5fcb466bbbad0a66d3fbe6411ef5fecb49e4e12777d7e51c5ee43");
        AssertTraceCases(
            "lifecycle-transient-serial",
            expectedHash:
                "637764ae11d12e1f03ebb51418498d4a58f4240dd06ee6d7d1c85e4a118e462c");

        BenchmarkV1BootstrappedSource overlapSource =
            BenchmarkV1SourceBootstrap.Create(Overlap);
        BenchmarkV1BootstrappedSource serialSource =
            BenchmarkV1SourceBootstrap.Create(Serial);
        Assert.Equal(
            overlapSource.PreviousRevisionAddress,
            serialSource.PreviousRevisionAddress);
        Assert.Equal(
            overlapSource.Cursor.FileScope.PreviousFileNumber,
            serialSource.Cursor.FileScope.PreviousFileNumber);
        Assert.Equal(
            overlapSource.Cursor.FileScope.CurrentFileNumber,
            serialSource.Cursor.FileScope.CurrentFileNumber);
        Assert.Equal(
            overlapSource.Cursor.PublishedRevisionAddress,
            serialSource.Cursor.PublishedRevisionAddress);
        Assert.Equal(
            overlapSource.Cursor.CurrentFileTailOffsetBytes,
            serialSource.Cursor.CurrentFileTailOffsetBytes);
        Assert.Equal(
            FileTails(overlapSource.Store),
            FileTails(serialSource.Store));
        Assert.Equal(
            overlapSource.Store.ReadLayout(overlapSource.PreviousRevisionAddress),
            serialSource.Store.ReadLayout(serialSource.PreviousRevisionAddress));
        Assert.Equal(
            overlapSource.Store.ReadLayout(
                overlapSource.Cursor.PublishedRevisionAddress),
            serialSource.Store.ReadLayout(
                serialSource.Cursor.PublishedRevisionAddress));
    }

    [Fact]
    public void Every_baseline_keeps_one_cadence_across_the_matched_pair() {
        foreach (StrategyBindingV1 strategy in BenchmarkV1Baselines.All) {
            SelectionTrace overlap = CaptureSelections(Overlap, strategy);
            SelectionTrace serial = CaptureSelections(Serial, strategy);
            bool noMigration = strategy.Identity == BenchmarkV1Baselines
                .DebtZeroThenRotateDeltaNoMigration.Identity;
            StrategyTargetV1[] expectedTargets = noMigration
                ? [
                    StrategyTargetV1.StayB,
                    StrategyTargetV1.StayB,
                    StrategyTargetV1.StayB,
                    StrategyTargetV1.StayB,
                ]
                : [
                    StrategyTargetV1.StayB,
                    StrategyTargetV1.StayB,
                    StrategyTargetV1.RotateC,
                    StrategyTargetV1.StayB,
                ];
            uint[][] expectedMigrations = noMigration
                ? [[], [], [], []]
                : [[10], [20], [], [10]];

            Assert.Equal(expectedTargets, overlap.Targets);
            Assert.Equal(expectedTargets, serial.Targets);
            Assert.Equal(expectedMigrations, overlap.Migrations);
            Assert.Equal(expectedMigrations, serial.Migrations);
        }
    }

    [Fact]
    public void Overlap_and_serial_freeze_lifecycle_cost_without_changing_final_state() {
        LifecycleRun noMigrationOverlap = Run(
            Overlap,
            BenchmarkV1Baselines.DebtZeroThenRotateDeltaNoMigration);
        LifecycleRun noMigrationSerial = Run(
            Serial,
            BenchmarkV1Baselines.DebtZeroThenRotateDeltaNoMigration);
        AssertRun(
            noMigrationOverlap,
            new RawVector(5, 1220, 444, 1008, 244),
            previousFileNumber: 2,
            currentFileNumber: 3);
        AssertRun(
            noMigrationSerial,
            new RawVector(5, 1220, 444, 1008, 244),
            previousFileNumber: 2,
            currentFileNumber: 3);
        Assert.Equal(noMigrationOverlap.Vector, noMigrationSerial.Vector);
        Assert.Equal(
            noMigrationOverlap.PhysicalState,
            noMigrationSerial.PhysicalState);

        List<LifecycleRun> pacedOverlap = [];
        List<LifecycleRun> pacedSerial = [];
        foreach (StrategyBindingV1 strategy in BenchmarkV1Baselines.All.Skip(1)) {
            LifecycleRun overlap = Run(Overlap, strategy);
            LifecycleRun serial = Run(Serial, strategy);
            AssertRun(
                overlap,
                new RawVector(5, 1452, 552, 1144, 284),
                previousFileNumber: 3,
                currentFileNumber: 4);
            AssertRun(
                serial,
                new RawVector(5, 1448, 552, 736, 284),
                previousFileNumber: 3,
                currentFileNumber: 4);
            Assert.Equal(
                overlap.Vector.TotalPhysicalWriteBytes,
                serial.Vector.TotalPhysicalWriteBytes + 4);
            Assert.Equal(
                overlap.Vector.PeakCommitWriteBytes,
                serial.Vector.PeakCommitWriteBytes);
            Assert.Equal(
                overlap.Vector.MaxCurrentFileTailBytes,
                serial.Vector.MaxCurrentFileTailBytes + 408);
            Assert.Equal(
                overlap.Vector.FinalColdHeadReadBytes,
                serial.Vector.FinalColdHeadReadBytes);
            Assert.Equal(overlap.PhysicalState, serial.PhysicalState);
            Assert.Equal(noMigrationOverlap.PhysicalState, overlap.PhysicalState);
            pacedOverlap.Add(overlap);
            pacedSerial.Add(serial);
        }

        Assert.All(pacedOverlap.Skip(1), run =>
            Assert.Equal(pacedOverlap[0].Vector, run.Vector));
        Assert.All(pacedSerial.Skip(1), run =>
            Assert.Equal(pacedSerial[0].Vector, run.Vector));

        // The 4-byte W difference is a current-layout consequence. The causal
        // lifecycle signal in this matched pair is the 408-byte F separation.
        AssertDominates(noMigrationOverlap.Vector, pacedOverlap[0].Vector);
        Assert.True(noMigrationSerial.Vector.TotalPhysicalWriteBytes <
            pacedSerial[0].Vector.TotalPhysicalWriteBytes);
        Assert.True(noMigrationSerial.Vector.PeakCommitWriteBytes <
            pacedSerial[0].Vector.PeakCommitWriteBytes);
        Assert.True(noMigrationSerial.Vector.MaxCurrentFileTailBytes >
            pacedSerial[0].Vector.MaxCurrentFileTailBytes);
        Assert.True(noMigrationSerial.Vector.FinalColdHeadReadBytes <
            pacedSerial[0].Vector.FinalColdHeadReadBytes);
    }

    private static SelectionTrace CaptureSelections(
        WorkloadTrace trace,
        StrategyBindingV1 strategy) {
        BenchmarkV1BootstrappedSource source = BenchmarkV1SourceBootstrap.Create(trace);
        StrategyRunContextV1 context = new(
            source.Store,
            source.Cursor,
            trace,
            firstWorkloadTraceStepIndex: 1,
            totalWorkloadStepCount: 4);
        List<StrategyTargetV1> targets = [];
        List<uint[]> migrations = [];
        while (context.CurrentStep is StrategyStepViewV1 view) {
            StrategySelectionV1 selection = BenchmarkV1Baselines.Select(
                strategy.Identity,
                view);
            Assert.Empty(selection.Stay.UpdateDecisions);
            Assert.Empty(selection.Rotate.BContainedUpdateDecisions);
            Assert.Empty(selection.Rotate.BContainedNoChangeBaseObjectIds);
            targets.Add(selection.Target);
            migrations.Add(selection.Stay.UnchangedMigrationObjectIds.ToArray());
            StrategyCommitStatusV1 status = context.Commit(selection);
            Assert.True(status is StrategyCommitStatusV1.AppliedStayB or
                StrategyCommitStatusV1.AppliedRotateC);
        }

        return new SelectionTrace(targets.ToArray(), migrations.ToArray());
    }

    private static LifecycleRun Run(
        WorkloadTrace trace,
        StrategyBindingV1 strategy) {
        BenchmarkV1CaseDefinition definition = Corpus.Cases.Single(
            benchmarkCase =>
                benchmarkCase.ManifestCase.TraceDefinition.Id ==
                    trace.ScenarioName &&
                benchmarkCase.ManifestCase.SelectionProfile == strategy.Identity);
        BenchmarkV1CaseExecution execution = BenchmarkV1Runner.ExecuteCase(definition);
        AdmittedEvaluatorRun admitted = Assert.IsType<AdmittedEvaluatorRun>(
            execution.Outcome);
        IReadOnlyDictionary<uint, LogicalObjectState> physical =
            PhysicalStateOracle.Materialize(
                execution.Store,
                ObjectVersionDictionaryReader.MaterializeLive(
                    execution.Store,
                    admitted.FinalCursor.PublishedRevisionAddress).Bindings);
        IReadOnlyDictionary<uint, LogicalObjectState> logical =
            WorkloadReplayer.Replay(trace);
        Assert.Equal([10U, 20U], physical.Keys.Order());
        Assert.Equal(logical, physical);
        return new LifecycleRun(
            execution,
            admitted,
            physical,
            new RawVector(
                admitted.Metrics.RealizedCommitCount,
                admitted.Metrics.TotalPhysicalWriteBytes,
                admitted.Metrics.PeakCommitWriteBytes,
                admitted.Metrics.MaxCurrentFileTailBytes,
                admitted.Metrics.FinalColdHeadReadBytes));
    }

    private static void AssertRun(
        LifecycleRun run,
        RawVector expected,
        uint previousFileNumber,
        uint currentFileNumber) {
        Assert.Equal(StrategyRunTerminationV1.Admitted, run.Execution.Product.Termination);
        Assert.Equal([0, 1, 2, 3], run.Execution.Product.WorkloadCommits.Select(
            static receipt => receipt.WorkloadStepOrdinal));
        Assert.Equal(expected, run.Vector);
        Assert.Equal(EvaluatorRunPhase.TerminalSettlement, run.Admitted.Position.Phase);
        Assert.Equal(4, run.Admitted.Position.CompletedWorkloadStepCount);
        Assert.Equal(4, run.Admitted.Position.TotalWorkloadStepCount);
        Assert.Equal(previousFileNumber,
            run.Admitted.FinalCursor.FileScope.PreviousFileNumber);
        Assert.Equal(currentFileNumber,
            run.Admitted.FinalCursor.FileScope.CurrentFileNumber);
        Assert.Empty(run.Admitted.Settlement.MigratedObjectIds);
        Assert.Equal(0, run.Admitted.Settlement.MaintenanceRevisionCount);
        Assert.Equal(1, run.Admitted.Settlement.RealizedRevisionCount);
    }

    private static void AssertDominates(RawVector better, RawVector worse) {
        Assert.True(better.TotalPhysicalWriteBytes <= worse.TotalPhysicalWriteBytes);
        Assert.True(better.PeakCommitWriteBytes <= worse.PeakCommitWriteBytes);
        Assert.True(better.MaxCurrentFileTailBytes <= worse.MaxCurrentFileTailBytes);
        Assert.True(better.FinalColdHeadReadBytes <= worse.FinalColdHeadReadBytes);
        Assert.NotEqual(better, worse);
    }

    private static string[] OperationMultiset(WorkloadTrace trace) => trace.Steps
        .Skip(1)
        .SelectMany(static step => step.Changes)
        .Select(static change => change switch {
            CreateObject create => $"C:{create.ObjectId}:{create.BasePayloadBytes}",
            RemoveObject remove => $"R:{remove.ObjectId}",
            _ => throw new InvalidDataException(
                $"Unexpected lifecycle operation '{change.GetType().Name}'."),
        })
        .Order(StringComparer.Ordinal)
        .ToArray();

    private static int PeakTransientLiveCount(WorkloadTrace trace) {
        HashSet<uint> live = [];
        int peak = 0;
        foreach (SaveStep step in trace.Steps.Skip(1)) {
            foreach (WorkloadChange change in step.Changes) {
                switch (change) {
                    case CreateObject create when create.ObjectId is 100 or 101:
                        Assert.True(live.Add(create.ObjectId));
                        break;
                    case RemoveObject remove when remove.ObjectId is 100 or 101:
                        Assert.True(live.Remove(remove.ObjectId));
                        break;
                }
            }

            peak = Math.Max(peak, live.Count);
        }

        Assert.Empty(live);
        return peak;
    }

    private static long[] FileTails(RbfFileStore store) => Enumerable
        .Range(1, store.FileCount)
        .Select(index => store.GetFile((uint)index).TailOffsetBytes)
        .ToArray();

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

    private static void AssertTraceCases(string traceId, string expectedHash) {
        BenchmarkV1CaseDefinition[] cases = Corpus.Cases
            .Where(benchmarkCase =>
                benchmarkCase.ManifestCase.TraceDefinition.Id == traceId)
            .ToArray();
        Assert.Equal(4, cases.Length);
        Assert.All(cases, benchmarkCase => {
            Assert.Equal(4, benchmarkCase.ManifestCase.EvaluatedWorkloadStepCount);
            Assert.Equal(expectedHash, benchmarkCase.ManifestCase.ResolvedTraceSha256);
        });
    }

    private sealed record SelectionTrace(
        IReadOnlyList<StrategyTargetV1> Targets,
        IReadOnlyList<uint[]> Migrations);

    private sealed record LifecycleRun(
        BenchmarkV1CaseExecution Execution,
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
