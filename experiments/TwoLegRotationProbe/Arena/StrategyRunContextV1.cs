using System.Collections.ObjectModel;
using Atelia.TwoLegRotationProbe.Evaluation;
using Atelia.TwoLegRotationProbe.Model;
using Atelia.TwoLegRotationProbe.Planning;
using Atelia.TwoLegRotationProbe.Policies;
using Atelia.TwoLegRotationProbe.Workloads;

namespace Atelia.TwoLegRotationProbe.Arena;

public enum StrategyCommitStatusV1 {
    AppliedStayB,
    AppliedRotateC,
    CapacityRejected,
    CompletionRejectedUnproven,
}

public enum StrategyRunTerminationV1 {
    Admitted,
    CapacityRejected,
    CompletionRejectedUnproven,
    Incomplete,
}

public readonly record struct StrategyRevisionCheckpointV1(
    uint PreviousFileNumber,
    uint CurrentFileNumber,
    uint PublishedRevisionFileNumber,
    long PublishedRevisionOffsetBytes,
    int PublishedRevisionLengthBytes,
    long CurrentFileTailOffsetBytes) {
    internal static StrategyRevisionCheckpointV1 Create(ProbeRevisionCursor cursor) {
        ArgumentNullException.ThrowIfNull(cursor);
        uint previous = cursor.FileScope.PreviousFileNumber ??
            throw new InvalidDataException(
                "A strategy checkpoint requires a two-file scope.");
        return new StrategyRevisionCheckpointV1(
            previous,
            cursor.FileScope.CurrentFileNumber,
            cursor.PublishedRevisionAddress.FileNumber,
            cursor.PublishedRevisionAddress.FrameTicket.OffsetBytes,
            cursor.PublishedRevisionAddress.FrameTicket.LengthBytes,
            cursor.CurrentFileTailOffsetBytes);
    }
}

public sealed record StrategyCommitReceiptV1 {
    internal StrategyCommitReceiptV1(
        int workloadStepOrdinal,
        StrategyTargetV1 selectedTarget,
        StrategyRevisionCheckpointV1 result) {
        WorkloadStepOrdinal = workloadStepOrdinal;
        SelectedTarget = selectedTarget;
        Result = result;
    }

    public int WorkloadStepOrdinal { get; }

    public StrategyTargetV1 SelectedTarget { get; }

    public StrategyRevisionCheckpointV1 Result { get; }
}

/// <summary>
/// Arena-certified in-memory output. It contains physical state and captured workload
/// Commit boundaries, but no strategy-declared metrics.
/// </summary>
public sealed class StrategyRunProductV1 {
    private readonly ReadOnlyCollection<StrategyCommitReceiptV1> _workloadCommits;

    internal StrategyRunProductV1(
        StrategyRunContextV1 owner,
        RbfFileStore store,
        IEnumerable<StrategyCommitReceiptV1> workloadCommits,
        StrategyRevisionCheckpointV1 finalCheckpoint,
        StrategyRunTerminationV1 termination,
        int terminalSettlementRevisionCount,
        EvaluatorRunOutcome outcome) {
        Owner = owner;
        Store = store;
        _workloadCommits = Array.AsReadOnly(workloadCommits.ToArray());
        FinalCheckpoint = finalCheckpoint;
        Termination = termination;
        TerminalSettlementRevisionCount = terminalSettlementRevisionCount;
        Outcome = outcome;
    }

    public RbfFileStore Store { get; }

    public IReadOnlyList<StrategyCommitReceiptV1> WorkloadCommits =>
        _workloadCommits;

    public StrategyRevisionCheckpointV1 FinalCheckpoint { get; }

    public StrategyRunTerminationV1 Termination { get; }

    public int TerminalSettlementRevisionCount { get; }

    internal StrategyRunContextV1 Owner { get; }

    internal EvaluatorRunOutcome Outcome { get; }
}

