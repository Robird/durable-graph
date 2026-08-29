namespace Atelia.TwoLegRotationProbe.Encoding;

internal sealed class RevisionCandidateCapacityException : Exception {
    public RevisionCandidateCapacityException(
        RevisionCandidateCapacityRejection rejection,
        Exception? innerException = null)
        : base(
            $"Revision candidate exceeds {rejection.Limit}: attempted " +
            $"{rejection.AttemptedValue}, maximum {rejection.MaximumValue}.",
            innerException) {
        Rejection = rejection;
    }

    public RevisionCandidateCapacityRejection Rejection { get; }
}
