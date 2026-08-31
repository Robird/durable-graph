using Atelia.TwoLegRotationProbe.Arena;

namespace Atelia.TwoLegRotationProbe.Policies;

/// <summary>
/// Pure v0 payload-heuristic selector. It does not construct, size, admit, or
/// apply a candidate.
/// </summary>
internal static class ReadAmplificationBaseBudgetPolicy {
    public static ReadAmplificationBaseBudgetPolicySelection Select(
        ReadAmplificationBaseBudgetPolicyProjection projection,
        ReadAmplificationBaseBudgetPolicyParameters parameters) {
        ArgumentNullException.ThrowIfNull(projection);
        ArgumentNullException.ThrowIfNull(parameters);

        long budgetBytes = GetPreferredBasePayloadBudgetBytes(
            projection.PostLiveGraphBasePayloadBytes,
            parameters.BaseBudgetFraction);
        StrategyTargetV1 target = SelectTarget(projection, parameters);
        StrategyStayDecisionV1 stayB = SelectStayB(
            projection,
            parameters,
            budgetBytes);
        StrategyRotateDecisionV1 rotateC = SelectRotateC(
            projection,
            parameters,
            budgetBytes);
        return new ReadAmplificationBaseBudgetPolicySelection(
            projection,
            parameters,
            new StrategySelectionV1(target, stayB, rotateC),
            budgetBytes);
    }

    private static long GetPreferredBasePayloadBudgetBytes(
        long graphBasePayloadBytes,
        decimal budgetFraction) {
        decimal unrounded = (decimal)graphBasePayloadBytes * budgetFraction;
        return checked((long)decimal.Floor(unrounded));
    }

    private static StrategyTargetV1 SelectTarget(
        ReadAmplificationBaseBudgetPolicyProjection projection,
        ReadAmplificationBaseBudgetPolicyParameters parameters) {
        long graphBytes = projection.PostLiveGraphBasePayloadBytes;
        if (graphBytes == 0) {
            return StrategyTargetV1.RotateC;
        }

        decimal triggerBytes = (decimal)graphBytes * parameters.BaseBudgetFraction;
        return (decimal)projection.ADependentEvacuationBasePayloadBytes < triggerBytes
            ? StrategyTargetV1.RotateC
            : StrategyTargetV1.StayB;
    }

    private static StrategyStayDecisionV1 SelectStayB(
            ReadAmplificationBaseBudgetPolicyProjection projection,
            ReadAmplificationBaseBudgetPolicyParameters parameters,
            long budgetBytes) {
        ReadAmplificationBaseBudgetPolicyObjectFact[] updates = projection
            .PostLiveObjects
            .Where(static fact =>
                fact.Kind == ReadAmplificationBaseBudgetPolicyObjectKind.Update)
            .ToArray();
        HashSet<uint> baseUpdateObjectIds = updates
            .Where(IsDominantBaseUpdate)
            .Select(static fact => fact.ObjectId)
            .ToHashSet();
        HashSet<uint> migrationObjectIds = [];

        ReadAmplificationBaseBudgetPolicyObjectFact[] motivated =
            projection.PostLiveObjects
                .Where(static fact =>
                    fact.Kind == ReadAmplificationBaseBudgetPolicyObjectKind.Update ||
                    (fact.Kind ==
                        ReadAmplificationBaseBudgetPolicyObjectKind.NoChange &&
                        fact.IsADependent))
                .Where(fact =>
                    !baseUpdateObjectIds.Contains(fact.ObjectId) &&
                    !migrationObjectIds.Contains(fact.ObjectId) &&
                    IsAboveAmplificationLimit(
                        fact,
                        parameters.ReadAmplificationLimit))
                .OrderBy(static fact => fact, AmplificationComparer.Instance)
                .ToArray();
        foreach (ReadAmplificationBaseBudgetPolicyObjectFact fact in
            SelectBudgetedPrefix(
                motivated,
                budgetBytes,
                allowIndivisibleFirstObject: true)) {
            SelectBase(fact, baseUpdateObjectIds, migrationObjectIds);
        }

        return new StrategyStayDecisionV1(
            updates.Select(fact => new StrategyUpdateWriteDecisionV1(
                fact.ObjectId,
                baseUpdateObjectIds.Contains(fact.ObjectId)
                    ? StrategyUpdateWriteModeV1.Base
                    : StrategyUpdateWriteModeV1.Delta)),
            migrationObjectIds);
    }

