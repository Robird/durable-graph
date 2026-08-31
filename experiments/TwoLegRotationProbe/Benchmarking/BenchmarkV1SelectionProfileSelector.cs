using Atelia.TwoLegRotationProbe.Planning;
using Atelia.TwoLegRotationProbe.Policies;

namespace Atelia.TwoLegRotationProbe.Benchmarking;

internal static class BenchmarkV1SelectionProfiles {
    public static readonly BenchmarkV1SelectionProfileDefinition
        DebtZeroThenRotateDeltaNoMigration = new(
            new BenchmarkComponentIdentityV1(
                "debt-zero-then-rotate-delta-no-migration",
                1),
            BenchmarkV1SelectionProfileKind.DebtZeroThenRotateNoMigration);

    public static readonly BenchmarkV1SelectionProfileDefinition
        DebtZeroThenRotateDeltaPacedOneDebtByObjectId = new(
            new BenchmarkComponentIdentityV1(
                "debt-zero-then-rotate-delta-paced-one-debt-by-object-id",
                1),
            BenchmarkV1SelectionProfileKind.DebtZeroThenRotatePaced);

    public static readonly BenchmarkV1SelectionProfileDefinition
        ReadAmplificationBaseBudgetR3B5Percent = new(
            new BenchmarkComponentIdentityV1(
                "read-amplification-base-budget-r3-b5pct",
                1),
            BenchmarkV1SelectionProfileKind.ReadAmplificationBaseBudget,
            new ReadAmplificationBaseBudgetPolicyParameters(3m, 0.05m));

    public static readonly BenchmarkV1SelectionProfileDefinition
        ReadAmplificationBaseBudgetR4B4Percent = new(
            new BenchmarkComponentIdentityV1(
                "read-amplification-base-budget-r4-b4pct",
                1),
            BenchmarkV1SelectionProfileKind.ReadAmplificationBaseBudget,
            new ReadAmplificationBaseBudgetPolicyParameters(4m, 0.04m));

    private static readonly BenchmarkV1SelectionProfileDefinition[] All = [
        DebtZeroThenRotateDeltaNoMigration,
        DebtZeroThenRotateDeltaPacedOneDebtByObjectId,
        ReadAmplificationBaseBudgetR3B5Percent,
        ReadAmplificationBaseBudgetR4B4Percent,
    ];

    public static BenchmarkV1SelectionProfileDefinition Resolve(
        BenchmarkComponentIdentityV1 selectionProfile) {
        ArgumentNullException.ThrowIfNull(selectionProfile);

        return All.SingleOrDefault(profile => profile.Identity == selectionProfile) ??
            throw new ArgumentException(
                $"Benchmark v1 does not recognize selection profile " +
                $"'{selectionProfile.Id}/{selectionProfile.Version}'.",
                nameof(selectionProfile));
    }
}

internal enum BenchmarkV1SelectionProfileKind {
    DebtZeroThenRotateNoMigration,
    DebtZeroThenRotatePaced,
    ReadAmplificationBaseBudget,
}

internal sealed class BenchmarkV1SelectionProfileDefinition {
    public BenchmarkV1SelectionProfileDefinition(
        BenchmarkComponentIdentityV1 identity,
        BenchmarkV1SelectionProfileKind kind,
        ReadAmplificationBaseBudgetPolicyParameters? adaptiveParameters = null) {
        Identity = identity ?? throw new ArgumentNullException(nameof(identity));
        if (!Enum.IsDefined(kind)) {
            throw new ArgumentOutOfRangeException(nameof(kind));
        }

        if ((kind == BenchmarkV1SelectionProfileKind.ReadAmplificationBaseBudget) !=
            (adaptiveParameters is not null)) {
            throw new ArgumentException(
                "Only an adaptive selection profile may carry adaptive parameters.",
                nameof(adaptiveParameters));
        }

        Kind = kind;
        AdaptiveParameters = adaptiveParameters;
    }

