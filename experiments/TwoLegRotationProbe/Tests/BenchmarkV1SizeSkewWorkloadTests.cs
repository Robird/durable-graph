using Atelia.TwoLegRotationProbe.Arena;
using Atelia.TwoLegRotationProbe.Baselines;
using Atelia.TwoLegRotationProbe.Benchmarking;
using Atelia.TwoLegRotationProbe.Evaluation;
using Atelia.TwoLegRotationProbe.Model;
using Atelia.TwoLegRotationProbe.Simulation;
using Atelia.TwoLegRotationProbe.Workloads;

namespace Atelia.TwoLegRotationProbe.Tests;

public sealed class BenchmarkV1SizeSkewWorkloadTests {
    private static readonly BenchmarkV1BatchDefinition Corpus =
        BenchmarkV1Corpus.Create(BenchmarkV1Baselines.All);

    private static readonly WorkloadTrace LowIdSmall = FindTrace(
        "size-skew-low-id-small");

    private static readonly WorkloadTrace LowIdLarge = FindTrace(
        "size-skew-low-id-large");

    [Fact]
    public void Matched_pair_preserves_bootstrap_geometry_and_swaps_only_payload_assignment() {
        BenchmarkV1BootstrappedSource smallSource =
            BenchmarkV1SourceBootstrap.Create(LowIdSmall);
        BenchmarkV1BootstrappedSource largeSource =
            BenchmarkV1SourceBootstrap.Create(LowIdLarge);

        Assert.Equal([10U, 20U], InitialObjectIds(LowIdSmall));
        Assert.Equal(InitialObjectIds(LowIdSmall), InitialObjectIds(LowIdLarge));
        Assert.Equal([20, 100], InitialPayloadMultiset(LowIdSmall));
        Assert.Equal(
            InitialPayloadMultiset(LowIdSmall),
            InitialPayloadMultiset(LowIdLarge));
        Assert.Equal(
            new CreateObject(1001, 1),
            Assert.IsType<CreateObject>(Assert.Single(LowIdSmall.Steps[1].Changes)));
        Assert.Same(LowIdSmall.Steps[1], LowIdLarge.Steps[1]);

        Assert.Equal(2, smallSource.Store.FileCount);
        Assert.Equal(smallSource.Store.FileCount, largeSource.Store.FileCount);
        Assert.Equal(
            FileTails(smallSource.Store),
            FileTails(largeSource.Store));
        Assert.Equal(
            smallSource.PreviousRevisionAddress,
            largeSource.PreviousRevisionAddress);
        Assert.Equal(
            smallSource.Cursor.FileScope.PreviousFileNumber,
            largeSource.Cursor.FileScope.PreviousFileNumber);
        Assert.Equal(
            smallSource.Cursor.FileScope.CurrentFileNumber,
            largeSource.Cursor.FileScope.CurrentFileNumber);
        Assert.Equal(
            smallSource.Cursor.PublishedRevisionAddress,
            largeSource.Cursor.PublishedRevisionAddress);
        Assert.Equal(
            smallSource.Cursor.CurrentFileTailOffsetBytes,
            largeSource.Cursor.CurrentFileTailOffsetBytes);
        Assert.Equal(
            smallSource.Store.ReadLayout(smallSource.PreviousRevisionAddress),
            largeSource.Store.ReadLayout(largeSource.PreviousRevisionAddress));
        Assert.Equal(
            smallSource.Store.ReadLayout(
                smallSource.Cursor.PublishedRevisionAddress),
            largeSource.Store.ReadLayout(
                largeSource.Cursor.PublishedRevisionAddress));
        AssertSharedBootstrapFrame(smallSource);
        AssertSharedBootstrapFrame(largeSource);

        StrategyStepViewV1 smallView = CreateFirstView(LowIdSmall);
        StrategyStepViewV1 largeView = CreateFirstView(LowIdLarge);
        AssertCommonView(smallView);
        AssertCommonView(largeView);
        AssertFact(smallView, objectId: 10, resultBasePayloadBytes: 20);
        AssertFact(smallView, objectId: 20, resultBasePayloadBytes: 100);
        AssertFact(largeView, objectId: 10, resultBasePayloadBytes: 100);
        AssertFact(largeView, objectId: 20, resultBasePayloadBytes: 20);

        foreach (StrategyBindingV1 strategy in BenchmarkV1Baselines.All) {
            StrategySelectionV1 smallSelection = BenchmarkV1Baselines.Select(
                strategy.Identity,
                smallView);
            StrategySelectionV1 largeSelection = BenchmarkV1Baselines.Select(
                strategy.Identity,
                largeView);

            AssertEquivalentSelection(smallSelection, largeSelection);
            Assert.Equal(StrategyTargetV1.StayB, smallSelection.Target);
            Assert.Empty(smallSelection.Stay.UpdateDecisions);
            Assert.Empty(smallSelection.Rotate.BContainedUpdateDecisions);
            Assert.Empty(smallSelection.Rotate.BContainedNoChangeBaseObjectIds);
            uint[] expectedMigrations = strategy.Identity ==
                BenchmarkV1Baselines.DebtZeroThenRotateDeltaNoMigration.Identity
                ? []
                : [10];
            Assert.Equal(
                expectedMigrations,
                smallSelection.Stay.UnchangedMigrationObjectIds);
        }
    }

