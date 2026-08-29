using Atelia.TwoLegRotationProbe.Encoding;

namespace Atelia.TwoLegRotationProbe.Planning;

internal abstract record CanPrepareAndRotateAttempt;

internal sealed record CanPrepareAndRotateProven(
    CanPrepareAndRotateCertificate Certificate) : CanPrepareAndRotateAttempt;

internal sealed record CanPrepareAndRotateRejectedUnproven(
    CanPrepareAndRotateRejection Rejection) : CanPrepareAndRotateAttempt;

internal enum CanPrepareAndRotateRejectionStage {
    PreparatoryMigration,
    FinalRotateC,
}

internal readonly record struct CanPrepareAndRotateRejection {
    public CanPrepareAndRotateRejection(
        CanPrepareAndRotateRejectionStage stage,
        int completedMigrationCount,
        uint? blockingObjectId,
        RevisionCandidateCapacityRejection capacity) {
        if (!Enum.IsDefined(stage)) {
            throw new ArgumentOutOfRangeException(nameof(stage));
        }

        ArgumentOutOfRangeException.ThrowIfNegative(completedMigrationCount);
        if (stage == CanPrepareAndRotateRejectionStage.PreparatoryMigration &&
            blockingObjectId is null) {
            throw new ArgumentException(
                "A blocked preparatory migration requires its ObjectId.",
                nameof(blockingObjectId));
        }

        Stage = stage;
        CompletedMigrationCount = completedMigrationCount;
        BlockingObjectId = blockingObjectId;
        Capacity = capacity;
    }

    public CanPrepareAndRotateRejectionStage Stage { get; }

    public int CompletedMigrationCount { get; }

    public uint? BlockingObjectId { get; }

    public RevisionCandidateCapacityRejection Capacity { get; }
}
