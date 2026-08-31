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
        new("after-every-workload-save-cold-load", 1);

    public static readonly BenchmarkComponentIdentityV1 Metrics =
        new("raw-wpfr", 5);

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
    public const int ManifestRevision = 16;
    public const string ActiveHundredMixedAdaptiveR3B5PercentCaseId =
        "active-hundred-mixed-read-amplification-r3-b5pct";
    public const string ActiveHundredMixedAdaptiveR4B4PercentCaseId =
        "active-hundred-mixed-read-amplification-r4-b4pct";
    public const string DebtZeroBeforeRotateAdaptiveR3B5PercentCaseId =
        "debt-zero-before-rotate-read-amplification-r3-b5pct";
    public const string DebtZeroBeforeRotateAdaptiveR4B4PercentCaseId =
        "debt-zero-before-rotate-read-amplification-r4-b4pct";
    public const string InsertBurstThreeOneAdaptiveR3B5PercentCaseId =
        "insert-burst-three-one-read-amplification-r3-b5pct";
    public const string InsertBurstThreeOneAdaptiveR4B4PercentCaseId =
        "insert-burst-three-one-read-amplification-r4-b4pct";
    public const string InsertBurstTwoTwoAdaptiveR3B5PercentCaseId =
        "insert-burst-two-two-read-amplification-r3-b5pct";
    public const string InsertBurstTwoTwoAdaptiveR4B4PercentCaseId =
        "insert-burst-two-two-read-amplification-r4-b4pct";
    public const string DebtZeroThenRotateAdaptiveR3B5PercentCaseId =
        "debt-zero-then-rotate-read-amplification-r3-b5pct";
    public const string DebtZeroThenRotateAdaptiveR4B4PercentCaseId =
        "debt-zero-then-rotate-read-amplification-r4-b4pct";
    public const string MixedSmallAdaptiveR3B5PercentCaseId =
        "mixed-small-read-amplification-r3-b5pct";
    public const string MixedSmallAdaptiveR4B4PercentCaseId =
        "mixed-small-read-amplification-r4-b4pct";
    public const string ThresholdBandAdaptiveR3B5PercentCaseId =
        "read-amplification-threshold-band-read-amplification-r3-b5pct";
    public const string ThresholdBandAdaptiveR4B4PercentCaseId =
        "read-amplification-threshold-band-read-amplification-r4-b4pct";
    public const string DebtShareDilutionAdaptiveR3B5PercentCaseId =
        "previous-debt-share-dilution-boundary-read-amplification-r3-b5pct";
    public const string DebtShareDilutionAdaptiveR4B4PercentCaseId =
        "previous-debt-share-dilution-boundary-read-amplification-r4-b4pct";
    public const string LocalityLowIdAdaptiveR3B5PercentCaseId =
        "locality-next-update-low-id-read-amplification-r3-b5pct";
    public const string LocalityLowIdAdaptiveR4B4PercentCaseId =
        "locality-next-update-low-id-read-amplification-r4-b4pct";
    public const string LocalityHighIdAdaptiveR3B5PercentCaseId =
        "locality-next-update-high-id-read-amplification-r3-b5pct";
    public const string LocalityHighIdAdaptiveR4B4PercentCaseId =
        "locality-next-update-high-id-read-amplification-r4-b4pct";
    public const string SizeSkewLowIdSmallAdaptiveR3B5PercentCaseId =
        "size-skew-low-id-small-read-amplification-r3-b5pct";
    public const string SizeSkewLowIdSmallAdaptiveR4B4PercentCaseId =
        "size-skew-low-id-small-read-amplification-r4-b4pct";
    public const string SizeSkewLowIdLargeAdaptiveR3B5PercentCaseId =
        "size-skew-low-id-large-read-amplification-r3-b5pct";
    public const string SizeSkewLowIdLargeAdaptiveR4B4PercentCaseId =
        "size-skew-low-id-large-read-amplification-r4-b4pct";
    public const string LifecycleTransientOverlapAdaptiveR3B5PercentCaseId =
        "lifecycle-transient-overlap-read-amplification-r3-b5pct";
    public const string LifecycleTransientOverlapAdaptiveR4B4PercentCaseId =
        "lifecycle-transient-overlap-read-amplification-r4-b4pct";
    public const string LifecycleTransientSerialAdaptiveR3B5PercentCaseId =
        "lifecycle-transient-serial-read-amplification-r3-b5pct";
    public const string LifecycleTransientSerialAdaptiveR4B4PercentCaseId =
        "lifecycle-transient-serial-read-amplification-r4-b4pct";
    public const string PreviousDebtGranularitySingleLargeAdaptiveR3B5PercentCaseId =
        "previous-debt-granularity-single-large-read-amplification-r3-b5pct";
    public const string PreviousDebtGranularitySingleLargeAdaptiveR4B4PercentCaseId =
        "previous-debt-granularity-single-large-read-amplification-r4-b4pct";
    public const string PreviousDebtGranularityThreeSmallAdaptiveR3B5PercentCaseId =
        "previous-debt-granularity-three-small-read-amplification-r3-b5pct";
    public const string PreviousDebtGranularityThreeSmallAdaptiveR4B4PercentCaseId =
        "previous-debt-granularity-three-small-read-amplification-r4-b4pct";

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

        (WorkloadTrace debtZeroBeforeRotateTrace,
            WorkloadTrace debtZeroThenRotateTrace) =
            CreateDebtZeroRotateHorizonPair();
        GeneratedScenario generated = ScenarioGenerator.Generate(
            CreateMixedSmallDefinition());
        GeneratedScenario activeHundred = ScenarioGenerator.Generate(
            CreateActiveHundredMixedDefinition());
        BenchmarkV1CaseDefinition[] debtZeroBeforeRotateCases =
            CreateStrategyCases(
                new BenchmarkComponentIdentityV1(
                    "debt-zero-before-rotate",
                    1),
                debtZeroBeforeRotateTrace,
                frozenStrategies);
        BenchmarkV1CaseDefinition[] debtZeroThenRotateCases = CreateStrategyCases(
            new BenchmarkComponentIdentityV1("debt-zero-then-rotate", 1),
            debtZeroThenRotateTrace,
            frozenStrategies);
        SaveStep insertBurstStep0 = new([new CreateObject(10, 10)]);
        SaveStep insertBurstStep1 = new([new UpdateObject(10, 10, 10)]);
        CreateObject insert100 = new(100, 300);
        CreateObject insert101 = new(101, 300);
        CreateObject insert102 = new(102, 300);
        CreateObject insert103 = new(103, 300);
        WorkloadTrace insertBurstThreeOneTrace = new(
            scenarioName: "insert-burst-three-one",
            generatorId: "handwritten",
            generatorVersion: 1,
            seed: 0,
            [
                insertBurstStep0,
                insertBurstStep1,
                new SaveStep([insert100, insert101, insert102]),
                new SaveStep([insert103]),
            ]);
        WorkloadTrace insertBurstTwoTwoTrace = new(
            scenarioName: "insert-burst-two-two",
            generatorId: "handwritten",
            generatorVersion: 1,
            seed: 0,
            [
                insertBurstStep0,
                insertBurstStep1,
                new SaveStep([insert100, insert101]),
                new SaveStep([insert102, insert103]),
            ]);
        BenchmarkV1CaseDefinition[] insertBurstThreeOneCases =
            CreateStrategyCases(
                new BenchmarkComponentIdentityV1(
                    "insert-burst-three-one",
                    1),
                insertBurstThreeOneTrace,
                frozenStrategies);
        BenchmarkV1CaseDefinition[] insertBurstTwoTwoCases =
            CreateStrategyCases(
                new BenchmarkComponentIdentityV1(
                    "insert-burst-two-two",
                    1),
                insertBurstTwoTwoTrace,
                frozenStrategies);
        BenchmarkV1CaseDefinition[] activeHundredMixedCases =
            CreateStrategyCases(
                new BenchmarkComponentIdentityV1("active-hundred-mixed", 1),
                activeHundred.Trace,
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
        SaveStep sizeSkewStep1 = new([new CreateObject(1001, 1)]);
        WorkloadTrace sizeSkewLowIdSmallTrace = new(
            scenarioName: "size-skew-low-id-small",
            generatorId: "handwritten",
            generatorVersion: 1,
            seed: 0,
            [
                new SaveStep([
                    new CreateObject(10, 20),
                    new CreateObject(20, 100),
                ]),
                sizeSkewStep1,
            ]);
        WorkloadTrace sizeSkewLowIdLargeTrace = new(
            scenarioName: "size-skew-low-id-large",
            generatorId: "handwritten",
            generatorVersion: 1,
            seed: 0,
            [
                new SaveStep([
                    new CreateObject(10, 100),
                    new CreateObject(20, 20),
                ]),
                sizeSkewStep1,
            ]);
        BenchmarkV1CaseDefinition[] sizeSkewLowIdSmallCases = CreateStrategyCases(
            new BenchmarkComponentIdentityV1("size-skew-low-id-small", 1),
            sizeSkewLowIdSmallTrace,
            frozenStrategies);
        BenchmarkV1CaseDefinition[] sizeSkewLowIdLargeCases = CreateStrategyCases(
            new BenchmarkComponentIdentityV1("size-skew-low-id-large", 1),
            sizeSkewLowIdLargeTrace,
            frozenStrategies);
        SaveStep lifecycleStep0 = new([
            new CreateObject(10, 100),
            new CreateObject(20, 100),
        ]);
        SaveStep lifecycleCreate100 = new([new CreateObject(100, 400)]);
        SaveStep lifecycleCreate101 = new([new CreateObject(101, 400)]);
        SaveStep lifecycleRemove100 = new([new RemoveObject(100)]);
        SaveStep lifecycleRemove101 = new([new RemoveObject(101)]);
        WorkloadTrace lifecycleTransientOverlapTrace = new(
            scenarioName: "lifecycle-transient-overlap",
            generatorId: "handwritten",
            generatorVersion: 1,
            seed: 0,
            [
                lifecycleStep0,
                lifecycleCreate100,
                lifecycleCreate101,
                lifecycleRemove100,
                lifecycleRemove101,
            ]);
        WorkloadTrace lifecycleTransientSerialTrace = new(
            scenarioName: "lifecycle-transient-serial",
            generatorId: "handwritten",
            generatorVersion: 1,
            seed: 0,
            [
                lifecycleStep0,
                lifecycleCreate100,
                lifecycleRemove100,
                lifecycleCreate101,
                lifecycleRemove101,
            ]);
        BenchmarkV1CaseDefinition[] lifecycleTransientOverlapCases =
            CreateStrategyCases(
                new BenchmarkComponentIdentityV1(
                    "lifecycle-transient-overlap",
                    1),
                lifecycleTransientOverlapTrace,
                frozenStrategies);
        BenchmarkV1CaseDefinition[] lifecycleTransientSerialCases =
            CreateStrategyCases(
                new BenchmarkComponentIdentityV1(
                    "lifecycle-transient-serial",
                    1),
                lifecycleTransientSerialTrace,
                frozenStrategies);
        SaveStep debtGranularityStep0 = new([
            new CreateObject(10, 100),
            new CreateObject(20, 100),
            new CreateObject(30, 100),
            new CreateObject(40, 300),
        ]);
        SaveStep debtGranularityCreate1001 = new([new CreateObject(1001, 1)]);
        SaveStep debtGranularityRewriteThreeSmall = new([
            new UpdateObject(10, 100, 100),
            new UpdateObject(20, 100, 100),
            new UpdateObject(30, 100, 100),
        ]);
        SaveStep debtGranularityRewriteSingleLarge = new([
            new UpdateObject(40, 300, 300),
        ]);
        SaveStep debtGranularityReconvergeThreeSmall = new([
            new UpdateObject(10, 100, 100),
            new UpdateObject(20, 100, 100),
            new UpdateObject(30, 100, 100),
        ]);
        WorkloadTrace previousDebtGranularitySingleLargeTrace = new(
            scenarioName: "previous-debt-granularity-single-large",
            generatorId: "handwritten",
            generatorVersion: 1,
            seed: 0,
            [
                debtGranularityStep0,
                debtGranularityRewriteThreeSmall,
                debtGranularityCreate1001,
                debtGranularityRewriteSingleLarge,
                debtGranularityReconvergeThreeSmall,
            ]);
        WorkloadTrace previousDebtGranularityThreeSmallTrace = new(
            scenarioName: "previous-debt-granularity-three-small",
            generatorId: "handwritten",
            generatorVersion: 1,
            seed: 0,
            [
                debtGranularityStep0,
                debtGranularityRewriteSingleLarge,
                debtGranularityCreate1001,
                debtGranularityRewriteThreeSmall,
                debtGranularityReconvergeThreeSmall,
            ]);
        BenchmarkV1CaseDefinition[] previousDebtGranularitySingleLargeCases =
            CreateStrategyCases(
                new BenchmarkComponentIdentityV1(
                    "previous-debt-granularity-single-large",
                    1),
                previousDebtGranularitySingleLargeTrace,
                frozenStrategies);
        BenchmarkV1CaseDefinition[] previousDebtGranularityThreeSmallCases =
            CreateStrategyCases(
                new BenchmarkComponentIdentityV1(
                    "previous-debt-granularity-three-small",
                    1),
                previousDebtGranularityThreeSmallTrace,
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
                .. debtZeroBeforeRotateCases,
                .. debtZeroThenRotateCases,
                .. insertBurstThreeOneCases,
                .. insertBurstTwoTwoCases,
                .. activeHundredMixedCases,
                .. mixedSmallCases,
                .. thresholdBandCases,
                .. debtShareDilutionCases,
                .. localityLowIdCases,
                .. localityHighIdCases,
                .. sizeSkewLowIdSmallCases,
                .. sizeSkewLowIdLargeCases,
                .. lifecycleTransientOverlapCases,
                .. lifecycleTransientSerialCases,
                .. previousDebtGranularitySingleLargeCases,
                .. previousDebtGranularityThreeSmallCases,
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

    private static (WorkloadTrace BeforeRotate, WorkloadTrace ThroughRotate)
        CreateDebtZeroRotateHorizonPair() {
        SaveStep step0 = new([
            new CreateObject(10, 100),
            new CreateObject(20, 200),
            new CreateObject(30, 300),
        ]);
        SaveStep step1 = new([new CreateObject(1001, 1)]);
        SaveStep step2 = new([new CreateObject(1002, 1)]);
        SaveStep step3 = new([new CreateObject(1003, 1)]);
        SaveStep step4 = new([new CreateObject(1004, 1)]);
        return (
            new WorkloadTrace(
                scenarioName: "debt-zero-before-rotate",
                generatorId: "handwritten",
                generatorVersion: 1,
                seed: 0,
                [step0, step1, step2, step3]),
            new WorkloadTrace(
                scenarioName: "debt-zero-then-rotate",
                generatorId: "handwritten",
                generatorVersion: 1,
                seed: 0,
                [step0, step1, step2, step3, step4]));
    }

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

    private static ScenarioDefinition CreateActiveHundredMixedDefinition() => new(
        name: "active-hundred-mixed",
        seed: 0xA11C_E100,
        stepCount: 65,
        initialPopulation: 100,
        createPerLaterStep: 0,
        maxUpdatePerLaterStep: 60,
        maxRemovePerLaterStep: 0,
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
