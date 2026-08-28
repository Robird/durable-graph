using System.Collections.ObjectModel;

namespace Atelia.TwoLegRotationProbe.Workloads;

internal static class WorkloadReplayer {
    public static IReadOnlyDictionary<uint, LogicalObjectState> Replay(WorkloadTrace trace) {
        ArgumentNullException.ThrowIfNull(trace);

        Dictionary<uint, LogicalObjectState> liveObjects = [];
        HashSet<uint> seenObjectIds = [];

        foreach (SaveStep step in trace.Steps) {
            ValidateStep(step, liveObjects, seenObjectIds);
            ApplyStep(step, liveObjects, seenObjectIds);
        }

        return new ReadOnlyDictionary<uint, LogicalObjectState>(liveObjects);
    }

    private static void ValidateStep(
        SaveStep step,
        IReadOnlyDictionary<uint, LogicalObjectState> liveObjects,
        IReadOnlySet<uint> seenObjectIds) {
        foreach (WorkloadChange change in step.Changes) {
            switch (change) {
                case CreateObject create:
                    ValidateCreate(create, seenObjectIds);
                    break;
                case UpdateObject update:
                    ValidateUpdate(update, liveObjects);
                    break;
                case RemoveObject remove:
                    ValidateRemove(remove, liveObjects);
                    break;
                default:
                    throw new InvalidDataException(
                        $"Unsupported workload change type '{change.GetType().FullName}'.");
            }
        }
    }

    private static void ValidateCreate(
        CreateObject create,
        IReadOnlySet<uint> seenObjectIds) {
        if (create.BasePayloadBytes < 0) {
            throw new InvalidDataException(
                $"Object {create.ObjectId} has a negative Base payload size.");
        }

        if (seenObjectIds.Contains(create.ObjectId)) {
            throw new InvalidDataException(
                $"Object id {create.ObjectId} has already been used and cannot be reused.");
        }
    }

    private static void ValidateUpdate(
        UpdateObject update,
        IReadOnlyDictionary<uint, LogicalObjectState> liveObjects) {
        if (!liveObjects.TryGetValue(update.ObjectId, out LogicalObjectState previous)) {
            throw new InvalidDataException(
                $"Cannot update object {update.ObjectId} because it is not live.");
        }

        if (update.ResultBasePayloadBytes < 0) {
            throw new InvalidDataException(
                $"Object {update.ObjectId} has a negative resulting Base payload size.");
        }

        if (update.DeltaPayloadBytes <= 0) {
            throw new InvalidDataException(
                $"Object {update.ObjectId} must have a positive Delta payload size.");
        }

        int minimumDeltaBytes = Math.Max(
            0,
            update.ResultBasePayloadBytes - previous.BasePayloadBytes);
        if (update.DeltaPayloadBytes < minimumDeltaBytes) {
            throw new InvalidDataException(
                $"Object {update.ObjectId} grows from {previous.BasePayloadBytes} to " +
                $"{update.ResultBasePayloadBytes} bytes, so its Delta must be at least " +
                $"{minimumDeltaBytes} bytes.");
        }

        if (previous.VersionOrdinal == int.MaxValue) {
            throw new InvalidDataException(
                $"Object {update.ObjectId} has exhausted the logical version ordinal range.");
        }
    }

    private static void ValidateRemove(
        RemoveObject remove,
        IReadOnlyDictionary<uint, LogicalObjectState> liveObjects) {
        if (!liveObjects.ContainsKey(remove.ObjectId)) {
            throw new InvalidDataException(
                $"Cannot remove object {remove.ObjectId} because it is not live.");
        }
    }

    private static void ApplyStep(
        SaveStep step,
        IDictionary<uint, LogicalObjectState> liveObjects,
        ISet<uint> seenObjectIds) {
        foreach (WorkloadChange change in step.Changes) {
            switch (change) {
                case CreateObject create:
                    seenObjectIds.Add(create.ObjectId);
                    liveObjects.Add(
                        create.ObjectId,
                        new LogicalObjectState(create.BasePayloadBytes, VersionOrdinal: 1));
                    break;
                case UpdateObject update:
                    LogicalObjectState previous = liveObjects[update.ObjectId];
                    liveObjects[update.ObjectId] = new LogicalObjectState(
                        update.ResultBasePayloadBytes,
                        checked(previous.VersionOrdinal + 1));
                    break;
                case RemoveObject remove:
                    liveObjects.Remove(remove.ObjectId);
                    break;
            }
        }
    }
}
