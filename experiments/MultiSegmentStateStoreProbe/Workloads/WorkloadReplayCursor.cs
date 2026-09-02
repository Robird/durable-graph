using System.Collections.ObjectModel;

namespace Atelia.MultiSegmentStateStoreProbe.Workloads;

internal sealed class WorkloadReplayCursor {
    private readonly Dictionary<uint, LogicalObjectState> _liveObjects = [];
    private readonly HashSet<uint> _usedObjectIds = [];

    public IReadOnlyDictionary<uint, LogicalObjectState> Apply(SaveStep step) {
        ArgumentNullException.ThrowIfNull(step);
        Validate(step);

        foreach (WorkloadChange change in step.Changes) {
            switch (change) {
                case CreateObject create:
                    _usedObjectIds.Add(create.ObjectId);
                    _liveObjects.Add(
                        create.ObjectId,
                        new LogicalObjectState(create.BasePayloadBytes, 1));
                    break;
                case UpdateObject update:
                    LogicalObjectState previous = _liveObjects[update.ObjectId];
                    _liveObjects[update.ObjectId] = new LogicalObjectState(
                        update.ResultBasePayloadBytes,
                        checked(previous.LogicalVersionOrdinal + 1));
                    break;
                case RemoveObject remove:
                    _liveObjects.Remove(remove.ObjectId);
                    break;
            }
        }

        return Snapshot();
    }

    public IReadOnlyDictionary<uint, LogicalObjectState> Snapshot() =>
        new ReadOnlyDictionary<uint, LogicalObjectState>(
            new SortedDictionary<uint, LogicalObjectState>(_liveObjects));

    private void Validate(SaveStep step) {
        foreach (WorkloadChange change in step.Changes) {
            switch (change) {
                case CreateObject create:
                    if (create.BasePayloadBytes < 0) {
                        throw new InvalidDataException(
                            $"Object {create.ObjectId} has a negative Base payload size.");
                    }

                    if (_usedObjectIds.Contains(create.ObjectId)) {
                        throw new InvalidDataException(
                            $"Object id {create.ObjectId} has already been used and cannot be reused.");
                    }

                    break;
                case UpdateObject update:
                    if (!_liveObjects.TryGetValue(update.ObjectId, out LogicalObjectState previous)) {
                        throw new InvalidDataException(
                            $"Cannot update object {update.ObjectId} because it is not live.");
                    }

                    if (update.ResultBasePayloadBytes < 0 || update.DeltaPayloadBytes <= 0) {
                        throw new InvalidDataException(
                            $"Object {update.ObjectId} has invalid payload sizes.");
                    }

                    int minimumDeltaBytes = Math.Max(
                        0,
                        update.ResultBasePayloadBytes - previous.BasePayloadBytes);
                    if (update.DeltaPayloadBytes < minimumDeltaBytes) {
                        throw new InvalidDataException(
                            $"Object {update.ObjectId} grows by {minimumDeltaBytes} bytes, " +
                            $"but its Delta has only {update.DeltaPayloadBytes} bytes.");
                    }

                    if (previous.LogicalVersionOrdinal == int.MaxValue) {
                        throw new InvalidDataException(
                            $"Object {update.ObjectId} exhausted its logical version ordinal.");
                    }

                    break;
                case RemoveObject remove when !_liveObjects.ContainsKey(remove.ObjectId):
                    throw new InvalidDataException(
                        $"Cannot remove object {remove.ObjectId} because it is not live.");
                case RemoveObject:
                    break;
                default:
                    throw new InvalidDataException(
                        $"Unsupported workload change '{change.GetType().FullName}'.");
            }
        }
    }
}
