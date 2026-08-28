using Atelia.TwoLegRotationProbe.Model;
using Atelia.TwoLegRotationProbe.Workloads;

namespace Atelia.TwoLegRotationProbe.Simulation;

internal static class WorkloadSimulator {
    public static SimulationRun Run(WorkloadTrace trace, BaselinePolicy policy) {
        ArgumentNullException.ThrowIfNull(trace);
        if (!Enum.IsDefined(policy)) {
            throw new ArgumentOutOfRangeException(nameof(policy));
        }

        _ = WorkloadReplayer.Replay(trace);

        RbfFileStore store = new();
        RbfFile currentFile = store.CreateFile();
        Dictionary<uint, AbsoluteFrameAddress> acceptedStateMap = [];
        List<AbsoluteFrameAddress> revisionAddresses = new(trace.Steps.Count);
        List<RevisionObservation> observations = new(trace.Steps.Count);
        WorkloadReplayCursor logicalCursor = new();

        for (int stepIndex = 0; stepIndex < trace.Steps.Count; stepIndex++) {
            SaveStep step = trace.Steps[stepIndex];
            Frame candidateFrame = BuildFrame(
                step,
                logicalCursor,
                acceptedStateMap,
                currentFile.FileNumber,
                policy);
            IReadOnlyDictionary<uint, LogicalObjectState> expectedState = logicalCursor.Apply(step);
            int objectPayloadBytes = GetObjectPayloadBytes(candidateFrame);
            FrameTicket candidateTicket = currentFile.Append(
                candidateFrame,
                objectPayloadBytes,
                tailMetaLengthBytes: 0);
            AbsoluteFrameAddress candidateAddress = new(currentFile.FileNumber, candidateTicket);
            Dictionary<uint, AbsoluteFrameAddress> candidateStateMap =
                BuildCandidateStateMap(acceptedStateMap, step, candidateAddress);

            IReadOnlyDictionary<uint, LogicalObjectState> actualState =
                PhysicalStateOracle.Materialize(store, candidateStateMap);
            EnsureExactState(expectedState, actualState);

            RevisionObservation observation = CreateObservation(
                stepIndex,
                step,
                candidateFrame,
                candidateAddress,
                currentFile.ReadLayout(candidateTicket),
                candidateStateMap.Count,
                PhysicalStateOracle.MeasureReconstruction(store, candidateStateMap));

            acceptedStateMap = candidateStateMap;
            revisionAddresses.Add(candidateAddress);
            observations.Add(observation);
        }

        return new SimulationRun(
            trace,
            policy,
            store,
            currentFile.FileNumber,
            acceptedStateMap,
            revisionAddresses,
            observations);
    }

    private static Frame BuildFrame(
        SaveStep step,
        WorkloadReplayCursor logicalCursor,
        IReadOnlyDictionary<uint, AbsoluteFrameAddress> stateMap,
        uint currentFileNumber,
        BaselinePolicy policy) {
        FrameBuilder builder = new();

        foreach (WorkloadChange change in step.Changes) {
            switch (change) {
                case CreateObject create:
                    ConfigureCreate(builder.Add(create.ObjectId), create);
                    break;
                case UpdateObject update:
                    ConfigureUpdate(
                        builder.Add(update.ObjectId),
                        update,
                        logicalCursor.GetLiveObjectState(update.ObjectId),
                        stateMap[update.ObjectId],
                        currentFileNumber,
                        policy);
                    break;
                case RemoveObject:
                    break;
            }
        }

        return builder.Build();
    }

    private static void ConfigureCreate(
        ObjectVersionBuilder builder,
        CreateObject create) {
        builder.Kind = ObjectVersionKind.Base;
        builder.PayloadBytes = create.BasePayloadBytes;
        builder.ResultBasePayloadBytes = create.BasePayloadBytes;
        builder.VersionOrdinal = 1;
    }

