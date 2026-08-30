using System.Collections.ObjectModel;
using Atelia.TwoLegRotationProbe.Encoding;
using Atelia.TwoLegRotationProbe.Evaluation;
using Atelia.TwoLegRotationProbe.Model;
using Atelia.TwoLegRotationProbe.Planning;

namespace Atelia.TwoLegRotationProbe.Benchmarking;

internal sealed class BenchmarkReportV1 {
    private readonly ReadOnlyCollection<BenchmarkCaseReportV1> _cases;

    public BenchmarkReportV1(
        BenchmarkManifestV1 manifest,
        IEnumerable<BenchmarkCaseReportV1> cases) {
        ArgumentNullException.ThrowIfNull(manifest);
        ArgumentNullException.ThrowIfNull(cases);

        BenchmarkCaseReportV1[] canonicalCases = cases
            .Select(static benchmarkCase => benchmarkCase ??
                throw new ArgumentException(
                    "A benchmark report cannot contain a null case.",
                    nameof(cases)))
            .OrderBy(static benchmarkCase => benchmarkCase.CaseId, StringComparer.Ordinal)
            .ToArray();
        string[] expectedCaseIds = manifest.Cases
            .Select(static benchmarkCase => benchmarkCase.CaseId)
            .ToArray();
        string[] actualCaseIds = canonicalCases
            .Select(static benchmarkCase => benchmarkCase.CaseId)
            .ToArray();
        if (!expectedCaseIds.SequenceEqual(actualCaseIds, StringComparer.Ordinal)) {
            throw new ArgumentException(
                "A benchmark report must contain exactly one result for every manifest case.",
                nameof(cases));
        }

        for (int index = 0; index < canonicalCases.Length; index++) {
            if (!StringComparer.Ordinal.Equals(
                manifest.Cases[index].ResolvedTraceSha256,
                canonicalCases[index].ResolvedTraceSha256)) {
                throw new ArgumentException(
                    $"Benchmark case '{canonicalCases[index].CaseId}' does not " +
                    "match its manifest trace SHA-256.",
                    nameof(cases));
            }

            ValidateOutcomePosition(
                manifest.Cases[index],
                canonicalCases[index],
                nameof(cases));
        }

        Schema = BenchmarkV1Identities.ReportSchema;
        ManifestId = manifest.ManifestId;
        ManifestRevision = manifest.ManifestRevision;
        ManifestSha256 = BenchmarkV1Json.ComputeManifestSha256(manifest);
        Evaluator = manifest.Evaluator;
        TerminalSettlement = manifest.TerminalSettlement;
        ReadSchedule = manifest.ReadSchedule;
        Metrics = manifest.Metrics;
        FrameLayout = manifest.FrameLayout;
        RevisionGrammar = manifest.RevisionGrammar;
        _cases = Array.AsReadOnly(canonicalCases);
    }

    private static void ValidateOutcomePosition(
        BenchmarkCaseManifestV1 manifestCase,
        BenchmarkCaseReportV1 reportCase,
        string parameterName) {
        EvaluatorPositionReportV1 position = reportCase.Outcome.Position;
        if (position.TotalWorkloadStepCount !=
            manifestCase.EvaluatedWorkloadStepCount) {
            throw new ArgumentException(
                $"Benchmark case '{reportCase.CaseId}' outcome declares " +
                $"{position.TotalWorkloadStepCount} workload steps, but its " +
                $"manifest declares {manifestCase.EvaluatedWorkloadStepCount}.",
                parameterName);
        }

        if (reportCase.Outcome is AdmittedOutcomeReportV1 &&
            position.Phase != EvaluatorRunPhase.TerminalSettlement) {
            throw new ArgumentException(
                $"Admitted benchmark case '{reportCase.CaseId}' must be at " +
                "terminal settlement.",
                parameterName);
        }

        if (reportCase.Outcome is CapacityRejectedOutcomeReportV1 &&
            position.Phase != EvaluatorRunPhase.Workload) {
            throw new ArgumentException(
                $"Capacity-rejected benchmark case '{reportCase.CaseId}' " +
                "must identify a workload step.",
                parameterName);
        }
    }

    public BenchmarkComponentIdentityV1 Schema { get; }

    public string ManifestId { get; }

    public int ManifestRevision { get; }

    public string ManifestSha256 { get; }

    public BenchmarkComponentIdentityV1 Evaluator { get; }

    public BenchmarkComponentIdentityV1 TerminalSettlement { get; }

    public BenchmarkComponentIdentityV1 ReadSchedule { get; }

    public BenchmarkComponentIdentityV1 Metrics { get; }

    public BenchmarkComponentIdentityV1 FrameLayout { get; }

    public BenchmarkComponentIdentityV1 RevisionGrammar { get; }

    public IReadOnlyList<BenchmarkCaseReportV1> Cases => _cases;
}