    [Fact]
    public void ObjectId_first_migration_exposes_a_matched_size_skew_cost() {
        SizeSkewRun noMigrationSmall = Run(
            LowIdSmall,
            BenchmarkV1Baselines.DebtZeroThenRotateDeltaNoMigration);
        SizeSkewRun noMigrationLarge = Run(
            LowIdLarge,
            BenchmarkV1Baselines.DebtZeroThenRotateDeltaNoMigration);
        AssertRun(
            noMigrationSmall,
            expectedWorkloadTail: 84,
            new RawVector(2, 220, 176, 176, 208),
            lowIdBasePayloadBytes: 20,
            highIdBasePayloadBytes: 100);
        AssertRun(
            noMigrationLarge,
            expectedWorkloadTail: 84,
            new RawVector(2, 220, 176, 176, 208),
            lowIdBasePayloadBytes: 100,
            highIdBasePayloadBytes: 20);
        Assert.Equal(noMigrationSmall.Vector, noMigrationLarge.Vector);

        foreach (StrategyBindingV1 strategy in BenchmarkV1Baselines.All.Skip(1)) {
            SizeSkewRun small = Run(LowIdSmall, strategy);
            SizeSkewRun large = Run(LowIdLarge, strategy);
            AssertRun(
                small,
                expectedWorkloadTail: 112,
                new RawVector(2, 224, 152, 152, 212),
                lowIdBasePayloadBytes: 20,
                highIdBasePayloadBytes: 100);
            AssertRun(
                large,
                expectedWorkloadTail: 192,
                new RawVector(2, 228, 152, 192, 216),
                lowIdBasePayloadBytes: 100,
                highIdBasePayloadBytes: 20);

            Assert.NotEqual(small.Vector, large.Vector);
            Assert.True(small.Vector.TotalPhysicalWriteBytes <
                large.Vector.TotalPhysicalWriteBytes);
            Assert.Equal(
                small.Vector.PeakCommitWriteBytes,
                large.Vector.PeakCommitWriteBytes);
            Assert.True(small.Vector.MaxCurrentFileTailBytes <
                large.Vector.MaxCurrentFileTailBytes);
            Assert.True(small.Vector.FinalColdHeadReadBytes <
                large.Vector.FinalColdHeadReadBytes);
        }
    }

