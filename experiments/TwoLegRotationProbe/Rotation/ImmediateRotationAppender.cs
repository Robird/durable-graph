using Atelia.TwoLegRotationProbe.Model;
using Atelia.TwoLegRotationProbe.Simulation;

namespace Atelia.TwoLegRotationProbe.Rotation;

/// <summary>
/// Appends a previously validated immediate-rotation candidate to a fresh Next file.
/// This is an explicit mutation boundary; it does not publish a new StateStore head.
/// </summary>
internal static class ImmediateRotationAppender {
    public static AbsoluteFrameAddress AppendToFreshNextFile(
        RbfFileStore store,
        ImmediateRotationPlan plan) {
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(plan);

        ValidateFileSequence(store, plan);
        ValidateCandidateAgainstSource(store, plan);

        PlannedRevisionV0 revision = plan.EvacuationRevision;
        (RbfFile file, FrameTicket ticket) = store.CreateFileWithFirstFrame(
            revision.Frame,
            revision.Estimate.RbfLayout);
        return new AbsoluteFrameAddress(file.FileNumber, ticket);
    }

    private static void ValidateFileSequence(
        RbfFileStore store,
        ImmediateRotationPlan plan) {
        if (plan.CurrentFileNumber < 2 ||
            plan.PreviousFileNumber != plan.CurrentFileNumber - 1 ||
            plan.NextFileNumber != checked(plan.CurrentFileNumber + 1) ||
            plan.PublishedRevisionAddress.FileNumber != plan.CurrentFileNumber ||
            plan.EvacuationRevision.FileNumber != plan.NextFileNumber ||
            plan.EvacuationRevision.Address.FileNumber != plan.NextFileNumber) {
            throw new InvalidDataException(
                "The immediate rotation plan does not describe one adjacent A/B to B/C step.");
        }

        if ((uint)store.FileCount != plan.CurrentFileNumber) {
            throw new InvalidDataException(
                $"The source store must end at Current file {plan.CurrentFileNumber}; " +
                $"it currently contains {store.FileCount} files.");
        }

        _ = store.GetFile(plan.PreviousFileNumber);
        _ = store.GetFile(plan.CurrentFileNumber);
    }

    private static void ValidateCandidateAgainstSource(
        RbfFileStore store,
        ImmediateRotationPlan plan) {
        IReadOnlyDictionary<uint, AbsoluteFrameAddress> sourceStateMap =
            ObjectVersionDictionaryReader.MaterializeLive(
                store,
                plan.PublishedRevisionAddress).Bindings;
        PlannedRevisionV0 revision = plan.EvacuationRevision;
        Frame frame = revision.Frame;
        ObjectVersionDictionary dictionary = frame.ObjectVersionDictionary
            ?? throw new InvalidDataException(
                "The planned Next Revision has no runtime OVD.");
        if (dictionary.Kind != ObjectVersionDictionaryKind.Base ||
            dictionary.ParentRevisionFrameTicket is not RelativeFrameTicket anchor ||
            ResolveRelative(revision.FileNumber, anchor) != plan.PublishedRevisionAddress) {
            throw new InvalidDataException(
                "The planned Next OVD is not a full Base anchored at the source PublishedRevision.");
        }

        if (!sourceStateMap.Keys.Order().SequenceEqual(dictionary.Entries.Keys.Order())) {
            throw new InvalidDataException(
                "The planned Next OVD does not preserve the source live ObjectId set.");
        }

        SortedSet<uint> selfObjectIds = [];
        foreach ((uint objectId, AbsoluteFrameAddress sourceAddress) in sourceStateMap) {
            ObjectReconstructionInspection source =
                PhysicalStateOracle.InspectObjectReconstruction(
                    store,
                    objectId,
                    sourceAddress);
            ObjectVersionDictionaryBinding binding = dictionary.Entries[objectId];
            switch (binding.Kind) {
                case ObjectVersionDictionaryBindingKind.Self:
                    selfObjectIds.Add(objectId);
                    if (!frame.ObjectVersions.TryGetValue(
                        objectId,
                        out ObjectVersion? relocated) ||
                        relocated.Kind != ObjectVersionKind.Base ||
                        relocated.ResultBasePayloadBytes != source.State.BasePayloadBytes ||
                        relocated.LogicalVersionOrdinal != source.State.LogicalVersionOrdinal ||
                        relocated.ParentFrameTicket != anchor) {
                        throw new InvalidDataException(
                            $"Planned relocated Base {objectId} does not preserve its source state and anchor.");
                    }

                    break;
                case ObjectVersionDictionaryBindingKind.External:
                    if (binding.ExternalFrameTicket is not RelativeFrameTicket external ||
                        ResolveRelative(revision.FileNumber, external) != sourceAddress ||
                        sourceAddress.FileNumber != plan.CurrentFileNumber ||
                        frame.ObjectVersions.ContainsKey(objectId) ||
                        source.ReconstructionFrameAddresses.Any(address =>
                            address.FileNumber != plan.CurrentFileNumber)) {
                        throw new InvalidDataException(
                            $"Planned retained object {objectId} is not a valid Current-file external binding.");
                    }

                    break;
                default:
                    throw new InvalidDataException(
                        $"A full planned OVD cannot use binding {binding.Kind} for object {objectId}.");
            }
        }

        if (!frame.ObjectVersions.Keys.Order().SequenceEqual(selfObjectIds) ||
            !plan.EvacuationObjectIds.SequenceEqual(selfObjectIds)) {
            throw new InvalidDataException(
                "The planned runtime records, Self bindings, and EvacuationSet disagree.");
        }
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