    public BenchmarkComponentIdentityV1 Identity { get; }

    public BenchmarkV1SelectionProfileKind Kind { get; }

    public ReadAmplificationBaseBudgetPolicyParameters? AdaptiveParameters { get; }
}

/// <summary>
/// Closed benchmark-v1 selection-profile registry. It deliberately accepts only
/// normalized source facts: no step index, future trace, feasibility, or candidate
/// observation.
/// </summary>
internal static class BenchmarkV1SelectionProfileSelector {
    public static void Validate(BenchmarkComponentIdentityV1 selectionProfile) =>
        _ = BenchmarkV1SelectionProfiles.Resolve(selectionProfile);

    public static BenchmarkV1StepSelection Select(
        BenchmarkComponentIdentityV1 selectionProfile,
        NormalizedSaveFacts facts) {
        ArgumentNullException.ThrowIfNull(facts);
        BenchmarkV1SelectionProfileDefinition profile =
            BenchmarkV1SelectionProfiles.Resolve(selectionProfile);
        return profile.Kind switch {
            BenchmarkV1SelectionProfileKind.DebtZeroThenRotateNoMigration =>
                SelectDebtZeroThenRotate(facts, paced: false),
            BenchmarkV1SelectionProfileKind.DebtZeroThenRotatePaced =>
                SelectDebtZeroThenRotate(facts, paced: true),
            BenchmarkV1SelectionProfileKind.ReadAmplificationBaseBudget =>
                SelectAdaptive(
                    facts,
                    profile.AdaptiveParameters ?? throw new InvalidDataException(
                        "An adaptive profile is missing its parameters.")),
            _ => throw new InvalidDataException(
                "The selection profile has an unknown behavior kind."),
        };
    }

    private static BenchmarkV1StepSelection SelectAdaptive(
        NormalizedSaveFacts facts,
        ReadAmplificationBaseBudgetPolicyParameters parameters) {
        ReadAmplificationBaseBudgetPolicyProjection projection =
            ReadAmplificationBaseBudgetPolicyProjection.Create(facts);
        ReadAmplificationBaseBudgetPolicySelection selection =
            ReadAmplificationBaseBudgetPolicy.Select(projection, parameters);
        return new BenchmarkV1StepSelection(
            selection.Target,
            selection.StayB,
            selection.RotateC);
    }

    private static BenchmarkV1StepSelection SelectDebtZeroThenRotate(
        NormalizedSaveFacts facts,
        bool paced) {
        bool hasPreviousDebt = facts.ParentLive.Values.Any(source =>
            IsPreviousDebt(facts, source));
        CandidateTarget target = hasPreviousDebt
            ? CandidateTarget.StayB
            : CandidateTarget.RotateC;

        uint[] migrations = paced
            ? facts.NoChanges
                .Where(noChange => IsPreviousDebt(facts, noChange.Source))
                .Select(static noChange => noChange.ObjectId)
                .Order()
                .Take(1)
                .ToArray()
            : [];

        StayBSaveDecision stayB = new(
            facts.Updates.Select(static update => new UpdateWriteDecision(
                update.ObjectId,
                UpdateWriteMode.Delta)),
            migrations);
        RotateCSaveDecision rotateC = new(
            facts.Updates
                .Where(update => !IsPreviousDebt(facts, update.Source))
                .Select(static update => new UpdateWriteDecision(
                    update.ObjectId,
                    UpdateWriteMode.Delta)),
            []);
        return new BenchmarkV1StepSelection(target, stayB, rotateC);
    }

    private static bool IsPreviousDebt(
        NormalizedSaveFacts facts,
        SourceObjectFact source) =>
        source.BaseAddress.FileNumber == facts.PreviousFileNumber;
}

internal sealed record BenchmarkV1StepSelection(
    CandidateTarget Target,
    StayBSaveDecision StayB,
    RotateCSaveDecision RotateC);
