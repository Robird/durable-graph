using Atelia.TwoLegRotationProbe.Planning;

namespace Atelia.TwoLegRotationProbe.Benchmarking;

internal static class BenchmarkV1TreatmentIdentities {
    public static readonly BenchmarkComponentIdentityV1 DebtZeroThenRotate =
        new("debt-zero-then-rotate", 1);

    public static readonly BenchmarkComponentIdentityV1 DeltaNoMigration =
        new("delta-no-migration", 1);

    public static readonly BenchmarkComponentIdentityV1 DeltaPacedOneDebtByObjectId =
        new("delta-paced-one-debt-by-object-id", 1);
}

/// <summary>
/// Closed benchmark-v1 treatment registry. It deliberately accepts only normalized
/// source facts: no step index, future trace, feasibility, or candidate observation.
/// </summary>
internal static class BenchmarkV1TreatmentSelector {
    public static void Validate(
        BenchmarkComponentIdentityV1 targetTreatment,
        BenchmarkComponentIdentityV1 decisionTreatment) {
        ArgumentNullException.ThrowIfNull(targetTreatment);
        ArgumentNullException.ThrowIfNull(decisionTreatment);

        if (targetTreatment != BenchmarkV1TreatmentIdentities.DebtZeroThenRotate) {
            throw new ArgumentException(
                $"Benchmark v1 does not recognize target treatment " +
                $"'{targetTreatment.Id}/{targetTreatment.Version}'.",
                nameof(targetTreatment));
        }

        if (decisionTreatment != BenchmarkV1TreatmentIdentities.DeltaNoMigration &&
            decisionTreatment !=
                BenchmarkV1TreatmentIdentities.DeltaPacedOneDebtByObjectId) {
            throw new ArgumentException(
                $"Benchmark v1 does not recognize decision treatment " +
                $"'{decisionTreatment.Id}/{decisionTreatment.Version}'.",
                nameof(decisionTreatment));
        }
    }

    public static BenchmarkV1StepSelection Select(
        BenchmarkComponentIdentityV1 targetTreatment,
        BenchmarkComponentIdentityV1 decisionTreatment,
        NormalizedSaveFacts facts) {
        ArgumentNullException.ThrowIfNull(facts);
        Validate(targetTreatment, decisionTreatment);

        bool hasPreviousDebt = facts.ParentLive.Values.Any(source =>
            IsPreviousDebt(facts, source));
        CandidateTarget target = hasPreviousDebt
            ? CandidateTarget.StayB
            : CandidateTarget.RotateC;

        uint[] migrations = decisionTreatment ==
            BenchmarkV1TreatmentIdentities.DeltaPacedOneDebtByObjectId
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
