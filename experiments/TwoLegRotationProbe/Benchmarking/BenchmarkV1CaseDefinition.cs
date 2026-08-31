using System.Collections.ObjectModel;
using Atelia.TwoLegRotationProbe.Workloads;

namespace Atelia.TwoLegRotationProbe.Benchmarking;

/// <summary>
/// One fully resolved benchmark case. The manifest leaf is derived from the same
/// frozen trace consumed by the runner, so identity and execution input cannot drift.
/// </summary>
internal sealed class BenchmarkV1CaseDefinition {
    public BenchmarkV1CaseDefinition(
        string caseId,
        BenchmarkComponentIdentityV1 traceDefinition,
        WorkloadTrace trace,
        BenchmarkComponentIdentityV1 selectionProfile) {
        ArgumentNullException.ThrowIfNull(traceDefinition);
        ArgumentNullException.ThrowIfNull(trace);
        ArgumentNullException.ThrowIfNull(selectionProfile);

        if (!StringComparer.Ordinal.Equals(traceDefinition.Id, trace.ScenarioName)) {
            throw new ArgumentException(
                $"Trace definition '{traceDefinition.Id}' must equal scenario name " +
                $"'{trace.ScenarioName}'.",
                nameof(traceDefinition));
        }

        BenchmarkV1SelectionProfileSelector.Validate(selectionProfile);

        Trace = trace;
        ManifestCase = new BenchmarkCaseManifestV1(
            caseId,
            BenchmarkV1Identities.SourceFixture,
            traceDefinition,
            new BenchmarkComponentIdentityV1(
                trace.GeneratorId,
                trace.GeneratorVersion),
            trace.Seed,
            WorkloadTraceSha256.Compute(trace),
            bootstrapStepCount: 1,
            traceStepCount: trace.Steps.Count,
            evaluatedWorkloadStepCount: trace.Steps.Count - 1,
            selectionProfile);
    }

    public BenchmarkCaseManifestV1 ManifestCase { get; }

    public WorkloadTrace Trace { get; }
}

internal sealed class BenchmarkV1BatchDefinition {
    private readonly ReadOnlyCollection<BenchmarkV1CaseDefinition> _cases;

    public BenchmarkV1BatchDefinition(
        string manifestId,
        int manifestRevision,
        BenchmarkComponentIdentityV1 evaluator,
        BenchmarkComponentIdentityV1 terminalSettlement,
        BenchmarkComponentIdentityV1 readSchedule,
        BenchmarkComponentIdentityV1 metrics,
        BenchmarkComponentIdentityV1 frameLayout,
        BenchmarkComponentIdentityV1 revisionGrammar,
        IEnumerable<BenchmarkV1CaseDefinition> cases) {
        ArgumentNullException.ThrowIfNull(cases);
        BenchmarkV1CaseDefinition[] canonicalCases = cases
            .Select(static definition => definition ??
                throw new ArgumentException(
                    "A benchmark batch cannot contain a null case definition.",
                    nameof(cases)))
            .OrderBy(
                static definition => definition.ManifestCase.CaseId,
                StringComparer.Ordinal)
            .ToArray();

        BenchmarkV1ProtocolIdentities.Validate(
            evaluator,
            terminalSettlement,
            readSchedule,
            metrics,
            frameLayout,
            revisionGrammar);

        Manifest = new BenchmarkManifestV1(
            manifestId,
            manifestRevision,
            evaluator,
            terminalSettlement,
            readSchedule,
            metrics,
            frameLayout,
            revisionGrammar,
            canonicalCases.Select(static definition => definition.ManifestCase));
        _cases = Array.AsReadOnly(canonicalCases);
    }

    public BenchmarkManifestV1 Manifest { get; }

    public IReadOnlyList<BenchmarkV1CaseDefinition> Cases => _cases;
}

internal sealed record BenchmarkV1BatchRun(
    BenchmarkV1BatchDefinition Definition,
    BenchmarkReportV1 Report);