    private static SizeSkewRun Run(
        WorkloadTrace trace,
        StrategyBindingV1 strategy) {
        BenchmarkV1CaseDefinition definition = Corpus.Cases.Single(
            benchmarkCase =>
                benchmarkCase.ManifestCase.TraceDefinition.Id ==
                    trace.ScenarioName &&
                benchmarkCase.ManifestCase.SelectionProfile == strategy.Identity);
        Assert.Same(trace, definition.Trace);
        BenchmarkV1CaseExecution execution = BenchmarkV1Runner.ExecuteCase(definition);
        AdmittedEvaluatorRun admitted = Assert.IsType<AdmittedEvaluatorRun>(
            execution.Outcome);
        return new SizeSkewRun(
            execution,
            admitted,
            new RawVector(
                admitted.Metrics.RealizedCommitCount,
                admitted.Metrics.TotalPhysicalWriteBytes,
                admitted.Metrics.PeakCommitWriteBytes,
                admitted.Metrics.MaxCurrentFileTailBytes,
                admitted.Metrics.TerminalColdHeadReadBytes));
    }

    private static void AssertRun(
        SizeSkewRun run,
        long expectedWorkloadTail,
        RawVector expectedVector,
        int lowIdBasePayloadBytes,
        int highIdBasePayloadBytes) {
        Assert.Equal(StrategyRunTerminationV1.Admitted, run.Execution.Product.Termination);
        StrategyCommitReceiptV1 receipt = Assert.Single(
            run.Execution.Product.WorkloadCommits);
        Assert.Equal(0, receipt.WorkloadStepOrdinal);
        Assert.Equal(StrategyTargetV1.StayB, receipt.SelectedTarget);
        Assert.Equal(1U, receipt.Result.PreviousFileNumber);
        Assert.Equal(2U, receipt.Result.CurrentFileNumber);
        Assert.Equal(2U, receipt.Result.PublishedRevisionFileNumber);
        Assert.Equal(expectedWorkloadTail, receipt.Result.CurrentFileTailOffsetBytes);

        Assert.Equal(expectedVector, run.Vector);
        Assert.Equal(EvaluatorRunPhase.TerminalSettlement, run.Admitted.Position.Phase);
        Assert.Equal(1, run.Admitted.Position.CompletedWorkloadStepCount);
        Assert.Equal(1, run.Admitted.Position.TotalWorkloadStepCount);
        Assert.Equal(2U, run.Admitted.FinalCursor.FileScope.PreviousFileNumber);
        Assert.Equal(3U, run.Admitted.FinalCursor.FileScope.CurrentFileNumber);
        Assert.Empty(run.Admitted.Settlement.MigratedObjectIds);
        Assert.Equal(0, run.Admitted.Settlement.MaintenanceRevisionCount);
        Assert.Equal(1, run.Admitted.Settlement.RealizedRevisionCount);

        IReadOnlyDictionary<uint, LogicalObjectState> state =
            PhysicalStateOracle.Materialize(
                run.Execution.Store,
                ObjectVersionDictionaryReader.MaterializeLive(
                    run.Execution.Store,
                    run.Admitted.FinalCursor.PublishedRevisionAddress).Bindings);
        Assert.Equal([10U, 20U, 1001U], state.Keys.Order());
        Assert.Equal(lowIdBasePayloadBytes, state[10].BasePayloadBytes);
        Assert.Equal(highIdBasePayloadBytes, state[20].BasePayloadBytes);
        Assert.Equal(1, state[1001].BasePayloadBytes);
        Assert.Equal(1, state[10].LogicalVersionOrdinal);
        Assert.Equal(1, state[20].LogicalVersionOrdinal);
        Assert.Equal(1, state[1001].LogicalVersionOrdinal);
    }

    private static StrategyStepViewV1 CreateFirstView(WorkloadTrace trace) {
        BenchmarkV1BootstrappedSource source = BenchmarkV1SourceBootstrap.Create(trace);
        StrategyRunContextV1 context = new(
            source.Store,
            source.Cursor,
            trace,
            firstWorkloadTraceStepIndex: 1,
            totalWorkloadStepCount: 1);
        return Assert.IsType<StrategyStepViewV1>(context.CurrentStep);
    }

