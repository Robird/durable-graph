namespace Atelia.DurableGraph.StateStore;

internal enum ObjectSaveChangeKind {
    Insert,
    Update,
    NoChange,
    BaseOnlyUpdate,
}

/// <summary>
/// Object payload byte estimates from one frozen save view. The input to Plan must
/// cover all post-save live objects; removed objects are excluded.
/// Null means inapplicable: Insert has no Delta or reconstruction estimate,
/// BaseOnlyUpdate also has neither, Update has both, and NoChange has only
/// the reconstruction estimate.
/// Known zero estimates are valid; Plan validates the complete input.
/// </summary>
internal readonly record struct ObjectSaveEstimate(
    uint ObjectId,
    ObjectSaveChangeKind ChangeKind,
    long EstimatedBaseWriteBytes,
    long? EstimatedDeltaWriteBytes,
    long? CurrentReconstructionBytes);
