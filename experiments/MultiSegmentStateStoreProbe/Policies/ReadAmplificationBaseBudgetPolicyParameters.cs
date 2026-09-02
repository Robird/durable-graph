namespace Atelia.MultiSegmentStateStoreProbe.Policies;

internal sealed class ReadAmplificationBaseBudgetPolicyParameters {
    public ReadAmplificationBaseBudgetPolicyParameters(
        decimal readAmplificationLimit,
        decimal baseBudgetFraction) {
        if (readAmplificationLimit < 1m) {
            throw new ArgumentOutOfRangeException(
                nameof(readAmplificationLimit),
                readAmplificationLimit,
                "The read-amplification limit must be at least one.");
        }

        if (baseBudgetFraction <= 0m || baseBudgetFraction > 1m) {
            throw new ArgumentOutOfRangeException(
                nameof(baseBudgetFraction),
                baseBudgetFraction,
                "The Base-budget fraction must be greater than zero and at most one.");
        }

        ReadAmplificationLimit = readAmplificationLimit;
        BaseBudgetFraction = baseBudgetFraction;
    }

    public decimal ReadAmplificationLimit { get; }

    public decimal BaseBudgetFraction { get; }
}
