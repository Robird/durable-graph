using Atelia.TwoLegRotationProbe.Encoding;
using Atelia.TwoLegRotationProbe.Model;
using Atelia.TwoLegRotationProbe.Workloads;

namespace Atelia.TwoLegRotationProbe.Planning;

/// <summary>
/// Proves the canonical terminal settlement on an exact scratch fork: try a direct
/// maintenance-only Rotate-C first, then migrate one A-debt object at a time in
/// ascending ObjectId order and retry Rotate-C after every prefix.
/// </summary>
internal static class CanonicalTerminalSettlementPlanner {
    public static CanonicalTerminalSettlementAttempt TryCreate(
        RbfFileStore store,
        ProbeRevisionCursor initialCursor) {
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(initialCursor);

        ValidateCursor(store, initialCursor);
        RbfFileStore scratch = store.ForkForProbe();
        return TryCreateOnScratch(scratch, initialCursor);
    }

    /// <summary>
    /// Continues on a caller-owned scratch Store. This method appends only to that
    /// scratch Store while proving the exact candidate chain.
    /// </summary>
    internal static CanonicalTerminalSettlementAttempt TryCreateOnScratch(
        RbfFileStore scratch,
        ProbeRevisionCursor initialCursor) {
        ArgumentNullException.ThrowIfNull(scratch);
        ArgumentNullException.ThrowIfNull(initialCursor);

        ValidateCursor(scratch, initialCursor);
        ProbeRevisionCursor cursor = initialCursor;
        NormalizedSaveFacts initialFacts = Normalize(scratch, cursor);
        IReadOnlyDictionary<uint, LogicalObjectState> expectedState =
            initialFacts.PostLiveStates;
        SortedSet<uint> expectedDebtObjectIds = GetDebtObjectIds(initialFacts);
        int maximumMigrationCount = expectedDebtObjectIds.Count;
        List<FeasibleCandidate<StayBRevisionPlan>> maintenanceSteps = [];
        NormalizedSaveFacts facts = initialFacts;

        while (true) {
            ValidateMaintenanceState(
                facts,
                expectedState,
                expectedDebtObjectIds);

            FeasibleCandidate<RotateCRevisionPlan>? finalRotateC =
                TryCreateFinalRotateC(scratch, facts, out var finalCapacity);
            if (finalRotateC is not null) {
                _ = ExplicitProbeRevisionApplier.ApplyRotateC(
                    scratch,
                    cursor,
                    finalRotateC);
                return new CanonicalTerminalSettlementProven(
                    new CanonicalTerminalSettlementCertificate(
                        initialCursor,
                        maintenanceSteps,
                        finalRotateC));
            }

            RevisionCandidateCapacityRejection rejectedFinal = finalCapacity
                ?? throw new InvalidDataException(
                    "A failed final Rotate-C proof has no capacity rejection.");
            if (expectedDebtObjectIds.Count == 0) {
                return Reject(
                    CanPrepareAndRotateRejectionStage.FinalRotateC,
                    maintenanceSteps.Count,
                    blockingObjectId: null,
                    rejectedFinal);
            }

            if (maintenanceSteps.Count >= maximumMigrationCount) {
                throw new InvalidDataException(
                    "The terminal settlement exhausted its initial A-debt count without " +
                    "eliminating the projected debt set.");
            }

            uint nextObjectId = expectedDebtObjectIds.Min;
            FeasibleCandidate<StayBRevisionPlan>? maintenanceStep =
                TryCreateMaintenanceStep(
                    scratch,
                    facts,
                    nextObjectId,
                    out var maintenanceCapacity);
            if (maintenanceStep is null) {
                return Reject(
                    CanPrepareAndRotateRejectionStage.PreparatoryMigration,
                    maintenanceSteps.Count,
                    nextObjectId,
                    maintenanceCapacity ?? throw new InvalidDataException(
                        "A failed preparatory migration has no capacity rejection."));
            }

            cursor = ExplicitProbeRevisionApplier.ApplyStayB(
                scratch,
                cursor,
                maintenanceStep);
            maintenanceSteps.Add(maintenanceStep);
            if (!expectedDebtObjectIds.Remove(nextObjectId)) {
                throw new InvalidDataException(
                    $"Projected A-debt object {nextObjectId} disappeared before migration.");
            }

            facts = Normalize(scratch, cursor);
        }
    }

    private static FeasibleCandidate<RotateCRevisionPlan>? TryCreateFinalRotateC(
        RbfFileStore store,
        NormalizedSaveFacts facts,
        out RevisionCandidateCapacityRejection? capacity) {
        RotateCRevisionPlan plan;
        try {
            plan = RotateCRevisionPlanner.Create(
                store,
                facts,
                new RotateCSaveDecision([], []));
        } catch (RevisionCandidateCapacityException exception) {
            capacity = exception.Rejection;
            return null;
        }

        capacity = null;
        CandidateRawObservation observation = CandidateRawObservationBuilder.Create(
            store,
            facts,
            CandidateTarget.RotateC,
            plan.Revision);
        return new FeasibleCandidate<RotateCRevisionPlan>(plan, observation);
    }

