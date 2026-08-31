using Atelia.TwoLegRotationProbe.Model;
using Atelia.TwoLegRotationProbe.Planning;
using Atelia.TwoLegRotationProbe.Policies;
using Atelia.TwoLegRotationProbe.Simulation;
using Atelia.TwoLegRotationProbe.Workloads;

namespace Atelia.TwoLegRotationProbe.Evaluation;

/// <summary>
/// Experiment-only coordinator for one frozen policy run. It owns an isolated Store fork,
/// accounts accepted workload Commits, and closes the terminal source epoch with one
/// actually replayed canonical settlement Commit.
/// </summary>
internal sealed class EvaluatorV1Session {
    private readonly EvaluatorRawMetricAccumulator _metrics;
    private readonly int _totalWorkloadStepCount;
    private ProbeRevisionCursor _cursor;
    private int _completedWorkloadStepCount;
    private EvaluatorRunOutcome? _terminalOutcome;
    private bool _completionRequested;

    public EvaluatorV1Session(
        RbfFileStore sourceStore,
        ProbeRevisionCursor initialCursor,
        int totalWorkloadStepCount) {
        ArgumentNullException.ThrowIfNull(sourceStore);
        ArgumentNullException.ThrowIfNull(initialCursor);
        ArgumentOutOfRangeException.ThrowIfNegative(totalWorkloadStepCount);

        Store = sourceStore.ForkForProbe();
        _cursor = CopyCursor(initialCursor);
        _totalWorkloadStepCount = totalWorkloadStepCount;
        _metrics = new EvaluatorRawMetricAccumulator(Store, _cursor);
    }

    public RbfFileStore Store { get; }

    public ProbeRevisionCursor Cursor => _cursor;

    public int CompletedWorkloadStepCount => _completedWorkloadStepCount;

    public RotationPolicyStepAttempt ApplySelectedWorkloadCommit(
        ExplicitCandidatePairEvaluation evaluation,
        CandidateTarget selectedTarget) {
        ArgumentNullException.ThrowIfNull(evaluation);
        EnsureMayAcceptWorkloadStep();
        _ = ExplicitProbeRevisionApplier.ValidateSource(
            Store,
            _cursor,
            evaluation.Facts);

        _metrics.BeginCommit(Store, _cursor);
        RotationPolicyStepAttempt attempt;
        try {
            attempt = ExplicitRotationPolicyStepHarness.TryApplySelected(
                Store,
                _cursor,
                evaluation,
                selectedTarget);
        } catch {
            _metrics.CancelUnrealizedCommit(Store, _cursor);
            throw;
        }

        switch (attempt) {
            case AppliedStayBPolicyStep appliedStay:
                AcceptWorkloadCommit(appliedStay.ResultCursor, evaluation.Facts);
                break;
            case AppliedRotateCPolicyStep appliedRotate:
                AcceptWorkloadCommit(appliedRotate.ResultCursor, evaluation.Facts);
                break;
            case SelectedPolicyCandidateCapacityRejected capacity:
                _metrics.CancelUnrealizedCommit(Store, _cursor);
                _terminalOutcome = new EvaluatorRunCapacityRejected(
                    WorkloadPosition(),
                    capacity.SelectedTarget,
                    capacity.Rejection);
                break;
            case StayBPolicyCompletionRejectedUnproven unproven:
                _metrics.CancelUnrealizedCommit(Store, _cursor);
                _terminalOutcome = new EvaluatorRunRejectedUnproven(
                    WorkloadPosition(),
                    unproven.Rejection);
                break;
            default:
                _metrics.CancelUnrealizedCommit(Store, _cursor);
                throw new InvalidDataException(
                    "The policy step harness returned an unknown attempt kind.");
        }

        return attempt;
    }

