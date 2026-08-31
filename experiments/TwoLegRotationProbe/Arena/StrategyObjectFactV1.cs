namespace Atelia.TwoLegRotationProbe.Arena;

/// <summary>
/// Identifies the role of one object in a normalized Save step.
/// </summary>
public enum StrategyObjectKindV1 {
    Insert,
    Update,
    Remove,
    NoChange,
}

/// <summary>
/// Immutable payload-only facts for one object in a normalized Save step.
/// Nullable properties are present only for the object kinds documented on them.
/// </summary>
public sealed class StrategyObjectFactV1 {
    private StrategyObjectFactV1(
        uint objectId,
        StrategyObjectKindV1 kind,
        bool? sourceIsPreviousDependent,
        int? sourceBasePayloadBytes,
        long? sourceHeadReconstructionPayloadBytes,
        int? resultBasePayloadBytes,
        int? deltaPayloadBytes) {
        ObjectId = objectId;
        Kind = kind;
        SourceIsPreviousDependent = sourceIsPreviousDependent;
        SourceBasePayloadBytes = sourceBasePayloadBytes;
        SourceHeadReconstructionPayloadBytes =
            sourceHeadReconstructionPayloadBytes;
        ResultBasePayloadBytes = resultBasePayloadBytes;
        DeltaPayloadBytes = deltaPayloadBytes;
    }

    public uint ObjectId { get; }

    public StrategyObjectKindV1 Kind { get; }

    /// <summary>
    /// Whether the source reconstruction chain terminates in the Previous file.
    /// Present for Update, Remove, and NoChange; absent for Insert.
    /// </summary>
    public bool? SourceIsPreviousDependent { get; }

    /// <summary>
    /// Base payload bytes of the source state.
    /// Present for Update, Remove, and NoChange; absent for Insert.
    /// </summary>
    public int? SourceBasePayloadBytes { get; }

    /// <summary>
    /// Object-payload bytes in the source head reconstruction chain (H).
    /// Present for Update, Remove, and NoChange; absent for Insert.
    /// </summary>
    public long? SourceHeadReconstructionPayloadBytes { get; }

    /// <summary>
    /// Base payload bytes of the post-Save state (B).
    /// Present for Insert, Update, and NoChange; absent for Remove.
    /// </summary>
    public int? ResultBasePayloadBytes { get; }

    /// <summary>
    /// Delta payload bytes of this Update (D). Present only for Update.
    /// </summary>
    public int? DeltaPayloadBytes { get; }

    public static StrategyObjectFactV1 Insert(
        uint objectId,
        int resultBasePayloadBytes) {
        ArgumentOutOfRangeException.ThrowIfNegative(resultBasePayloadBytes);
        return new StrategyObjectFactV1(
            objectId,
            StrategyObjectKindV1.Insert,
            sourceIsPreviousDependent: null,
            sourceBasePayloadBytes: null,
            sourceHeadReconstructionPayloadBytes: null,
            resultBasePayloadBytes,
            deltaPayloadBytes: null);
    }

    public static StrategyObjectFactV1 Update(
        uint objectId,
        bool sourceIsPreviousDependent,
        int sourceBasePayloadBytes,
        long sourceHeadReconstructionPayloadBytes,
        int resultBasePayloadBytes,
        int deltaPayloadBytes) {
        ArgumentOutOfRangeException.ThrowIfNegative(sourceBasePayloadBytes);
        ArgumentOutOfRangeException.ThrowIfNegative(
            sourceHeadReconstructionPayloadBytes);
        ArgumentOutOfRangeException.ThrowIfNegative(resultBasePayloadBytes);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(deltaPayloadBytes);
        return new StrategyObjectFactV1(
            objectId,
            StrategyObjectKindV1.Update,
            sourceIsPreviousDependent,
            sourceBasePayloadBytes,
            sourceHeadReconstructionPayloadBytes,
            resultBasePayloadBytes,
            deltaPayloadBytes);
    }

    public static StrategyObjectFactV1 Remove(
        uint objectId,
        bool sourceIsPreviousDependent,
        int sourceBasePayloadBytes,
        long sourceHeadReconstructionPayloadBytes) {
        ArgumentOutOfRangeException.ThrowIfNegative(sourceBasePayloadBytes);
        ArgumentOutOfRangeException.ThrowIfNegative(
            sourceHeadReconstructionPayloadBytes);
        return new StrategyObjectFactV1(
            objectId,
            StrategyObjectKindV1.Remove,
            sourceIsPreviousDependent,
            sourceBasePayloadBytes,
            sourceHeadReconstructionPayloadBytes,
            resultBasePayloadBytes: null,
            deltaPayloadBytes: null);
    }

    public static StrategyObjectFactV1 NoChange(
        uint objectId,
        bool sourceIsPreviousDependent,
        int sourceBasePayloadBytes,
        long sourceHeadReconstructionPayloadBytes) {
        ArgumentOutOfRangeException.ThrowIfNegative(sourceBasePayloadBytes);
        ArgumentOutOfRangeException.ThrowIfNegative(
            sourceHeadReconstructionPayloadBytes);
        return new StrategyObjectFactV1(
            objectId,
            StrategyObjectKindV1.NoChange,
            sourceIsPreviousDependent,
            sourceBasePayloadBytes,
            sourceHeadReconstructionPayloadBytes,
            resultBasePayloadBytes: sourceBasePayloadBytes,
            deltaPayloadBytes: null);
    }
}
