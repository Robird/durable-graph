using Atelia.TwoLegRotationProbe.Encoding;
using Atelia.TwoLegRotationProbe.Model;
using Atelia.TwoLegRotationProbe.Workloads;

namespace Atelia.TwoLegRotationProbe.Simulation;

internal static class WorkloadSimulator {
    public static SimulationRun Run(WorkloadTrace trace, BaselinePolicy policy) {
        return Run(trace, policy, AccountingScope.ObjectPayloadOnly);
    }

    public static SimulationRun Run(
        WorkloadTrace trace,
        BaselinePolicy policy,
        AccountingScope accountingScope) {
        ArgumentNullException.ThrowIfNull(trace);
        if (!Enum.IsDefined(policy)) {
            throw new ArgumentOutOfRangeException(nameof(policy));
        }
        if (!Enum.IsDefined(accountingScope)) {
            throw new ArgumentOutOfRangeException(nameof(accountingScope));
        }

        _ = WorkloadReplayer.Replay(trace);

        RbfFileStore store = new();
        RbfFile currentFile = store.CreateFile();
        Dictionary<uint, AbsoluteFrameAddress> acceptedStateMap = [];
        List<AbsoluteFrameAddress> revisionAddresses = new(trace.Steps.Count);
        List<RevisionObservation> observations = new(trace.Steps.Count);
        Dictionary<AbsoluteFrameAddress, FrameAccountingEstimate> accountingEstimates = [];
        WorkloadReplayCursor logicalCursor = new();

        for (int stepIndex = 0; stepIndex < trace.Steps.Count; stepIndex++) {
            SaveStep step = trace.Steps[stepIndex];
            Frame candidateFrame = BuildFrame(
                step,
                logicalCursor,
                store,
                acceptedStateMap,
                currentFile.FileNumber,
                revisionAddresses.Count == 0 ? null : revisionAddresses[^1],
                policy);
            IReadOnlyDictionary<uint, LogicalObjectState> expectedState = logicalCursor.Apply(step);
            FrameAccountingEstimate accountingEstimate = EstimateCandidateFrame(
                accountingScope,
                candidateFrame,
                currentFile);
            RbfFrameLayoutEstimate expectedLayout = accountingEstimate.RbfLayout;
            FrameTicket candidateTicket = currentFile.Append(
                candidateFrame,
                expectedLayout.PayloadLengthBytes,
                expectedLayout.TailMetaLengthBytes);
            RbfFrameLayoutEstimate storedLayout = currentFile.ReadLayout(candidateTicket);
            if (candidateTicket != expectedLayout.Ticket || storedLayout != expectedLayout) {
                throw new InvalidDataException(
                    "RBF append did not match the precomputed accounting estimate.");
            }

            AbsoluteFrameAddress candidateAddress = new(currentFile.FileNumber, candidateTicket);
            accountingEstimates.Add(candidateAddress, accountingEstimate);
            ObjectVersionDictionaryMaterializationInspection materializedOvd =
                ObjectVersionDictionaryReader.MaterializeLive(store, candidateAddress);
            Dictionary<uint, AbsoluteFrameAddress> candidateStateMap =
                new(materializedOvd.Bindings);
            EnsurePointLookupsMatchMaterialization(
                store,
                candidateAddress,
                step,
                candidateStateMap);

            IReadOnlyDictionary<uint, LogicalObjectState> actualState =
                PhysicalStateOracle.Materialize(store, candidateStateMap);
            EnsureExactState(expectedState, actualState);

            RevisionObservation observation = CreateObservation(
                stepIndex,
                step,
                candidateFrame,
                candidateAddress,
                accountingEstimate,
                candidateStateMap.Count,
                PhysicalStateOracle.MeasureReconstruction(
                    store,
                    candidateStateMap,
                    accountingScope,
                    accountingEstimates));

            acceptedStateMap = candidateStateMap;
            revisionAddresses.Add(candidateAddress);
            observations.Add(observation);
        }

        return new SimulationRun(
            trace,
            policy,
            accountingScope,
            store,
            currentFile.FileNumber,
            acceptedStateMap,
            revisionAddresses,
            observations,
            accountingEstimates);
    }

