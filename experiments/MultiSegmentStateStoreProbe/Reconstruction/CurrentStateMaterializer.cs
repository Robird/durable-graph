using Atelia.MultiSegmentStateStoreProbe.Model;
using Atelia.MultiSegmentStateStoreProbe.Planning;
using Atelia.MultiSegmentStateStoreProbe.Storage;

namespace Atelia.MultiSegmentStateStoreProbe.Reconstruction;

internal static class CurrentStateMaterializer {
    public static MaterializedCurrentState Materialize(
        InMemorySegmentStore store,
        AbsoluteFrameAddress publishedHead) {
        return Materialize(store, publishedHead, overlay: null);
    }

    internal static MaterializedCurrentState Materialize(
        InMemorySegmentStore store,
        AbsoluteFrameAddress publishedHead,
        RenderedFrameCandidate? overlay) {
        ArgumentNullException.ThrowIfNull(store);

        ExactOvdMaterialization ovd = ExactOvdMaterializer.Materialize(
            store,
            publishedHead,
            overlay);
        SortedDictionary<uint, LogicalObjectState> states = [];
        SortedDictionary<uint, IReadOnlyList<AbsoluteFrameAddress>> paths = [];
        foreach ((uint objectId, AbsoluteFrameAddress headAddress) in ovd.Bindings) {
            ObjectReconstruction reconstruction = ReconstructObject(
                store,
                objectId,
                headAddress,
                overlay);
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

    private static ObjectReconstruction ReconstructObject(
        InMemorySegmentStore store,
        uint objectId,
        AbsoluteFrameAddress headAddress,
        RenderedFrameCandidate? overlay) {
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
                $"ObjectVersion for ObjectId {objectId}",
                overlay);
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
        string role,
        RenderedFrameCandidate? overlay) {
        if (overlay is not null && address == overlay.Address) {
            return overlay.RevisionFrame ?? throw new InvalidDataException(
                $"{role} overlay Frame {address} is not a semantic Revision Frame.");
        }

        RenderedFrameCandidate candidate;
        try {
            candidate = store.Read(address);
        } catch (KeyNotFoundException exception) {
            throw new InvalidDataException($"{role} Frame {address} is missing.", exception);
        }

        return candidate.RevisionFrame ?? throw new InvalidDataException(
            $"{role} Frame {address} is not a semantic Revision Frame.");
    }

    private sealed record ObjectReconstruction(
        LogicalObjectState State,
        IReadOnlyList<AbsoluteFrameAddress> Path);
}
