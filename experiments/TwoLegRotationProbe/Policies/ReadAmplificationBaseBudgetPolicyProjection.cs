using System.Collections.ObjectModel;
using Atelia.TwoLegRotationProbe.Planning;

namespace Atelia.TwoLegRotationProbe.Policies;

/// <summary>
/// A policy-local payload projection over one exact normalized Save-facts instance.
/// It deliberately excludes physical Frame and encoded-record sizing.
/// </summary>
internal sealed class ReadAmplificationBaseBudgetPolicyProjection {
    private readonly ReadOnlyCollection<ReadAmplificationBaseBudgetPolicyObjectFact>
        _postLiveObjects;

    private ReadAmplificationBaseBudgetPolicyProjection(
        NormalizedSaveFacts facts,
        ReadAmplificationBaseBudgetPolicyObjectFact[] postLiveObjects,
        long postLiveGraphBasePayloadBytes,
        long aDependentEvacuationBasePayloadBytes) {
        Facts = facts;
        _postLiveObjects = Array.AsReadOnly(postLiveObjects);
        PostLiveGraphBasePayloadBytes = postLiveGraphBasePayloadBytes;
        ADependentEvacuationBasePayloadBytes =
            aDependentEvacuationBasePayloadBytes;
    }

    public NormalizedSaveFacts Facts { get; }

    public IReadOnlyList<ReadAmplificationBaseBudgetPolicyObjectFact> PostLiveObjects =>
        _postLiveObjects;

    public long PostLiveGraphBasePayloadBytes { get; }

    public long ADependentEvacuationBasePayloadBytes { get; }

    public static ReadAmplificationBaseBudgetPolicyProjection Create(
        NormalizedSaveFacts facts) {
        ArgumentNullException.ThrowIfNull(facts);

        List<ReadAmplificationBaseBudgetPolicyObjectFact> objects = [];
        long graphBasePayloadBytes = 0;
        long evacuationBasePayloadBytes = 0;
        foreach (NormalizedSaveFact fact in facts.AllFacts) {
            ReadAmplificationBaseBudgetPolicyObjectFact? projected = fact switch {
                NormalizedInsertFact insert => ProjectInsert(insert),
                NormalizedUpdateFact update => ProjectUpdate(facts, update),
                NormalizedRemoveFact => null,
                NormalizedNoChangeFact noChange => ProjectNoChange(facts, noChange),
                _ => throw new InvalidDataException(
                    $"Unsupported normalized Save fact type '{fact.GetType().FullName}'."),
            };
            if (projected is null) {
                continue;
            }

            objects.Add(projected);
            graphBasePayloadBytes = checked(
                graphBasePayloadBytes + projected.PostSaveBasePayloadBytes);
            if (projected.IsADependent) {
                evacuationBasePayloadBytes = checked(
                    evacuationBasePayloadBytes + projected.PostSaveBasePayloadBytes);
            }
        }

        ReadAmplificationBaseBudgetPolicyObjectFact[] canonicalObjects = objects
            .OrderBy(static fact => fact.ObjectId)
            .ToArray();
        ValidatePostLiveIdentity(facts, canonicalObjects);
        return new ReadAmplificationBaseBudgetPolicyProjection(
            facts,
            canonicalObjects,
            graphBasePayloadBytes,
            evacuationBasePayloadBytes);
    }

    private static ReadAmplificationBaseBudgetPolicyObjectFact ProjectInsert(
        NormalizedInsertFact insert) => new(
            insert.ObjectId,
            ReadAmplificationBaseBudgetPolicyObjectKind.Insert,
            isADependent: false,
            insert.ResultState.BasePayloadBytes,
            sourceHeadReconstructionObjectPayloadBytes: 0,
            deltaPayloadBytes: null);

    private static ReadAmplificationBaseBudgetPolicyObjectFact ProjectUpdate(
        NormalizedSaveFacts facts,
        NormalizedUpdateFact update) => new(
            update.ObjectId,
            ReadAmplificationBaseBudgetPolicyObjectKind.Update,
            IsADependent(facts, update.Source),
            update.ResultState.BasePayloadBytes,
            update.Source.HeadReconstructionObjectPayloadBytes,
            update.DeltaPayloadBytes);

    private static ReadAmplificationBaseBudgetPolicyObjectFact ProjectNoChange(
        NormalizedSaveFacts facts,
        NormalizedNoChangeFact noChange) => new(
            noChange.ObjectId,
            ReadAmplificationBaseBudgetPolicyObjectKind.NoChange,
            IsADependent(facts, noChange.Source),
            noChange.Source.State.BasePayloadBytes,
            noChange.Source.HeadReconstructionObjectPayloadBytes,
            deltaPayloadBytes: null);

    private static bool IsADependent(
        NormalizedSaveFacts facts,
        SourceObjectFact source) {
        if (source.BaseAddress.FileNumber == facts.PreviousFileNumber) {
            return true;
        }

        if (source.BaseAddress.FileNumber == facts.CurrentFileNumber) {
            return false;
        }

        throw new InvalidDataException(
            $"Object {source.ObjectId} terminating Base {source.BaseAddress} is outside " +
            $"source files {facts.PreviousFileNumber}/{facts.CurrentFileNumber}.");
    }

    private static void ValidatePostLiveIdentity(
        NormalizedSaveFacts facts,
        IReadOnlyList<ReadAmplificationBaseBudgetPolicyObjectFact> objects) {
        if (objects.Count != facts.PostLiveStates.Count) {
            throw new InvalidDataException(
                "Policy projection does not cover the normalized post-live object set.");
        }

        for (int index = 0; index < objects.Count; index++) {
            ReadAmplificationBaseBudgetPolicyObjectFact projected = objects[index];
            if (index > 0 && objects[index - 1].ObjectId >= projected.ObjectId) {
                throw new InvalidDataException(
                    "Policy projection ObjectIds must be strictly increasing.");
            }

            if (!facts.PostLiveStates.TryGetValue(
                projected.ObjectId,
                out var postLiveState) ||
                postLiveState.BasePayloadBytes != projected.PostSaveBasePayloadBytes) {
                throw new InvalidDataException(
                    $"Policy projection for object {projected.ObjectId} does not match " +
                    "the normalized post-live state.");
            }
        }
    }
}
