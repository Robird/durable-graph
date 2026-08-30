using System.Collections.ObjectModel;

namespace Atelia.TwoLegRotationProbe.Benchmarking;

internal sealed class BenchmarkManifestV1 {
    private readonly ReadOnlyCollection<BenchmarkCaseManifestV1> _cases;

    public BenchmarkManifestV1(
        string manifestId,
        int manifestRevision,
        BenchmarkComponentIdentityV1 evaluator,
        BenchmarkComponentIdentityV1 terminalSettlement,
        BenchmarkComponentIdentityV1 readSchedule,
        BenchmarkComponentIdentityV1 metrics,
        BenchmarkComponentIdentityV1 frameLayout,
        BenchmarkComponentIdentityV1 revisionGrammar,
        IEnumerable<BenchmarkCaseManifestV1> cases) {
        BenchmarkV1Text.ValidateId(manifestId, nameof(manifestId));
        if (manifestRevision <= 0) {
            throw new ArgumentOutOfRangeException(nameof(manifestRevision));
        }

        ArgumentNullException.ThrowIfNull(cases);
        BenchmarkCaseManifestV1[] canonicalCases = cases
            .Select(static benchmarkCase => benchmarkCase ??
                throw new ArgumentException(
                    "A benchmark manifest cannot contain a null case.",
                    nameof(cases)))
            .OrderBy(static benchmarkCase => benchmarkCase.CaseId, StringComparer.Ordinal)
            .ToArray();
        for (int index = 1; index < canonicalCases.Length; index++) {
            if (StringComparer.Ordinal.Equals(
                canonicalCases[index - 1].CaseId,
                canonicalCases[index].CaseId)) {
                throw new ArgumentException(
                    $"Benchmark case '{canonicalCases[index].CaseId}' occurs more than once.",
                    nameof(cases));
            }
        }

        Schema = BenchmarkV1Identities.ManifestSchema;
        ManifestId = manifestId;
        ManifestRevision = manifestRevision;
        Evaluator = evaluator ?? throw new ArgumentNullException(nameof(evaluator));
        TerminalSettlement = terminalSettlement ??
            throw new ArgumentNullException(nameof(terminalSettlement));
        ReadSchedule = readSchedule ??
            throw new ArgumentNullException(nameof(readSchedule));
        Metrics = metrics ?? throw new ArgumentNullException(nameof(metrics));
        FrameLayout = frameLayout ??
            throw new ArgumentNullException(nameof(frameLayout));
        RevisionGrammar = revisionGrammar ??
            throw new ArgumentNullException(nameof(revisionGrammar));
        _cases = Array.AsReadOnly(canonicalCases);
    }

    public BenchmarkComponentIdentityV1 Schema { get; }

    public string ManifestId { get; }

    public int ManifestRevision { get; }

    public BenchmarkComponentIdentityV1 Evaluator { get; }

    public BenchmarkComponentIdentityV1 TerminalSettlement { get; }

    public BenchmarkComponentIdentityV1 ReadSchedule { get; }

    public BenchmarkComponentIdentityV1 Metrics { get; }

    public BenchmarkComponentIdentityV1 FrameLayout { get; }

    public BenchmarkComponentIdentityV1 RevisionGrammar { get; }

    public IReadOnlyList<BenchmarkCaseManifestV1> Cases => _cases;
}

internal sealed record BenchmarkCaseManifestV1 {
    public BenchmarkCaseManifestV1(
        string caseId,
        BenchmarkComponentIdentityV1 sourceFixture,
        BenchmarkComponentIdentityV1 traceDefinition,
        BenchmarkComponentIdentityV1 generator,
        ulong seed,
        string resolvedTraceSha256,
        int bootstrapStepCount,
        int traceStepCount,
        int evaluatedWorkloadStepCount,
        BenchmarkComponentIdentityV1 targetTreatment,
        BenchmarkComponentIdentityV1 decisionTreatment) {
        BenchmarkV1Text.ValidateId(caseId, nameof(caseId));
        ArgumentNullException.ThrowIfNull(sourceFixture);
        if (sourceFixture != BenchmarkV1Identities.SourceFixture) {
            throw new ArgumentException(
                $"Benchmark v1 requires source fixture " +
                $"'{BenchmarkV1Identities.SourceFixture.Id}' version " +
                $"{BenchmarkV1Identities.SourceFixture.Version}.",
                nameof(sourceFixture));
        }

        if (bootstrapStepCount != 1) {
            throw new ArgumentOutOfRangeException(
                nameof(bootstrapStepCount),
                bootstrapStepCount,
                "Benchmark v1 source fixture consumes exactly trace step 0.");
        }
        if (traceStepCount <= 0) {
            throw new ArgumentOutOfRangeException(nameof(traceStepCount));
        }

        ArgumentOutOfRangeException.ThrowIfNegative(evaluatedWorkloadStepCount);
        if (bootstrapStepCount > traceStepCount ||
            evaluatedWorkloadStepCount != traceStepCount - bootstrapStepCount) {
            throw new ArgumentException(
                "Evaluated workload steps must equal trace steps minus bootstrap steps.",
                nameof(evaluatedWorkloadStepCount));
        }

        CaseId = caseId;
        SourceFixture = sourceFixture;
        TraceDefinition = traceDefinition ??
            throw new ArgumentNullException(nameof(traceDefinition));
        Generator = generator ?? throw new ArgumentNullException(nameof(generator));
        Seed = seed;
        BenchmarkV1Text.ValidateSha256(
            resolvedTraceSha256,
            nameof(resolvedTraceSha256));
        ResolvedTraceSha256 = resolvedTraceSha256;
        BootstrapStepCount = bootstrapStepCount;
        TraceStepCount = traceStepCount;
        EvaluatedWorkloadStepCount = evaluatedWorkloadStepCount;
        TargetTreatment = targetTreatment ??
            throw new ArgumentNullException(nameof(targetTreatment));
        DecisionTreatment = decisionTreatment ??
            throw new ArgumentNullException(nameof(decisionTreatment));
    }

    public string CaseId { get; }

    public BenchmarkComponentIdentityV1 SourceFixture { get; }

    public BenchmarkComponentIdentityV1 TraceDefinition { get; }

    public BenchmarkComponentIdentityV1 Generator { get; }

    public ulong Seed { get; }

    public string ResolvedTraceSha256 { get; }

    public int BootstrapStepCount { get; }

    public int TraceStepCount { get; }

    public int EvaluatedWorkloadStepCount { get; }

    public BenchmarkComponentIdentityV1 TargetTreatment { get; }

    public BenchmarkComponentIdentityV1 DecisionTreatment { get; }
}
