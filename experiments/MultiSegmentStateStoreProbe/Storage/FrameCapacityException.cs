namespace Atelia.MultiSegmentStateStoreProbe.Storage;

internal sealed class FrameCapacityException : Exception {
    public FrameCapacityException(FrameCapacityRejection rejection)
        : base(
            $"Frame candidate exceeds {rejection.Limit}: attempted " +
            $"{rejection.AttemptedValue}, maximum {rejection.MaximumValue}.") {
        Rejection = rejection;
    }

    public FrameCapacityRejection Rejection { get; }
}
