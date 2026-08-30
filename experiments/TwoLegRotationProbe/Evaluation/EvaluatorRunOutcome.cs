using Atelia.TwoLegRotationProbe.Encoding;
using Atelia.TwoLegRotationProbe.Planning;

namespace Atelia.TwoLegRotationProbe.Evaluation;

internal abstract record EvaluatorRunOutcome;

internal sealed record AdmittedEvaluatorRun(
    EvaluatorRunPosition Position,
    ProbeRevisionCursor FinalCursor,
    EvaluatorRawMetrics Metrics,
    TerminalSettlementObservation Settlement) : EvaluatorRunOutcome;

internal sealed record EvaluatorRunCapacityRejected(
    EvaluatorRunPosition Position,
    CandidateTarget SelectedTarget,
    RevisionCandidateCapacityRejection Rejection) : EvaluatorRunOutcome;

internal sealed record EvaluatorRunRejectedUnproven(
    EvaluatorRunPosition Position,
    CanPrepareAndRotateRejection Rejection) : EvaluatorRunOutcome;

internal sealed record EvaluatorRunIncomplete(
    EvaluatorRunPosition Position) : EvaluatorRunOutcome;

internal enum EvaluatorRunPhase {
    Workload,
    TerminalSettlement,
}

internal readonly record struct EvaluatorRunPosition {
    public EvaluatorRunPosition(
        EvaluatorRunPhase phase,
        int completedWorkloadStepCount,
        int totalWorkloadStepCount) {
        if (!Enum.IsDefined(phase)) {
            throw new ArgumentOutOfRangeException(nameof(phase));
        }

        ArgumentOutOfRangeException.ThrowIfNegative(completedWorkloadStepCount);
        ArgumentOutOfRangeException.ThrowIfNegative(totalWorkloadStepCount);
        if (completedWorkloadStepCount > totalWorkloadStepCount) {
            throw new ArgumentOutOfRangeException(
                nameof(completedWorkloadStepCount),
                completedWorkloadStepCount,
                "Completed workload steps cannot exceed the declared total.");
        }

        if (phase == EvaluatorRunPhase.Workload &&
            completedWorkloadStepCount == totalWorkloadStepCount) {
            throw new ArgumentException(
                "A Workload position must identify an unconsumed workload step.",
                nameof(phase));
        }

        if (phase == EvaluatorRunPhase.TerminalSettlement &&
            completedWorkloadStepCount != totalWorkloadStepCount) {
            throw new ArgumentException(
                "Terminal settlement starts only after all workload steps are consumed.",
                nameof(phase));
        }

        Phase = phase;
        CompletedWorkloadStepCount = completedWorkloadStepCount;
        TotalWorkloadStepCount = totalWorkloadStepCount;
    }

    public EvaluatorRunPhase Phase { get; }

    public int CompletedWorkloadStepCount { get; }

    public int TotalWorkloadStepCount { get; }
}
