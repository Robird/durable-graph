namespace Atelia.MultiSegmentStateStoreProbe.Policies;

/// <summary>
/// Pure object-representation selector over synthetic payload proxies. It has no
/// file-placement, OVD-mode, sizing, admission, append, retry, or publication authority.
/// </summary>
internal static class ReadAmplificationBaseBudgetPolicy {
    public static ReadAmplificationBaseBudgetPolicySelection Select(
        ReadAmplificationBaseBudgetPolicyInput input,
        ReadAmplificationBaseBudgetPolicyParameters parameters) {
        ArgumentNullException.ThrowIfNull(input);
        ArgumentNullException.ThrowIfNull(parameters);

        long budgetBytes = checked((long)decimal.Floor(
            (decimal)input.PostLiveGraphBasePayloadBytes *
            parameters.BaseBudgetFraction));
        HashSet<uint> baseUpdateObjectIds = input.Facts
            .Where(IsDominantBaseUpdate)
            .Select(static fact => fact.ObjectId)
            .ToHashSet();
        HashSet<uint> sameStateRebaseObjectIds = [];

        ReadAmplificationBaseBudgetPolicyFact[] motivated = input.Facts
            .Where(fact => IsMotivated(
                fact,
                baseUpdateObjectIds,
                parameters.ReadAmplificationLimit))
            .OrderBy(static fact => fact, AmplificationComparer.Instance)
            .ToArray();
        long selectedBytes = 0;
        for (int index = 0; index < motivated.Length; index++) {
            ReadAmplificationBaseBudgetPolicyFact fact = motivated[index];
            long nextSelectedBytes = checked(
                selectedBytes + fact.PostSaveBasePayloadBytes!.Value);
            if (nextSelectedBytes > budgetBytes) {
                if (index == 0) {
                    SelectBase(fact, baseUpdateObjectIds, sameStateRebaseObjectIds);
                }

                break;
            }

            SelectBase(fact, baseUpdateObjectIds, sameStateRebaseObjectIds);
            selectedBytes = nextSelectedBytes;
        }

        return new ReadAmplificationBaseBudgetPolicySelection(
            input.Facts
                .Where(static fact => fact.Kind == ObjectRepresentationFactKind.Update)
                .Select(fact => new UpdateRepresentationDecision(
                    fact.ObjectId,
                    baseUpdateObjectIds.Contains(fact.ObjectId)
                        ? ObjectVersionWriteMode.Base
                        : ObjectVersionWriteMode.Delta)),
            sameStateRebaseObjectIds,
            budgetBytes);
    }

    private static bool IsDominantBaseUpdate(
        ReadAmplificationBaseBudgetPolicyFact fact) =>
        fact.Kind == ObjectRepresentationFactKind.Update &&
        fact.PostSaveBasePayloadBytes!.Value <= fact.DeltaPayloadBytes!.Value;

    private static bool IsMotivated(
        ReadAmplificationBaseBudgetPolicyFact fact,
        IReadOnlySet<uint> dominantBaseUpdateObjectIds,
        decimal limit) {
        if (fact.Kind == ObjectRepresentationFactKind.Update &&
            !dominantBaseUpdateObjectIds.Contains(fact.ObjectId)) {
            return IsStrictlyAboveLimit(fact, limit);
        }

        return fact.Kind == ObjectRepresentationFactKind.NoChange &&
            IsStrictlyAboveLimit(fact, limit);
    }

    private static bool IsStrictlyAboveLimit(
        ReadAmplificationBaseBudgetPolicyFact fact,
        decimal limit) {
        (long numerator, int denominator, bool infinity) = NormalizeRatio(fact);
        if (infinity) {
            return true;
        }

        if (limit >= long.MaxValue) {
            return false;
        }

        return (decimal)numerator > (decimal)denominator * limit;
    }

    private static void SelectBase(
        ReadAmplificationBaseBudgetPolicyFact fact,
        ISet<uint> baseUpdateObjectIds,
        ISet<uint> sameStateRebaseObjectIds) {
        if (fact.Kind == ObjectRepresentationFactKind.Update) {
            baseUpdateObjectIds.Add(fact.ObjectId);
            return;
        }

        if (fact.Kind == ObjectRepresentationFactKind.NoChange) {
            sameStateRebaseObjectIds.Add(fact.ObjectId);
            return;
        }

        throw new InvalidDataException(
            $"Object {fact.ObjectId} cannot be a read-motivated Base candidate.");
    }

    private static (long Numerator, int Denominator, bool Infinity) NormalizeRatio(
        ReadAmplificationBaseBudgetPolicyFact fact) {
        long numerator = fact.Kind switch {
            ObjectRepresentationFactKind.Update => checked(
                fact.SourceReconstructionPayloadBytes!.Value +
                fact.DeltaPayloadBytes!.Value),
            ObjectRepresentationFactKind.NoChange =>
                fact.SourceReconstructionPayloadBytes!.Value,
            _ => throw new InvalidDataException(
                $"Object {fact.ObjectId} has no read-amplification ratio."),
        };
        int denominator = fact.PostSaveBasePayloadBytes!.Value;
        if (denominator != 0) {
            return (numerator, denominator, false);
        }

        return numerator == 0
            ? (1, 1, false)
            : (0, 1, true);
    }

    private sealed class AmplificationComparer :
        IComparer<ReadAmplificationBaseBudgetPolicyFact> {
        public static AmplificationComparer Instance { get; } = new();

        public int Compare(
            ReadAmplificationBaseBudgetPolicyFact? x,
            ReadAmplificationBaseBudgetPolicyFact? y) {
            if (ReferenceEquals(x, y)) {
                return 0;
            }

            ArgumentNullException.ThrowIfNull(x);
            ArgumentNullException.ThrowIfNull(y);
            int ratio = -CompareRatio(x, y);
            return ratio != 0 ? ratio : x.ObjectId.CompareTo(y.ObjectId);
        }

        private static int CompareRatio(
            ReadAmplificationBaseBudgetPolicyFact x,
            ReadAmplificationBaseBudgetPolicyFact y) {
            (long xNumerator, int xDenominator, bool xInfinity) = NormalizeRatio(x);
            (long yNumerator, int yDenominator, bool yInfinity) = NormalizeRatio(y);
            if (xInfinity || yInfinity) {
                return xInfinity.CompareTo(yInfinity);
            }

            decimal left = (decimal)xNumerator * yDenominator;
            decimal right = (decimal)yNumerator * xDenominator;
            return left.CompareTo(right);
        }
    }
}
