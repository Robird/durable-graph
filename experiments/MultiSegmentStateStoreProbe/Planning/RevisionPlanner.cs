using Atelia.MultiSegmentStateStoreProbe.Model;
using Atelia.MultiSegmentStateStoreProbe.Normalization;

namespace Atelia.MultiSegmentStateStoreProbe.Planning;

internal static class RevisionPlanner {
    public static RevisionPlan Create(
        NormalizedSaveFacts facts,
        RevisionSaveSelection selection) {
        ArgumentNullException.ThrowIfNull(facts);
        ArgumentNullException.ThrowIfNull(selection);
        ValidateSelections(facts, selection);
        if (facts.ExpectedParent is null &&
            selection.DictionaryKind != ObjectVersionDictionaryKind.Base) {
            throw new InvalidDataException("The genesis Revision requires an OVD Base.");
        }

        List<OriginFreeObjectVersion> versions = [];
        HashSet<uint> writtenObjectIds = [];
        foreach (NormalizedInsertFact insert in facts.Inserts) {
            versions.Add(OriginFreeObjectVersion.Base(insert.ObjectId, insert.ResultState));
            _ = writtenObjectIds.Add(insert.ObjectId);
        }

        foreach (NormalizedUpdateFact update in facts.Updates) {
            UpdateWriteMode mode = selection.Updates[update.ObjectId];
            versions.Add(mode switch {
                UpdateWriteMode.Base => OriginFreeObjectVersion.Base(
                    update.ObjectId,
                    update.ResultState),
                UpdateWriteMode.Delta => OriginFreeObjectVersion.Delta(
                    update.ObjectId,
                    update.DeltaPayloadBytes,
                    update.Source.State,
                    update.ResultState,
                    update.Source.HeadAddress),
                _ => throw new InvalidDataException(
                    $"Update ObjectId {update.ObjectId} has an invalid write mode."),
            });
            _ = writtenObjectIds.Add(update.ObjectId);
        }

        foreach (NormalizedNoChangeFact noChange in facts.NoChanges) {
            if (selection.NoChanges[noChange.ObjectId] ==
                NoChangeWriteMode.SameStateRebase) {
                versions.Add(OriginFreeObjectVersion.Base(
                    noChange.ObjectId,
                    noChange.Source.State));
                _ = writtenObjectIds.Add(noChange.ObjectId);
            }
        }

        List<OriginFreeObjectVersionDictionaryEntry> entries =
            selection.DictionaryKind == ObjectVersionDictionaryKind.Base
                ? BuildBaseEntries(facts, writtenObjectIds)
                : BuildDeltaEntries(facts, writtenObjectIds);
        OriginFreeRevisionFramePlan framePlan = new(
            facts.ExpectedParent,
            selection.DictionaryKind,
            versions,
            entries);
        return new RevisionPlan(facts, selection, framePlan);
    }

    private static List<OriginFreeObjectVersionDictionaryEntry> BuildBaseEntries(
        NormalizedSaveFacts facts,
        IReadOnlySet<uint> writtenObjectIds) {
        List<OriginFreeObjectVersionDictionaryEntry> entries = [];
        foreach (uint objectId in facts.PostLiveStates.Keys) {
            if (writtenObjectIds.Contains(objectId)) {
                entries.Add(OriginFreeObjectVersionDictionaryEntry.BindSelf(objectId));
                continue;
            }

            SourceObjectFact source = facts.ParentLive[objectId];
            entries.Add(OriginFreeObjectVersionDictionaryEntry.External(
                objectId,
                source.HeadAddress));
        }

        return entries;
    }

    private static List<OriginFreeObjectVersionDictionaryEntry> BuildDeltaEntries(
        NormalizedSaveFacts facts,
        IReadOnlySet<uint> writtenObjectIds) {
        List<OriginFreeObjectVersionDictionaryEntry> entries = [];
        foreach (uint objectId in writtenObjectIds.Order()) {
            entries.Add(OriginFreeObjectVersionDictionaryEntry.BindSelf(objectId));
        }

        foreach (NormalizedRemoveFact remove in facts.Removes) {
            entries.Add(OriginFreeObjectVersionDictionaryEntry.Remove(remove.ObjectId));
        }

        return entries;
    }

    private static void ValidateSelections(
        NormalizedSaveFacts facts,
        RevisionSaveSelection selection) {
        uint[] expectedUpdates = facts.Updates.Select(static fact => fact.ObjectId).ToArray();
        uint[] expectedNoChanges = facts.NoChanges.Select(static fact => fact.ObjectId).ToArray();
        if (!expectedUpdates.SequenceEqual(selection.Updates.Keys)) {
            throw new InvalidDataException(
                "Update selections must cover the exact normalized Update set.");
        }

        if (!expectedNoChanges.SequenceEqual(selection.NoChanges.Keys)) {
            throw new InvalidDataException(
                "NoChange selections must cover the exact normalized NoChange set.");
        }
    }
}
