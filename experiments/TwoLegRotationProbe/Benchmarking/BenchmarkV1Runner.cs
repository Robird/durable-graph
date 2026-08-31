using Atelia.TwoLegRotationProbe.Arena;
using Atelia.TwoLegRotationProbe.Evaluation;
using Atelia.TwoLegRotationProbe.Model;
using Atelia.TwoLegRotationProbe.Planning;
using Atelia.TwoLegRotationProbe.Simulation;
using Atelia.TwoLegRotationProbe.Workloads;

namespace Atelia.TwoLegRotationProbe.Benchmarking;

internal static class BenchmarkV1Runner {
    public static BenchmarkV1BatchRun Run(BenchmarkV1BatchDefinition definition) {
        ArgumentNullException.ThrowIfNull(definition);
        ValidateDefinitionClosure(definition);

        BenchmarkCaseReportV1[] cases = definition.Cases
            .Select(RunCase)
            .ToArray();
        BenchmarkReportV1 report = new(definition.Manifest, cases);
        return new BenchmarkV1BatchRun(definition, report);
    }

    private static BenchmarkCaseReportV1 RunCase(
        BenchmarkV1CaseDefinition definition) {
        BenchmarkV1CaseExecution execution = ExecuteCase(definition);
        BenchmarkCaseManifestV1 manifestCase = definition.ManifestCase;
        return new BenchmarkCaseReportV1(
            manifestCase.CaseId,
            manifestCase.ResolvedTraceSha256,
            BenchmarkOutcomeReportV1.Project(execution.Outcome));
    }

    internal static BenchmarkV1CaseExecution ExecuteCase(
        BenchmarkV1CaseDefinition definition) {
        ArgumentNullException.ThrowIfNull(definition);
        WorkloadTrace trace = definition.Trace;
        BenchmarkCaseManifestV1 manifestCase = definition.ManifestCase;
        BenchmarkV1BootstrappedSource source =
            BenchmarkV1SourceBootstrap.Create(trace);
        StrategyBindingV1 strategy = definition.Strategy ??
            throw new InvalidDataException(
                $"Benchmark case '{manifestCase.CaseId}' has no resolved strategy binding.");
        StrategyRunContextV1 context = new(
            source.Store,
            source.Cursor,
            trace,
            manifestCase.BootstrapStepCount,
            manifestCase.EvaluatedWorkloadStepCount);
        StrategyRunProductV1 product = strategy.Execute(context);
        context.ValidateReturnedProduct(product);
        EvaluatorRunOutcome outcome = product.Outcome;
        if (outcome is AdmittedEvaluatorRun admitted) {
            ValidateFinalState(product.Store, admitted, trace);
        }

        return new BenchmarkV1CaseExecution(product, outcome);
    }

    private static void ValidateDefinitionClosure(
        BenchmarkV1BatchDefinition definition) {
        BenchmarkCaseManifestV1[] manifestCases = definition.Manifest.Cases.ToArray();
        BenchmarkCaseManifestV1[] resolvedCases = definition.Cases
            .Select(static benchmarkCase => benchmarkCase.ManifestCase)
            .ToArray();
        if (!manifestCases.SequenceEqual(resolvedCases)) {
            throw new InvalidDataException(
                "The benchmark manifest and resolved case definitions have diverged.");
        }
    }

    private static void ValidateFinalState(
        RbfFileStore store,
        AdmittedEvaluatorRun admitted,
        WorkloadTrace trace) {
        IReadOnlyDictionary<uint, LogicalObjectState> expected =
            WorkloadReplayer.Replay(trace);
        IReadOnlyDictionary<uint, LogicalObjectState> actual =
            PhysicalStateOracle.Materialize(
                store,
                ObjectVersionDictionaryReader.MaterializeLive(
                    store,
                    admitted.FinalCursor.PublishedRevisionAddress).Bindings);
        if (expected.Count != actual.Count || expected.Any(pair =>
            !actual.TryGetValue(pair.Key, out LogicalObjectState state) ||
            state != pair.Value)) {
            throw new InvalidDataException(
                "An admitted benchmark run does not match full-trace logical replay.");
        }
    }
}

internal sealed record BenchmarkV1CaseExecution(
    StrategyRunProductV1 Product,
    EvaluatorRunOutcome Outcome) {
    public RbfFileStore Store => Product.Store;
}
