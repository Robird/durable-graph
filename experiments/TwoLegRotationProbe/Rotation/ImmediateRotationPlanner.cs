using Atelia.TwoLegRotationProbe.Model;
using Atelia.TwoLegRotationProbe.Simulation;

namespace Atelia.TwoLegRotationProbe.Rotation;

/// <summary>
/// Builds the smallest one-shot A/B to B/C rotation witness. The method is deliberately
/// pure: it reads the source store but never creates a file, appends a frame, or publishes.
/// </summary>
internal static class ImmediateRotationPlanner {
    public static ImmediateRotationPlan Create(
        RbfFileStore store,
        uint currentFileNumber,
        AbsoluteFrameAddress publishedRevisionAddress) {
        ArgumentNullException.ThrowIfNull(store);

        (uint previousFileNumber, uint nextFileNumber) =
            ValidateFileScope(store, currentFileNumber);
        ValidatePublishedRevision(
            store,
            currentFileNumber,
            publishedRevisionAddress);

        IReadOnlyDictionary<uint, AbsoluteFrameAddress> sourceStateMap =
            ObjectVersionDictionaryReader.MaterializeLive(
                store,
                publishedRevisionAddress).Bindings;

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
        HashSet<uint> evacuationSet = evacuationObjectIds.ToHashSet();

        FileScope nextScope = new(nextFileNumber);
        PlannedRevisionV0 evacuationRevision = CreateEvacuationRevision(
            nextFileNumber,
            publishedRevisionAddress,
            nextScope,
            sourceStateMap,
            inspections,
            evacuationSet);

        return new ImmediateRotationPlan(
            previousFileNumber,
            currentFileNumber,
            nextFileNumber,
            publishedRevisionAddress,
            evacuationRevision);
    }

    private static (
        uint PreviousFileNumber,
        uint NextFileNumber) ValidateFileScope(
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
        _ = store.GetFile(currentFileNumber);
        return (previousFileNumber, nextFileNumber);
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

    private static PlannedRevisionV0 CreateEvacuationRevision(
        uint nextFileNumber,
        AbsoluteFrameAddress publishedRevisionAddress,
        FileScope nextScope,
        IReadOnlyDictionary<uint, AbsoluteFrameAddress> sourceStateMap,
        IReadOnlyDictionary<uint, ObjectReconstructionInspection> inspections,
        IReadOnlySet<uint> evacuationSet) {
        RelativeFrameTicket publishedRevisionLocator =
            nextScope.Relativize(publishedRevisionAddress);
        ObjectVersionDictionaryBuilder dictionary = new() {
            Kind = ObjectVersionDictionaryKind.Base,
            ParentRevisionFrameTicket = publishedRevisionLocator,
        };
        FrameBuilder frame = new() { ObjectVersionDictionary = dictionary };

        foreach ((uint objectId, AbsoluteFrameAddress sourceAddress) in
            sourceStateMap.OrderBy(static pair => pair.Key)) {
            if (!evacuationSet.Contains(objectId)) {
                dictionary.BindExternal(
                    objectId,
                    nextScope.Relativize(sourceAddress));
                continue;
            }

            ObjectReconstructionInspection inspection = inspections[objectId];
            ObjectVersionBuilder version = frame.Add(objectId);
            version.Kind = ObjectVersionKind.Base;
            version.PayloadBytes = inspection.State.BasePayloadBytes;
            version.ReconstructionObjectPayloadBytes = inspection.State.BasePayloadBytes;
            version.ResultBasePayloadBytes = inspection.State.BasePayloadBytes;
            version.LogicalVersionOrdinal = inspection.State.LogicalVersionOrdinal;
            dictionary.BindSelf(objectId);
        }

        return new PlannedRevisionV0(
            nextFileNumber,
            frame.Build(),
            RbfV040Layout.InitialTailOffsetBytes);
    }
}
