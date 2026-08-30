using Atelia.TwoLegRotationProbe.Planning;

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
        CandidateTarget target = SelectTarget(projection, parameters);
        (StayBSaveDecision stayB, uint? progressOverrideObjectId) =
            SelectStayB(projection, parameters, budgetBytes);
        RotateCSaveDecision rotateC = SelectRotateC(
            projection,
            parameters,
            budgetBytes);
        return new ReadAmplificationBaseBudgetPolicySelection(
            projection,
            parameters,
            target,
            budgetBytes,
            progressOverrideObjectId,
            stayB,
            rotateC);
    }

    private static long GetPreferredBasePayloadBudgetBytes(
        long graphBasePayloadBytes,
        decimal budgetFraction) {
        decimal unrounded = (decimal)graphBasePayloadBytes * budgetFraction;
        return checked((long)decimal.Floor(unrounded));
    }

    private static CandidateTarget SelectTarget(
        ReadAmplificationBaseBudgetPolicyProjection projection,
        ReadAmplificationBaseBudgetPolicyParameters parameters) {
        long graphBytes = projection.PostLiveGraphBasePayloadBytes;
        if (graphBytes == 0) {
            return CandidateTarget.RotateC;
        }

        decimal triggerBytes = (decimal)graphBytes * parameters.BaseBudgetFraction;
        return (decimal)projection.ADependentEvacuationBasePayloadBytes < triggerBytes
            ? CandidateTarget.RotateC
            : CandidateTarget.StayB;
    }

    private static (StayBSaveDecision Decision, uint? ProgressOverrideObjectId)
        SelectStayB(
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

        long remainingBudgetBytes = budgetBytes;
        uint? progressOverrideObjectId = null;
        bool hasADebt = projection.PostLiveObjects.Any(static fact =>
            fact.IsADependent &&
            fact.Kind != ReadAmplificationBaseBudgetPolicyObjectKind.Insert);
        bool dominantUpdateRetiresADebt = updates.Any(fact =>
            fact.IsADependent && baseUpdateObjectIds.Contains(fact.ObjectId));
        if (hasADebt && !dominantUpdateRetiresADebt) {
            ReadAmplificationBaseBudgetPolicyObjectFact? progress = projection
                .PostLiveObjects
                .Where(static fact =>
                    fact.IsADependent &&
                    fact.Kind == ReadAmplificationBaseBudgetPolicyObjectKind.NoChange)
                .OrderBy(static fact => fact, AmplificationComparer.Instance)
                .FirstOrDefault();
            progress ??= updates
                .Where(static fact => fact.IsADependent)
                .OrderBy(static fact => fact, AmplificationComparer.Instance)
                .First();

            progressOverrideObjectId = progress.ObjectId;
            SelectBase(progress, baseUpdateObjectIds, migrationObjectIds);
            remainingBudgetBytes = ConsumeSoftBudget(
                remainingBudgetBytes,
                progress.PostSaveBasePayloadBytes);
        }

        IEnumerable<ReadAmplificationBaseBudgetPolicyObjectFact> discretionary =
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
                .OrderBy(static fact => fact, AmplificationComparer.Instance);
        foreach (ReadAmplificationBaseBudgetPolicyObjectFact fact in discretionary) {
            if (fact.PostSaveBasePayloadBytes > remainingBudgetBytes) {
                continue;
            }

            SelectBase(fact, baseUpdateObjectIds, migrationObjectIds);
            remainingBudgetBytes -= fact.PostSaveBasePayloadBytes;
        }

        StayBSaveDecision decision = new(
            updates.Select(fact => new UpdateWriteDecision(
                fact.ObjectId,
                baseUpdateObjectIds.Contains(fact.ObjectId)
                    ? UpdateWriteMode.Base
                    : UpdateWriteMode.Delta)),
            migrationObjectIds);
        return (decision, progressOverrideObjectId);
    }

    private static RotateCSaveDecision SelectRotateC(
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
        long remainingBudgetBytes = ConsumeSoftBudget(
            budgetBytes,
            projection.ADependentEvacuationBasePayloadBytes);

        IEnumerable<ReadAmplificationBaseBudgetPolicyObjectFact> discretionary =
            bContainedUpdates
                .Where(fact =>
                    !baseUpdateObjectIds.Contains(fact.ObjectId) &&
                    IsAboveAmplificationLimit(
                        fact,
                        parameters.ReadAmplificationLimit))
                .OrderBy(static fact => fact, AmplificationComparer.Instance);
        foreach (ReadAmplificationBaseBudgetPolicyObjectFact fact in discretionary) {
            if (fact.PostSaveBasePayloadBytes > remainingBudgetBytes) {
                continue;
            }

            baseUpdateObjectIds.Add(fact.ObjectId);
            remainingBudgetBytes -= fact.PostSaveBasePayloadBytes;
        }

        return new RotateCSaveDecision(
            bContainedUpdates.Select(fact => new UpdateWriteDecision(
                fact.ObjectId,
                baseUpdateObjectIds.Contains(fact.ObjectId)
                    ? UpdateWriteMode.Base
                    : UpdateWriteMode.Delta)),
            []);
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
