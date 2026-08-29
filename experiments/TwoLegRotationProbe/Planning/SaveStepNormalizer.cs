using Atelia.TwoLegRotationProbe.Model;
using Atelia.TwoLegRotationProbe.Simulation;
using Atelia.TwoLegRotationProbe.Workloads;

namespace Atelia.TwoLegRotationProbe.Planning;

internal static class SaveStepNormalizer {
    public static NormalizedSaveFacts Normalize(
        RbfFileStore store,
        uint currentFileNumber,
        AbsoluteFrameAddress publishedRevisionAddress,
        SaveStep step) {
        ArgumentNullException.ThrowIfNull(step);

        return NormalizeCore(
            store,
            currentFileNumber,
            publishedRevisionAddress,
            step.Changes);
    }

    public static NormalizedSaveFacts NormalizeMaintenanceOnly(
        RbfFileStore store,
        uint currentFileNumber,
        AbsoluteFrameAddress publishedRevisionAddress) => NormalizeCore(
            store,
            currentFileNumber,
            publishedRevisionAddress,
            []);

    private static NormalizedSaveFacts NormalizeCore(
        RbfFileStore store,
        uint currentFileNumber,
        AbsoluteFrameAddress publishedRevisionAddress,
        IReadOnlyList<WorkloadChange> changes) {
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(changes);

        uint previousFileNumber = ValidateFileScope(store, currentFileNumber);
        ValidatePublishedRevision(
            store,
            currentFileNumber,
            publishedRevisionAddress);

        ObjectVersionDictionaryMaterializationInspection materialization =
            ObjectVersionDictionaryReader.MaterializeLive(
                store,
                publishedRevisionAddress);
        ValidateDictionaryClosure(
            materialization,
            previousFileNumber,
            currentFileNumber);
        IReadOnlyDictionary<uint, AbsoluteFrameAddress> liveBindings =
            materialization.Bindings;
        SortedDictionary<uint, SourceObjectFact> parentLive = InspectParentLive(
            store,
            previousFileNumber,
            currentFileNumber,
            liveBindings);

        List<NormalizedSaveFact> explicitFacts = new(changes.Count);
        HashSet<uint> changedObjectIds = [];
        foreach (WorkloadChange change in changes) {
            _ = changedObjectIds.Add(change.ObjectId);
            explicitFacts.Add(change switch {
                CreateObject create => NormalizeInsert(parentLive, create),
                UpdateObject update => NormalizeUpdate(parentLive, update),
                RemoveObject remove => NormalizeRemove(parentLive, remove),
                _ => throw new InvalidDataException(
                    $"Unsupported workload change type '{change.GetType().FullName}'."),
            });
        }

        List<NormalizedSaveFact> allFacts = new(
            explicitFacts.Count + parentLive.Count -
            changedObjectIds.Count(parentLive.ContainsKey));
        allFacts.AddRange(explicitFacts);
        foreach ((uint objectId, SourceObjectFact source) in parentLive) {
            if (!changedObjectIds.Contains(objectId)) {
                allFacts.Add(new NormalizedNoChangeFact(source));
            }
        }

        return new NormalizedSaveFacts(
            previousFileNumber,
            currentFileNumber,
            publishedRevisionAddress,
            parentLive.Values,
            allFacts);
    }

    private static uint ValidateFileScope(
        RbfFileStore store,
        uint currentFileNumber) {
        if (currentFileNumber < 2) {
            throw new InvalidDataException(
                "Save normalization requires adjacent Previous and Current files.");
        }

        if ((uint)store.FileCount != currentFileNumber) {
            throw new InvalidDataException(
                $"Current file {currentFileNumber} must be the highest existing RBF file; " +
                $"the store currently has {store.FileCount} files.");
        }

        uint previousFileNumber = currentFileNumber - 1;
        _ = store.GetFile(previousFileNumber);
        _ = store.GetFile(currentFileNumber);
        return previousFileNumber;
    }

    private static void ValidatePublishedRevision(
        RbfFileStore store,
        uint currentFileNumber,
        AbsoluteFrameAddress publishedRevisionAddress) {
        if (publishedRevisionAddress.FileNumber != currentFileNumber) {
            throw new InvalidDataException(
                $"Published revision {publishedRevisionAddress} must be in Current file " +
                $"{currentFileNumber}.");
        }

        try {
            _ = store.ReadFrame(publishedRevisionAddress);
        } catch (Exception exception) when (
            exception is ArgumentOutOfRangeException or KeyNotFoundException) {
            throw new InvalidDataException(
                $"Published revision {publishedRevisionAddress} is not readable.",
                exception);
        }
    }

