using Atelia.TwoLegRotationProbe.Encoding;

namespace Atelia.TwoLegRotationProbe.Planning;

internal abstract record ExactCandidateAttempt<TPlan>
    where TPlan : class;

internal sealed record FeasibleCandidate<TPlan>(
    TPlan Plan,
    CandidateRawObservation Observation) : ExactCandidateAttempt<TPlan>
    where TPlan : class;

internal sealed record CapacityRejectedCandidate<TPlan>(
    RevisionCandidateCapacityRejection Rejection) : ExactCandidateAttempt<TPlan>
    where TPlan : class;
