using System.Collections.ObjectModel;
using Atelia.TwoLegRotationProbe.Arena;

namespace Atelia.TwoLegRotationProbe.Policies;

/// <summary>
/// A policy-local payload projection over the public Arena Save-step view.
/// It deliberately excludes physical Frame and encoded-record sizing.
/// </summary>
internal sealed class ReadAmplificationBaseBudgetPolicyProjection {
    private readonly ReadOnlyCollection<ReadAmplificationBaseBudgetPolicyObjectFact>
        _postLiveObjects;

    private ReadAmplificationBaseBudgetPolicyProjection(
        StrategyStepViewV1 view,
        ReadAmplificationBaseBudgetPolicyObjectFact[] postLiveObjects) {
        View = view;
        _postLiveObjects = Array.AsReadOnly(postLiveObjects);
    }

    public StrategyStepViewV1 View { get; }

    public IReadOnlyList<ReadAmplificationBaseBudgetPolicyObjectFact> PostLiveObjects =>
        _postLiveObjects;

    public long PostLiveGraphBasePayloadBytes =>
        View.PostLiveGraphBasePayloadBytes;

    public long ADependentEvacuationBasePayloadBytes =>
        View.ADependentEvacuationBasePayloadBytes;

    public static ReadAmplificationBaseBudgetPolicyProjection Create(
        StrategyStepViewV1 view) {
        ArgumentNullException.ThrowIfNull(view);

        ReadAmplificationBaseBudgetPolicyObjectFact[] objects = view.Objects
            .Select(Project)
            .Where(static fact => fact is not null)
            .Cast<ReadAmplificationBaseBudgetPolicyObjectFact>()
            .ToArray();
        if (objects.Sum(static fact => (long)fact.PostSaveBasePayloadBytes) !=
            view.PostLiveGraphBasePayloadBytes ||
            objects.Where(static fact => fact.IsADependent)
                .Sum(static fact => (long)fact.PostSaveBasePayloadBytes) !=
                view.ADependentEvacuationBasePayloadBytes) {
            throw new InvalidDataException(
                "Adaptive policy projection diverged from the Arena payload totals.");
        }

        return new ReadAmplificationBaseBudgetPolicyProjection(view, objects);
    }

    private static ReadAmplificationBaseBudgetPolicyObjectFact? Project(
        StrategyObjectFactV1 fact) => fact.Kind switch {
            StrategyObjectKindV1.Insert => new(
                fact.ObjectId,
                ReadAmplificationBaseBudgetPolicyObjectKind.Insert,
                isADependent: false,
                fact.ResultBasePayloadBytes!.Value,
                sourceHeadReconstructionObjectPayloadBytes: 0,
                deltaPayloadBytes: null),
            StrategyObjectKindV1.Update => new(
                fact.ObjectId,
                ReadAmplificationBaseBudgetPolicyObjectKind.Update,
                fact.SourceIsPreviousDependent!.Value,
                fact.ResultBasePayloadBytes!.Value,
                fact.SourceHeadReconstructionPayloadBytes!.Value,
                fact.DeltaPayloadBytes),
            StrategyObjectKindV1.Remove => null,
            StrategyObjectKindV1.NoChange => new(
                fact.ObjectId,
                ReadAmplificationBaseBudgetPolicyObjectKind.NoChange,
                fact.SourceIsPreviousDependent!.Value,
                fact.ResultBasePayloadBytes!.Value,
                fact.SourceHeadReconstructionPayloadBytes!.Value,
                deltaPayloadBytes: null),
            _ => throw new InvalidDataException(
                $"Unsupported strategy fact kind '{fact.Kind}'."),
        };
}
