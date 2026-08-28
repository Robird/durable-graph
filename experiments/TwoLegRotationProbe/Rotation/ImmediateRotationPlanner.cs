using System.Collections.ObjectModel;
using Atelia.TwoLegRotationProbe.Encoding;
using Atelia.TwoLegRotationProbe.Model;
using Atelia.TwoLegRotationProbe.Simulation;

namespace Atelia.TwoLegRotationProbe.Rotation;

/// <summary>
/// Builds the smallest one-shot A/B to B/C rotation witness. The method is deliberately
/// pure: it reads the source store but never creates a file, appends a frame, or publishes.
/// </summary>
/// <remarks>
/// The supplied state map is the current in-memory authority for this probe. Until a persisted
/// OVD reader exists, readability of <paramref name="publishedRevisionAddress"/> does not prove
/// that the frame encoded the same map.
/// </remarks>
internal static class ImmediateRotationPlanner {
    public static ImmediateRotationPlan Create(
        RbfFileStore store,
        uint currentFileNumber,
        AbsoluteFrameAddress publishedRevisionAddress,
        IReadOnlyDictionary<uint, AbsoluteFrameAddress> stateMap) {
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(stateMap);

        (uint previousFileNumber, uint nextFileNumber, RbfFile currentFile) =
            ValidateFileScope(store, currentFileNumber);
        ValidatePublishedRevision(
            store,
            currentFileNumber,
            publishedRevisionAddress);

        ReadOnlyDictionary<uint, AbsoluteFrameAddress> sourceStateMap =
            FreezeStateMap(stateMap);
        PhysicalStateOracle.ValidateLineage(store, sourceStateMap);

        SortedDictionary<uint, ObjectReconstructionInspection> inspections =
            InspectSourceState(
                store,
                previousFileNumber,
                currentFileNumber,
                sourceStateMap);
        uint[] evacuationObjectIds = inspections
            .Where(pair => pair.Value.BaseAddress.FileNumber == previousFileNumber)
            .Select(static pair => pair.Key)
            .ToArray();
        uint[] relayObjectIds = evacuationObjectIds
            .Where(objectId =>
                inspections[objectId].HeadAddress.FileNumber == previousFileNumber)
            .ToArray();
        HashSet<uint> evacuationSet = evacuationObjectIds.ToHashSet();
        HashSet<uint> relaySet = relayObjectIds.ToHashSet();

        FileScope currentScope = new(currentFileNumber);
        PlannedRevisionV0? relayRevision = CreateRelayRevision(
            currentFile,
            previousFileNumber,
            currentFileNumber,
            publishedRevisionAddress,
            currentScope,
            relayObjectIds,
            inspections);

        FileScope nextScope = new(nextFileNumber);
        PlannedRevisionV0 evacuationRevision = CreateEvacuationRevision(
            currentFileNumber,
            nextFileNumber,
            publishedRevisionAddress,
            relayRevision,
            nextScope,
            sourceStateMap,
            inspections,
            evacuationSet,
            relaySet);

        IReadOnlyDictionary<uint, AbsoluteFrameAddress> projectedStateMap =
            ResolveProjectedStateMap(evacuationRevision);
        ValidateProjection(
            currentFileNumber,
            nextFileNumber,
            sourceStateMap,
            inspections,
            evacuationSet,
            evacuationRevision,
            projectedStateMap);

        return new ImmediateRotationPlan(
            previousFileNumber,
            currentFileNumber,
            nextFileNumber,
            publishedRevisionAddress,
            evacuationObjectIds,
            relayObjectIds,
            relayRevision,
            evacuationRevision,
            projectedStateMap);
    }

