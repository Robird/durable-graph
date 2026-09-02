using System.Collections.ObjectModel;

namespace Atelia.MultiSegmentStateStoreProbe.Policies;

internal enum ObjectRepresentationFactKind {
    Insert,
    Update,
    Remove,
    NoChange,
}

/// <summary>
/// Frozen payload proxies for one object in an exact-parent Save. These are policy
/// inputs, not encoded-size or admission authority.
/// </summary>
internal sealed class ReadAmplificationBaseBudgetPolicyFact {
    private ReadAmplificationBaseBudgetPolicyFact(
        uint objectId,
        ObjectRepresentationFactKind kind,
        int? postSaveBasePayloadBytes,
        long? sourceReconstructionPayloadBytes,
        int? deltaPayloadBytes) {
        ObjectId = objectId;
        Kind = kind;
        PostSaveBasePayloadBytes = postSaveBasePayloadBytes;
        SourceReconstructionPayloadBytes = sourceReconstructionPayloadBytes;
        DeltaPayloadBytes = deltaPayloadBytes;
    }

    public uint ObjectId { get; }

    public ObjectRepresentationFactKind Kind { get; }

    /// <summary>B: post-Save Base payload bytes; absent only for Remove.</summary>
    public int? PostSaveBasePayloadBytes { get; }

    /// <summary>H: source reconstruction payload bytes; Update/Remove/NoChange only.</summary>
    public long? SourceReconstructionPayloadBytes { get; }

    /// <summary>D: this Save's Delta payload bytes; Update only.</summary>
    public int? DeltaPayloadBytes { get; }

    public static ReadAmplificationBaseBudgetPolicyFact Insert(
        uint objectId,
        int postSaveBasePayloadBytes) {
        ArgumentOutOfRangeException.ThrowIfNegative(postSaveBasePayloadBytes);
        return new(
            objectId,
            ObjectRepresentationFactKind.Insert,
            postSaveBasePayloadBytes,
            sourceReconstructionPayloadBytes: null,
            deltaPayloadBytes: null);
    }

    public static ReadAmplificationBaseBudgetPolicyFact Update(
        uint objectId,
        int postSaveBasePayloadBytes,
        long sourceReconstructionPayloadBytes,
        int deltaPayloadBytes) {
        ArgumentOutOfRangeException.ThrowIfNegative(postSaveBasePayloadBytes);
        ArgumentOutOfRangeException.ThrowIfNegative(sourceReconstructionPayloadBytes);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(deltaPayloadBytes);
        return new(
            objectId,
            ObjectRepresentationFactKind.Update,
            postSaveBasePayloadBytes,
            sourceReconstructionPayloadBytes,
            deltaPayloadBytes);
    }

    public static ReadAmplificationBaseBudgetPolicyFact Remove(
        uint objectId,
        long sourceReconstructionPayloadBytes) {
        ArgumentOutOfRangeException.ThrowIfNegative(sourceReconstructionPayloadBytes);
        return new(
            objectId,
            ObjectRepresentationFactKind.Remove,
            postSaveBasePayloadBytes: null,
            sourceReconstructionPayloadBytes,
            deltaPayloadBytes: null);
    }

    public static ReadAmplificationBaseBudgetPolicyFact NoChange(
        uint objectId,
        int postSaveBasePayloadBytes,
        long sourceReconstructionPayloadBytes) {
        ArgumentOutOfRangeException.ThrowIfNegative(postSaveBasePayloadBytes);
        ArgumentOutOfRangeException.ThrowIfNegative(sourceReconstructionPayloadBytes);
        return new(
            objectId,
            ObjectRepresentationFactKind.NoChange,
            postSaveBasePayloadBytes,
            sourceReconstructionPayloadBytes,
            deltaPayloadBytes: null);
    }
}

internal sealed class ReadAmplificationBaseBudgetPolicyInput {
    private readonly ReadOnlyCollection<ReadAmplificationBaseBudgetPolicyFact> _facts;

    public ReadAmplificationBaseBudgetPolicyInput(
        IEnumerable<ReadAmplificationBaseBudgetPolicyFact> facts) {
        ArgumentNullException.ThrowIfNull(facts);
        ReadAmplificationBaseBudgetPolicyFact[] canonical = facts
            .Select(static fact => fact ?? throw new ArgumentException(
                "Policy input cannot contain null facts.",
                nameof(facts)))
            .OrderBy(static fact => fact.ObjectId)
            .ToArray();
        for (int index = 1; index < canonical.Length; index++) {
            if (canonical[index - 1].ObjectId == canonical[index].ObjectId) {
                throw new ArgumentException(
                    $"Policy input contains duplicate ObjectId {canonical[index].ObjectId}.",
                    nameof(facts));
            }
        }

        long graphBasePayloadBytes = 0;
        foreach (ReadAmplificationBaseBudgetPolicyFact fact in canonical) {
            if (fact.PostSaveBasePayloadBytes is int baseBytes) {
                graphBasePayloadBytes = checked(graphBasePayloadBytes + baseBytes);
            }
        }

        _facts = Array.AsReadOnly(canonical);
        PostLiveGraphBasePayloadBytes = graphBasePayloadBytes;
    }

    public IReadOnlyList<ReadAmplificationBaseBudgetPolicyFact> Facts => _facts;

    /// <summary>G: sum of B over Insert, Update, and NoChange.</summary>
    public long PostLiveGraphBasePayloadBytes { get; }
}