    private static void AssertCommonView(StrategyStepViewV1 view) {
        Assert.Equal(121, view.PostLiveGraphBasePayloadBytes);
        Assert.Equal(120, view.ADependentEvacuationBasePayloadBytes);
        Assert.True(view.HasParentPreviousDebt);
        Assert.Equal(
            [10U, 20U, 1001U],
            view.Objects.Select(static fact => fact.ObjectId));
        Assert.Equal(
            [
                StrategyObjectKindV1.NoChange,
                StrategyObjectKindV1.NoChange,
                StrategyObjectKindV1.Insert,
            ],
            view.Objects.Select(static fact => fact.Kind));
    }

    private static void AssertFact(
        StrategyStepViewV1 view,
        uint objectId,
        int resultBasePayloadBytes) {
        StrategyObjectFactV1 fact = view.Objects.Single(candidate =>
            candidate.ObjectId == objectId);
        Assert.Equal(StrategyObjectKindV1.NoChange, fact.Kind);
        Assert.True(fact.SourceIsPreviousDependent);
        Assert.Equal(resultBasePayloadBytes, fact.SourceBasePayloadBytes);
        Assert.Equal(
            resultBasePayloadBytes,
            fact.SourceHeadReconstructionPayloadBytes);
        Assert.Equal(resultBasePayloadBytes, fact.ResultBasePayloadBytes);
        Assert.Null(fact.DeltaPayloadBytes);
    }

    private static void AssertEquivalentSelection(
        StrategySelectionV1 expected,
        StrategySelectionV1 actual) {
        Assert.Equal(expected.Target, actual.Target);
        Assert.Equal(expected.Stay.UpdateDecisions, actual.Stay.UpdateDecisions);
        Assert.Equal(
            expected.Stay.UnchangedMigrationObjectIds,
            actual.Stay.UnchangedMigrationObjectIds);
        Assert.Equal(
            expected.Rotate.BContainedUpdateDecisions,
            actual.Rotate.BContainedUpdateDecisions);
        Assert.Equal(
            expected.Rotate.BContainedNoChangeBaseObjectIds,
            actual.Rotate.BContainedNoChangeBaseObjectIds);
    }

    private static uint[] InitialObjectIds(WorkloadTrace trace) => trace.Steps[0]
        .Changes
        .Cast<CreateObject>()
        .Select(static create => create.ObjectId)
        .Order()
        .ToArray();

    private static int[] InitialPayloadMultiset(WorkloadTrace trace) => trace.Steps[0]
        .Changes
        .Cast<CreateObject>()
        .Select(static create => create.BasePayloadBytes)
        .Order()
        .ToArray();

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

    private static void AssertSharedBootstrapFrame(
        BenchmarkV1BootstrappedSource source) {
        Assert.Equal(1, source.Store.GetFile(1).FrameCount);
        IReadOnlyDictionary<uint, AbsoluteFrameAddress> bindings =
            ObjectVersionDictionaryReader.MaterializeLive(
                source.Store,
                source.Cursor.PublishedRevisionAddress).Bindings;
        Assert.Equal([10U, 20U], bindings.Keys.Order());
        Assert.All(bindings, pair => Assert.Equal(
            source.PreviousRevisionAddress,
            PhysicalStateOracle.InspectObjectReconstruction(
                source.Store,
                pair.Key,
                pair.Value).BaseAddress));
    }

    private sealed record SizeSkewRun(
        BenchmarkV1CaseExecution Execution,
        AdmittedEvaluatorRun Admitted,
        RawVector Vector);

    private readonly record struct RawVector(
        int RealizedCommitCount,
        long TotalPhysicalWriteBytes,
        long PeakCommitWriteBytes,
        long MaxCurrentFileTailBytes,
        long FinalColdHeadReadBytes);
}