    private static (
        uint PreviousFileNumber,
        uint NextFileNumber,
        RbfFile CurrentFile) ValidateFileScope(
            RbfFileStore store,
            uint currentFileNumber) {
        if (currentFileNumber < 2) {
            throw new InvalidDataException(
                "Immediate rotation requires adjacent Previous and Current files.");
        }

        if (store.FileCount != currentFileNumber) {
            throw new InvalidDataException(
                $"Current file {currentFileNumber} must be the highest existing RBF file; " +
                $"the store currently has {store.FileCount} files.");
        }

        uint previousFileNumber = currentFileNumber - 1;
        uint nextFileNumber;
        try {
            nextFileNumber = checked(currentFileNumber + 1);
        } catch (OverflowException exception) {
            throw new InvalidDataException(
                "The next RBF file number is not representable.",
                exception);
        }

        _ = store.GetFile(previousFileNumber);
        return (previousFileNumber, nextFileNumber, store.GetFile(currentFileNumber));
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

    private static ReadOnlyDictionary<uint, AbsoluteFrameAddress> FreezeStateMap(
        IReadOnlyDictionary<uint, AbsoluteFrameAddress> stateMap) {
        Dictionary<uint, AbsoluteFrameAddress> copy = new(stateMap.Count);
        foreach ((uint objectId, AbsoluteFrameAddress address) in
            stateMap.OrderBy(static pair => pair.Key)) {
            copy.Add(objectId, address);
        }

        return new ReadOnlyDictionary<uint, AbsoluteFrameAddress>(copy);
    }

    private static SortedDictionary<uint, ObjectReconstructionInspection> InspectSourceState(
        RbfFileStore store,
        uint previousFileNumber,
        uint currentFileNumber,
        IReadOnlyDictionary<uint, AbsoluteFrameAddress> stateMap) {
        SortedDictionary<uint, ObjectReconstructionInspection> inspections = [];
        foreach ((uint objectId, AbsoluteFrameAddress headAddress) in stateMap) {
            if (headAddress.FileNumber != previousFileNumber &&
                headAddress.FileNumber != currentFileNumber) {
                throw new InvalidDataException(
                    $"Live object {objectId} head {headAddress} is outside source files " +
                    $"{previousFileNumber}/{currentFileNumber}.");
            }

            ObjectReconstructionInspection inspection =
                PhysicalStateOracle.InspectObjectReconstruction(
                    store,
                    objectId,
                    headAddress);
            if (inspection.ReconstructionFrameAddresses.Any(address =>
                address.FileNumber != previousFileNumber &&
                address.FileNumber != currentFileNumber)) {
                throw new InvalidDataException(
                    $"Live object {objectId} reconstruction escapes source files " +
                    $"{previousFileNumber}/{currentFileNumber}.");
            }

            inspections.Add(objectId, inspection);
        }

        return inspections;
    }

    private static PlannedRevisionV0? CreateRelayRevision(
        RbfFile currentFile,
        uint previousFileNumber,
        uint currentFileNumber,
        AbsoluteFrameAddress publishedRevisionAddress,
        FileScope currentScope,
        IReadOnlyList<uint> relayObjectIds,
        IReadOnlyDictionary<uint, ObjectReconstructionInspection> inspections) {
        if (relayObjectIds.Count == 0) {
            return null;
        }

        ProvisionalDomainRecordInput[] relayRecords = relayObjectIds
            .Select(objectId => {
                AbsoluteFrameAddress headAddress = inspections[objectId].HeadAddress;
                if (headAddress.FileNumber != previousFileNumber) {
                    throw new InvalidDataException(
                        $"Relay object {objectId} head is not in Previous file " +
                        $"{previousFileNumber}.");
                }

                return new ProvisionalDomainRecordInput(
                    objectId,
                    ProvisionalDomainRecordRole.Relay,
                    SyntheticPayloadBytes: 0,
                    currentScope.Relativize(headAddress));
            })
            .ToArray();
        ProvisionalRevisionV0Input input = new(
            relayRecords,
            new ProvisionalObjectVersionDictionaryInput(
                ProvisionalObjectVersionDictionaryKind.Delta,
                currentScope.Relativize(publishedRevisionAddress),
                entries: []));
        ProvisionalRevisionV0Estimate estimate =
            ProvisionalRevisionV0Estimator.Estimate(input, currentFile.TailOffsetBytes);
        AbsoluteFrameAddress address = new(
            currentFileNumber,
            estimate.RbfLayout.Ticket);
        return new PlannedRevisionV0(
            PlannedRevisionV0Role.Relay,
            currentFileNumber,
            address,
            input,
            estimate);
    }

    private static PlannedRevisionV0 CreateEvacuationRevision(
        uint currentFileNumber,
        uint nextFileNumber,
        AbsoluteFrameAddress publishedRevisionAddress,
        PlannedRevisionV0? relayRevision,
        FileScope nextScope,
        IReadOnlyDictionary<uint, AbsoluteFrameAddress> sourceStateMap,
        IReadOnlyDictionary<uint, ObjectReconstructionInspection> inspections,
        IReadOnlySet<uint> evacuationSet,
        IReadOnlySet<uint> relaySet) {
        ProvisionalDomainRecordInput[] evacuationRecords = evacuationSet
            .Order()
            .Select(objectId => {
                ObjectReconstructionInspection inspection = inspections[objectId];
                AbsoluteFrameAddress lineageParent = relaySet.Contains(objectId)
                    ? relayRevision?.Address ?? throw new InvalidDataException(
                        $"Relay object {objectId} has no planned relay revision.")
                    : inspection.HeadAddress;
                return new ProvisionalDomainRecordInput(
                    objectId,
                    ProvisionalDomainRecordRole.Base,
                    inspection.State.BasePayloadBytes,
                    nextScope.Relativize(lineageParent));
            })
            .ToArray();

        ProvisionalObjectVersionDictionaryEntry[] bindings = sourceStateMap
            .Select(pair => evacuationSet.Contains(pair.Key)
                ? ProvisionalObjectVersionDictionaryEntry.BindSelf(pair.Key)
                : ProvisionalObjectVersionDictionaryEntry.BindExternal(
                    pair.Key,
                    nextScope.Relativize(pair.Value)))
            .ToArray();
        AbsoluteFrameAddress ovdParentAddress =
            relayRevision?.Address ?? publishedRevisionAddress;
        ProvisionalRevisionV0Input input = new(
            evacuationRecords,
            new ProvisionalObjectVersionDictionaryInput(
                ProvisionalObjectVersionDictionaryKind.Base,
                nextScope.Relativize(ovdParentAddress),
                bindings));
        ProvisionalRevisionV0Estimate estimate = ProvisionalRevisionV0Estimator.Estimate(
            input,
            RbfV040Layout.InitialTailOffsetBytes);
        AbsoluteFrameAddress address = new(nextFileNumber, estimate.RbfLayout.Ticket);
        return new PlannedRevisionV0(
            PlannedRevisionV0Role.Evacuation,
            nextFileNumber,
            address,
            input,
            estimate);
    }

    private static IReadOnlyDictionary<uint, AbsoluteFrameAddress> ResolveProjectedStateMap(
        PlannedRevisionV0 evacuationRevision) {
        ProvisionalObjectVersionDictionaryInput ovd =
            evacuationRevision.GrammarInput.ObjectVersionDictionary;
        if (ovd.Kind != ProvisionalObjectVersionDictionaryKind.Base) {
            throw new InvalidDataException(
                "An evacuation revision must carry a full OVD Base.");
        }

        Dictionary<uint, AbsoluteFrameAddress> projected = new(ovd.Entries.Count);
        foreach (ProvisionalObjectVersionDictionaryEntry entry in ovd.Entries) {
            ulong token = entry.BindingKind switch {
                ProvisionalObjectVersionDictionaryBindingKind.Self =>
                    ProvisionalObjectVersionDictionaryBinding.EncodeSelf(),
                ProvisionalObjectVersionDictionaryBindingKind.External
                    when entry.ExternalFrameTicket is RelativeFrameTicket external =>
                    ProvisionalObjectVersionDictionaryBinding.EncodeExternal(external),
                _ => throw new InvalidDataException(
                    $"Evacuation OVD Base entry {entry.ObjectId} has invalid binding " +
                    $"{entry.BindingKind}.")
            };
            AbsoluteFrameAddress resolved =
                ProvisionalObjectVersionDictionaryBinding.Resolve(
                    token,
                    evacuationRevision.FileNumber,
                    evacuationRevision.Address.FrameTicket);
            projected.Add(entry.ObjectId, resolved);
        }

        return new ReadOnlyDictionary<uint, AbsoluteFrameAddress>(projected);
    }

    private static void ValidateProjection(
        uint currentFileNumber,
        uint nextFileNumber,
        IReadOnlyDictionary<uint, AbsoluteFrameAddress> sourceStateMap,
        IReadOnlyDictionary<uint, ObjectReconstructionInspection> inspections,
        IReadOnlySet<uint> evacuationSet,
        PlannedRevisionV0 evacuationRevision,
        IReadOnlyDictionary<uint, AbsoluteFrameAddress> projectedStateMap) {
        if (!sourceStateMap.Keys.SequenceEqual(projectedStateMap.Keys)) {
            throw new InvalidDataException(
                "The projected full OVD does not preserve the exact live ObjectId set.");
        }

        Dictionary<uint, ProvisionalDomainRecordInput> evacuationRecords =
            evacuationRevision.GrammarInput.DomainRecords.ToDictionary(
                static record => record.ObjectId);
        foreach ((uint objectId, AbsoluteFrameAddress address) in projectedStateMap) {
            if (evacuationSet.Contains(objectId)) {
                if (address != evacuationRevision.Address ||
                    !evacuationRecords.TryGetValue(
                        objectId,
                        out ProvisionalDomainRecordInput record) ||
                    record.Role != ProvisionalDomainRecordRole.Base ||
                    record.SyntheticPayloadBytes != inspections[objectId].State.BasePayloadBytes) {
                    throw new InvalidDataException(
                        $"Evacuated object {objectId} is not a self-contained Base in Next file.");
                }

                continue;
            }

            if (address.FileNumber != currentFileNumber ||
                address != sourceStateMap[objectId] ||
                inspections[objectId].ReconstructionFrameAddresses.Any(
                    candidate => candidate.FileNumber != currentFileNumber)) {
                throw new InvalidDataException(
                    $"Retained object {objectId} is not reconstructible solely from Current file.");
            }
        }

        if (projectedStateMap.Values.Any(address =>
            address.FileNumber != currentFileNumber &&
            address.FileNumber != nextFileNumber)) {
            throw new InvalidDataException(
                $"Projected reconstruction escapes files {currentFileNumber}/{nextFileNumber}.");
        }
    }
}
