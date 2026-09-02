namespace Atelia.MultiSegmentStateStoreProbe.Storage;

internal readonly record struct FrameCapacityRejection(
    FrameCapacityLimit Limit,
    long AttemptedValue,
    long MaximumValue);
