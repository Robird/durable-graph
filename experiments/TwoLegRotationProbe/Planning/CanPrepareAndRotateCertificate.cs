using System.Collections.ObjectModel;
using Atelia.TwoLegRotationProbe.Model;
using Atelia.TwoLegRotationProbe.Workloads;

namespace Atelia.TwoLegRotationProbe.Planning;

/// <summary>
/// One exact, finite probe-only script proving that a selected Stay-B candidate can be
/// followed by zero or more maintenance Stay-B steps and one Rotate-C step.
/// </summary>
internal sealed class CanPrepareAndRotateCertificate {
    private readonly ReadOnlyCollection<FeasibleCandidate<StayBRevisionPlan>>
        _maintenanceStayBSteps;

    internal CanPrepareAndRotateCertificate(
        ProbeRevisionCursor initialCursor,
        FeasibleCandidate<StayBRevisionPlan> initialStayB,
        IEnumerable<FeasibleCandidate<StayBRevisionPlan>> maintenanceStayBSteps,
        FeasibleCandidate<RotateCRevisionPlan> finalRotateC) {
        InitialCursor = initialCursor ??
            throw new ArgumentNullException(nameof(initialCursor));
        InitialStayB = initialStayB ??
            throw new ArgumentNullException(nameof(initialStayB));
        ArgumentNullException.ThrowIfNull(maintenanceStayBSteps);
        FinalRotateC = finalRotateC ??
            throw new ArgumentNullException(nameof(finalRotateC));

        FeasibleCandidate<StayBRevisionPlan>[] frozenMaintenanceSteps =
            maintenanceStayBSteps
                .Select(static step => step ?? throw new ArgumentException(
                    "A completion certificate cannot contain a null maintenance step.",
                    nameof(maintenanceStayBSteps)))
                .ToArray();
        _maintenanceStayBSteps = Array.AsReadOnly(frozenMaintenanceSteps);

        ValidateChain();
    }

    public ProbeRevisionCursor InitialCursor { get; }

    public FeasibleCandidate<StayBRevisionPlan> InitialStayB { get; }

    public IReadOnlyList<FeasibleCandidate<StayBRevisionPlan>>
        MaintenanceStayBSteps => _maintenanceStayBSteps;

    public FeasibleCandidate<RotateCRevisionPlan> FinalRotateC { get; }

    private void ValidateChain() {
        StayBRevisionPlan initialPlan = ValidateStayBIdentity(
            InitialStayB,
            "initial Stay-B");
        NormalizedSaveFacts initialFacts = initialPlan.Facts;
        uint previousFileNumber = initialFacts.PreviousFileNumber;
        uint currentFileNumber = initialFacts.CurrentFileNumber;
        if (InitialCursor.FileScope.PreviousFileNumber != previousFileNumber ||
            InitialCursor.FileScope.CurrentFileNumber != currentFileNumber ||
            InitialCursor.PublishedRevisionAddress !=
                initialFacts.PublishedRevisionAddress ||
            InitialCursor.CurrentFileTailOffsetBytes !=
                initialPlan.Revision.Estimate.RbfLayout.FrameStartOffsetBytes) {
            throw new ArgumentException(
                "The initial cursor does not identify the exact source and tail of the " +
                "initial Stay-B candidate.",
                nameof(InitialCursor));
        }

        IReadOnlyDictionary<uint, LogicalObjectState> expectedState =
            initialFacts.PostLiveStates;
        Dictionary<uint, AbsoluteFrameAddress> expectedHeads =
            ProjectPostLiveHeads(initialPlan);
        AbsoluteFrameAddress expectedPublishedAddress = initialPlan.Revision.Address;
        long expectedTailOffsetBytes =
            initialPlan.Revision.Estimate.RbfLayout.TailOffsetAfterBytes;

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
            if (plan.Decision.UpdateDecisions.Count != 0 ||
                plan.Decision.UnchangedMigrationObjectIds.Count != 1 ||
                plan.Revision.Estimate.RbfLayout.FrameStartOffsetBytes !=
                    expectedTailOffsetBytes) {
                throw new ArgumentException(
                    $"Maintenance Stay-B step {index} is not one exact single-object " +
                    "migration at the preceding B tail.",
                    nameof(MaintenanceStayBSteps));
            }

            ApplyCandidateHeads(expectedHeads, plan);
            expectedPublishedAddress = plan.Revision.Address;
            expectedTailOffsetBytes =
                plan.Revision.Estimate.RbfLayout.TailOffsetAfterBytes;
        }

        RotateCRevisionPlan finalPlan = ValidateRotateCIdentity(FinalRotateC);
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
                $"The {role} does not preserve the initial Stay-B PostLive state and " +
                "exact PublishedRevision chain.");
        }
    }

    private static bool StatesEqual(
        IReadOnlyDictionary<uint, LogicalObjectState> left,
        IReadOnlyDictionary<uint, LogicalObjectState> right) =>
        left.Count == right.Count && left.All(pair =>
            right.TryGetValue(pair.Key, out LogicalObjectState value) &&
            value == pair.Value);

    private static Dictionary<uint, AbsoluteFrameAddress> ProjectPostLiveHeads(
        StayBRevisionPlan plan) {
        Dictionary<uint, AbsoluteFrameAddress> result = [];
        foreach (uint objectId in plan.Facts.PostLiveStates.Keys) {
            if (plan.Revision.Frame.ObjectVersions.ContainsKey(objectId)) {
                result.Add(objectId, plan.Revision.Address);
                continue;
            }

            if (!plan.Facts.ParentLive.TryGetValue(
                objectId,
                out SourceObjectFact? source)) {
                throw new ArgumentException(
                    $"Initial PostLive object {objectId} has no candidate record or " +
                    "inherited source head.",
                    nameof(plan));
            }

            result.Add(objectId, source.HeadAddress);
        }

        return result;
    }

    private static void ApplyCandidateHeads(
        IDictionary<uint, AbsoluteFrameAddress> expectedHeads,
        StayBRevisionPlan plan) {
        foreach (uint objectId in plan.Revision.Frame.ObjectVersions.Keys) {
            if (!expectedHeads.ContainsKey(objectId)) {
                throw new ArgumentException(
                    $"Maintenance candidate writes non-PostLive object {objectId}.",
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