    private static SortedDictionary<uint, SourceObjectFact> InspectParentLive(
        RbfFileStore store,
        uint previousFileNumber,
        uint currentFileNumber,
        IReadOnlyDictionary<uint, AbsoluteFrameAddress> liveBindings) {
        SortedDictionary<uint, SourceObjectFact> parentLive = [];
        foreach ((uint objectId, AbsoluteFrameAddress headAddress) in liveBindings) {
            EnsureAddressInScope(
                objectId,
                headAddress,
                previousFileNumber,
                currentFileNumber,
                "head");

            ObjectReconstructionInspection inspection =
                PhysicalStateOracle.InspectObjectReconstruction(
                    store,
                    objectId,
                    headAddress);
            foreach (AbsoluteFrameAddress reconstructionAddress in
                inspection.ReconstructionFrameAddresses) {
                EnsureAddressInScope(
                    objectId,
                    reconstructionAddress,
                    previousFileNumber,
                    currentFileNumber,
                    "reconstruction");
            }

            Frame headFrame = store.ReadFrame(headAddress);
            if (!headFrame.ObjectVersions.TryGetValue(
                objectId,
                out ObjectVersion? headVersion)) {
                throw new InvalidDataException(
                    $"Live object {objectId} head {headAddress} has no matching object record.");
            }

            parentLive.Add(
                objectId,
                new SourceObjectFact(
                    objectId,
                    inspection.State,
                    headAddress,
                    inspection.BaseAddress,
                    headVersion.ReconstructionObjectPayloadBytes,
                    inspection.ReconstructionFrameAddresses));
        }

        return parentLive;
    }

    private static void ValidateDictionaryClosure(
        ObjectVersionDictionaryMaterializationInspection materialization,
        uint previousFileNumber,
        uint currentFileNumber) {
        foreach (AbsoluteFrameAddress revisionAddress in
            materialization.DictionaryRevisionAddresses) {
            if (revisionAddress.FileNumber != previousFileNumber &&
                revisionAddress.FileNumber != currentFileNumber) {
                throw new InvalidDataException(
                    $"Published Revision OVD chain address {revisionAddress} is outside " +
                    $"source files {previousFileNumber}/{currentFileNumber}.");
            }
        }
    }

    private static void EnsureAddressInScope(
        uint objectId,
        AbsoluteFrameAddress address,
        uint previousFileNumber,
        uint currentFileNumber,
        string role) {
        if (address.FileNumber != previousFileNumber &&
            address.FileNumber != currentFileNumber) {
            throw new InvalidDataException(
                $"Live object {objectId} {role} address {address} is outside source files " +
                $"{previousFileNumber}/{currentFileNumber}.");
        }
    }

    private static NormalizedInsertFact NormalizeInsert(
        IReadOnlyDictionary<uint, SourceObjectFact> parentLive,
        CreateObject create) {
        if (parentLive.ContainsKey(create.ObjectId)) {
            throw new InvalidDataException(
                $"Cannot insert object {create.ObjectId} because it is already live.");
        }

        if (create.BasePayloadBytes < 0) {
            throw new InvalidDataException(
                $"Object {create.ObjectId} has a negative Base payload size.");
        }

        return new NormalizedInsertFact(
            create.ObjectId,
            new LogicalObjectState(
                create.BasePayloadBytes,
                LogicalVersionOrdinal: 1));
    }

    private static NormalizedUpdateFact NormalizeUpdate(
        IReadOnlyDictionary<uint, SourceObjectFact> parentLive,
        UpdateObject update) {
        if (!parentLive.TryGetValue(update.ObjectId, out SourceObjectFact? source)) {
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
            update.ResultBasePayloadBytes - source.State.BasePayloadBytes);
        if (update.DeltaPayloadBytes < minimumDeltaBytes) {
            throw new InvalidDataException(
                $"Object {update.ObjectId} grows from {source.State.BasePayloadBytes} to " +
                $"{update.ResultBasePayloadBytes} bytes, so its Delta must be at least " +
                $"{minimumDeltaBytes} bytes.");
        }

        if (source.State.LogicalVersionOrdinal == int.MaxValue) {
            throw new InvalidDataException(
                $"Object {update.ObjectId} has exhausted the logical version ordinal range.");
        }

        return new NormalizedUpdateFact(
            source,
            new LogicalObjectState(
                update.ResultBasePayloadBytes,
                checked(source.State.LogicalVersionOrdinal + 1)),
            update.DeltaPayloadBytes);
    }

    private static NormalizedRemoveFact NormalizeRemove(
        IReadOnlyDictionary<uint, SourceObjectFact> parentLive,
        RemoveObject remove) {
        if (!parentLive.TryGetValue(remove.ObjectId, out SourceObjectFact? source)) {
            throw new InvalidDataException(
                $"Cannot remove object {remove.ObjectId} because it is not live.");
        }

        return new NormalizedRemoveFact(source);
    }
}
