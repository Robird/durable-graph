using System.Collections.ObjectModel;

namespace Atelia.TwoLegRotationProbe.Workloads;

internal sealed class WorkloadReplayCursor {
    private readonly Dictionary<uint, LogicalObjectState> _liveObjects = [];
    private readonly HashSet<uint> _seenObjectIds = [];

    public LogicalObjectState GetLiveObjectState(uint objectId) {
        if (!_liveObjects.TryGetValue(objectId, out LogicalObjectState state)) {
            throw new InvalidDataException($"Object {objectId} is not live.");
        }

        return state;
    }

    public IReadOnlyDictionary<uint, LogicalObjectState> Apply(SaveStep step) {
        ArgumentNullException.ThrowIfNull(step);

        ValidateStep(step);
        ApplyStep(step);
        return Snapshot();
    }

    public IReadOnlyDictionary<uint, LogicalObjectState> Snapshot() =>
        new ReadOnlyDictionary<uint, LogicalObjectState>(
            new Dictionary<uint, LogicalObjectState>(_liveObjects));

    private void ValidateStep(SaveStep step) {
        foreach (WorkloadChange change in step.Changes) {
            switch (change) {
                case CreateObject create:
                    ValidateCreate(create);
                    break;
                case UpdateObject update:
                    ValidateUpdate(update);
                    break;
                case RemoveObject remove:
                    ValidateRemove(remove);
                    break;
                default:
                    throw new InvalidDataException(
                        $"Unsupported workload change type '{change.GetType().FullName}'.");
            }
        }
    }

    private void ValidateCreate(CreateObject create) {
        if (create.BasePayloadBytes < 0) {
            throw new InvalidDataException(
                $"Object {create.ObjectId} has a negative Base payload size.");
        }

        if (_seenObjectIds.Contains(create.ObjectId)) {
            throw new InvalidDataException(
                $"Object id {create.ObjectId} has already been used and cannot be reused.");
        }
    }

    private void ValidateUpdate(UpdateObject update) {
        if (!_liveObjects.TryGetValue(update.ObjectId, out LogicalObjectState previous)) {
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

    private void ValidateRemove(RemoveObject remove) {
        if (!_liveObjects.ContainsKey(remove.ObjectId)) {
            throw new InvalidDataException(
                $"Cannot remove object {remove.ObjectId} because it is not live.");
        }
    }

    private void ApplyStep(SaveStep step) {
        foreach (WorkloadChange change in step.Changes) {
            switch (change) {
                case CreateObject create:
                    _seenObjectIds.Add(create.ObjectId);
                    _liveObjects.Add(
                        create.ObjectId,
                        new LogicalObjectState(create.BasePayloadBytes, VersionOrdinal: 1));
                    break;
                case UpdateObject update:
                    LogicalObjectState previous = _liveObjects[update.ObjectId];
                    _liveObjects[update.ObjectId] = new LogicalObjectState(
                        update.ResultBasePayloadBytes,
                        checked(previous.VersionOrdinal + 1));
                    break;
                case RemoveObject remove:
                    _liveObjects.Remove(remove.ObjectId);
                    break;
            }
        }
    }
}