/// <summary>
/// Streaming workload capability supplied by Arena. It reveals only the current Save
/// facts, records exact Commit boundaries, and keeps terminal settlement and metrics
/// under Arena authority.
/// </summary>
public sealed class StrategyRunContextV1 {
    private readonly WorkloadTrace _trace;
    private readonly int _firstWorkloadTraceStepIndex;
    private readonly EvaluatorV1Session _session;
    private readonly List<StrategyCommitReceiptV1> _workloadCommits = [];
    private NormalizedSaveFacts? _currentFacts;
    private StrategyStepViewV1? _currentStep;
    private bool _terminalAttemptObserved;
    private StrategyRunProductV1? _product;

    internal StrategyRunContextV1(
        RbfFileStore sourceStore,
        ProbeRevisionCursor initialCursor,
        WorkloadTrace trace,
        int firstWorkloadTraceStepIndex,
        int totalWorkloadStepCount) {
        _trace = trace ?? throw new ArgumentNullException(nameof(trace));
        ArgumentOutOfRangeException.ThrowIfNegative(firstWorkloadTraceStepIndex);
        if (firstWorkloadTraceStepIndex > trace.Steps.Count ||
            totalWorkloadStepCount != trace.Steps.Count - firstWorkloadTraceStepIndex) {
            throw new ArgumentException(
                "The strategy workload horizon does not match the frozen trace.");
        }

        _firstWorkloadTraceStepIndex = firstWorkloadTraceStepIndex;
        _session = new EvaluatorV1Session(
            sourceStore,
            initialCursor,
            totalWorkloadStepCount);
    }

    public StrategyStepViewV1? CurrentStep {
        get {
            EnsureProductNotCreated();
            if (_terminalAttemptObserved ||
                _session.CompletedWorkloadStepCount == TotalWorkloadStepCount) {
                return null;
            }

            EnsureCurrentFacts();
            return _currentStep;
        }
    }

    internal int CompletedWorkloadStepCount => _session.CompletedWorkloadStepCount;

    internal int TotalWorkloadStepCount =>
        _trace.Steps.Count - _firstWorkloadTraceStepIndex;

    public StrategyCommitStatusV1 Commit(StrategySelectionV1 selection) {
        ArgumentNullException.ThrowIfNull(selection);
        EnsureProductNotCreated();
        if (_terminalAttemptObserved ||
            _session.CompletedWorkloadStepCount == TotalWorkloadStepCount) {
            throw new InvalidOperationException(
                "The strategy run has no current workload step.");
        }

        EnsureCurrentFacts();
        NormalizedSaveFacts facts = _currentFacts ??
            throw new InvalidDataException("Current strategy facts are unavailable.");
        ExplicitCandidatePairEvaluation pair = ExplicitCandidatePairEvaluator.Evaluate(
            _session.Store,
            facts,
            ProjectStay(selection.Stay),
            ProjectRotate(selection.Rotate));
        CandidateTarget target = ProjectTarget(selection.Target);
        RotationPolicyStepAttempt attempt =
            _session.ApplySelectedWorkloadCommit(pair, target);

        StrategyCommitStatusV1 status;
        switch (attempt) {
            case AppliedStayBPolicyStep appliedStay:
                status = StrategyCommitStatusV1.AppliedStayB;
                RecordApplied(selection.Target, appliedStay.ResultCursor);
                break;
            case AppliedRotateCPolicyStep appliedRotate:
                status = StrategyCommitStatusV1.AppliedRotateC;
                RecordApplied(selection.Target, appliedRotate.ResultCursor);
                break;
            case SelectedPolicyCandidateCapacityRejected:
                status = StrategyCommitStatusV1.CapacityRejected;
                _terminalAttemptObserved = true;
                break;
            case StayBPolicyCompletionRejectedUnproven:
                status = StrategyCommitStatusV1.CompletionRejectedUnproven;
                _terminalAttemptObserved = true;
                break;
            default:
                throw new InvalidDataException(
                    "The Arena policy harness returned an unsupported attempt kind.");
        }

        _currentFacts = null;
        _currentStep = null;
        return status;
    }

