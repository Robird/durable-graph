using Atelia.DurableGraph.StateStore.Storage;

namespace Atelia.DurableGraph.StateStore;

/// <summary>Verified head and actual reconstruction payload for one stored object.</summary>
internal readonly record struct ObjectStorageInfo(FrameAddress Head, long ReconstructionPayloadBytes);
