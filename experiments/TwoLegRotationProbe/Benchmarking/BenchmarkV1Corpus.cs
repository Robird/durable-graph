using Atelia.TwoLegRotationProbe.Arena;
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
    public const int ManifestRevision = 6;
    public const string DebtZeroThenRotateNoMigrationCaseId =
        "debt-zero-then-rotate-no-migration";
    public const string DebtZeroThenRotatePacedCaseId =
        "debt-zero-then-rotate-paced";
    public const string DebtZeroThenRotateAdaptiveR3B5PercentCaseId =
        "debt-zero-then-rotate-read-amplification-r3-b5pct";
    public const string DebtZeroThenRotateAdaptiveR4B4PercentCaseId =
        "debt-zero-then-rotate-read-amplification-r4-b4pct";
    public const string MixedSmallNoMigrationCaseId =
        "mixed-small-no-migration";
    public const string MixedSmallPacedCaseId =
        "mixed-small-paced";
    public const string MixedSmallAdaptiveR3B5PercentCaseId =
        "mixed-small-read-amplification-r3-b5pct";
    public const string MixedSmallAdaptiveR4B4PercentCaseId =
        "mixed-small-read-amplification-r4-b4pct";
    public const string ThresholdBandNoMigrationCaseId =
        "read-amplification-threshold-band-no-migration";
    public const string ThresholdBandPacedCaseId =
        "read-amplification-threshold-band-paced";
    public const string ThresholdBandAdaptiveR3B5PercentCaseId =
        "read-amplification-threshold-band-read-amplification-r3-b5pct";
    public const string ThresholdBandAdaptiveR4B4PercentCaseId =
        "read-amplification-threshold-band-read-amplification-r4-b4pct";
    public const string DebtShareDilutionNoMigrationCaseId =
        "previous-debt-share-dilution-boundary-no-migration";
    public const string DebtShareDilutionPacedCaseId =
        "previous-debt-share-dilution-boundary-paced";
    public const string DebtShareDilutionAdaptiveR3B5PercentCaseId =
        "previous-debt-share-dilution-boundary-read-amplification-r3-b5pct";
    public const string DebtShareDilutionAdaptiveR4B4PercentCaseId =
        "previous-debt-share-dilution-boundary-read-amplification-r4-b4pct";
    public const string LocalityLowIdNoMigrationCaseId =
        "locality-next-update-low-id-no-migration";
    public const string LocalityLowIdPacedCaseId =
        "locality-next-update-low-id-paced";
    public const string LocalityLowIdAdaptiveR3B5PercentCaseId =
        "locality-next-update-low-id-read-amplification-r3-b5pct";
    public const string LocalityLowIdAdaptiveR4B4PercentCaseId =
        "locality-next-update-low-id-read-amplification-r4-b4pct";
    public const string LocalityHighIdNoMigrationCaseId =
        "locality-next-update-high-id-no-migration";
    public const string LocalityHighIdPacedCaseId =
        "locality-next-update-high-id-paced";
    public const string LocalityHighIdAdaptiveR3B5PercentCaseId =
        "locality-next-update-high-id-read-amplification-r3-b5pct";
    public const string LocalityHighIdAdaptiveR4B4PercentCaseId =
        "locality-next-update-high-id-read-amplification-r4-b4pct";

    public static BenchmarkV1BatchDefinition Create(
        IEnumerable<StrategyBindingV1> strategies) {
        ArgumentNullException.ThrowIfNull(strategies);
        StrategyBindingV1[] frozenStrategies = strategies
            .Select(static strategy => strategy ?? throw new ArgumentException(
                "A benchmark strategy matrix cannot contain null.",
                nameof(strategies)))
            .OrderBy(static strategy => strategy.CaseIdSuffix, StringComparer.Ordinal)
            .ToArray();
        if (frozenStrategies.Length == 0) {
            throw new ArgumentException(
                "A benchmark strategy matrix cannot be empty.",
                nameof(strategies));
        }

        if (frozenStrategies.Select(static strategy => strategy.CaseIdSuffix)
            .Distinct(StringComparer.Ordinal).Count() != frozenStrategies.Length ||
            frozenStrategies.Select(static strategy => strategy.Identity)
                .Distinct().Count() != frozenStrategies.Length) {
            throw new ArgumentException(
                "Benchmark strategy identities and case suffixes must be unique.",
                nameof(strategies));
        }

        WorkloadTrace debtZeroThenRotateTrace = CreateDebtZeroThenRotateTrace();
        GeneratedScenario generated = ScenarioGenerator.Generate(
            CreateMixedSmallDefinition());
        BenchmarkV1CaseDefinition[] debtZeroThenRotateCases = CreateStrategyCases(
            new BenchmarkComponentIdentityV1("debt-zero-then-rotate", 1),
            debtZeroThenRotateTrace,
            frozenStrategies);
        BenchmarkV1CaseDefinition[] mixedSmallCases = CreateStrategyCases(
            new BenchmarkComponentIdentityV1("mixed-small", 1),
            generated.Trace,
            frozenStrategies);
        WorkloadTrace thresholdBandTrace = CreateThresholdBandTrace();
        BenchmarkV1CaseDefinition[] thresholdBandCases = CreateStrategyCases(
            new BenchmarkComponentIdentityV1(
                "read-amplification-threshold-band",
                1),
            thresholdBandTrace,
            frozenStrategies);
        WorkloadTrace debtShareDilutionTrace = CreateDebtShareDilutionTrace();
        BenchmarkV1CaseDefinition[] debtShareDilutionCases = CreateStrategyCases(
            new BenchmarkComponentIdentityV1(
                "previous-debt-share-dilution-boundary",
                1),
            debtShareDilutionTrace,
            frozenStrategies);
        SaveStep localityStep0 = new([
            new CreateObject(10, 100),
            new CreateObject(20, 100),
        ]);
        SaveStep localityStep1 = new([new CreateObject(1001, 1)]);
        WorkloadTrace localityLowIdTrace = new(
            scenarioName: "locality-next-update-low-id",
            generatorId: "handwritten",
            generatorVersion: 1,
            seed: 0,
            [
                localityStep0,
                localityStep1,
                new SaveStep([
                    new UpdateObject(10, 100, 100),
                    new RemoveObject(1001),
                ]),
            ]);
        WorkloadTrace localityHighIdTrace = new(
            scenarioName: "locality-next-update-high-id",
            generatorId: "handwritten",
            generatorVersion: 1,
            seed: 0,
            [
                localityStep0,
                localityStep1,
                new SaveStep([
                    new UpdateObject(20, 100, 100),
                    new RemoveObject(1001),
                ]),
            ]);
        BenchmarkV1CaseDefinition[] localityLowIdCases = CreateStrategyCases(
            new BenchmarkComponentIdentityV1("locality-next-update-low-id", 1),
            localityLowIdTrace,
            frozenStrategies);
        BenchmarkV1CaseDefinition[] localityHighIdCases = CreateStrategyCases(
            new BenchmarkComponentIdentityV1("locality-next-update-high-id", 1),
            localityHighIdTrace,
            frozenStrategies);
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
                .. debtZeroThenRotateCases,
                .. mixedSmallCases,
                .. thresholdBandCases,
                .. debtShareDilutionCases,
                .. localityLowIdCases,
                .. localityHighIdCases,
            ]);
    }

    private static BenchmarkV1CaseDefinition[] CreateStrategyCases(
        BenchmarkComponentIdentityV1 traceDefinition,
        WorkloadTrace trace,
        IEnumerable<StrategyBindingV1> strategies) => strategies
            .Select(strategy => new BenchmarkV1CaseDefinition(
                $"{traceDefinition.Id}-{strategy.CaseIdSuffix}",
                traceDefinition,
                trace,
                strategy))
            .ToArray();

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

    private static WorkloadTrace CreateThresholdBandTrace() => new(
        scenarioName: "read-amplification-threshold-band",
        generatorId: "handwritten",
        generatorVersion: 1,
        seed: 0,
        [
            new SaveStep([
                new CreateObject(1, 10),
                new CreateObject(2, 1),
                new CreateObject(3, 1),
                new CreateObject(4, 1),
                new CreateObject(5, 1),
                new CreateObject(6, 1),
                new CreateObject(7, 1),
                new CreateObject(8, 1),
                new CreateObject(100, 1000),
            ]),
            new SaveStep([new CreateObject(1001, 1)]),
            new SaveStep([new UpdateObject(1, 10, 5)]),
            new SaveStep([new UpdateObject(1, 10, 5)]),
            new SaveStep([new UpdateObject(1, 10, 5)]),
            new SaveStep([new UpdateObject(1, 10, 5)]),
            new SaveStep([new UpdateObject(1, 10, 5)]),
            new SaveStep([new UpdateObject(1, 10, 5)]),
            new SaveStep([
                new RemoveObject(2),
                new RemoveObject(3),
                new RemoveObject(4),
                new RemoveObject(5),
                new RemoveObject(6),
                new RemoveObject(7),
                new RemoveObject(1001),
            ]),
        ]);

    private static WorkloadTrace CreateDebtShareDilutionTrace() => new(
        scenarioName: "previous-debt-share-dilution-boundary",
        generatorId: "handwritten",
        generatorVersion: 1,
        seed: 0,
        [
            new SaveStep([
                new CreateObject(1, 40),
                new CreateObject(100, 960),
            ]),
            new SaveStep([new UpdateObject(100, 960, 960)]),
            new SaveStep([new UpdateObject(1, 40, 40)]),
            new SaveStep([new UpdateObject(100, 960, 960)]),
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
