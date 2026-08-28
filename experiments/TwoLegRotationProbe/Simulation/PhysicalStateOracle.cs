using System.Collections.ObjectModel;
using Atelia.TwoLegRotationProbe.Model;
using Atelia.TwoLegRotationProbe.Workloads;

namespace Atelia.TwoLegRotationProbe.Simulation;

internal static class PhysicalStateOracle {
    public static IReadOnlyDictionary<uint, LogicalObjectState> Materialize(SimulationRun run) {
        ArgumentNullException.ThrowIfNull(run);
        return Materialize(run.FileStore, run.StateMap);
    }

    public static IReadOnlyDictionary<uint, LogicalObjectState> Materialize(
        RbfFileStore store,
        IReadOnlyDictionary<uint, AbsoluteFrameAddress> stateMap) {
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(stateMap);

        Dictionary<uint, LogicalObjectState> result = new(stateMap.Count);
        foreach ((uint objectId, AbsoluteFrameAddress headAddress) in stateMap) {
            result.Add(objectId, Reconstruct(store, objectId, headAddress));
        }

        return new ReadOnlyDictionary<uint, LogicalObjectState>(result);
    }

    public static void ValidateLineage(SimulationRun run) {
        ArgumentNullException.ThrowIfNull(run);
        ValidateLineage(run.FileStore, run.StateMap);
    }

    public static void ValidateLineage(
        RbfFileStore store,
        IReadOnlyDictionary<uint, AbsoluteFrameAddress> stateMap) {
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(stateMap);

        foreach ((uint objectId, AbsoluteFrameAddress headAddress) in stateMap) {
            ValidateObjectLineage(store, objectId, headAddress);
        }
    }

    private static LogicalObjectState Reconstruct(
        RbfFileStore store,
        uint objectId,
        AbsoluteFrameAddress headAddress) {
        List<ObjectVersion> pendingDeltas = [];
        HashSet<AbsoluteFrameAddress> visited = [];
        AbsoluteFrameAddress address = headAddress;
        LogicalObjectState current;

        while (true) {
            if (!visited.Add(address)) {
                throw new InvalidDataException(
                    $"Object {objectId} has a cycle in its reconstruction chain.");
            }

            ObjectVersion version = ReadObjectVersion(store, objectId, address);
            if (version.Kind == ObjectVersionKind.Base) {
                current = new LogicalObjectState(
                    version.ResultBasePayloadBytes,
                    version.VersionOrdinal);
                break;
            }

            pendingDeltas.Add(version);
            address = ResolveRequiredParent(address, version, objectId);
        }

        for (int index = pendingDeltas.Count - 1; index >= 0; index--) {
            ObjectVersion delta = pendingDeltas[index];
            int expectedParentBytes = delta.ExpectedParentBasePayloadBytes
                ?? throw new InvalidDataException(
                    $"Delta for object {objectId} has no expected parent size.");

            if (current.BasePayloadBytes != expectedParentBytes) {
                throw new InvalidDataException(
                    $"Delta for object {objectId} expected a {expectedParentBytes}-byte parent, " +
                    $"but reconstructed {current.BasePayloadBytes} bytes.");
            }

            int expectedVersionOrdinal = checked(current.VersionOrdinal + 1);
            if (delta.VersionOrdinal != expectedVersionOrdinal) {
                throw new InvalidDataException(
                    $"Delta for object {objectId} has version ordinal {delta.VersionOrdinal}, " +
                    $"but its parent requires {expectedVersionOrdinal}.");
            }

            int minimumDeltaBytes = Math.Max(
                0,
                delta.ResultBasePayloadBytes - current.BasePayloadBytes);
            if (delta.PayloadBytes < minimumDeltaBytes) {
                throw new InvalidDataException(
                    $"Delta for object {objectId} is {delta.PayloadBytes} bytes but must be at " +
                    $"least {minimumDeltaBytes} bytes for the modeled growth.");
            }

            current = new LogicalObjectState(
                delta.ResultBasePayloadBytes,
                delta.VersionOrdinal);
        }

        return current;
    }

    private static void ValidateObjectLineage(
        RbfFileStore store,
        uint objectId,
        AbsoluteFrameAddress headAddress) {
        HashSet<AbsoluteFrameAddress> visited = [];
        AbsoluteFrameAddress address = headAddress;

        while (true) {
            if (!visited.Add(address)) {
                throw new InvalidDataException(
                    $"Object {objectId} has a cycle in its lineage chain.");
            }

            ObjectVersion version = ReadObjectVersion(store, objectId, address);
            if (version.ParentFrameTicket is not RelativeFrameTicket parentTicket) {
                if (version.VersionOrdinal != 1) {
                    throw new InvalidDataException(
                        $"Object {objectId} version {version.VersionOrdinal} has no lineage parent.");
                }

                return;
            }

            AbsoluteFrameAddress parentAddress = ResolveParent(address, parentTicket);
            ObjectVersion parent = ReadObjectVersion(store, objectId, parentAddress);
            if (parent.VersionOrdinal != version.VersionOrdinal - 1) {
                throw new InvalidDataException(
                    $"Object {objectId} version {version.VersionOrdinal} points to lineage " +
                    $"version {parent.VersionOrdinal}.");
            }

            address = parentAddress;
        }
    }

    private static AbsoluteFrameAddress ResolveRequiredParent(
        AbsoluteFrameAddress childAddress,
        ObjectVersion version,
        uint objectId) {
        RelativeFrameTicket parentTicket = version.ParentFrameTicket
            ?? throw new InvalidDataException(
                $"Delta for object {objectId} has no reconstruction parent.");
        return ResolveParent(childAddress, parentTicket);
    }

    private static AbsoluteFrameAddress ResolveParent(
        AbsoluteFrameAddress childAddress,
        RelativeFrameTicket parentTicket) {
        AbsoluteFrameAddress parentAddress;
        try {
            parentAddress = new FileScope(childAddress.FileNumber).Resolve(parentTicket);
        } catch (InvalidOperationException exception) {
            throw new InvalidDataException(
                $"Frame {childAddress} has an invalid relative parent {parentTicket}.",
                exception);
        }

        if (parentAddress.FileNumber == childAddress.FileNumber &&
            parentAddress.FrameTicket.Value >= childAddress.FrameTicket.Value) {
            throw new InvalidDataException(
                $"Frame {childAddress} points to non-earlier frame {parentAddress}.");
        }

        return parentAddress;
    }

    private static ObjectVersion ReadObjectVersion(
        RbfFileStore store,
        uint objectId,
        AbsoluteFrameAddress address) {
        Frame frame;
        try {
            frame = store.ReadFrame(address);
        } catch (Exception exception) when (
            exception is ArgumentOutOfRangeException or KeyNotFoundException) {
            throw new InvalidDataException(
                $"Object {objectId} points to missing frame {address}.",
                exception);
        }

        if (!frame.ObjectVersions.TryGetValue(objectId, out ObjectVersion? version)) {
            throw new InvalidDataException(
                $"Frame {address} does not contain object {objectId}.");
        }

        return version;
    }
}
