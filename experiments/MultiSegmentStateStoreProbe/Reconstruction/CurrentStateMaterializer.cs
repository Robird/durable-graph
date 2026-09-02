using Atelia.MultiSegmentStateStoreProbe.Model;
using Atelia.MultiSegmentStateStoreProbe.Planning;
using Atelia.MultiSegmentStateStoreProbe.Storage;

namespace Atelia.MultiSegmentStateStoreProbe.Reconstruction;

internal static class CurrentStateMaterializer {
    public static MaterializedCurrentState Materialize(
        InMemorySegmentStore store,
        AbsoluteFrameAddress publishedHead) {
        ArgumentNullException.ThrowIfNull(store);

        OvdMaterialization ovd = MaterializeOvd(store, publishedHead);
        SortedDictionary<uint, LogicalObjectState> states = [];
        SortedDictionary<uint, IReadOnlyList<AbsoluteFrameAddress>> paths = [];
        foreach ((uint objectId, AbsoluteFrameAddress headAddress) in ovd.Bindings) {
            ObjectReconstruction reconstruction = ReconstructObject(
                store,
                objectId,
                headAddress);
            states.Add(objectId, reconstruction.State);
            paths.Add(objectId, reconstruction.Path);
        }

        // Nothing escapes before every live binding and every object has reconstructed.
        return new MaterializedCurrentState(
            publishedHead,
            ovd.Bindings,
            states,
            paths,
            ovd.RevisionAddresses);
    }

    private static OvdMaterialization MaterializeOvd(
        InMemorySegmentStore store,
        AbsoluteFrameAddress publishedHead) {
        List<RevisionRecord> records = [];
        HashSet<AbsoluteFrameAddress> visited = [];
        AbsoluteFrameAddress address = publishedHead;

        while (true) {
            if (!visited.Add(address)) {
                throw new InvalidDataException(
                    $"OVD reconstruction contains a Revision cycle at {address}.");
            }

            RevisionFrame revision = ReadRevision(store, address, "Revision");
            AbsoluteFrameAddress? prior = ResolveOptionalPrior(address, revision);
            records.Add(new RevisionRecord(address, revision));
            switch (revision.ObjectVersionDictionary.Kind) {
                case ObjectVersionDictionaryKind.Base:
                    goto Replay;
                case ObjectVersionDictionaryKind.Delta:
                    address = prior ?? throw new InvalidDataException(
                        $"OVD Delta Revision {address} has no shared PriorRevision.");
                    break;
                default:
                    throw new InvalidDataException(
                        $"Revision {address} has an invalid OVD kind.");
            }
        }

    Replay:
        SortedDictionary<uint, AbsoluteFrameAddress> bindings = [];
        for (int index = records.Count - 1; index >= 0; index--) {
            RevisionRecord source = records[index];
            foreach ((uint objectId, ObjectVersionDictionaryBinding binding) in
                source.Revision.ObjectVersionDictionary.Entries) {
                switch (binding.Kind) {
                    case ObjectVersionDictionaryBindingKind.BindSelf:
                        if (!source.Revision.ObjectVersions.ContainsKey(objectId)) {
                            throw new InvalidDataException(
                                $"Revision {source.Address} binds ObjectId {objectId} to Self " +
                                "without a same-ObjectId record.");
                        }

                        bindings[objectId] = source.Address;
                        break;
                    case ObjectVersionDictionaryBindingKind.External:
                        RelativeFrameTicket external = binding.ExternalReference
                            ?? throw new InvalidDataException(
                                $"External binding for ObjectId {objectId} has no reference.");
                        AbsoluteFrameAddress externalAddress = ResolveEarlier(
                            source.Address,
                            external,
                            $"External binding for ObjectId {objectId}");
                        RevisionFrame target = ReadRevision(
                            store,
                            externalAddress,
                            $"External ObjectVersion for ObjectId {objectId}");
                        if (!target.ObjectVersions.ContainsKey(objectId)) {
                            throw new InvalidDataException(
                                $"External Frame {externalAddress} does not contain " +
                                $"ObjectId {objectId}.");
                        }

                        bindings[objectId] = externalAddress;
                        break;
                    case ObjectVersionDictionaryBindingKind.Remove:
                        _ = bindings.Remove(objectId);
                        break;
                    default:
                        throw new InvalidDataException(
                            $"Revision {source.Address} has an invalid OVD binding kind.");
                }
            }
        }

        return new OvdMaterialization(
            bindings,
            records.Select(static record => record.Address).ToArray());
    }