    private static FeasibleCandidate<StayBRevisionPlan>? TryCreateMaintenanceStep(
        RbfFileStore store,
        NormalizedSaveFacts facts,
        uint objectId,
        out RevisionCandidateCapacityRejection? capacity) {
        StayBRevisionPlan plan;
        try {
            plan = StayBRevisionPlanner.Create(
                store,
                facts,
                new StayBSaveDecision([], [objectId]));
        } catch (RevisionCandidateCapacityException exception) {
            capacity = exception.Rejection;
            return null;
        }

        capacity = null;
        CandidateRawObservation observation = CandidateRawObservationBuilder.Create(
            store,
            facts,
            CandidateTarget.StayB,
            plan.Revision);
        return new FeasibleCandidate<StayBRevisionPlan>(plan, observation);
    }

    private static NormalizedSaveFacts Normalize(
        RbfFileStore store,
        ProbeRevisionCursor cursor) =>
        SaveStepNormalizer.NormalizeMaintenanceOnly(
            store,
            cursor.FileScope.CurrentFileNumber,
            cursor.PublishedRevisionAddress);

    private static SortedSet<uint> GetDebtObjectIds(
        NormalizedSaveFacts facts) => new(
        facts.NoChanges
            .Where(noChange => noChange.Source.BaseAddress.FileNumber ==
                facts.PreviousFileNumber)
            .Select(static noChange => noChange.ObjectId));

    private static CanonicalTerminalSettlementRejectedUnproven Reject(
        CanPrepareAndRotateRejectionStage stage,
        int completedMigrationCount,
        uint? blockingObjectId,
        RevisionCandidateCapacityRejection capacity) => new(
        new CanPrepareAndRotateRejection(
            stage,
            completedMigrationCount,
            blockingObjectId,
            capacity));

    private static void ValidateCursor(
        RbfFileStore store,
        ProbeRevisionCursor cursor) {
        uint currentFileNumber = cursor.FileScope.CurrentFileNumber;
        uint previousFileNumber = cursor.FileScope.PreviousFileNumber
            ?? throw new InvalidDataException(
                "Terminal settlement requires an adjacent two-file scope.");
        if (currentFileNumber != checked(previousFileNumber + 1) ||
            currentFileNumber != (uint)store.FileCount ||
            cursor.PublishedRevisionAddress.FileNumber != currentFileNumber) {
            throw new InvalidDataException(
                "The terminal settlement cursor must identify the highest adjacent " +
                "two-file scope and a PublishedRevision in Current.");
        }

        RbfFile current = store.GetFile(currentFileNumber);
        _ = store.GetFile(previousFileNumber);
        if (current.TailOffsetBytes != cursor.CurrentFileTailOffsetBytes) {
            throw new InvalidDataException(
                "The terminal settlement cursor Current tail does not match the Store.");
        }

        try {
            _ = store.ReadFrame(cursor.PublishedRevisionAddress);
            _ = store.ReadLayout(cursor.PublishedRevisionAddress);
        } catch (Exception exception) when (
            exception is ArgumentOutOfRangeException or KeyNotFoundException) {
            throw new InvalidDataException(
                "The terminal settlement PublishedRevision is not readable.",
                exception);
        }
    }

    private static void ValidateMaintenanceState(
        NormalizedSaveFacts facts,
        IReadOnlyDictionary<uint, LogicalObjectState> expectedState,
        IReadOnlySet<uint> expectedDebtObjectIds) {
        if (facts.Inserts.Count != 0 ||
            facts.Updates.Count != 0 ||
            facts.Removes.Count != 0 ||
            facts.NoChanges.Count != expectedState.Count ||
            !StatesEqual(facts.ParentLiveStates, expectedState) ||
            !StatesEqual(facts.PostLiveStates, expectedState)) {
            throw new InvalidDataException(
                "Scratch replay no longer preserves the terminal source logical state.");
        }

        uint[] actualDebtObjectIds = facts.NoChanges
            .Where(noChange => noChange.Source.BaseAddress.FileNumber ==
                facts.PreviousFileNumber)
            .Select(static noChange => noChange.ObjectId)
            .ToArray();
        if (!actualDebtObjectIds.SequenceEqual(expectedDebtObjectIds)) {
            throw new InvalidDataException(
                "Scratch replay A-debt differs from the deterministic terminal prefix.");
        }
    }

    private static bool StatesEqual(
        IReadOnlyDictionary<uint, LogicalObjectState> left,
        IReadOnlyDictionary<uint, LogicalObjectState> right) =>
        left.Count == right.Count && left.All(pair =>
            right.TryGetValue(pair.Key, out LogicalObjectState value) &&
            value == pair.Value);
}