internal sealed record BenchmarkCaseReportV1 {
    public BenchmarkCaseReportV1(
        string caseId,
        string resolvedTraceSha256,
        BenchmarkOutcomeReportV1 outcome) {
        BenchmarkV1Text.ValidateId(caseId, nameof(caseId));
        BenchmarkV1Text.ValidateSha256(resolvedTraceSha256, nameof(resolvedTraceSha256));

        CaseId = caseId;
        ResolvedTraceSha256 = resolvedTraceSha256;
        Outcome = outcome ?? throw new ArgumentNullException(nameof(outcome));
    }

    public string CaseId { get; }

    public string ResolvedTraceSha256 { get; }

    public BenchmarkOutcomeReportV1 Outcome { get; }
}

internal abstract record BenchmarkOutcomeReportV1 {
    protected BenchmarkOutcomeReportV1(EvaluatorPositionReportV1 position) {
        Position = position ?? throw new ArgumentNullException(nameof(position));
    }

    public EvaluatorPositionReportV1 Position { get; }

    public static BenchmarkOutcomeReportV1 Project(EvaluatorRunOutcome outcome) {
        ArgumentNullException.ThrowIfNull(outcome);
        return outcome switch {
            AdmittedEvaluatorRun admitted => new AdmittedOutcomeReportV1(
                EvaluatorPositionReportV1.Project(admitted.Position),
                EvaluatorMetricsReportV1.Project(admitted.Metrics),
                FinalCursorReportV1.Project(admitted.FinalCursor),
                TerminalSettlementReportV1.Project(admitted.Settlement)),
            EvaluatorRunCapacityRejected capacity =>
                new CapacityRejectedOutcomeReportV1(
                    EvaluatorPositionReportV1.Project(capacity.Position),
                    capacity.SelectedTarget,
                    CapacityRejectionReportV1.Project(capacity.Rejection)),
            EvaluatorRunRejectedUnproven unproven =>
                new RejectedUnprovenOutcomeReportV1(
                    EvaluatorPositionReportV1.Project(unproven.Position),
                    CompletionRejectionReportV1.Project(unproven.Rejection)),
            EvaluatorRunIncomplete incomplete => new IncompleteOutcomeReportV1(
                EvaluatorPositionReportV1.Project(incomplete.Position)),
            _ => throw new InvalidDataException(
                "The evaluator returned an unsupported outcome kind."),
        };
    }
}

internal sealed record AdmittedOutcomeReportV1 : BenchmarkOutcomeReportV1 {
    public AdmittedOutcomeReportV1(
        EvaluatorPositionReportV1 position,
        EvaluatorMetricsReportV1 metrics,
        FinalCursorReportV1 finalCursor,
        TerminalSettlementReportV1 settlement) : base(position) {
        Metrics = metrics ?? throw new ArgumentNullException(nameof(metrics));
        FinalCursor = finalCursor ?? throw new ArgumentNullException(nameof(finalCursor));
        Settlement = settlement ?? throw new ArgumentNullException(nameof(settlement));
    }

    public EvaluatorMetricsReportV1 Metrics { get; }

    public FinalCursorReportV1 FinalCursor { get; }

    public TerminalSettlementReportV1 Settlement { get; }
}

internal sealed record CapacityRejectedOutcomeReportV1 : BenchmarkOutcomeReportV1 {
    public CapacityRejectedOutcomeReportV1(
        EvaluatorPositionReportV1 position,
        CandidateTarget selectedTarget,
        CapacityRejectionReportV1 rejection) : base(position) {
        if (!Enum.IsDefined(selectedTarget)) {
            throw new ArgumentOutOfRangeException(nameof(selectedTarget));
        }

        SelectedTarget = selectedTarget;
        Rejection = rejection ?? throw new ArgumentNullException(nameof(rejection));
    }

    public CandidateTarget SelectedTarget { get; }

    public CapacityRejectionReportV1 Rejection { get; }
}

internal sealed record RejectedUnprovenOutcomeReportV1 : BenchmarkOutcomeReportV1 {
    public RejectedUnprovenOutcomeReportV1(
        EvaluatorPositionReportV1 position,
        CompletionRejectionReportV1 rejection) : base(position) {
        Rejection = rejection ?? throw new ArgumentNullException(nameof(rejection));
    }

    public CompletionRejectionReportV1 Rejection { get; }
}

internal sealed record IncompleteOutcomeReportV1 : BenchmarkOutcomeReportV1 {
    public IncompleteOutcomeReportV1(EvaluatorPositionReportV1 position) : base(position) {
    }
}

