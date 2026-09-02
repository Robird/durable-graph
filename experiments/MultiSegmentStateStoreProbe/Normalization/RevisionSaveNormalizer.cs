using Atelia.MultiSegmentStateStoreProbe.Model;
using Atelia.MultiSegmentStateStoreProbe.Reconstruction;
using Atelia.MultiSegmentStateStoreProbe.Storage;
using Atelia.MultiSegmentStateStoreProbe.Workloads;
using ModelState = Atelia.MultiSegmentStateStoreProbe.Model.LogicalObjectState;
using WorkloadState = Atelia.MultiSegmentStateStoreProbe.Workloads.LogicalObjectState;

namespace Atelia.MultiSegmentStateStoreProbe.Normalization;

internal static class RevisionSaveNormalizer {
    public static NormalizedSaveFacts Normalize(
        InMemorySegmentStore store,
        AbsoluteFrameAddress? expectedParent,
        IReadOnlySet<uint> usedObjectIds,
        SaveStep step) {
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(usedObjectIds);
        ArgumentNullException.ThrowIfNull(step);

        SortedDictionary<uint, SourceObjectFact> parentLive = expectedParent is { } parent
            ? InspectParent(CurrentStateMaterializer.Materialize(store, parent))
            : [];
        List<NormalizedInsertFact> inserts = [];
        List<NormalizedUpdateFact> updates = [];
        List<NormalizedRemoveFact> removes = [];
        HashSet<uint> changed = [];

        foreach (WorkloadChange change in step.Changes) {
            ArgumentOutOfRangeException.ThrowIfZero(change.ObjectId);
            if (!changed.Add(change.ObjectId)) {
                throw new InvalidDataException(
                    $"ObjectId {change.ObjectId} occurs more than once in one Save.");
            }

            switch (change) {
                case CreateObject create:
                    if (create.BasePayloadBytes < 0) {
                        throw new InvalidDataException(
                            $"Create ObjectId {create.ObjectId} has a negative Base size.");
                    }

                    if (parentLive.ContainsKey(create.ObjectId) ||
                        usedObjectIds.Contains(create.ObjectId)) {
                        throw new InvalidDataException(
                            $"ObjectId {create.ObjectId} is live or has already been used.");
                    }

                    inserts.Add(new(
                        create.ObjectId,
                        WorkloadStateMapper.ToModel(new WorkloadState(
                            create.BasePayloadBytes,
                            LogicalVersionOrdinal: 1))));
                    break;
                case UpdateObject update:
                    SourceObjectFact updateSource = GetLive(
                        parentLive,
                        update.ObjectId,
                        "update");
                    WorkloadState sourceWorkload = WorkloadStateMapper.ToWorkload(
                        updateSource.State);
                    if (update.ResultBasePayloadBytes < 0 ||
                        update.DeltaPayloadBytes <= 0) {
                        throw new InvalidDataException(
                            $"Update ObjectId {update.ObjectId} has invalid payload sizes.");
                    }

                    int minimumDeltaBytes = Math.Max(
                        0,
                        update.ResultBasePayloadBytes - sourceWorkload.BasePayloadBytes);
                    if (update.DeltaPayloadBytes < minimumDeltaBytes) {
                        throw new InvalidDataException(
                            $"Update ObjectId {update.ObjectId} Delta is too small for growth.");
                    }

                    if (sourceWorkload.LogicalVersionOrdinal == int.MaxValue) {
                        throw new InvalidDataException(
                            $"ObjectId {update.ObjectId} exhausted its logical ordinal.");
                    }

                    updates.Add(new(
                        updateSource,
                        WorkloadStateMapper.ToModel(new WorkloadState(
                            update.ResultBasePayloadBytes,
                            checked(sourceWorkload.LogicalVersionOrdinal + 1))),
                        update.DeltaPayloadBytes));
                    break;
                case RemoveObject remove:
                    removes.Add(new(GetLive(parentLive, remove.ObjectId, "remove")));
                    break;
                default:
                    throw new InvalidDataException(
                        $"Unsupported workload change '{change.GetType().FullName}'.");
            }
        }

        List<NormalizedNoChangeFact> noChanges = [];
        foreach ((uint objectId, SourceObjectFact source) in parentLive) {
            if (!changed.Contains(objectId)) {
                noChanges.Add(new(source));
            }
        }

        return new NormalizedSaveFacts(
            expectedParent,
            parentLive.Values,
            inserts,
            updates,
            removes,
            noChanges);
    }

    private static SortedDictionary<uint, SourceObjectFact> InspectParent(
        MaterializedCurrentState current) {
        SortedDictionary<uint, SourceObjectFact> result = [];
        foreach ((uint objectId, ModelState state) in current.States) {
            result.Add(objectId, new SourceObjectFact(
                objectId,
                state,
                current.ObjectVersionHeads[objectId],
                current.ObjectReconstructionPaths[objectId]));
        }

        return result;
    }

    private static SourceObjectFact GetLive(
        IReadOnlyDictionary<uint, SourceObjectFact> parentLive,
        uint objectId,
        string operation) => parentLive.TryGetValue(objectId, out SourceObjectFact? source)
        ? source
        : throw new InvalidDataException(
            $"Cannot {operation} ObjectId {objectId} because it is not live.");
}
