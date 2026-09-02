using System.Collections.ObjectModel;

namespace Atelia.MultiSegmentStateStoreProbe.Policies;

internal enum ObjectVersionWriteMode {
    Base,
    Delta,
}

internal readonly record struct UpdateRepresentationDecision(
    uint ObjectId,
    ObjectVersionWriteMode Mode);

internal sealed class ReadAmplificationBaseBudgetPolicySelection {
    private readonly ReadOnlyCollection<UpdateRepresentationDecision> _updateDecisions;
    private readonly ReadOnlyCollection<uint> _sameStateRebaseObjectIds;

    internal ReadAmplificationBaseBudgetPolicySelection(
        IEnumerable<UpdateRepresentationDecision> updateDecisions,
        IEnumerable<uint> sameStateRebaseObjectIds,
        long preferredBasePayloadBudgetBytes) {
        ArgumentNullException.ThrowIfNull(updateDecisions);
        ArgumentNullException.ThrowIfNull(sameStateRebaseObjectIds);
        ArgumentOutOfRangeException.ThrowIfNegative(preferredBasePayloadBudgetBytes);

        UpdateRepresentationDecision[] updates = updateDecisions
            .OrderBy(static decision => decision.ObjectId)
            .ToArray();
        uint[] rebases = sameStateRebaseObjectIds.Order().ToArray();
        _updateDecisions = Array.AsReadOnly(updates);
        _sameStateRebaseObjectIds = Array.AsReadOnly(rebases);
        PreferredBasePayloadBudgetBytes = preferredBasePayloadBudgetBytes;
    }

    public IReadOnlyList<UpdateRepresentationDecision> UpdateDecisions =>
        _updateDecisions;

    public IReadOnlyList<uint> SameStateRebaseObjectIds =>
        _sameStateRebaseObjectIds;

    /// <summary>Q: floor(G * BaseBudgetFraction), retained as a diagnostic.</summary>
    public long PreferredBasePayloadBudgetBytes { get; }
}