internal sealed record EvaluatorPositionReportV1 {
    public EvaluatorPositionReportV1(
        EvaluatorRunPhase phase,
        int completedWorkloadStepCount,
        int totalWorkloadStepCount) {
        _ = new EvaluatorRunPosition(
            phase,
            completedWorkloadStepCount,
            totalWorkloadStepCount);
        Phase = phase;
        CompletedWorkloadStepCount = completedWorkloadStepCount;
        TotalWorkloadStepCount = totalWorkloadStepCount;
    }

    public EvaluatorRunPhase Phase { get; }

    public int CompletedWorkloadStepCount { get; }

    public int TotalWorkloadStepCount { get; }

    public static EvaluatorPositionReportV1 Project(EvaluatorRunPosition position) => new(
        position.Phase,
        position.CompletedWorkloadStepCount,
        position.TotalWorkloadStepCount);
}

internal sealed record EvaluatorMetricsReportV1 {
    public EvaluatorMetricsReportV1(
        int realizedCommitCount,
        long totalPhysicalWriteBytes,
        long peakCommitWriteBytes,
        long maxCurrentFileTailBytes,
        long finalColdHeadReadBytes) {
        ArgumentOutOfRangeException.ThrowIfNegative(realizedCommitCount);
        ArgumentOutOfRangeException.ThrowIfNegative(totalPhysicalWriteBytes);
        ArgumentOutOfRangeException.ThrowIfNegative(peakCommitWriteBytes);
        if (maxCurrentFileTailBytes < RbfV040Layout.InitialTailOffsetBytes) {
            throw new ArgumentOutOfRangeException(nameof(maxCurrentFileTailBytes));
        }

        ArgumentOutOfRangeException.ThrowIfNegative(finalColdHeadReadBytes);
        if (peakCommitWriteBytes > totalPhysicalWriteBytes) {
            throw new ArgumentException(
                "Peak Commit writes cannot exceed total writes.",
                nameof(peakCommitWriteBytes));
        }

        if (realizedCommitCount == 0 && peakCommitWriteBytes != 0) {
            throw new ArgumentException(
                "A run without realized Commits cannot have a nonzero write peak.",
                nameof(peakCommitWriteBytes));
        }

        RealizedCommitCount = realizedCommitCount;
        TotalPhysicalWriteBytes = totalPhysicalWriteBytes;
        PeakCommitWriteBytes = peakCommitWriteBytes;
        MaxCurrentFileTailBytes = maxCurrentFileTailBytes;
        FinalColdHeadReadBytes = finalColdHeadReadBytes;
    }

    public int RealizedCommitCount { get; }

    public long TotalPhysicalWriteBytes { get; }

    public long PeakCommitWriteBytes { get; }

    public long MaxCurrentFileTailBytes { get; }

    public long FinalColdHeadReadBytes { get; }

    public static EvaluatorMetricsReportV1 Project(EvaluatorRawMetrics metrics) {
        ArgumentNullException.ThrowIfNull(metrics);
        return new EvaluatorMetricsReportV1(
            metrics.RealizedCommitCount,
            metrics.TotalPhysicalWriteBytes,
            metrics.PeakCommitWriteBytes,
            metrics.MaxCurrentFileTailBytes,
            metrics.FinalColdHeadReadBytes);
    }
}

internal sealed record FinalCursorReportV1 {
    public FinalCursorReportV1(
        uint previousFileNumber,
        uint currentFileNumber,
        FrameAddressReportV1 publishedRevision,
        long currentFileTailBytes) {
        ArgumentOutOfRangeException.ThrowIfZero(previousFileNumber);
        if (currentFileNumber != checked(previousFileNumber + 1)) {
            throw new ArgumentException(
                "The final cursor must describe adjacent Previous and Current files.",
                nameof(currentFileNumber));
        }

        ArgumentNullException.ThrowIfNull(publishedRevision);
        if (publishedRevision.FileNumber != currentFileNumber) {
            throw new ArgumentException(
                "The final PublishedRevision must be in the Current file.",
                nameof(publishedRevision));
        }

        long publishedAppendEnd = checked(
            publishedRevision.OffsetBytes +
            publishedRevision.LengthBytes +
            RbfV040Layout.TrailingFenceBytes);
        if (currentFileTailBytes < publishedAppendEnd) {
            throw new ArgumentOutOfRangeException(nameof(currentFileTailBytes));
        }

        if ((currentFileTailBytes & RbfV040Layout.AlignmentMask) != 0) {
            throw new ArgumentOutOfRangeException(
                nameof(currentFileTailBytes),
                currentFileTailBytes,
                "The final Current-file tail must be aligned.");
        }

        PreviousFileNumber = previousFileNumber;
        CurrentFileNumber = currentFileNumber;
        PublishedRevision = publishedRevision;
        CurrentFileTailBytes = currentFileTailBytes;
    }

    public uint PreviousFileNumber { get; }

    public uint CurrentFileNumber { get; }

    public FrameAddressReportV1 PublishedRevision { get; }

    public long CurrentFileTailBytes { get; }

