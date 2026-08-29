namespace Atelia.TwoLegRotationProbe.Encoding;

internal readonly record struct RevisionCandidateCapacityRejection(
    RevisionCandidateCapacityLimit Limit,
    long AttemptedValue,
    long MaximumValue);