    private static ObjectReconstruction ReconstructObject(
        InMemorySegmentStore store,
        uint objectId,
        AbsoluteFrameAddress headAddress) {
        List<(AbsoluteFrameAddress Address, ObjectVersion Version)> pendingDeltas = [];
        List<AbsoluteFrameAddress> path = [];
        HashSet<AbsoluteFrameAddress> visited = [];
        AbsoluteFrameAddress address = headAddress;
        LogicalObjectState state;

        while (true) {
            if (!visited.Add(address)) {
                throw new InvalidDataException(
                    $"ObjectId {objectId} reconstruction contains a cycle at {address}.");
            }

            RevisionFrame frame = ReadRevision(
                store,
                address,
                $"ObjectVersion for ObjectId {objectId}");
            if (!frame.ObjectVersions.TryGetValue(objectId, out ObjectVersion? version) ||
                version.ObjectId != objectId) {
                throw new InvalidDataException(
                    $"Frame {address} does not contain exact ObjectId {objectId}.");
            }

            path.Add(address);
            switch (version.Kind) {
                case ObjectVersionKind.Base:
                    state = version.ResultState;
                    goto ApplyDeltas;
                case ObjectVersionKind.Delta:
                    RelativeFrameTicket parent = version.DeltaParent
                        ?? throw new InvalidDataException(
                            $"Delta for ObjectId {objectId} has no exact parent.");
                    pendingDeltas.Add((address, version));
                    address = ResolveEarlier(
                        address,
                        parent,
                        $"Delta parent for ObjectId {objectId}");
                    break;
                default:
                    throw new InvalidDataException(
                        $"ObjectId {objectId} has an invalid ObjectVersion kind.");
            }
        }

    ApplyDeltas:
        for (int index = pendingDeltas.Count - 1; index >= 0; index--) {
            ObjectVersion delta = pendingDeltas[index].Version;
            LogicalObjectState expected = delta.ExpectedParentState
                ?? throw new InvalidDataException(
                    $"Delta for ObjectId {objectId} has no expected parent state.");
            if (state != expected) {
                throw new InvalidDataException(
                    $"Delta for ObjectId {objectId} does not target its exact parent state.");
            }

            if (state.LogicalVersionOrdinal == int.MaxValue ||
                delta.ResultState.LogicalVersionOrdinal !=
                    state.LogicalVersionOrdinal + 1) {
                throw new InvalidDataException(
                    $"Delta for ObjectId {objectId} does not advance the exact parent ordinal once.");
            }

            int minimumPayloadBytes = Math.Max(
                0,
                delta.ResultState.BasePayloadBytes - state.BasePayloadBytes);
            if (delta.PayloadBytes < minimumPayloadBytes) {
                throw new InvalidDataException(
                    $"Delta for ObjectId {objectId} is too small for its modeled growth.");
            }

            state = delta.ResultState;
        }

        return new ObjectReconstruction(state, path.AsReadOnly());
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

    private sealed record RevisionRecord(
        AbsoluteFrameAddress Address,
        RevisionFrame Revision);

    private sealed record OvdMaterialization(
        IReadOnlyDictionary<uint, AbsoluteFrameAddress> Bindings,
        IReadOnlyList<AbsoluteFrameAddress> RevisionAddresses);

    private sealed record ObjectReconstruction(
        LogicalObjectState State,
        IReadOnlyList<AbsoluteFrameAddress> Path);
}
