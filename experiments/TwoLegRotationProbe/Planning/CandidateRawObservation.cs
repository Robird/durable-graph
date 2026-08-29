using Atelia.TwoLegRotationProbe.Encoding;
using Atelia.TwoLegRotationProbe.Model;
using Atelia.TwoLegRotationProbe.Rotation;

namespace Atelia.TwoLegRotationProbe.Planning;

/// <summary>
/// Non-weighted observations for one exact feasible candidate. Physical sizing is
/// delegated to the retained candidate's sole estimator result.
/// </summary>
internal sealed class CandidateRawObservation {
    internal CandidateRawObservation(
        NormalizedSaveFacts facts,
        CandidateTarget target,
        PlannedRevisionV0 candidate,
        int foregroundDomainRecordBytes,
        int maintenanceDomainRecordBytes,
        CandidateReconstructionObservation postLiveReconstruction) {
        if (!Enum.IsDefined(target)) {
            throw new ArgumentOutOfRangeException(nameof(target));
        }

        ArgumentOutOfRangeException.ThrowIfNegative(foregroundDomainRecordBytes);
        ArgumentOutOfRangeException.ThrowIfNegative(maintenanceDomainRecordBytes);
        Facts = facts ?? throw new ArgumentNullException(nameof(facts));
        Target = target;
        Candidate = candidate ?? throw new ArgumentNullException(nameof(candidate));
        ForegroundDomainRecordBytes = foregroundDomainRecordBytes;
        MaintenanceDomainRecordBytes = maintenanceDomainRecordBytes;
        PostLiveReconstruction = postLiveReconstruction ??
            throw new ArgumentNullException(nameof(postLiveReconstruction));

        if (Candidate.FileNumber != PostLiveReconstruction.ResultScope.CurrentFileNumber) {
            throw new ArgumentException(
                "Candidate file and reconstruction result scope disagree.",
                nameof(postLiveReconstruction));
        }
    }

    public NormalizedSaveFacts Facts { get; }

    public CandidateTarget Target { get; }

    public PlannedRevisionV0 Candidate { get; }

    public int ForegroundDomainRecordBytes { get; }

    public int MaintenanceDomainRecordBytes { get; }

    public CandidateReconstructionObservation PostLiveReconstruction { get; }

    public ProvisionalRevisionV0Estimate Estimate => Candidate.Estimate;

    public RbfFrameLayoutEstimate Layout => Candidate.Estimate.RbfLayout;
}
