namespace Atelia.MultiSegmentStateStoreProbe.Storage;

internal enum FrameCapacityLimit {
    FrameStart,
    TailMetadataLength,
    PayloadAndMetadataLength,
    FileNumber,
}
