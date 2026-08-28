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
            result.Add(objectId, Reconstruct(store, objectId, headAddress, visit: null));
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

    public static PostSaveReconstructionMetrics MeasureReconstruction(SimulationRun run) {
        ArgumentNullException.ThrowIfNull(run);
        return MeasureReconstruction(
            run.FileStore,
            run.StateMap,
            run.AccountingScope,
            run.AccountingEstimates);
    }

    public static PostSaveReconstructionMetrics MeasureReconstruction(
        RbfFileStore store,
        IReadOnlyDictionary<uint, AbsoluteFrameAddress> stateMap) {
        return MeasureReconstructionCore(
            store,
            stateMap,
            AccountingScope.ObjectPayloadOnly,
            accountingEstimates: null);
    }

    internal static PostSaveReconstructionMetrics MeasureReconstruction(
        RbfFileStore store,
        IReadOnlyDictionary<uint, AbsoluteFrameAddress> stateMap,
        AccountingScope accountingScope,
        IReadOnlyDictionary<AbsoluteFrameAddress, FrameAccountingEstimate>
            accountingEstimates) {
        ArgumentNullException.ThrowIfNull(accountingEstimates);
        return MeasureReconstructionCore(
            store,
            stateMap,
            accountingScope,
            accountingEstimates);
    }

    private static PostSaveReconstructionMetrics MeasureReconstructionCore(
        RbfFileStore store,
        IReadOnlyDictionary<uint, AbsoluteFrameAddress> stateMap,
        AccountingScope accountingScope,
        IReadOnlyDictionary<AbsoluteFrameAddress, FrameAccountingEstimate>?
            accountingEstimates) {
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(stateMap);
        if (!Enum.IsDefined(accountingScope)) {
            throw new ArgumentOutOfRangeException(nameof(accountingScope));
        }

        HashSet<AbsoluteFrameAddress> uniqueFrames = [];
        HashSet<VisitedObjectVersion> requiredVersions = [];
        long requiredObjectPayloadBytes = 0;

        foreach ((uint objectId, AbsoluteFrameAddress headAddress) in stateMap) {
            _ = Reconstruct(
                store,
                objectId,
                headAddress,
                (address, version) => {
                    uniqueFrames.Add(address);
                    if (requiredVersions.Add(new VisitedObjectVersion(objectId, address))) {
                        requiredObjectPayloadBytes = checked(
                            requiredObjectPayloadBytes + version.PayloadBytes);
                    }
                });
        }

        long objectPayloadBytesInUniqueFrames = 0;
        long modeledRbfFrameBytesRead = 0;
        foreach (AbsoluteFrameAddress frameAddress in uniqueFrames) {
            Frame frame = store.ReadFrame(frameAddress);
            long frameObjectPayloadBytes = 0;
            foreach (ObjectVersion version in frame.ObjectVersions.Values) {
                frameObjectPayloadBytes = checked(
                    frameObjectPayloadBytes + version.PayloadBytes);
            }

            RbfFrameLayoutEstimate layout = store.ReadLayout(frameAddress);
            if (accountingEstimates is null) {
                if (layout.TailMetaLengthBytes != 0 ||
                    layout.PayloadLengthBytes != frameObjectPayloadBytes) {
                    throw new InvalidDataException(
                        $"Frame {frameAddress} was not appended with ObjectPayloadOnly accounting.");
                }
            } else {
                if (!accountingEstimates.TryGetValue(
                    frameAddress,
                    out FrameAccountingEstimate? estimate)) {
                    throw new InvalidDataException(
                        $"Frame {frameAddress} has no accounting-estimate provenance.");
                }

                if (estimate.Scope != accountingScope || estimate.RbfLayout != layout) {
                    throw new InvalidDataException(
                        $"Frame {frameAddress} accounting provenance does not match the run or stored layout.");
                }

                switch (accountingScope) {
                    case AccountingScope.ObjectPayloadOnly:
                        if (layout.TailMetaLengthBytes != 0 ||
                            layout.PayloadLengthBytes != frameObjectPayloadBytes) {
                            throw new InvalidDataException(
                                $"Frame {frameAddress} was not appended with ObjectPayloadOnly accounting.");
                        }

                        break;
                    case AccountingScope.ProvisionalRevisionV0:
                        if (estimate.ProvisionalRevisionV0 is not { } provisional ||
                            provisional.SyntheticObjectPayloadBytes != frameObjectPayloadBytes) {
                            throw new InvalidDataException(
                                $"Frame {frameAddress} has inconsistent provisional accounting provenance.");
                        }

                        break;
                    default:
                        throw new ArgumentOutOfRangeException(nameof(accountingScope));
                }
            }

            objectPayloadBytesInUniqueFrames = checked(
                objectPayloadBytesInUniqueFrames + frameObjectPayloadBytes);
            modeledRbfFrameBytesRead = checked(
                modeledRbfFrameBytesRead + layout.FrameLengthBytes);
        }

        return new PostSaveReconstructionMetrics(
            accountingScope,
            stateMap.Count,
            requiredVersions.Count,
            uniqueFrames.Count,
            requiredObjectPayloadBytes,
            objectPayloadBytesInUniqueFrames,
            modeledRbfFrameBytesRead);
    }

    private static LogicalObjectState Reconstruct(
        RbfFileStore store,
        uint objectId,
        AbsoluteFrameAddress headAddress,
        Action<AbsoluteFrameAddress, ObjectVersion>? visit) {
        List<ObjectVersion> pendingDeltas = [];
        HashSet<AbsoluteFrameAddress> visited = [];
        AbsoluteFrameAddress address = headAddress;
        LogicalObjectState current;
        long currentReconstructionObjectPayloadBytes;

        while (true) {
            if (!visited.Add(address)) {
                throw new InvalidDataException(
                    $"Object {objectId} has a cycle in its reconstruction chain.");
            }

            ObjectVersion version = ReadObjectVersion(store, objectId, address);
            visit?.Invoke(address, version);
            if (version.Kind == ObjectVersionKind.Base) {
                if (version.ReconstructionObjectPayloadBytes != version.PayloadBytes) {
                    throw new InvalidDataException(
                        $"Base for object {objectId} declares " +
                        $"{version.ReconstructionObjectPayloadBytes} reconstruction payload bytes, " +
                        $"but contains {version.PayloadBytes} payload bytes.");
                }

                current = new LogicalObjectState(
                    version.ResultBasePayloadBytes,
                    version.VersionOrdinal);
                currentReconstructionObjectPayloadBytes = version.PayloadBytes;
                break;
            }

            pendingDeltas.Add(version);
            address = ResolveRequiredParent(address, version, objectId);
        }

        for (int index = pendingDeltas.Count - 1; index >= 0; index--) {
            ObjectVersion delta = pendingDeltas[index];
            long expectedReconstructionObjectPayloadBytes;
            try {
                expectedReconstructionObjectPayloadBytes = checked(
                    currentReconstructionObjectPayloadBytes + delta.PayloadBytes);
            } catch (OverflowException exception) {
                throw new InvalidDataException(
                    $"Reconstruction payload size overflowed for object {objectId}.",
                    exception);
            }

            if (delta.ReconstructionObjectPayloadBytes !=
                expectedReconstructionObjectPayloadBytes) {
                throw new InvalidDataException(
                    $"Delta for object {objectId} declares " +
                    $"{delta.ReconstructionObjectPayloadBytes} reconstruction payload bytes, " +
                    $"but its parent and payload require " +
                    $"{expectedReconstructionObjectPayloadBytes} bytes.");
            }

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
            currentReconstructionObjectPayloadBytes =
                expectedReconstructionObjectPayloadBytes;
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
            parentAddress.FrameTicket.OffsetBytes >= childAddress.FrameTicket.OffsetBytes) {
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

    private readonly record struct VisitedObjectVersion(
        uint ObjectId,
        AbsoluteFrameAddress FrameAddress);
}