    /// <summary>
    /// Explicitly ends a truncated run without executing terminal settlement.
    /// No partial raw metrics or cursor are promoted into the outcome.
    /// </summary>
    public EvaluatorRunIncomplete StopIncomplete() {
        EnsureCompletionNotRequested();
        if (_terminalOutcome is not null) {
            throw new InvalidOperationException(
                "A rejected evaluator run cannot also be marked incomplete.");
        }

        _metrics.ValidateClosedBoundary(Store, _cursor);
        _completionRequested = true;
        EvaluatorRunPosition position = _completedWorkloadStepCount <
            _totalWorkloadStepCount
            ? WorkloadPosition()
            : TerminalPosition();
        EvaluatorRunIncomplete outcome = new(position);
        _terminalOutcome = outcome;
        return outcome;
    }

    public EvaluatorRunOutcome Complete() {
        EnsureCompletionNotRequested();
        _metrics.ValidateClosedBoundary(Store, _cursor);
        _completionRequested = true;

        if (_terminalOutcome is not null) {
            return _terminalOutcome;
        }

        if (_completedWorkloadStepCount != _totalWorkloadStepCount) {
            EvaluatorRunIncomplete incomplete = new(WorkloadPosition());
            _terminalOutcome = incomplete;
            return incomplete;
        }

        CanonicalTerminalSettlementAttempt settlementAttempt =
            CanonicalTerminalSettlementPlanner.TryCreate(Store, _cursor);
        if (settlementAttempt is
            CanonicalTerminalSettlementRejectedUnproven rejected) {
            EvaluatorRunRejectedUnproven outcome = new(
                TerminalPosition(),
                rejected.Rejection);
            _terminalOutcome = outcome;
            return outcome;
        }

        CanonicalTerminalSettlementCertificate settlement =
            (settlementAttempt as CanonicalTerminalSettlementProven)?.Certificate
            ?? throw new InvalidDataException(
                "The terminal settlement planner returned an unknown attempt kind.");
        ProbeRevisionCursor terminalSource = _cursor;
        IReadOnlyDictionary<uint, LogicalObjectState> expectedState =
            settlement.FinalRotateC.Plan.Facts.PostLiveStates;

        _metrics.BeginCommit(Store, _cursor);
        foreach (FeasibleCandidate<StayBRevisionPlan> maintenance in
            settlement.MaintenanceStayBSteps) {
            _cursor = ExplicitProbeRevisionApplier.ApplyStayB(
                Store,
                _cursor,
                maintenance);
            _metrics.ObserveAcceptedRevision(Store, _cursor);
        }

        _cursor = ExplicitProbeRevisionApplier.ApplyRotateC(
            Store,
            _cursor,
            settlement.FinalRotateC);
        _metrics.ObserveAcceptedRevision(Store, _cursor);
        _metrics.EndTerminalSettlementCommit(Store, _cursor);

        ValidateClosedTerminalEpoch(terminalSource, expectedState);
        EvaluatorRawMetrics metrics = _metrics.Complete(Store, _cursor);
        if (metrics.WorkloadColdReadSampleCount != _completedWorkloadStepCount) {
            throw new InvalidDataException(
                "Every accepted workload Save must contribute exactly one cold-read sample.");
        }

        AdmittedEvaluatorRun admitted = new(
            TerminalPosition(),
            _cursor,
            metrics,
            new TerminalSettlementObservation(settlement));
        _terminalOutcome = admitted;
        return admitted;
    }

    private void AcceptWorkloadCommit(
        ProbeRevisionCursor resultCursor,
        NormalizedSaveFacts facts) {
        _metrics.ObserveAcceptedRevision(Store, resultCursor);
        _metrics.EndWorkloadCommit(
            Store,
            resultCursor,
            facts);
        _cursor = resultCursor;
        _completedWorkloadStepCount = checked(
            _completedWorkloadStepCount + 1);
    }

