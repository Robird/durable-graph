using Atelia.MultiSegmentStateStoreProbe.Model;
using Atelia.MultiSegmentStateStoreProbe.Planning;
using Atelia.MultiSegmentStateStoreProbe.Storage;

namespace Atelia.MultiSegmentStateStoreProbe.Reconstruction;

/// <summary>
/// Explicit historical query. Delta records carry their exact per-object parent; Base
/// records consult the containing Revision's shared prior and its exact current OVD.
/// Current-state loading deliberately does not call this reader.
/// </summary>
internal static class ObjectLineageReader {
    public static ObjectLineage Read(
        InMemorySegmentStore store,
        uint objectId,
        AbsoluteFrameAddress objectVersionHead) {
        ArgumentNullException.ThrowIfNull(store);
        ArgumentOutOfRangeException.ThrowIfZero(objectId);

        List<ObjectLineageEntry> entries = [];
        HashSet<AbsoluteFrameAddress> visited = [];
        AbsoluteFrameAddress address = objectVersionHead;
        ObjectVersion? childDelta = null;

        while (true) {
            if (!visited.Add(address)) {
                throw new InvalidDataException(
                    $"ObjectId {objectId} lineage contains a cycle at {address}.");
            }

            RevisionFrame revision = ReadRevision(
                store,
                address,
                $"ObjectVersion lineage for ObjectId {objectId}");
            if (!revision.ObjectVersions.TryGetValue(objectId, out ObjectVersion? version) ||
                version.ObjectId != objectId) {
                throw new InvalidDataException(
                    $"Frame {address} does not contain exact ObjectId {objectId}.");
            }

            if (childDelta is not null) {
                ValidateDeltaAgainstExactParent(objectId, childDelta, version.ResultState);
                childDelta = null;
            }

            entries.Add(new ObjectLineageEntry(address, version.Kind, version.ResultState));
            switch (version.Kind) {
                case ObjectVersionKind.Delta:
                    RelativeFrameTicket deltaParent = version.DeltaParent
                        ?? throw new InvalidDataException(
                            $"Delta for ObjectId {objectId} has no exact parent.");
                    childDelta = version;
                    address = ResolveEarlier(
                        address,
                        deltaParent,
                        $"Delta parent for ObjectId {objectId}");
                    break;
                case ObjectVersionKind.Base:
                    AbsoluteFrameAddress? priorRevision = ResolveOptionalPrior(
                        address,
                        revision);
                    if (priorRevision is null) {
                        EnsureUniqueGenesisFrame(store, address, objectId);
                        EnsureOrdinalOneRoot(objectId, version, "genesis");
                        return new ObjectLineage(objectId, entries);
                    }

                    ExactOvdLookup lookup = LookupPriorOvd(
                        store,
                        objectId,
                        priorRevision.Value);
                    switch (lookup.Kind) {
                        case ExactOvdLookupKind.Found:
                            AbsoluteFrameAddress priorObjectVersionHead =
                                lookup.ObjectVersionHead
                                ?? throw new InvalidDataException(
                                    "A found prior OVD binding has no ObjectVersion head.");
                            LogicalObjectState priorState = ReadExactObjectVersion(
                                store,
                                objectId,
                                priorObjectVersionHead).ResultState;
                            ValidateBaseAgainstPrior(objectId, version.ResultState, priorState);
                            address = priorObjectVersionHead;
                            break;
                        case ExactOvdLookupKind.AbsentAtBase:
                            EnsureOrdinalOneRoot(objectId, version, "Insert");
                            return new ObjectLineage(objectId, entries);
                        case ExactOvdLookupKind.Removed:
                            throw new InvalidDataException(
                                $"ObjectId {objectId} was removed in the prior OVD and " +
                                "cannot start a new lineage.");
                        default:
                            throw new InvalidDataException(
                                $"ObjectId {objectId} prior OVD lookup has an invalid result.");
                    }

                    break;
                default:
                    throw new InvalidDataException(
                        $"ObjectId {objectId} has an invalid ObjectVersion kind.");
            }
        }
    }

    private static ExactOvdLookup LookupPriorOvd(
        InMemorySegmentStore store,
        uint objectId,
        AbsoluteFrameAddress priorRevision) {
        try {
            return ExactOvdMaterializer.Materialize(store, priorRevision).Lookup(objectId);
        } catch (InvalidDataException exception) {
            throw new InvalidDataException(
                $"Prior OVD at {priorRevision} cannot be materialized for " +
                $"ObjectId {objectId} lineage.",
                exception);
        }
    }

