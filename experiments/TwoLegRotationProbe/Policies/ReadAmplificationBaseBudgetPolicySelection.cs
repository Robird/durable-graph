using Atelia.TwoLegRotationProbe.Planning;

namespace Atelia.TwoLegRotationProbe.Policies;

/// <summary>
/// Pure policy output over one exact projection and parameter instance.
/// Candidate construction, admission, and apply remain outside this value.
/// </summary>
internal sealed class ReadAmplificationBaseBudgetPolicySelection {
    internal ReadAmplificationBaseBudgetPolicySelection(
        ReadAmplificationBaseBudgetPolicyProjection projection,
        ReadAmplificationBaseBudgetPolicyParameters parameters,
        CandidateTarget target,
        long preferredBasePayloadBudgetBytes,
        uint? stayProgressOverrideObjectId,
        StayBSaveDecision stayB,
        RotateCSaveDecision rotateC) {
        Projection = projection ?? throw new ArgumentNullException(nameof(projection));
        Parameters = parameters ?? throw new ArgumentNullException(nameof(parameters));
        if (!Enum.IsDefined(target)) {
            throw new ArgumentOutOfRangeException(nameof(target));
        }

        ArgumentOutOfRangeException.ThrowIfNegative(
            preferredBasePayloadBudgetBytes);
        Target = target;
        PreferredBasePayloadBudgetBytes = preferredBasePayloadBudgetBytes;
        StayProgressOverrideObjectId = stayProgressOverrideObjectId;
        StayB = stayB ?? throw new ArgumentNullException(nameof(stayB));
        RotateC = rotateC ?? throw new ArgumentNullException(nameof(rotateC));
    }

    public ReadAmplificationBaseBudgetPolicyProjection Projection { get; }

    public ReadAmplificationBaseBudgetPolicyParameters Parameters { get; }

    public CandidateTarget Target { get; }

    public long PreferredBasePayloadBudgetBytes { get; }

    public uint? StayProgressOverrideObjectId { get; }

    public StayBSaveDecision StayB { get; }

    public RotateCSaveDecision RotateC { get; }
}