    private void ValidateClosedTerminalEpoch(
        ProbeRevisionCursor source,
        IReadOnlyDictionary<uint, LogicalObjectState> expectedState) {
        uint oldPreviousFileNumber = source.FileScope.PreviousFileNumber
            ?? throw new InvalidDataException(
                "Terminal settlement requires a two-file source scope.");
        uint oldCurrentFileNumber = source.FileScope.CurrentFileNumber;
        if (_cursor.FileScope.PreviousFileNumber != oldCurrentFileNumber ||
            _cursor.FileScope.CurrentFileNumber != checked(oldCurrentFileNumber + 1)) {
            throw new InvalidDataException(
                "Terminal settlement did not advance the source A/B scope to B/C.");
        }

        ObjectVersionDictionaryMaterializationInspection materialized =
            ObjectVersionDictionaryReader.MaterializeLive(
                Store,
                _cursor.PublishedRevisionAddress);
        EnsureAddressesInsideFinalScope(
            materialized.DictionaryRevisionAddresses,
            oldPreviousFileNumber,
            oldCurrentFileNumber);

        IReadOnlyDictionary<uint, LogicalObjectState> actualState =
            PhysicalStateOracle.Materialize(Store, materialized.Bindings);
        if (!StatesEqual(expectedState, actualState)) {
            throw new InvalidDataException(
                "Terminal settlement changed the final logical state.");
        }

        foreach ((uint objectId, AbsoluteFrameAddress head) in
            materialized.Bindings) {
            ObjectReconstructionInspection reconstruction =
                PhysicalStateOracle.InspectObjectReconstruction(
                    Store,
                    objectId,
                    head);
            EnsureAddressesInsideFinalScope(
                reconstruction.ReconstructionFrameAddresses,
                oldPreviousFileNumber,
                oldCurrentFileNumber);
        }
    }

    private void EnsureAddressesInsideFinalScope(
        IEnumerable<AbsoluteFrameAddress> addresses,
        uint oldPreviousFileNumber,
        uint oldCurrentFileNumber) {
        foreach (AbsoluteFrameAddress address in addresses) {
            if (address.FileNumber == oldPreviousFileNumber ||
                (address.FileNumber != oldCurrentFileNumber &&
                    address.FileNumber != _cursor.FileScope.CurrentFileNumber)) {
                throw new InvalidDataException(
                    $"Terminal settlement retained out-of-scope current reconstruction " +
                    $"frame {address}.");
            }
        }
    }

    private static bool StatesEqual(
        IReadOnlyDictionary<uint, LogicalObjectState> left,
        IReadOnlyDictionary<uint, LogicalObjectState> right) =>
        left.Count == right.Count && left.All(pair =>
            right.TryGetValue(pair.Key, out LogicalObjectState value) &&
            value == pair.Value);

    private void EnsureMayAcceptWorkloadStep() {
        if (_completionRequested) {
            throw new InvalidOperationException(
                "The evaluator run has already completed.");
        }

        if (_terminalOutcome is not null) {
            throw new InvalidOperationException(
                "A rejected evaluator run cannot accept more workload steps.");
        }

        if (_completedWorkloadStepCount >= _totalWorkloadStepCount) {
            throw new InvalidOperationException(
                "All declared workload steps have already been consumed.");
        }
    }

    private void EnsureCompletionNotRequested() {
        if (_completionRequested) {
            throw new InvalidOperationException(
                "The evaluator run has already been completed or stopped.");
        }
    }

    private EvaluatorRunPosition WorkloadPosition() => new(
        EvaluatorRunPhase.Workload,
        _completedWorkloadStepCount,
        _totalWorkloadStepCount);

    private EvaluatorRunPosition TerminalPosition() => new(
        EvaluatorRunPhase.TerminalSettlement,
        _completedWorkloadStepCount,
        _totalWorkloadStepCount);

    private static ProbeRevisionCursor CopyCursor(ProbeRevisionCursor source) => new(
        new FileScope(source.FileScope.CurrentFileNumber),
        source.PublishedRevisionAddress,
        source.CurrentFileTailOffsetBytes);
}