    private static StrategyRotateDecisionV1 SelectRotateC(
        ReadAmplificationBaseBudgetPolicyProjection projection,
        ReadAmplificationBaseBudgetPolicyParameters parameters,
        long budgetBytes) {
        ReadAmplificationBaseBudgetPolicyObjectFact[] bContainedUpdates = projection
            .PostLiveObjects
            .Where(static fact =>
                fact.Kind == ReadAmplificationBaseBudgetPolicyObjectKind.Update &&
                !fact.IsADependent)
            .ToArray();
        HashSet<uint> baseUpdateObjectIds = bContainedUpdates
            .Where(IsDominantBaseUpdate)
            .Select(static fact => fact.ObjectId)
            .ToHashSet();
        HashSet<uint> noChangeBaseObjectIds = [];
        long remainingBudgetBytes = ConsumeSoftBudget(
            budgetBytes,
            projection.ADependentEvacuationBasePayloadBytes);

        ReadAmplificationBaseBudgetPolicyObjectFact[] motivated = projection
            .PostLiveObjects
                .Where(fact =>
                    !fact.IsADependent &&
                    (fact.Kind == ReadAmplificationBaseBudgetPolicyObjectKind.Update ||
                        fact.Kind ==
                            ReadAmplificationBaseBudgetPolicyObjectKind.NoChange) &&
                    !baseUpdateObjectIds.Contains(fact.ObjectId) &&
                    IsAboveAmplificationLimit(
                        fact,
                        parameters.ReadAmplificationLimit))
                .OrderBy(static fact => fact, AmplificationComparer.Instance)
                .ToArray();
        foreach (ReadAmplificationBaseBudgetPolicyObjectFact fact in
            SelectBudgetedPrefix(
                motivated,
                remainingBudgetBytes,
                allowIndivisibleFirstObject:
                    projection.ADependentEvacuationBasePayloadBytes == 0)) {
            SelectBase(fact, baseUpdateObjectIds, noChangeBaseObjectIds);
        }

        return new StrategyRotateDecisionV1(
            bContainedUpdates.Select(fact => new StrategyUpdateWriteDecisionV1(
                fact.ObjectId,
                baseUpdateObjectIds.Contains(fact.ObjectId)
                    ? StrategyUpdateWriteModeV1.Base
                    : StrategyUpdateWriteModeV1.Delta)),
            noChangeBaseObjectIds);
    }

    private static IEnumerable<ReadAmplificationBaseBudgetPolicyObjectFact>
        SelectBudgetedPrefix(
            IReadOnlyList<ReadAmplificationBaseBudgetPolicyObjectFact> candidates,
            long budgetBytes,
            bool allowIndivisibleFirstObject) {
        long selectedBytes = 0;
        for (int index = 0; index < candidates.Count; index++) {
            ReadAmplificationBaseBudgetPolicyObjectFact candidate =
                candidates[index];
            long nextSelectedBytes = checked(
                selectedBytes + candidate.PostSaveBasePayloadBytes);
            if (nextSelectedBytes > budgetBytes) {
                if (index == 0 && allowIndivisibleFirstObject) {
                    yield return candidate;
                }

                yield break;
            }

            yield return candidate;
            selectedBytes = nextSelectedBytes;
        }
    }

    private static bool IsDominantBaseUpdate(
        ReadAmplificationBaseBudgetPolicyObjectFact fact) =>
        fact.Kind == ReadAmplificationBaseBudgetPolicyObjectKind.Update &&
        fact.PostSaveBasePayloadBytes <= fact.DeltaPayloadBytes!.Value;

    private static bool IsAboveAmplificationLimit(
        ReadAmplificationBaseBudgetPolicyObjectFact fact,
        decimal limit) {
        long numerator = fact.ReadAmplificationNumeratorBytes!.Value;
        int denominator = fact.PostSaveBasePayloadBytes;
        if (denominator == 0) {
            return numerator > 0;
        }

        if (limit >= long.MaxValue) {
            return false;
        }

        return (decimal)numerator > (decimal)denominator * limit;
    }

    private static void SelectBase(
        ReadAmplificationBaseBudgetPolicyObjectFact fact,
        HashSet<uint> baseUpdateObjectIds,
        HashSet<uint> migrationObjectIds) {
        if (fact.Kind == ReadAmplificationBaseBudgetPolicyObjectKind.Update) {
            baseUpdateObjectIds.Add(fact.ObjectId);
            return;
        }

        if (fact.Kind == ReadAmplificationBaseBudgetPolicyObjectKind.NoChange) {
            migrationObjectIds.Add(fact.ObjectId);
            return;
        }

        throw new InvalidDataException(
            $"Object {fact.ObjectId} cannot be selected as a discretionary Base.");
    }

    private static long ConsumeSoftBudget(long remainingBytes, long consumedBytes) =>
        consumedBytes >= remainingBytes
            ? 0
            : remainingBytes - consumedBytes;

    private sealed class AmplificationComparer :
        IComparer<ReadAmplificationBaseBudgetPolicyObjectFact> {
        public static AmplificationComparer Instance { get; } = new();

        public int Compare(
            ReadAmplificationBaseBudgetPolicyObjectFact? x,
            ReadAmplificationBaseBudgetPolicyObjectFact? y) {
            if (ReferenceEquals(x, y)) {
                return 0;
            }

            ArgumentNullException.ThrowIfNull(x);
            ArgumentNullException.ThrowIfNull(y);
            int descendingRatio = -CompareRatio(x, y);
            return descendingRatio != 0
                ? descendingRatio
                : x.ObjectId.CompareTo(y.ObjectId);
        }

        private static int CompareRatio(
            ReadAmplificationBaseBudgetPolicyObjectFact x,
            ReadAmplificationBaseBudgetPolicyObjectFact y) {
            (long xNumerator, int xDenominator, bool xInfinity) = Normalize(x);
            (long yNumerator, int yDenominator, bool yInfinity) = Normalize(y);
            if (xInfinity || yInfinity) {
                return xInfinity.CompareTo(yInfinity);
            }

            decimal left = (decimal)xNumerator * yDenominator;
            decimal right = (decimal)yNumerator * xDenominator;
            return left.CompareTo(right);
        }

        private static (long Numerator, int Denominator, bool Infinity) Normalize(
            ReadAmplificationBaseBudgetPolicyObjectFact fact) {
            long numerator = fact.ReadAmplificationNumeratorBytes!.Value;
            int denominator = fact.PostSaveBasePayloadBytes;
            if (denominator != 0) {
                return (numerator, denominator, false);
            }

            return numerator == 0
                ? (1, 1, false)
                : (0, 1, true);
        }
    }
}
