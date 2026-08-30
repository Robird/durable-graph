using Atelia.TwoLegRotationProbe.Workloads;
using Atelia.TwoLegRotationProbe.Workloads.Generation;

namespace Atelia.TwoLegRotationProbe.Benchmarking;

internal static class BenchmarkV1ProtocolIdentities {
    public static readonly BenchmarkComponentIdentityV1 Evaluator =
        new("two-leg-evaluator", 1);

    public static readonly BenchmarkComponentIdentityV1 TerminalSettlement =
        new("direct-rotate-else-ascending-single-debt", 1);

    public static readonly BenchmarkComponentIdentityV1 ReadSchedule =
        new("final-head-cold-load", 1);

    public static readonly BenchmarkComponentIdentityV1 Metrics =
        new("raw-wpfr", 1);

    public static readonly BenchmarkComponentIdentityV1 FrameLayout =
        new("rbf-v0.40-envelope", 1);

    public static readonly BenchmarkComponentIdentityV1 RevisionGrammar =
        new("provisional-revision-v0", 1);

    public static void Validate(
        BenchmarkComponentIdentityV1 evaluator,
        BenchmarkComponentIdentityV1 terminalSettlement,
        BenchmarkComponentIdentityV1 readSchedule,
        BenchmarkComponentIdentityV1 metrics,
        BenchmarkComponentIdentityV1 frameLayout,
        BenchmarkComponentIdentityV1 revisionGrammar) {
        Require(evaluator, Evaluator, nameof(evaluator));
        Require(
            terminalSettlement,
            TerminalSettlement,
            nameof(terminalSettlement));
        Require(readSchedule, ReadSchedule, nameof(readSchedule));
        Require(metrics, Metrics, nameof(metrics));
        Require(frameLayout, FrameLayout, nameof(frameLayout));
        Require(revisionGrammar, RevisionGrammar, nameof(revisionGrammar));
    }

    private static void Require(
        BenchmarkComponentIdentityV1 actual,
        BenchmarkComponentIdentityV1 expected,
        string parameterName) {
        ArgumentNullException.ThrowIfNull(actual, parameterName);
        if (actual != expected) {
            throw new ArgumentException(
                $"Benchmark v1 runner requires '{expected.Id}/{expected.Version}', " +
                $"not '{actual.Id}/{actual.Version}'.",
                parameterName);
        }
    }
}

internal static class BenchmarkV1Corpus {
    public const string ManifestId = "benchmark-v1";
    public const int ManifestRevision = 1;
    public const string DebtZeroThenRotatePacedCaseId =
        "debt-zero-then-rotate-paced";
    public const string MixedSmallNoMigrationCaseId =
        "mixed-small-no-migration";

    public static BenchmarkV1BatchDefinition Create() {
        GeneratedScenario generated = ScenarioGenerator.Generate(
            CreateMixedSmallDefinition());
        return new BenchmarkV1BatchDefinition(
            ManifestId,
            ManifestRevision,
            BenchmarkV1ProtocolIdentities.Evaluator,
            BenchmarkV1ProtocolIdentities.TerminalSettlement,
            BenchmarkV1ProtocolIdentities.ReadSchedule,
            BenchmarkV1ProtocolIdentities.Metrics,
            BenchmarkV1ProtocolIdentities.FrameLayout,
            BenchmarkV1ProtocolIdentities.RevisionGrammar,
            [
                new BenchmarkV1CaseDefinition(
                    DebtZeroThenRotatePacedCaseId,
                    new BenchmarkComponentIdentityV1(
                        "debt-zero-then-rotate",
                        1),
                    CreateDebtZeroThenRotateTrace(),
                    BenchmarkV1TreatmentIdentities.DebtZeroThenRotate,
                    BenchmarkV1TreatmentIdentities
                        .DeltaPacedOneDebtByObjectId),
                new BenchmarkV1CaseDefinition(
                    MixedSmallNoMigrationCaseId,
                    new BenchmarkComponentIdentityV1("mixed-small", 1),
                    generated.Trace,
                    BenchmarkV1TreatmentIdentities.DebtZeroThenRotate,
                    BenchmarkV1TreatmentIdentities.DeltaNoMigration),
            ]);
    }

    private static WorkloadTrace CreateDebtZeroThenRotateTrace() => new(
        scenarioName: "debt-zero-then-rotate",
        generatorId: "handwritten",
        generatorVersion: 1,
        seed: 0,
        [
            new SaveStep([
                new CreateObject(10, 100),
                new CreateObject(20, 200),
                new CreateObject(30, 300),
            ]),
            new SaveStep([new CreateObject(1001, 1)]),
            new SaveStep([new CreateObject(1002, 1)]),
            new SaveStep([new CreateObject(1003, 1)]),
            new SaveStep([new CreateObject(1004, 1)]),
        ]);

    private static ScenarioDefinition CreateMixedSmallDefinition() => new(
        name: "mixed-small",
        seed: 12345,
        stepCount: 3,
        initialPopulation: 3,
        createPerLaterStep: 1,
        maxUpdatePerLaterStep: 2,
        maxRemovePerLaterStep: 1,
        fieldObjectWeight: 1,
        listObjectWeight: 1,
        fieldBehavior: new FieldBehaviorParameters(
            componentCount: 4,
            initialComponentBytesMinInclusive: 4,
            initialComponentBytesMaxExclusive: 20,
            replacementComponentBytesMinInclusive: 1,
            replacementComponentBytesMaxExclusive: 32,
            maxReplacedComponentsPerUpdate: 2,
            deltaOperationOverheadBytes: 2,
            deltaComponentOverheadBytes: 1),
        listBehavior: new ListBehaviorParameters(
            initialItemCount: 2,
            initialItemBytesMinInclusive: 10,
            initialItemBytesMaxExclusive: 11,
            insertedItemBytesMinInclusive: 5,
            insertedItemBytesMaxExclusive: 6,
            replacementItemBytesMinInclusive: 7,
            replacementItemBytesMaxExclusive: 8,
            deltaOperationOverheadBytes: 2,
            insertWeight: 1,
            removeWeight: 1,
            replaceWeight: 1));
}
