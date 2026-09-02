using Atelia.MultiSegmentStateStoreProbe.Model;
using Atelia.MultiSegmentStateStoreProbe.Normalization;
using Atelia.MultiSegmentStateStoreProbe.Policies;
using Atelia.MultiSegmentStateStoreProbe.Storage;

namespace Atelia.MultiSegmentStateStoreProbe.Planning;

internal sealed record AdaptedReadAmplificationSelection(
    ReadAmplificationBaseBudgetPolicyInput PolicyInput,
    ReadAmplificationBaseBudgetPolicySelection PolicySelection,
    RevisionSaveSelection RevisionSelection);

/// <summary>
/// Projects exact normalized reconstruction facts into payload-only policy facts, then
/// adapts the pure result back to the planner contract. OVD mode remains caller-owned.
/// </summary>
internal static class ReadAmplificationPolicyAdapter {
    public static AdaptedReadAmplificationSelection Select(
        InMemorySegmentStore store,
        NormalizedSaveFacts facts,
        ReadAmplificationBaseBudgetPolicyParameters parameters,
        ObjectVersionDictionaryKind dictionaryKind) {
        ReadAmplificationBaseBudgetPolicyInput input = Project(store, facts);
        ReadAmplificationBaseBudgetPolicySelection policySelection =
            ReadAmplificationBaseBudgetPolicy.Select(input, parameters);
        HashSet<uint> rebases = policySelection.SameStateRebaseObjectIds.ToHashSet();
        RevisionSaveSelection revisionSelection = new(
            dictionaryKind,
            policySelection.UpdateDecisions.Select(static decision => new UpdateWriteSelection(
                decision.ObjectId,
                decision.Mode == ObjectVersionWriteMode.Base
                    ? UpdateWriteMode.Base
                    : UpdateWriteMode.Delta)),
            facts.NoChanges.Select(noChange => new NoChangeWriteSelection(
                noChange.ObjectId,
                rebases.Contains(noChange.ObjectId)
                    ? NoChangeWriteMode.SameStateRebase
                    : NoChangeWriteMode.Inherit)));
        return new AdaptedReadAmplificationSelection(
            input,
            policySelection,
            revisionSelection);
    }

    public static ReadAmplificationBaseBudgetPolicyInput Project(
        InMemorySegmentStore store,
        NormalizedSaveFacts facts) {
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(facts);
        List<ReadAmplificationBaseBudgetPolicyFact> result = [];
        result.AddRange(facts.Inserts.Select(static insert =>
            ReadAmplificationBaseBudgetPolicyFact.Insert(
                insert.ObjectId,
                insert.ResultState.BasePayloadBytes)));
        result.AddRange(facts.Updates.Select(update =>
            ReadAmplificationBaseBudgetPolicyFact.Update(
                update.ObjectId,
                update.ResultState.BasePayloadBytes,
                SumObjectPayloadBytes(store, update.Source),
                update.DeltaPayloadBytes)));
        result.AddRange(facts.Removes.Select(remove =>
            ReadAmplificationBaseBudgetPolicyFact.Remove(
                remove.ObjectId,
                SumObjectPayloadBytes(store, remove.Source))));
        result.AddRange(facts.NoChanges.Select(noChange =>
            ReadAmplificationBaseBudgetPolicyFact.NoChange(
                noChange.ObjectId,
                noChange.Source.State.BasePayloadBytes,
                SumObjectPayloadBytes(store, noChange.Source))));
        return new ReadAmplificationBaseBudgetPolicyInput(result);
    }

    private static long SumObjectPayloadBytes(
        InMemorySegmentStore store,
        SourceObjectFact source) {
        long total = 0;
        foreach (AbsoluteFrameAddress address in source.ReconstructionPath) {
            RevisionFrame frame = store.Read(address).RevisionFrame
                ?? throw new InvalidDataException(
                    $"ObjectId {source.ObjectId} reconstruction Frame {address} is not a Revision.");
            if (!frame.ObjectVersions.TryGetValue(
                source.ObjectId,
                out ObjectVersion? version)) {
                throw new InvalidDataException(
                    $"ObjectId {source.ObjectId} is absent from reconstruction Frame {address}.");
            }

            total = checked(total + version.PayloadBytes);
        }

        return total;
    }
}