    private static Frame BuildFrame(
        SaveStep step,
        WorkloadReplayCursor logicalCursor,
        RbfFileStore store,
        IReadOnlyDictionary<uint, AbsoluteFrameAddress> stateMap,
        uint currentFileNumber,
        AbsoluteFrameAddress? previousRevisionAddress,
        BaselinePolicy policy) {
        ObjectVersionDictionaryBuilder dictionary = new();
        if (previousRevisionAddress is AbsoluteFrameAddress previousRevision) {
            dictionary.Kind = ObjectVersionDictionaryKind.Delta;
            dictionary.ParentRevisionFrameTicket =
                ToRelativeCurrentFile(currentFileNumber, previousRevision);
        }

        FrameBuilder builder = new() { ObjectVersionDictionary = dictionary };

        foreach (WorkloadChange change in step.Changes) {
            switch (change) {
                case CreateObject create:
                    ConfigureCreate(builder.Add(create.ObjectId), create);
                    dictionary.BindSelf(create.ObjectId);
                    break;
                case UpdateObject update:
                    ConfigureUpdate(
                        builder.Add(update.ObjectId),
                        update,
                        logicalCursor.GetLiveObjectState(update.ObjectId),
                        GetHeadObjectVersion(store, stateMap[update.ObjectId], update.ObjectId),
                        stateMap[update.ObjectId],
                        previousRevisionAddress ?? throw new InvalidOperationException(
                            "An update requires a previous published Revision."),
                        currentFileNumber,
                        policy);
                    dictionary.BindSelf(update.ObjectId);
                    break;
                case RemoveObject remove:
                    dictionary.Remove(remove.ObjectId);
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
        builder.ReconstructionObjectPayloadBytes = create.BasePayloadBytes;
        builder.ResultBasePayloadBytes = create.BasePayloadBytes;
        builder.LogicalVersionOrdinal = 1;
    }

    private static void ConfigureUpdate(
        ObjectVersionBuilder builder,
        UpdateObject update,
        LogicalObjectState previousState,
        ObjectVersion previousVersion,
        AbsoluteFrameAddress previousAddress,
        AbsoluteFrameAddress previousRevisionAddress,
        uint currentFileNumber,
        BaselinePolicy policy) {
        builder.ResultBasePayloadBytes = update.ResultBasePayloadBytes;
        builder.LogicalVersionOrdinal = checked(previousState.LogicalVersionOrdinal + 1);

        bool writeBase = policy switch {
            BaselinePolicy.AlwaysBase => true,
            BaselinePolicy.AlwaysDeltaWhenLegal => false,
            BaselinePolicy.ObjectPayloadReadAmplification3 =>
                ObjectPayloadReadAmplificationPolicy.ShouldWriteBase(
                    update.ResultBasePayloadBytes,
                    update.DeltaPayloadBytes,
                    previousVersion.ReconstructionObjectPayloadBytes),
            _ => throw new ArgumentOutOfRangeException(nameof(policy)),
        };

        if (writeBase) {
            builder.Kind = ObjectVersionKind.Base;
            builder.ParentFrameTicket =
                ToRelativeCurrentFile(currentFileNumber, previousRevisionAddress);
            builder.PayloadBytes = update.ResultBasePayloadBytes;
            builder.ReconstructionObjectPayloadBytes = update.ResultBasePayloadBytes;
        } else {
            builder.Kind = ObjectVersionKind.Delta;
            builder.ParentFrameTicket = ToRelativeCurrentFile(currentFileNumber, previousAddress);
            builder.PayloadBytes = update.DeltaPayloadBytes;
            builder.ReconstructionObjectPayloadBytes = checked(
                previousVersion.ReconstructionObjectPayloadBytes + update.DeltaPayloadBytes);
            builder.ExpectedParentBasePayloadBytes = previousState.BasePayloadBytes;
        }
    }

    private static ObjectVersion GetHeadObjectVersion(
        RbfFileStore store,
        AbsoluteFrameAddress address,
        uint objectId) {
        Frame frame = store.ReadFrame(address);
        if (!frame.ObjectVersions.TryGetValue(objectId, out ObjectVersion? version)) {
            throw new InvalidOperationException(
                $"StateMap head {address} does not contain object {objectId}.");
        }

        return version;
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

    private static void EnsurePointLookupsMatchMaterialization(
        RbfFileStore store,
        AbsoluteFrameAddress revisionAddress,
        SaveStep step,
        IReadOnlyDictionary<uint, AbsoluteFrameAddress> materializedStateMap) {
        foreach ((uint objectId, AbsoluteFrameAddress expectedAddress) in materializedStateMap) {
            ObjectVersionDictionaryLookupInspection lookup =
                ObjectVersionDictionaryReader.LookupLive(store, revisionAddress, objectId);
            if (lookup.Disposition != ObjectVersionDictionaryLookupDisposition.Found ||
                lookup.ResolvedObjectVersionAddress != expectedAddress) {
                throw new InvalidDataException(
                    $"Point lookup for live object {objectId} disagrees with OVD materialization.");
            }
        }

        foreach (WorkloadChange change in step.Changes) {
            if (change is not RemoveObject) {
                continue;
            }

            ObjectVersionDictionaryLookupInspection lookup =
                ObjectVersionDictionaryReader.LookupLive(store, revisionAddress, change.ObjectId);
            if (lookup.Disposition != ObjectVersionDictionaryLookupDisposition.Removed) {
                throw new InvalidDataException(
                    $"Point lookup for removed object {change.ObjectId} did not stop at Remove.");
            }
        }
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

    private static FrameAccountingEstimate EstimateCandidateFrame(
        AccountingScope accountingScope,
        Frame frame,
        RbfFile currentFile) {
        switch (accountingScope) {
            case AccountingScope.ObjectPayloadOnly:
                return FrameAccountingEstimate.ObjectPayloadOnly(
                    RbfV040Layout.Estimate(
                        currentFile.TailOffsetBytes,
                        GetObjectPayloadBytes(frame),
                        tailMetaLengthBytes: 0));
            case AccountingScope.ProvisionalRevisionV0:
                return FrameAccountingEstimate.Provisional(
                    ProvisionalRevisionV0Estimator.Estimate(
                        frame,
                        currentFile.TailOffsetBytes));
            default:
                throw new ArgumentOutOfRangeException(nameof(accountingScope));
        }
    }

    private static RevisionObservation CreateObservation(
        int stepIndex,
        SaveStep step,
        Frame frame,
        AbsoluteFrameAddress address,
        FrameAccountingEstimate accountingEstimate,
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
            accountingEstimate.Scope,
            address,
            accountingEstimate,
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
