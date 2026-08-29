using Atelia.TwoLegRotationProbe.Model;
using Atelia.TwoLegRotationProbe.Simulation;

namespace Atelia.TwoLegRotationProbe.Rotation;

/// <summary>
/// Revalidates and appends one planned preparatory Base-migration Revision to Current file B.
/// This mutation boundary does not publish a StateStore head.
/// </summary>
internal static class PreparatoryBaseMigrationAppender {
    public static AbsoluteFrameAddress AppendToCurrentFile(
        RbfFileStore store,
        PreparatoryBaseMigrationPlan plan) {
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(plan);

        RbfFile currentFile = ValidateFileScope(store, plan);
        ValidateTail(currentFile, plan);
        ValidateCandidateAgainstSource(store, plan);

        PlannedRevisionV0 revision = plan.MigrationRevision;
        FrameTicket ticket = currentFile.Append(
            revision.Frame,
            revision.Estimate.RbfLayout.PayloadLengthBytes,
            revision.Estimate.RbfLayout.TailMetaLengthBytes);
        if (ticket != revision.Address.FrameTicket ||
            currentFile.ReadLayout(ticket) != revision.Estimate.RbfLayout) {
            throw new InvalidDataException(
                "The appended migration Revision does not match its precomputed RBF layout.");
        }

        return new AbsoluteFrameAddress(currentFile.FileNumber, ticket);
    }

    private static RbfFile ValidateFileScope(
        RbfFileStore store,
        PreparatoryBaseMigrationPlan plan) {
        if (plan.CurrentFileNumber < 2 ||
            plan.PublishedRevisionAddress.FileNumber != plan.CurrentFileNumber ||
            plan.MigrationRevision.FileNumber != plan.CurrentFileNumber ||
            plan.MigrationRevision.Address.FileNumber != plan.CurrentFileNumber) {
            throw new InvalidDataException(
                "The preparatory Base-migration plan does not describe one B-local Revision.");
        }

        if ((uint)store.FileCount != plan.CurrentFileNumber) {
            throw new InvalidDataException(
                $"The source store must end at Current file {plan.CurrentFileNumber}; " +
                $"it currently contains {store.FileCount} files.");
        }

        _ = store.GetFile(plan.CurrentFileNumber - 1);
        return store.GetFile(plan.CurrentFileNumber);
    }

    private static void ValidateTail(
        RbfFile currentFile,
        PreparatoryBaseMigrationPlan plan) {
        RbfFrameLayoutEstimate layout = plan.MigrationRevision.Estimate.RbfLayout;
        if (currentFile.TailOffsetBytes != layout.FrameStartOffsetBytes ||
            plan.MigrationRevision.Address.FrameTicket != layout.Ticket) {
            throw new InvalidDataException(
                $"The migration plan is stale: Current file {currentFile.FileNumber} tail is " +
                $"{currentFile.TailOffsetBytes}, but the plan starts at " +
                $"{layout.FrameStartOffsetBytes}.");
        }
    }

    private static void ValidateCandidateAgainstSource(
        RbfFileStore store,
        PreparatoryBaseMigrationPlan plan) {
        IReadOnlyDictionary<uint, AbsoluteFrameAddress> sourceStateMap =
            ObjectVersionDictionaryReader.MaterializeLive(
                store,
                plan.PublishedRevisionAddress).Bindings;
        uint previousFileNumber = plan.CurrentFileNumber - 1;
        IReadOnlyDictionary<uint, ObjectReconstructionInspection> sourceInspections =
            InspectSourceState(
                store,
                previousFileNumber,
                plan.CurrentFileNumber,
                sourceStateMap);
        PlannedRevisionV0 revision = plan.MigrationRevision;
        Frame frame = revision.Frame;
        ObjectVersionDictionary dictionary = frame.ObjectVersionDictionary
            ?? throw new InvalidDataException(
                "The planned migration Revision has no runtime OVD.");
        if (dictionary.Kind != ObjectVersionDictionaryKind.Delta ||
            dictionary.ParentRevisionFrameTicket is not RelativeFrameTicket parent ||
            ResolveRelative(revision.FileNumber, parent) !=
                plan.PublishedRevisionAddress) {
            throw new InvalidDataException(
                "The planned migration OVD is not a Delta over the source PublishedRevision.");
        }

        uint[] dictionaryObjectIds = dictionary.Entries.Keys.Order().ToArray();
        uint[] frameObjectIds = frame.ObjectVersions.Keys.Order().ToArray();
        if (!dictionaryObjectIds.SequenceEqual(frameObjectIds) ||
            !plan.RelocatedObjectIds.SequenceEqual(frameObjectIds) ||
            frameObjectIds.Length == 0 ||
            dictionary.Entries.Values.Any(binding =>
                binding.Kind != ObjectVersionDictionaryBindingKind.Self)) {
            throw new InvalidDataException(
                "The planned migration records, Self bindings, and relocation batch disagree.");
        }

        foreach (uint objectId in frameObjectIds) {
            if (!sourceInspections.TryGetValue(
                objectId,
                out ObjectReconstructionInspection? source)) {
                throw new InvalidDataException(
                    $"Planned migration object {objectId} is not live in the source Revision.");
            }

            if (source.BaseAddress.FileNumber != previousFileNumber) {
                throw new InvalidDataException(
                    $"Planned migration object {objectId} is no longer A-based within the " +
                    "source A/B reconstruction scope.");
            }

            ObjectVersion relocated = frame.ObjectVersions[objectId];
            if (relocated.Kind != ObjectVersionKind.Base ||
                relocated.PayloadBytes != source.State.BasePayloadBytes ||
                relocated.ReconstructionObjectPayloadBytes !=
                    source.State.BasePayloadBytes ||
                relocated.ResultBasePayloadBytes != source.State.BasePayloadBytes ||
                relocated.LogicalVersionOrdinal != source.State.LogicalVersionOrdinal ||
                relocated.ExpectedParentBasePayloadBytes is not null ||
                relocated.DeltaParentFrameTicket is not null) {
                throw new InvalidDataException(
                    $"Planned relocated Base {objectId} does not preserve its source state.");
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

    private static AbsoluteFrameAddress ResolveRelative(
        uint originFileNumber,
        RelativeFrameTicket ticket) {
        try {
            return new FileScope(originFileNumber).Resolve(ticket);
        } catch (InvalidOperationException exception) {
            throw new InvalidDataException(
                $"File {originFileNumber} cannot resolve relative ticket {ticket}.",
                exception);
        }
    }
}