    private static void ConfigureUpdate(
        ObjectVersionBuilder builder,
        UpdateObject update,
        LogicalObjectState previousState,
        AbsoluteFrameAddress previousAddress,
        uint currentFileNumber,
        BaselinePolicy policy) {
        builder.ParentFrameTicket = ToRelativeCurrentFile(currentFileNumber, previousAddress);
        builder.ResultBasePayloadBytes = update.ResultBasePayloadBytes;
        builder.VersionOrdinal = checked(previousState.VersionOrdinal + 1);

        switch (policy) {
            case BaselinePolicy.AlwaysBase:
                builder.Kind = ObjectVersionKind.Base;
                builder.PayloadBytes = update.ResultBasePayloadBytes;
                break;
            case BaselinePolicy.AlwaysDeltaWhenLegal:
                builder.Kind = ObjectVersionKind.Delta;
                builder.PayloadBytes = update.DeltaPayloadBytes;
                builder.ExpectedParentBasePayloadBytes = previousState.BasePayloadBytes;
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(policy));
        }
    }

    private static RelativeFrameTicket ToRelativeCurrentFile(
        uint currentFileNumber,
        AbsoluteFrameAddress address) {
        if (address.FileNumber != currentFileNumber) {
            throw new InvalidOperationException(
                "The single-file baseline cannot encode a parent from another file.");
        }

        return new RelativeFrameTicket(IsPreviousFile: false, address.FrameTicket);
    }

    private static Dictionary<uint, AbsoluteFrameAddress> BuildCandidateStateMap(
        IReadOnlyDictionary<uint, AbsoluteFrameAddress> acceptedStateMap,
        SaveStep step,
        AbsoluteFrameAddress candidateAddress) {
        Dictionary<uint, AbsoluteFrameAddress> candidate = new(acceptedStateMap);
        foreach (WorkloadChange change in step.Changes) {
            switch (change) {
                case CreateObject or UpdateObject:
                    candidate[change.ObjectId] = candidateAddress;
                    break;
                case RemoveObject:
                    candidate.Remove(change.ObjectId);
                    break;
            }
        }

        return candidate;
    }

    private static int GetObjectPayloadBytes(Frame frame) {
        long total = 0;
        foreach (ObjectVersion version in frame.ObjectVersions.Values) {
            total = checked(total + version.PayloadBytes);
        }

        if (total > RbfV040Layout.MaxPayloadAndTailMetaLengthBytes) {
            throw new InvalidDataException(
                $"Object payload length {total} exceeds the modeled RBF payload capacity.");
        }

        return (int)total;
    }

    private static RevisionObservation CreateObservation(
        int stepIndex,
        SaveStep step,
        Frame frame,
        AbsoluteFrameAddress address,
        RbfFrameLayoutEstimate layout,
        int liveBindingCount,
        PostSaveReconstructionMetrics reconstruction) {
        int createdObjectCount = 0;
        int updatedObjectCount = 0;
        int removedObjectCount = 0;
        foreach (WorkloadChange change in step.Changes) {
            switch (change) {
                case CreateObject:
                    createdObjectCount++;
                    break;
                case UpdateObject:
                    updatedObjectCount++;
                    break;
                case RemoveObject:
                    removedObjectCount++;
                    break;
            }
        }

        int baseVersionCount = 0;
        int deltaVersionCount = 0;
        long baseObjectPayloadBytes = 0;
        long deltaObjectPayloadBytes = 0;
        foreach (ObjectVersion version in frame.ObjectVersions.Values) {
            if (version.Kind == ObjectVersionKind.Base) {
                baseVersionCount++;
                baseObjectPayloadBytes = checked(baseObjectPayloadBytes + version.PayloadBytes);
            } else {
                deltaVersionCount++;
                deltaObjectPayloadBytes = checked(deltaObjectPayloadBytes + version.PayloadBytes);
            }
        }

        return new RevisionObservation(
            stepIndex,
            AccountingScope.ObjectPayloadOnly,
            address,
            layout,
            frame.ObjectVersions.Count,
            createdObjectCount,
            updatedObjectCount,
            removedObjectCount,
            liveBindingCount,
            baseVersionCount,
            deltaVersionCount,
            baseObjectPayloadBytes,
            deltaObjectPayloadBytes,
            reconstruction);
    }

    private static void EnsureExactState(
        IReadOnlyDictionary<uint, LogicalObjectState> expected,
        IReadOnlyDictionary<uint, LogicalObjectState> actual) {
        if (expected.Count != actual.Count) {
            throw new InvalidOperationException(
                $"Physical candidate has {actual.Count} live objects; expected {expected.Count}.");
        }

        foreach ((uint objectId, LogicalObjectState expectedState) in expected) {
            if (!actual.TryGetValue(objectId, out LogicalObjectState actualState) ||
                actualState != expectedState) {
                throw new InvalidOperationException(
                    $"Physical candidate does not match logical object {objectId}: " +
                    $"expected {expectedState}, actual {actualState}.");
            }
        }
    }
}
