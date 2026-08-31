using Atelia.TwoLegRotationProbe.Arena;
using Atelia.TwoLegRotationProbe.Baselines;
using Atelia.TwoLegRotationProbe.Benchmarking;
using Atelia.TwoLegRotationProbe.Planning;
using Atelia.TwoLegRotationProbe.Workloads;

namespace Atelia.TwoLegRotationProbe.Tests;

public sealed class StrategyArenaContractTests {
    [Fact]
    public void Baseline_bindings_execute_from_the_independent_Baselines_assembly() {
        Assert.NotEqual(
            typeof(StrategyBindingV1).Assembly,
            typeof(BenchmarkV1Baselines).Assembly);
        Assert.All(BenchmarkV1Baselines.All, binding => Assert.Equal(
            typeof(BenchmarkV1Baselines).Assembly,
            binding.ExecutorFactory.Method.Module.Assembly));
    }

    [Fact]
    public void Removed_previous_debt_is_distinct_from_post_live_evacuation_cost() {
        WorkloadTrace trace = new(
            "remove-previous-debt-insert-live",
            "handwritten",
            generatorVersion: 1,
            seed: 0,
            [
                new SaveStep([new CreateObject(10, 100)]),
                new SaveStep([
                    new RemoveObject(10),
                    new CreateObject(20, 200),
                ]),
            ]);
        BenchmarkV1BootstrappedSource source = BenchmarkV1SourceBootstrap.Create(
            trace);
        NormalizedSaveFacts facts = SaveStepNormalizer.Normalize(
            source.Store,
            source.Cursor.FileScope.CurrentFileNumber,
            source.Cursor.PublishedRevisionAddress,
            trace.Steps[1]);

        StrategyStepViewV1 view = StrategyStepViewV1.Create(facts);

        Assert.Equal(200, view.PostLiveGraphBasePayloadBytes);
        Assert.Equal(0, view.ADependentEvacuationBasePayloadBytes);
        Assert.True(view.HasParentPreviousDebt);
        Assert.Equal(
            [StrategyObjectKindV1.Remove, StrategyObjectKindV1.Insert],
            view.Objects.Select(static fact => fact.Kind));
        StrategyObjectFactV1 removed = Assert.Single(
            view.Objects,
            static fact => fact.Kind == StrategyObjectKindV1.Remove);
        Assert.Equal(10U, removed.ObjectId);
        Assert.True(removed.SourceIsPreviousDependent);
        Assert.Equal(100, removed.SourceBasePayloadBytes);
        Assert.Equal(100, removed.SourceHeadReconstructionPayloadBytes);
        Assert.Null(removed.ResultBasePayloadBytes);
        StrategyObjectFactV1 inserted = Assert.Single(
            view.Objects,
            static fact => fact.Kind == StrategyObjectKindV1.Insert);
        Assert.Equal(20U, inserted.ObjectId);
        Assert.Null(inserted.SourceIsPreviousDependent);
        Assert.Equal(200, inserted.ResultBasePayloadBytes);

        StrategySelectionV1 debtSelection = BenchmarkV1Baselines.Select(
            BenchmarkV1Baselines.DebtZeroThenRotateDeltaNoMigration.Identity,
            view);
        StrategySelectionV1 adaptiveSelection = BenchmarkV1Baselines.Select(
            BenchmarkV1Baselines.ReadAmplificationBaseBudgetR3B5Percent.Identity,
            view);

        Assert.Equal(StrategyTargetV1.StayB, debtSelection.Target);
        Assert.Equal(StrategyTargetV1.RotateC, adaptiveSelection.Target);
    }

    [Fact]
    public void Arena_certified_product_exposes_store_and_workload_commit_receipts() {
        BenchmarkV1CaseDefinition benchmarkCase = BenchmarkV1Corpus.Create(
            [BenchmarkV1Baselines.DebtZeroThenRotateDeltaNoMigration]).Cases
            .Single(definition => definition.ManifestCase.CaseId ==
                BenchmarkV1Corpus.DebtZeroThenRotateNoMigrationCaseId);

        BenchmarkV1CaseExecution execution = BenchmarkV1Runner.ExecuteCase(
            benchmarkCase);
        StrategyRunProductV1 product = execution.Product;

        Assert.Same(execution.Store, product.Store);
        Assert.Equal(StrategyRunTerminationV1.Admitted, product.Termination);
        Assert.Equal(4, product.WorkloadCommits.Count);
        Assert.Equal(
            [0, 1, 2, 3],
            product.WorkloadCommits.Select(static receipt =>
                receipt.WorkloadStepOrdinal));
        Assert.All(product.WorkloadCommits, receipt => Assert.Equal(
            StrategyTargetV1.StayB,
            receipt.SelectedTarget));
        Assert.Equal(1, product.TerminalSettlementRevisionCount);
        Assert.Equal(3, product.Store.FileCount);
        Assert.Equal(
            product.FinalCheckpoint.CurrentFileNumber,
            product.FinalCheckpoint.PublishedRevisionFileNumber);
        Assert.DoesNotContain(
            typeof(StrategyRunProductV1).GetProperties(),
            static property => property.Name.Contains(
                "Metric",
                StringComparison.Ordinal));
    }

    [Fact]
    public void Runner_requests_a_fresh_whole_run_executor_for_each_case_execution() {
        int factoryCallCount = 0;
        StrategyBindingV1 binding = new(
            new BenchmarkComponentIdentityV1("fresh-executor-probe", 1),
            "fresh-executor-probe",
            () => {
                factoryCallCount++;
                return static context => context.Complete();
            });
        WorkloadTrace trace = new(
            "fresh-executor-probe",
            "handwritten",
            generatorVersion: 1,
            seed: 0,
            [new SaveStep([new CreateObject(1, 1)])]);
        BenchmarkV1CaseDefinition benchmarkCase = new(
            "fresh-executor-probe",
            new BenchmarkComponentIdentityV1("fresh-executor-probe", 1),
            trace,
            binding);

        BenchmarkV1CaseExecution first = BenchmarkV1Runner.ExecuteCase(benchmarkCase);
        BenchmarkV1CaseExecution second = BenchmarkV1Runner.ExecuteCase(benchmarkCase);

        Assert.Equal(2, factoryCallCount);
        Assert.Equal(StrategyRunTerminationV1.Admitted, first.Product.Termination);
        Assert.Equal(StrategyRunTerminationV1.Admitted, second.Product.Termination);
        Assert.NotSame(first.Product.Store, second.Product.Store);
    }
}