    private static void ValidateDeltaAgainstExactParent(
        uint objectId,
        ObjectVersion delta,
        LogicalObjectState parentState) {
        LogicalObjectState expected = delta.ExpectedParentState
            ?? throw new InvalidDataException(
                $"Delta for ObjectId {objectId} has no expected parent state.");
        if (parentState != expected) {
            throw new InvalidDataException(
                $"Delta for ObjectId {objectId} does not target its exact parent state.");
        }

        if (parentState.LogicalVersionOrdinal == int.MaxValue ||
            delta.ResultState.LogicalVersionOrdinal !=
                parentState.LogicalVersionOrdinal + 1) {
            throw new InvalidDataException(
                $"Delta for ObjectId {objectId} does not advance the exact parent ordinal once.");
        }

        int minimumPayloadBytes = Math.Max(
            0,
            delta.ResultState.BasePayloadBytes - parentState.BasePayloadBytes);
        if (delta.PayloadBytes < minimumPayloadBytes) {
            throw new InvalidDataException(
                $"Delta for ObjectId {objectId} is too small for its modeled growth.");
        }
    }

    private static void ValidateBaseAgainstPrior(
        uint objectId,
        LogicalObjectState current,
        LogicalObjectState prior) {
        if (current.LogicalVersionOrdinal == prior.LogicalVersionOrdinal) {
            if (current != prior) {
                throw new InvalidDataException(
                    $"SameStateRebase for ObjectId {objectId} changed logical state.");
            }

            return;
        }

        if (prior.LogicalVersionOrdinal == int.MaxValue ||
            current.LogicalVersionOrdinal != prior.LogicalVersionOrdinal + 1) {
            throw new InvalidDataException(
                $"Domain Base for ObjectId {objectId} must advance its prior ordinal once.");
        }
    }

    private static void EnsureOrdinalOneRoot(
        uint objectId,
        ObjectVersion version,
        string rootKind) {
        if (version.ResultState.LogicalVersionOrdinal != 1) {
            throw new InvalidDataException(
                $"ObjectId {objectId} {rootKind} lineage root must have ordinal 1.");
        }
    }

    private static void EnsureUniqueGenesisFrame(
        InMemorySegmentStore store,
        AbsoluteFrameAddress address,
        uint objectId) {
        if (address.FrameTicket.OffsetBytes !=
            ProvisionalFrameEnvelopeEstimator.InitialTailOffsetBytes) {
            throw new InvalidDataException(
                $"ObjectId {objectId} has no PriorRevision outside the Store genesis Frame.");
        }

        uint fileNumber = address.FileNumber.Value;
        if (fileNumber == 1) {
            return;
        }

        try {
            _ = store.GetSegment(new FileNumber(fileNumber - 1));
        } catch (KeyNotFoundException) {
            // A probe Store may deliberately start at a non-one FileNumber fixture. Its
            // first Frame remains the unique in-memory genesis.
            return;
        }

        throw new InvalidDataException(
            $"ObjectId {objectId} has no PriorRevision outside the Store genesis Frame.");
    }

    private static AbsoluteFrameAddress? ResolveOptionalPrior(
        AbsoluteFrameAddress containingAddress,
        RevisionFrame revision) => revision.PriorRevision is { } prior
            ? ResolveEarlier(containingAddress, prior, "PriorRevision")
            : null;

    private static AbsoluteFrameAddress ResolveEarlier(
        AbsoluteFrameAddress containingAddress,
        RelativeFrameTicket reference,
        string role) {
        try {
            return new FileScope(containingAddress.FileNumber).ResolveEarlier(
                containingAddress.FrameTicket,
                reference);
        } catch (Exception exception) when (
            exception is ArgumentOutOfRangeException or InvalidDataException) {
            throw new InvalidDataException(
                $"{role} in Frame {containingAddress} is not a valid earlier reference.",
                exception);
        }
    }

    private static RevisionFrame ReadRevision(
        InMemorySegmentStore store,
        AbsoluteFrameAddress address,
        string role) {
        RenderedFrameCandidate candidate;
        try {
            candidate = store.Read(address);
        } catch (KeyNotFoundException exception) {
            throw new InvalidDataException($"{role} Frame {address} is missing.", exception);
        }

        return candidate.RevisionFrame ?? throw new InvalidDataException(
            $"{role} Frame {address} is not a semantic Revision Frame.");
    }

    private static ObjectVersion ReadExactObjectVersion(
        InMemorySegmentStore store,
        uint objectId,
        AbsoluteFrameAddress address) {
        RevisionFrame revision = ReadRevision(
            store,
            address,
            $"ObjectVersion for ObjectId {objectId}");
        if (!revision.ObjectVersions.TryGetValue(objectId, out ObjectVersion? version) ||
            version.ObjectId != objectId) {
            throw new InvalidDataException(
                $"Frame {address} does not contain exact ObjectId {objectId}.");
        }

        return version;
    }
}
