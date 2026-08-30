using System.Collections.ObjectModel;
using Atelia.TwoLegRotationProbe.Model;
using Atelia.TwoLegRotationProbe.Workloads;

namespace Atelia.TwoLegRotationProbe.Planning;

/// <summary>
/// One exact, finite probe-only script that preserves the source logical state while
/// advancing its A/B scope through zero or more maintenance Stay-B steps and one
/// final Rotate-C step.
/// </summary>
internal sealed class CanonicalTerminalSettlementCertificate {
    private readonly ReadOnlyCollection<FeasibleCandidate<StayBRevisionPlan>>
        _maintenanceStayBSteps;

    internal CanonicalTerminalSettlementCertificate(
        ProbeRevisionCursor initialCursor,
        IEnumerable<FeasibleCandidate<StayBRevisionPlan>> maintenanceStayBSteps,
        FeasibleCandidate<RotateCRevisionPlan> finalRotateC) {
        InitialCursor = initialCursor ??
            throw new ArgumentNullException(nameof(initialCursor));
        ArgumentNullException.ThrowIfNull(maintenanceStayBSteps);
        FinalRotateC = finalRotateC ??
            throw new ArgumentNullException(nameof(finalRotateC));

        FeasibleCandidate<StayBRevisionPlan>[] frozenMaintenanceSteps =
            maintenanceStayBSteps
                .Select(static step => step ?? throw new ArgumentException(
                    "A terminal settlement certificate cannot contain a null " +
                    "maintenance step.",
                    nameof(maintenanceStayBSteps)))
                .ToArray();
        _maintenanceStayBSteps = Array.AsReadOnly(frozenMaintenanceSteps);

        ValidateChain();
    }

    public ProbeRevisionCursor InitialCursor { get; }

    public IReadOnlyList<FeasibleCandidate<StayBRevisionPlan>>
        MaintenanceStayBSteps => _maintenanceStayBSteps;

    public FeasibleCandidate<RotateCRevisionPlan> FinalRotateC { get; }

    private void ValidateChain() {
        RotateCRevisionPlan finalPlan = ValidateRotateCIdentity(FinalRotateC);
        NormalizedSaveFacts initialFacts = _maintenanceStayBSteps.Count == 0
            ? finalPlan.Facts
            : ValidateStayBIdentity(
                _maintenanceStayBSteps[0],
                "maintenance Stay-B step 0").Facts;
        uint previousFileNumber = initialFacts.PreviousFileNumber;
        uint currentFileNumber = initialFacts.CurrentFileNumber;
        ValidateInitialCursor(initialFacts, previousFileNumber, currentFileNumber);

        IReadOnlyDictionary<uint, LogicalObjectState> expectedState =
            initialFacts.PostLiveStates;
        Dictionary<uint, AbsoluteFrameAddress> expectedHeads =
            initialFacts.ParentLive.ToDictionary(
                static pair => pair.Key,
                static pair => pair.Value.HeadAddress);
        AbsoluteFrameAddress expectedPublishedAddress =
            initialFacts.PublishedRevisionAddress;
        long expectedTailOffsetBytes = InitialCursor.CurrentFileTailOffsetBytes;
        uint? precedingMigrationObjectId = null;

        for (int index = 0; index < _maintenanceStayBSteps.Count; index++) {
            FeasibleCandidate<StayBRevisionPlan> step = _maintenanceStayBSteps[index];
            StayBRevisionPlan plan = ValidateStayBIdentity(
                step,
                $"maintenance Stay-B step {index}");
            ValidateMaintenanceFacts(
                plan.Facts,
                expectedState,
                previousFileNumber,
                currentFileNumber,
                expectedPublishedAddress,
                $"maintenance Stay-B step {index}");
            ValidateParentHeads(
                plan.Facts,
                expectedHeads,
                $"maintenance Stay-B step {index}");
            uint migrationObjectId = AssertSingleMigration(
                plan,
                expectedTailOffsetBytes,
                index);
            if (precedingMigrationObjectId is uint preceding &&
                migrationObjectId <= preceding) {
                throw new ArgumentException(
                    "Terminal settlement migrations must be in ascending ObjectId order.",
                    nameof(MaintenanceStayBSteps));
            }

            precedingMigrationObjectId = migrationObjectId;
            ApplyCandidateHeads(expectedHeads, plan);
            expectedPublishedAddress = plan.Revision.Address;
            expectedTailOffsetBytes =
                plan.Revision.Estimate.RbfLayout.TailOffsetAfterBytes;
        }

        ValidateMaintenanceFacts(
            finalPlan.Facts,
            expectedState,
            previousFileNumber,
            currentFileNumber,
            expectedPublishedAddress,
            "final Rotate-C step");
        ValidateParentHeads(
            finalPlan.Facts,
            expectedHeads,
            "final Rotate-C step");
        if (finalPlan.Decision.BContainedUpdateDecisions.Count != 0 ||
            finalPlan.Decision.BContainedNoChangeBaseObjectIds.Count != 0 ||
            finalPlan.Revision.FileNumber != checked(currentFileNumber + 1) ||
            finalPlan.Revision.Estimate.RbfLayout.FrameStartOffsetBytes !=
                RbfV040Layout.InitialTailOffsetBytes) {
            throw new ArgumentException(
                "The final Rotate-C step is not the exact external-default maintenance " +
                "candidate for a fresh C file.",
                nameof(FinalRotateC));
        }
    }

