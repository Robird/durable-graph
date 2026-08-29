using Atelia.TwoLegRotationProbe.Model;
using Atelia.TwoLegRotationProbe.Simulation;

namespace Atelia.TwoLegRotationProbe.Rotation;

/// <summary>
/// Builds one pure, nonempty B-local Base-migration candidate from a published B Revision.
/// </summary>
internal static class PreparatoryBaseMigrationPlanner {
    public static PreparatoryBaseMigrationPlan Create(
        RbfFileStore store,
        uint currentFileNumber,
        AbsoluteFrameAddress publishedRevisionAddress,
        IEnumerable<uint> objectIds) {
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(objectIds);

        SortedSet<uint> selectedObjectIds = FreezeSelection(objectIds);
        (uint previousFileNumber, RbfFile currentFile) = ValidateFileScope(
            store,
            currentFileNumber);
        ValidatePublishedRevision(
            store,
            currentFileNumber,
            publishedRevisionAddress);

        IReadOnlyDictionary<uint, AbsoluteFrameAddress> sourceStateMap =
            ObjectVersionDictionaryReader.MaterializeLive(
                store,
                publishedRevisionAddress).Bindings;
        ValidateSelectedObjectsAreLive(sourceStateMap, selectedObjectIds);
        SortedDictionary<uint, ObjectReconstructionInspection> sourceInspections =
            InspectSourceState(
                store,
                previousFileNumber,
                currentFileNumber,
                sourceStateMap);
        ValidateSelectedObjectsArePreviousBased(
            sourceInspections,
            previousFileNumber,
            currentFileNumber,
            selectedObjectIds);

        FileScope currentScope = new(currentFileNumber);
        ObjectVersionDictionaryBuilder dictionary = new() {
            Kind = ObjectVersionDictionaryKind.Delta,
            ParentRevisionFrameTicket = currentScope.Relativize(
                publishedRevisionAddress),
        };
        FrameBuilder frame = new() { ObjectVersionDictionary = dictionary };
        foreach (uint objectId in selectedObjectIds) {
            ObjectReconstructionInspection inspection = sourceInspections[objectId];
            ObjectVersionBuilder version = frame.Add(objectId);
            version.Kind = ObjectVersionKind.Base;
            version.PayloadBytes = inspection.State.BasePayloadBytes;
            version.ReconstructionObjectPayloadBytes = inspection.State.BasePayloadBytes;
            version.ResultBasePayloadBytes = inspection.State.BasePayloadBytes;
            version.LogicalVersionOrdinal = inspection.State.LogicalVersionOrdinal;
            dictionary.BindSelf(objectId);
        }

        PlannedRevisionV0 migrationRevision = new(
            currentFileNumber,
            frame.Build(),
            currentFile.TailOffsetBytes);
        return new PreparatoryBaseMigrationPlan(
            currentFileNumber,
            publishedRevisionAddress,
            migrationRevision);
    }

    private static SortedSet<uint> FreezeSelection(IEnumerable<uint> objectIds) {
        SortedSet<uint> selectedObjectIds = [];
        foreach (uint objectId in objectIds) {
            if (!selectedObjectIds.Add(objectId)) {
                throw new ArgumentException(
                    $"ObjectId {objectId} occurs more than once in the migration batch.",
                    nameof(objectIds));
            }
        }

        if (selectedObjectIds.Count == 0) {
            throw new ArgumentException(
                "A preparatory Base-migration batch cannot be empty.",
                nameof(objectIds));
        }

        return selectedObjectIds;
    }

    private static (uint PreviousFileNumber, RbfFile CurrentFile) ValidateFileScope(
        RbfFileStore store,
        uint currentFileNumber) {
        if (currentFileNumber < 2) {
            throw new InvalidDataException(
                "Preparatory Base migration requires adjacent Previous and Current files.");
        }

        if ((uint)store.FileCount != currentFileNumber) {
            throw new InvalidDataException(
                $"Current file {currentFileNumber} must be the highest existing RBF file; " +
                $"the store currently has {store.FileCount} files.");
        }

        uint previousFileNumber = currentFileNumber - 1;
        _ = store.GetFile(previousFileNumber);
        return (previousFileNumber, store.GetFile(currentFileNumber));
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

    private static void ValidateSelectedObjectsAreLive(
        IReadOnlyDictionary<uint, AbsoluteFrameAddress> sourceStateMap,
        IEnumerable<uint> selectedObjectIds) {
        foreach (uint objectId in selectedObjectIds) {
            if (!sourceStateMap.ContainsKey(objectId)) {
                throw new ArgumentException(
                    $"Selected ObjectId {objectId} is not live in the published Revision.",
                    nameof(selectedObjectIds));
            }
        }
    }

    private static SortedDictionary<uint, ObjectReconstructionInspection> InspectSourceState(
            RbfFileStore store,
            uint previousFileNumber,
            uint currentFileNumber,
            IReadOnlyDictionary<uint, AbsoluteFrameAddress> sourceStateMap) {
        SortedDictionary<uint, ObjectReconstructionInspection> inspections = [];
        foreach ((uint objectId, AbsoluteFrameAddress headAddress) in sourceStateMap) {
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

    private static void ValidateSelectedObjectsArePreviousBased(
        IReadOnlyDictionary<uint, ObjectReconstructionInspection> sourceInspections,
        uint previousFileNumber,
        uint currentFileNumber,
        IEnumerable<uint> selectedObjectIds) {
        foreach (uint objectId in selectedObjectIds) {
            if (sourceInspections[objectId].BaseAddress.FileNumber != previousFileNumber) {
                throw new ArgumentException(
                    $"Selected ObjectId {objectId} is already based in Current file " +
                    $"{currentFileNumber}.",
                    nameof(selectedObjectIds));
            }
        }
    }
}
