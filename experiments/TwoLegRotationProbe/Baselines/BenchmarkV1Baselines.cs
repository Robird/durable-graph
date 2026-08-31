using System.Collections.ObjectModel;
using Atelia.TwoLegRotationProbe.Arena;
using Atelia.TwoLegRotationProbe.Benchmarking;
using Atelia.TwoLegRotationProbe.Policies;

namespace Atelia.TwoLegRotationProbe.Baselines;

/// <summary>
/// Organizer-ready bindings for the four historical benchmark-v1 strategies.
/// Their implementations live entirely in the Baselines assembly.
/// </summary>
public static class BenchmarkV1Baselines {
    public static StrategyBindingV1 DebtZeroThenRotateDeltaNoMigration { get; } =
        new(
            new BenchmarkComponentIdentityV1(
                "debt-zero-then-rotate-delta-no-migration",
                1),
            "no-migration",
            static () => RunDebtZeroNoMigration);

    public static StrategyBindingV1 DebtZeroThenRotateDeltaPacedOneDebtByObjectId {
        get;
    } = new(
        new BenchmarkComponentIdentityV1(
            "debt-zero-then-rotate-delta-paced-one-debt-by-object-id",
            1),
        "paced",
        static () => RunDebtZeroPaced);

    public static StrategyBindingV1 ReadAmplificationBaseBudgetR3B5Percent {
        get;
    } = new(
        new BenchmarkComponentIdentityV1(
            "read-amplification-base-budget-r3-b5pct",
            1),
        "read-amplification-r3-b5pct",
        static () => RunAdaptiveR3B5Percent);

    public static StrategyBindingV1 ReadAmplificationBaseBudgetR4B4Percent {
        get;
    } = new(
        new BenchmarkComponentIdentityV1(
            "read-amplification-base-budget-r4-b4pct",
            1),
        "read-amplification-r4-b4pct",
        static () => RunAdaptiveR4B4Percent);

    private static readonly ReadOnlyCollection<StrategyBindingV1> FrozenAll =
        Array.AsReadOnly(new[] {
            DebtZeroThenRotateDeltaNoMigration,
            DebtZeroThenRotateDeltaPacedOneDebtByObjectId,
            ReadAmplificationBaseBudgetR3B5Percent,
            ReadAmplificationBaseBudgetR4B4Percent,
        });

    public static IReadOnlyList<StrategyBindingV1> All => FrozenAll;

    internal static StrategySelectionV1 Select(
        BenchmarkComponentIdentityV1 identity,
        StrategyStepViewV1 view) {
        ArgumentNullException.ThrowIfNull(identity);
        ArgumentNullException.ThrowIfNull(view);
        if (identity == DebtZeroThenRotateDeltaNoMigration.Identity) {
            return SelectDebtZeroThenRotate(view, paced: false);
        }

        if (identity == DebtZeroThenRotateDeltaPacedOneDebtByObjectId.Identity) {
            return SelectDebtZeroThenRotate(view, paced: true);
        }

        if (identity == ReadAmplificationBaseBudgetR3B5Percent.Identity) {
            return SelectAdaptive(
                view,
                new ReadAmplificationBaseBudgetPolicyParameters(3m, 0.05m));
        }

        if (identity == ReadAmplificationBaseBudgetR4B4Percent.Identity) {
            return SelectAdaptive(
                view,
                new ReadAmplificationBaseBudgetPolicyParameters(4m, 0.04m));
        }

        throw new ArgumentException(
            $"Baselines does not recognize strategy '{identity.Id}/{identity.Version}'.",
            nameof(identity));
    }

    private static StrategyRunProductV1 RunDebtZeroNoMigration(
        StrategyRunContextV1 context) => Run(
            context,
            static view => SelectDebtZeroThenRotate(view, paced: false));

    private static StrategyRunProductV1 RunDebtZeroPaced(
        StrategyRunContextV1 context) => Run(
            context,
            static view => SelectDebtZeroThenRotate(view, paced: true));

    private static StrategyRunProductV1 RunAdaptiveR3B5Percent(
        StrategyRunContextV1 context) => Run(
            context,
            static view => SelectAdaptive(
                view,
                new ReadAmplificationBaseBudgetPolicyParameters(3m, 0.05m)));

    private static StrategyRunProductV1 RunAdaptiveR4B4Percent(
        StrategyRunContextV1 context) => Run(
            context,
            static view => SelectAdaptive(
                view,
                new ReadAmplificationBaseBudgetPolicyParameters(4m, 0.04m)));

    private static StrategyRunProductV1 Run(
        StrategyRunContextV1 context,
        Func<StrategyStepViewV1, StrategySelectionV1> select) {
        ArgumentNullException.ThrowIfNull(context);
        while (context.CurrentStep is StrategyStepViewV1 view) {
            StrategyCommitStatusV1 status = context.Commit(select(view));
            if (status is StrategyCommitStatusV1.AppliedStayB or
                StrategyCommitStatusV1.AppliedRotateC) {
                continue;
            }

            break;
        }

        return context.Complete();
    }

    private static StrategySelectionV1 SelectAdaptive(
        StrategyStepViewV1 view,
        ReadAmplificationBaseBudgetPolicyParameters parameters) =>
        ReadAmplificationBaseBudgetPolicy.Select(
            ReadAmplificationBaseBudgetPolicyProjection.Create(view),
            parameters).Selection;

    private static StrategySelectionV1 SelectDebtZeroThenRotate(
        StrategyStepViewV1 view,
        bool paced) {
        StrategyTargetV1 target = view.HasParentPreviousDebt
            ? StrategyTargetV1.StayB
            : StrategyTargetV1.RotateC;
        uint[] migrations = paced
            ? view.Objects
                .Where(static fact =>
                    fact.Kind == StrategyObjectKindV1.NoChange &&
                    fact.SourceIsPreviousDependent is true)
                .Select(static fact => fact.ObjectId)
                .Take(1)
                .ToArray()
            : [];
        StrategyStayDecisionV1 stay = new(
            view.Objects
                .Where(static fact => fact.Kind == StrategyObjectKindV1.Update)
                .Select(static fact => new StrategyUpdateWriteDecisionV1(
                    fact.ObjectId,
                    StrategyUpdateWriteModeV1.Delta)),
            migrations);
        StrategyRotateDecisionV1 rotate = new(
            view.Objects
                .Where(static fact =>
                    fact.Kind == StrategyObjectKindV1.Update &&
                    fact.SourceIsPreviousDependent is false)
                .Select(static fact => new StrategyUpdateWriteDecisionV1(
                    fact.ObjectId,
                    StrategyUpdateWriteModeV1.Delta)),
            []);
        return new StrategySelectionV1(target, stay, rotate);
    }
}