    private void ValidateInitialCursor(
        NormalizedSaveFacts facts,
        uint previousFileNumber,
        uint currentFileNumber) {
        if (InitialCursor.FileScope.PreviousFileNumber != previousFileNumber ||
            InitialCursor.FileScope.CurrentFileNumber != currentFileNumber ||
            InitialCursor.PublishedRevisionAddress !=
                facts.PublishedRevisionAddress) {
            throw new ArgumentException(
                "The initial cursor does not identify the terminal settlement source.",
                nameof(InitialCursor));
        }
    }

    private static StayBRevisionPlan ValidateStayBIdentity(
        FeasibleCandidate<StayBRevisionPlan> feasible,
        string role) {
        StayBRevisionPlan plan = feasible.Plan ?? throw new ArgumentException(
            $"The {role} has no plan.",
            nameof(feasible));
        CandidateRawObservation observation = feasible.Observation ??
            throw new ArgumentException(
                $"The {role} has no observation.",
                nameof(feasible));
        if (observation.Target != CandidateTarget.StayB ||
            !ReferenceEquals(observation.Facts, plan.Facts) ||
            !ReferenceEquals(observation.Candidate, plan.Revision)) {
            throw new ArgumentException(
                $"The {role} does not retain exact Stay-B facts and candidate identity.",
                nameof(feasible));
        }

        return plan;
    }

    private static RotateCRevisionPlan ValidateRotateCIdentity(
        FeasibleCandidate<RotateCRevisionPlan> feasible) {
        RotateCRevisionPlan plan = feasible.Plan ?? throw new ArgumentException(
            "The final Rotate-C step has no plan.",
            nameof(feasible));
        CandidateRawObservation observation = feasible.Observation ??
            throw new ArgumentException(
                "The final Rotate-C step has no observation.",
                nameof(feasible));
        if (observation.Target != CandidateTarget.RotateC ||
            !ReferenceEquals(observation.Facts, plan.Facts) ||
            !ReferenceEquals(observation.Candidate, plan.Revision)) {
            throw new ArgumentException(
                "The final Rotate-C step does not retain exact facts and candidate identity.",
                nameof(feasible));
        }

        return plan;
    }

    private static void ValidateMaintenanceFacts(
        NormalizedSaveFacts facts,
        IReadOnlyDictionary<uint, LogicalObjectState> expectedState,
        uint expectedPreviousFileNumber,
        uint expectedCurrentFileNumber,
        AbsoluteFrameAddress expectedPublishedAddress,
        string role) {
        if (facts.PreviousFileNumber != expectedPreviousFileNumber ||
            facts.CurrentFileNumber != expectedCurrentFileNumber ||
            facts.PublishedRevisionAddress != expectedPublishedAddress ||
            facts.Inserts.Count != 0 ||
            facts.Updates.Count != 0 ||
            facts.Removes.Count != 0 ||
            facts.NoChanges.Count != expectedState.Count ||
            !StatesEqual(facts.ParentLiveStates, expectedState) ||
            !StatesEqual(facts.PostLiveStates, expectedState)) {
            throw new ArgumentException(
                $"The {role} does not preserve the terminal source logical state and " +
                "exact PublishedRevision chain.");
        }
    }

    private static uint AssertSingleMigration(
        StayBRevisionPlan plan,
        long expectedTailOffsetBytes,
        int index) {
        if (plan.Decision.UpdateDecisions.Count != 0 ||
            plan.Decision.UnchangedMigrationObjectIds.Count != 1 ||
            plan.Revision.Estimate.RbfLayout.FrameStartOffsetBytes !=
                expectedTailOffsetBytes) {
            throw new ArgumentException(
                $"Maintenance Stay-B step {index} is not one exact single-object " +
                "migration at the preceding B tail.",
                nameof(MaintenanceStayBSteps));
        }

        return plan.Decision.UnchangedMigrationObjectIds[0];
    }

    private static bool StatesEqual(
        IReadOnlyDictionary<uint, LogicalObjectState> left,
        IReadOnlyDictionary<uint, LogicalObjectState> right) =>
        left.Count == right.Count && left.All(pair =>
            right.TryGetValue(pair.Key, out LogicalObjectState value) &&
            value == pair.Value);

    private static void ApplyCandidateHeads(
        IDictionary<uint, AbsoluteFrameAddress> expectedHeads,
        StayBRevisionPlan plan) {
        foreach (uint objectId in plan.Revision.Frame.ObjectVersions.Keys) {
            if (!expectedHeads.ContainsKey(objectId)) {
                throw new ArgumentException(
                    $"Maintenance candidate writes non-live object {objectId}.",
                    nameof(plan));
            }

            expectedHeads[objectId] = plan.Revision.Address;
        }
    }

    private static void ValidateParentHeads(
        NormalizedSaveFacts facts,
        IReadOnlyDictionary<uint, AbsoluteFrameAddress> expectedHeads,
        string role) {
        if (facts.ParentLive.Count != expectedHeads.Count ||
            expectedHeads.Any(pair =>
                !facts.ParentLive.TryGetValue(
                    pair.Key,
                    out SourceObjectFact? source) ||
                source.HeadAddress != pair.Value)) {
            throw new ArgumentException(
                $"The {role} parent-live heads do not continue the exact preceding candidate.");
        }
    }
}