    public StrategyRunProductV1 Complete() {
        EnsureProductNotCreated();
        EvaluatorRunOutcome outcome = _session.Complete();
        StrategyRunTerminationV1 termination = outcome switch {
            AdmittedEvaluatorRun => StrategyRunTerminationV1.Admitted,
            EvaluatorRunCapacityRejected => StrategyRunTerminationV1.CapacityRejected,
            EvaluatorRunRejectedUnproven =>
                StrategyRunTerminationV1.CompletionRejectedUnproven,
            EvaluatorRunIncomplete => StrategyRunTerminationV1.Incomplete,
            _ => throw new InvalidDataException(
                "The evaluator returned an unsupported strategy outcome kind."),
        };
        int terminalRevisionCount = outcome is AdmittedEvaluatorRun admitted
            ? admitted.Settlement.RealizedRevisionCount
            : 0;
        _product = new StrategyRunProductV1(
            this,
            _session.Store,
            _workloadCommits,
            StrategyRevisionCheckpointV1.Create(_session.Cursor),
            termination,
            terminalRevisionCount,
            outcome);
        return _product;
    }

    internal void ValidateReturnedProduct(StrategyRunProductV1 product) {
        ArgumentNullException.ThrowIfNull(product);
        if (!ReferenceEquals(product.Owner, this) ||
            !ReferenceEquals(product, _product) ||
            !ReferenceEquals(product.Store, _session.Store)) {
            throw new InvalidDataException(
                "A strategy returned a product that was not certified by its Arena context.");
        }
    }

    private void EnsureCurrentFacts() {
        if (_currentFacts is not null) {
            return;
        }

        int traceStepIndex = checked(
            _firstWorkloadTraceStepIndex + _session.CompletedWorkloadStepCount);
        _currentFacts = SaveStepNormalizer.Normalize(
            _session.Store,
            _session.Cursor.FileScope.CurrentFileNumber,
            _session.Cursor.PublishedRevisionAddress,
            _trace.Steps[traceStepIndex]);
        _currentStep = StrategyStepViewV1.Create(_currentFacts);
    }

    private void RecordApplied(
        StrategyTargetV1 selectedTarget,
        ProbeRevisionCursor resultCursor) {
        int ordinal = _session.CompletedWorkloadStepCount - 1;
        _workloadCommits.Add(new StrategyCommitReceiptV1(
            ordinal,
            selectedTarget,
            StrategyRevisionCheckpointV1.Create(resultCursor)));
    }

    internal static CandidateTarget ProjectTarget(StrategyTargetV1 target) =>
        target switch {
            StrategyTargetV1.StayB => CandidateTarget.StayB,
            StrategyTargetV1.RotateC => CandidateTarget.RotateC,
            _ => throw new ArgumentOutOfRangeException(nameof(target)),
        };

    internal static StayBSaveDecision ProjectStay(
        StrategyStayDecisionV1 decision) =>
        new(
            decision.UpdateDecisions.Select(static update =>
                new UpdateWriteDecision(update.ObjectId, ProjectMode(update.Mode))),
            decision.UnchangedMigrationObjectIds);

    internal static RotateCSaveDecision ProjectRotate(
        StrategyRotateDecisionV1 decision) => new(
            decision.BContainedUpdateDecisions.Select(static update =>
                new UpdateWriteDecision(update.ObjectId, ProjectMode(update.Mode))),
            decision.BContainedNoChangeBaseObjectIds);

    internal static UpdateWriteMode ProjectMode(StrategyUpdateWriteModeV1 mode) =>
        mode switch {
            StrategyUpdateWriteModeV1.Base => UpdateWriteMode.Base,
            StrategyUpdateWriteModeV1.Delta => UpdateWriteMode.Delta,
            _ => throw new ArgumentOutOfRangeException(nameof(mode)),
        };

    private void EnsureProductNotCreated() {
        if (_product is not null) {
            throw new InvalidOperationException(
                "The strategy run product has already been created.");
        }
    }
}
