using Atelia.TwoLegRotationProbe.Encoding;
using Atelia.TwoLegRotationProbe.Model;
using Atelia.TwoLegRotationProbe.Workloads;

namespace Atelia.TwoLegRotationProbe.Planning;

/// <summary>
/// Proves one fixed, finite completion path on an exact scratch fork. It explores only
/// ascending single-object A-debt migration prefixes and never mutates the caller Store.
/// </summary>
internal static class CanPrepareAndRotateCertificatePlanner {
    public static CanPrepareAndRotateAttempt TryCreate(
        RbfFileStore store,
        ProbeRevisionCursor initialCursor,
        FeasibleCandidate<StayBRevisionPlan> initialStayB) {
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(initialCursor);
        ArgumentNullException.ThrowIfNull(initialStayB);

        ValidateInitialIdentity(initialStayB);
        RbfFileStore scratch = store.ForkForProbe();
        ProbeRevisionCursor cursor = ExplicitProbeRevisionApplier.ApplyStayB(
            scratch,
            initialCursor,
            initialStayB);

        IReadOnlyDictionary<uint, LogicalObjectState> expectedState =
            initialStayB.Plan.Facts.PostLiveStates;
        SortedSet<uint> expectedDebtObjectIds = new(
            initialStayB.Observation
                .PostLiveReconstruction
                .PreviousFileDependentObjectIds);
        int maximumMigrationCount = expectedDebtObjectIds.Count;
        List<FeasibleCandidate<StayBRevisionPlan>> maintenanceSteps = [];

        while (true) {
            NormalizedSaveFacts maintenanceFacts =
                SaveStepNormalizer.NormalizeMaintenanceOnly(
                    scratch,
                    cursor.FileScope.CurrentFileNumber,
                    cursor.PublishedRevisionAddress);
            ValidateMaintenanceState(
                maintenanceFacts,
                expectedState,
                expectedDebtObjectIds);

            RotateCRevisionPlan? finalPlan = null;
            RevisionCandidateCapacityRejection? finalCapacity = null;
            try {
                finalPlan = RotateCRevisionPlanner.Create(
                    scratch,
                    maintenanceFacts,
                    new RotateCSaveDecision([], []));
            } catch (RevisionCandidateCapacityException exception) {
                finalCapacity = exception.Rejection;
            }

            if (finalPlan is not null) {
                CandidateRawObservation finalObservation =
                    CandidateRawObservationBuilder.Create(
                        scratch,
                        maintenanceFacts,
                        CandidateTarget.RotateC,
                        finalPlan.Revision);
                FeasibleCandidate<RotateCRevisionPlan> finalRotateC = new(
                    finalPlan,
                    finalObservation);
                _ = ExplicitProbeRevisionApplier.ApplyRotateC(
                    scratch,
                    cursor,
                    finalRotateC);
                return new CanPrepareAndRotateProven(
                    new CanPrepareAndRotateCertificate(
                        initialCursor,
                        initialStayB,
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
                    "The completion proof exhausted its initial A-debt count without " +
                    "eliminating the projected debt set.");
            }

            uint nextObjectId = expectedDebtObjectIds.Min;
            StayBRevisionPlan maintenancePlan;
            try {
                maintenancePlan = StayBRevisionPlanner.Create(
                    scratch,
                    maintenanceFacts,
                    new StayBSaveDecision([], [nextObjectId]));
            } catch (RevisionCandidateCapacityException exception) {
                return Reject(
                    CanPrepareAndRotateRejectionStage.PreparatoryMigration,
                    maintenanceSteps.Count,
                    nextObjectId,
                    exception.Rejection);
            }

            CandidateRawObservation maintenanceObservation =
                CandidateRawObservationBuilder.Create(
                    scratch,
                    maintenanceFacts,
                    CandidateTarget.StayB,
                    maintenancePlan.Revision);
            FeasibleCandidate<StayBRevisionPlan> maintenanceStep = new(
                maintenancePlan,
                maintenanceObservation);
            cursor = ExplicitProbeRevisionApplier.ApplyStayB(
                scratch,
                cursor,
                maintenanceStep);
            maintenanceSteps.Add(maintenanceStep);
            if (!expectedDebtObjectIds.Remove(nextObjectId)) {
                throw new InvalidDataException(
                    $"Projected A-debt object {nextObjectId} disappeared before migration.");
            }
        }
    }

    private static CanPrepareAndRotateRejectedUnproven Reject(
        CanPrepareAndRotateRejectionStage stage,
        int completedMigrationCount,
        uint? blockingObjectId,
        RevisionCandidateCapacityRejection capacity) => new(
        new CanPrepareAndRotateRejection(
            stage,
            completedMigrationCount,
            blockingObjectId,
            capacity));

    private static void ValidateInitialIdentity(
        FeasibleCandidate<StayBRevisionPlan> initialStayB) {
        StayBRevisionPlan plan = initialStayB.Plan ?? throw new ArgumentException(
            "The initial feasible Stay-B selection has no plan.",
            nameof(initialStayB));
        CandidateRawObservation observation = initialStayB.Observation ??
            throw new ArgumentException(
                "The initial feasible Stay-B selection has no observation.",
                nameof(initialStayB));
        if (observation.Target != CandidateTarget.StayB ||
            !ReferenceEquals(observation.Facts, plan.Facts) ||
            !ReferenceEquals(observation.Candidate, plan.Revision)) {
            throw new ArgumentException(
                "The initial feasible Stay-B selection does not retain exact facts and " +
                "candidate identity.",
                nameof(initialStayB));
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
                "Scratch replay no longer preserves the initial Stay-B PostLive state.");
        }

        uint[] actualDebtObjectIds = facts.NoChanges
            .Where(noChange => noChange.Source.BaseAddress.FileNumber ==
                facts.PreviousFileNumber)
            .Select(static noChange => noChange.ObjectId)
            .ToArray();
        if (!actualDebtObjectIds.SequenceEqual(expectedDebtObjectIds)) {
            throw new InvalidDataException(
                "Scratch replay A-debt differs from the deterministic completion prefix.");
        }
    }

    private static bool StatesEqual(
        IReadOnlyDictionary<uint, LogicalObjectState> left,
        IReadOnlyDictionary<uint, LogicalObjectState> right) =>
        left.Count == right.Count && left.All(pair =>
            right.TryGetValue(pair.Key, out LogicalObjectState value) &&
            value == pair.Value);
}
