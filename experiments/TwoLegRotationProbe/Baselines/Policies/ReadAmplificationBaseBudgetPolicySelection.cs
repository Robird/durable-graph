using Atelia.TwoLegRotationProbe.Arena;

namespace Atelia.TwoLegRotationProbe.Policies;

/// <summary>
/// Pure policy output over one exact projection and parameter instance.
/// Candidate construction, admission, and apply remain in Arena.
/// </summary>
internal sealed class ReadAmplificationBaseBudgetPolicySelection {
    internal ReadAmplificationBaseBudgetPolicySelection(
        ReadAmplificationBaseBudgetPolicyProjection projection,
        ReadAmplificationBaseBudgetPolicyParameters parameters,
        StrategySelectionV1 selection,
        long preferredBasePayloadBudgetBytes,
        uint? stayProgressOverrideObjectId) {
        Projection = projection ?? throw new ArgumentNullException(nameof(projection));
        Parameters = parameters ?? throw new ArgumentNullException(nameof(parameters));
        Selection = selection ?? throw new ArgumentNullException(nameof(selection));
        ArgumentOutOfRangeException.ThrowIfNegative(
            preferredBasePayloadBudgetBytes);
        PreferredBasePayloadBudgetBytes = preferredBasePayloadBudgetBytes;
        StayProgressOverrideObjectId = stayProgressOverrideObjectId;
    }

    public ReadAmplificationBaseBudgetPolicyProjection Projection { get; }

    public ReadAmplificationBaseBudgetPolicyParameters Parameters { get; }

    public StrategySelectionV1 Selection { get; }

    public StrategyTargetV1 Target => Selection.Target;

    public long PreferredBasePayloadBudgetBytes { get; }

    public uint? StayProgressOverrideObjectId { get; }

    public StrategyStayDecisionV1 StayB => Selection.Stay;

    public StrategyRotateDecisionV1 RotateC => Selection.Rotate;
}
