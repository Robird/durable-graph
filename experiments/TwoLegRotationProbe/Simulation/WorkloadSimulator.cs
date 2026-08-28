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
        WorkloadReplayCursor logicalCursor = new();

        foreach (SaveStep step in trace.Steps) {
            Frame candidateFrame = BuildFrame(
                step,
                logicalCursor,
                acceptedStateMap,
                currentFile.FileNumber,
                policy);
            IReadOnlyDictionary<uint, LogicalObjectState> expectedState = logicalCursor.Apply(step);
            FrameTicket candidateTicket = currentFile.Append(candidateFrame);
            AbsoluteFrameAddress candidateAddress = new(currentFile.FileNumber, candidateTicket);
            Dictionary<uint, AbsoluteFrameAddress> candidateStateMap =
                BuildCandidateStateMap(acceptedStateMap, step, candidateAddress);

            IReadOnlyDictionary<uint, LogicalObjectState> actualState =
                PhysicalStateOracle.Materialize(store, candidateStateMap);
            EnsureExactState(expectedState, actualState);

            acceptedStateMap = candidateStateMap;
            revisionAddresses.Add(candidateAddress);
        }

        return new SimulationRun(
            trace,
            policy,
            store,
            currentFile.FileNumber,
            acceptedStateMap,
            revisionAddresses);
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