    public static FinalCursorReportV1 Project(ProbeRevisionCursor cursor) {
        ArgumentNullException.ThrowIfNull(cursor);
        uint previous = cursor.FileScope.PreviousFileNumber ??
            throw new InvalidDataException(
                "An admitted evaluator cursor must have a Previous file.");
        return new FinalCursorReportV1(
            previous,
            cursor.FileScope.CurrentFileNumber,
            FrameAddressReportV1.Project(cursor.PublishedRevisionAddress),
            cursor.CurrentFileTailOffsetBytes);
    }
}

internal sealed record FrameAddressReportV1 {
    public FrameAddressReportV1(uint fileNumber, long offsetBytes, int lengthBytes) {
        FrameTicket ticket = new(offsetBytes, lengthBytes);
        _ = new AbsoluteFrameAddress(fileNumber, ticket);
        FileNumber = fileNumber;
        OffsetBytes = offsetBytes;
        LengthBytes = lengthBytes;
    }

    public uint FileNumber { get; }

    public long OffsetBytes { get; }

    public int LengthBytes { get; }

    public static FrameAddressReportV1 Project(AbsoluteFrameAddress address) => new(
        address.FileNumber,
        address.FrameTicket.OffsetBytes,
        address.FrameTicket.LengthBytes);
}

internal sealed class TerminalSettlementReportV1 {
    private readonly ReadOnlyCollection<uint> _migratedObjectIds;

    public TerminalSettlementReportV1(IEnumerable<uint> migratedObjectIds) {
        ArgumentNullException.ThrowIfNull(migratedObjectIds);
        uint[] snapshot = migratedObjectIds.ToArray();
        if (!snapshot.SequenceEqual(
            snapshot.OrderBy(static objectId => objectId).Distinct())) {
            throw new ArgumentException(
                "Settlement migrations must contain unique ascending ObjectIds.",
                nameof(migratedObjectIds));
        }

        _migratedObjectIds = Array.AsReadOnly(snapshot);
    }

    public IReadOnlyList<uint> MigratedObjectIds => _migratedObjectIds;

    public int MaintenanceRevisionCount => _migratedObjectIds.Count;

    public int RealizedRevisionCount => checked(MaintenanceRevisionCount + 1);

    public static TerminalSettlementReportV1 Project(
        TerminalSettlementObservation settlement) {
        ArgumentNullException.ThrowIfNull(settlement);
        return new TerminalSettlementReportV1(settlement.MigratedObjectIds);
    }
}

internal sealed record CapacityRejectionReportV1 {
    public CapacityRejectionReportV1(
        RevisionCandidateCapacityLimit limit,
        long attemptedValue,
        long maximumValue) {
        if (!Enum.IsDefined(limit)) {
            throw new ArgumentOutOfRangeException(nameof(limit));
        }

        ArgumentOutOfRangeException.ThrowIfNegative(attemptedValue);
        ArgumentOutOfRangeException.ThrowIfNegative(maximumValue);
        if (attemptedValue <= maximumValue) {
            throw new ArgumentException(
                "A capacity rejection requires an attempted value above its maximum.",
                nameof(attemptedValue));
        }

        Limit = limit;
        AttemptedValue = attemptedValue;
        MaximumValue = maximumValue;
    }

    public RevisionCandidateCapacityLimit Limit { get; }

    public long AttemptedValue { get; }

    public long MaximumValue { get; }

    public static CapacityRejectionReportV1 Project(
        RevisionCandidateCapacityRejection rejection) => new(
        rejection.Limit,
        rejection.AttemptedValue,
        rejection.MaximumValue);
}

internal sealed record CompletionRejectionReportV1 {
    public CompletionRejectionReportV1(
        CanPrepareAndRotateRejectionStage stage,
        int completedMigrationCount,
        uint? blockingObjectId,
        CapacityRejectionReportV1 capacity) {
        ArgumentNullException.ThrowIfNull(capacity);
        _ = new CanPrepareAndRotateRejection(
            stage,
            completedMigrationCount,
            blockingObjectId,
            new RevisionCandidateCapacityRejection(
                capacity.Limit,
                capacity.AttemptedValue,
                capacity.MaximumValue));
        Stage = stage;
        CompletedMigrationCount = completedMigrationCount;
        BlockingObjectId = blockingObjectId;
        Capacity = capacity;
    }

    public CanPrepareAndRotateRejectionStage Stage { get; }

    public int CompletedMigrationCount { get; }

    public uint? BlockingObjectId { get; }

    public CapacityRejectionReportV1 Capacity { get; }

    public static CompletionRejectionReportV1 Project(
        CanPrepareAndRotateRejection rejection) => new(
        rejection.Stage,
        rejection.CompletedMigrationCount,
        rejection.BlockingObjectId,
        CapacityRejectionReportV1.Project(rejection.Capacity));
}
