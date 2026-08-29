using Atelia.TwoLegRotationProbe.Model;
using Atelia.TwoLegRotationProbe.Simulation;
using Atelia.TwoLegRotationProbe.Workloads;

namespace Atelia.TwoLegRotationProbe.Tests;

public sealed class MaintenanceObjectVersionSemanticsTests {
    private const uint ObjectId = 7;

    [Fact]
    public void Relocated_base_reconstructs_from_C_while_lineage_spans_A_B_C() {
        MaintenanceChain chain = CreateMaintenanceChain();

        ObjectReconstructionInspection reconstruction =
            PhysicalStateOracle.InspectObjectReconstruction(
                chain.Store,
                ObjectId,
                chain.RelocatedBaseAddress);
        ObjectLineageInspection lineage = PhysicalStateOracle.InspectObjectLineage(
            chain.Store,
            ObjectId,
            chain.RelocatedBaseAddress);

        Assert.Equal(new LogicalObjectState(10, 1), reconstruction.State);
        Assert.Equal(chain.RelocatedBaseAddress, reconstruction.HeadAddress);
        Assert.Equal(chain.RelocatedBaseAddress, reconstruction.BaseAddress);
        Assert.Equal([chain.RelocatedBaseAddress], reconstruction.ReconstructionFrameAddresses);

        Assert.Equal(ObjectId, lineage.ObjectId);
        Assert.Equal(new LogicalObjectState(10, 1), lineage.HeadState);
        Assert.Equal(chain.RelocatedBaseAddress, lineage.HeadAddress);
        Assert.Equal(chain.OriginalBaseAddress, lineage.RootAddress);
        Assert.Equal(
            [
                chain.RelocatedBaseAddress,
                chain.RelayAddress,
                chain.OriginalBaseAddress,
            ],
            lineage.HeadToRootFrameAddresses);
    }

    [Fact]
    public void Relay_head_preserves_logical_state_and_reconstructs_through_B_and_A() {
        MaintenanceChain chain = CreateMaintenanceChain();

        ObjectReconstructionInspection reconstruction =
            PhysicalStateOracle.InspectObjectReconstruction(
                chain.Store,
                ObjectId,
                chain.RelayAddress);
        ObjectLineageInspection lineage = PhysicalStateOracle.InspectObjectLineage(
            chain.Store,
            ObjectId,
            chain.RelayAddress);

        Assert.Equal(new LogicalObjectState(10, 1), reconstruction.State);
        Assert.Equal(chain.OriginalBaseAddress, reconstruction.BaseAddress);
        Assert.Equal(
            [chain.RelayAddress, chain.OriginalBaseAddress],
            reconstruction.ReconstructionFrameAddresses);
        Assert.Equal(new LogicalObjectState(10, 1), lineage.HeadState);
        Assert.Equal(
            [chain.RelayAddress, chain.OriginalBaseAddress],
            lineage.HeadToRootFrameAddresses);
    }

    [Fact]
    public void Domain_delta_after_relay_advances_logical_version_once() {
        RbfFileStore store = new();
        RbfFile previous = store.CreateFile();
        AbsoluteFrameAddress original = AppendBase(
            previous,
            ObjectId,
            payloadBytes: 10,
            logicalVersionOrdinal: 1,
            parentAddress: null);
        RbfFile current = store.CreateFile();
        AbsoluteFrameAddress relay = AppendDelta(
            current,
            ObjectId,
            payloadBytes: 0,
            reconstructionPayloadBytes: 10,
            resultBasePayloadBytes: 10,
            expectedParentBasePayloadBytes: 10,
            logicalVersionOrdinal: 1,
            parentAddress: original);
        RbfFile next = store.CreateFile();
        AbsoluteFrameAddress domainDelta = AppendDelta(
            next,
            ObjectId,
            payloadBytes: 5,
            reconstructionPayloadBytes: 15,
            resultBasePayloadBytes: 15,
            expectedParentBasePayloadBytes: 10,
            logicalVersionOrdinal: 2,
            parentAddress: relay);

        ObjectReconstructionInspection reconstruction =
            PhysicalStateOracle.InspectObjectReconstruction(
                store,
                ObjectId,
                domainDelta);
        ObjectLineageInspection lineage = PhysicalStateOracle.InspectObjectLineage(
            store,
            ObjectId,
            domainDelta);

        Assert.Equal(new LogicalObjectState(15, 2), reconstruction.State);
        Assert.Equal(
            [domainDelta, relay, original],
            reconstruction.ReconstructionFrameAddresses);
        Assert.Equal(new LogicalObjectState(15, 2), lineage.HeadState);
        Assert.Equal([domainDelta, relay, original], lineage.HeadToRootFrameAddresses);
    }

    [Fact]
    public void Domain_base_after_relay_advances_logical_version_once() {
        MaintenanceChain chain = CreateMaintenanceChain(includeRelocatedBase: false);
        RbfFile next = chain.Store.CreateFile();
        AbsoluteFrameAddress domainBase = AppendBase(
            next,
            ObjectId,
            payloadBytes: 17,
            logicalVersionOrdinal: 2,
            parentAddress: chain.RelayAddress);

        ObjectReconstructionInspection reconstruction =
            PhysicalStateOracle.InspectObjectReconstruction(
                chain.Store,
                ObjectId,
                domainBase);
        ObjectLineageInspection lineage = PhysicalStateOracle.InspectObjectLineage(
            chain.Store,
            ObjectId,
            domainBase);

        Assert.Equal(new LogicalObjectState(17, 2), reconstruction.State);
        Assert.Equal([domainBase], reconstruction.ReconstructionFrameAddresses);
        Assert.Equal(new LogicalObjectState(17, 2), lineage.HeadState);
        Assert.Equal(
            [domainBase, chain.RelayAddress, chain.OriginalBaseAddress],
            lineage.HeadToRootFrameAddresses);
    }

    [Fact]
    public void Same_version_relay_with_positive_payload_fails_closed() {
        (RbfFileStore store, RbfFile previous, AbsoluteFrameAddress original) =
            CreateOriginalBase();
        RbfFile current = store.CreateFile();
        AbsoluteFrameAddress malformedRelay = AppendDelta(
            current,
            ObjectId,
            payloadBytes: 1,
            reconstructionPayloadBytes: 11,
            resultBasePayloadBytes: 10,
            expectedParentBasePayloadBytes: 10,
            logicalVersionOrdinal: 1,
            parentAddress: original);

        Assert.Throws<InvalidDataException>(() =>
            PhysicalStateOracle.InspectObjectReconstruction(
                store,
                ObjectId,
                malformedRelay));
    }

    [Fact]
    public void Same_version_relay_that_changes_result_fails_closed() {
        (RbfFileStore store, RbfFile previous, AbsoluteFrameAddress original) =
            CreateOriginalBase();
        RbfFile current = store.CreateFile();
        Assert.Throws<ArgumentException>(() => AppendDelta(
                current,
                ObjectId,
                payloadBytes: 0,
                reconstructionPayloadBytes: 10,
                resultBasePayloadBytes: 11,
                expectedParentBasePayloadBytes: 10,
                logicalVersionOrdinal: 1,
                parentAddress: original));
    }

    [Fact]
    public void Same_version_relay_with_wrong_cumulative_payload_fails_closed() {
        (RbfFileStore store, RbfFile previous, AbsoluteFrameAddress original) =
            CreateOriginalBase();
        RbfFile current = store.CreateFile();
        AbsoluteFrameAddress malformedRelay = AppendDelta(
            current,
            ObjectId,
            payloadBytes: 0,
            reconstructionPayloadBytes: 11,
            resultBasePayloadBytes: 10,
            expectedParentBasePayloadBytes: 10,
            logicalVersionOrdinal: 1,
            parentAddress: original);

        Assert.Throws<InvalidDataException>(() =>
            PhysicalStateOracle.InspectObjectReconstruction(
                store,
                ObjectId,
                malformedRelay));
    }

    [Fact]
    public void Relocated_base_lineage_rejects_a_malformed_hidden_relay() {
        (RbfFileStore store, RbfFile previous, AbsoluteFrameAddress original) =
            CreateOriginalBase();
        RbfFile current = store.CreateFile();
        AbsoluteFrameAddress malformedRelay = AppendDelta(
            current,
            ObjectId,
            payloadBytes: 0,
            reconstructionPayloadBytes: 11,
            resultBasePayloadBytes: 10,
            expectedParentBasePayloadBytes: 10,
            logicalVersionOrdinal: 1,
            parentAddress: original);
        RbfFile next = store.CreateFile();
        AbsoluteFrameAddress relocatedBase = AppendBase(
            next,
            ObjectId,
            payloadBytes: 10,
            logicalVersionOrdinal: 1,
            parentAddress: malformedRelay);

        ObjectReconstructionInspection reconstruction =
            PhysicalStateOracle.InspectObjectReconstruction(
                store,
                ObjectId,
                relocatedBase);
        Assert.Equal(new LogicalObjectState(10, 1), reconstruction.State);
        Assert.Equal([relocatedBase], reconstruction.ReconstructionFrameAddresses);

        Assert.Throws<InvalidDataException>(() =>
            PhysicalStateOracle.InspectObjectLineage(
                store,
                ObjectId,
                relocatedBase));
    }

    [Fact]
    public void Relocated_base_lineage_rejects_hidden_delta_with_impossible_growth() {
        (RbfFileStore store, RbfFile previous, AbsoluteFrameAddress original) =
            CreateOriginalBase();
        RbfFile current = store.CreateFile();
        AbsoluteFrameAddress malformedDelta = AppendDelta(
            current,
            ObjectId,
            payloadBytes: 1,
            reconstructionPayloadBytes: 11,
            resultBasePayloadBytes: 100,
            expectedParentBasePayloadBytes: 10,
            logicalVersionOrdinal: 2,
            parentAddress: original);
        RbfFile next = store.CreateFile();
        AbsoluteFrameAddress relocatedBase = AppendBase(
            next,
            ObjectId,
            payloadBytes: 100,
            logicalVersionOrdinal: 2,
            parentAddress: malformedDelta);

        ObjectReconstructionInspection reconstruction =
            PhysicalStateOracle.InspectObjectReconstruction(
                store,
                ObjectId,
                relocatedBase);
        Assert.Equal(new LogicalObjectState(100, 2), reconstruction.State);
        Assert.Equal([relocatedBase], reconstruction.ReconstructionFrameAddresses);

        Assert.Throws<InvalidDataException>(() =>
            PhysicalStateOracle.InspectObjectLineage(
                store,
                ObjectId,
                relocatedBase));
    }

    [Fact]
    public void Same_version_relocated_base_that_changes_result_fails_lineage() {
        MaintenanceChain chain = CreateMaintenanceChain(includeRelocatedBase: false);
        RbfFile next = chain.Store.CreateFile();
        AbsoluteFrameAddress malformedRelocatedBase = AppendBase(
            next,
            ObjectId,
            payloadBytes: 11,
            logicalVersionOrdinal: 1,
            parentAddress: chain.RelayAddress);

        ObjectReconstructionInspection reconstruction =
            PhysicalStateOracle.InspectObjectReconstruction(
                chain.Store,
                ObjectId,
                malformedRelocatedBase);
        Assert.Equal(new LogicalObjectState(11, 1), reconstruction.State);
        Assert.Equal(
            [malformedRelocatedBase],
            reconstruction.ReconstructionFrameAddresses);

        Assert.Throws<InvalidDataException>(() =>
            PhysicalStateOracle.InspectObjectLineage(
                chain.Store,
                ObjectId,
                malformedRelocatedBase));
    }

    [Fact]
    public void Skipped_and_regressed_logical_versions_fail_closed() {
        (RbfFileStore skippedStore, RbfFile skippedPrevious, AbsoluteFrameAddress skippedRoot) =
            CreateOriginalBase();
        RbfFile skippedCurrent = skippedStore.CreateFile();
        AbsoluteFrameAddress skipped = AppendDelta(
            skippedCurrent,
            ObjectId,
            payloadBytes: 1,
            reconstructionPayloadBytes: 11,
            resultBasePayloadBytes: 11,
            expectedParentBasePayloadBytes: 10,
            logicalVersionOrdinal: 3,
            parentAddress: skippedRoot);

        Assert.Throws<InvalidDataException>(() =>
            PhysicalStateOracle.InspectObjectReconstruction(
                skippedStore,
                ObjectId,
                skipped));

        (RbfFileStore regressedStore, RbfFile regressedPrevious, AbsoluteFrameAddress root) =
            CreateOriginalBase();
        RbfFile middleFile = regressedStore.CreateFile();
        AbsoluteFrameAddress versionTwo = AppendDelta(
            middleFile,
            ObjectId,
            payloadBytes: 1,
            reconstructionPayloadBytes: 11,
            resultBasePayloadBytes: 11,
            expectedParentBasePayloadBytes: 10,
            logicalVersionOrdinal: 2,
            parentAddress: root);
        RbfFile nextFile = regressedStore.CreateFile();
        AbsoluteFrameAddress regressed = AppendBase(
            nextFile,
            ObjectId,
            payloadBytes: 11,
            logicalVersionOrdinal: 1,
            parentAddress: versionTwo);

        Assert.Throws<InvalidDataException>(() =>
            PhysicalStateOracle.InspectObjectLineage(
                regressedStore,
                ObjectId,
                regressed));
    }

    [Fact]
    public void Lineage_inspection_is_repeatable_and_independent_of_frame_insertion_order() {
        IReadOnlyList<LineageSnapshot> forward = CaptureTwoObjectLineages(reverse: false);
        IReadOnlyList<LineageSnapshot> repeatedForward =
            CaptureTwoObjectLineages(reverse: false);
        IReadOnlyList<LineageSnapshot> reverse = CaptureTwoObjectLineages(reverse: true);

        Assert.Equal(forward, repeatedForward);
        Assert.Equal(forward, reverse);
    }

    private static MaintenanceChain CreateMaintenanceChain(
        bool includeRelocatedBase = true) {
        (RbfFileStore store, RbfFile previous, AbsoluteFrameAddress original) =
            CreateOriginalBase();
        RbfFile current = store.CreateFile();
        AbsoluteFrameAddress relay = AppendDelta(
            current,
            ObjectId,
            payloadBytes: 0,
            reconstructionPayloadBytes: 10,
            resultBasePayloadBytes: 10,
            expectedParentBasePayloadBytes: 10,
            logicalVersionOrdinal: 1,
            parentAddress: original);

        AbsoluteFrameAddress relocated = default;
        if (includeRelocatedBase) {
            RbfFile next = store.CreateFile();
            relocated = AppendBase(
                next,
                ObjectId,
                payloadBytes: 10,
                logicalVersionOrdinal: 1,
                parentAddress: relay);
        }

        return new MaintenanceChain(store, original, relay, relocated);
    }

    private static (RbfFileStore Store, RbfFile Previous, AbsoluteFrameAddress Address)
        CreateOriginalBase() {
        RbfFileStore store = new();
        RbfFile previous = store.CreateFile();
        AbsoluteFrameAddress original = AppendBase(
            previous,
            ObjectId,
            payloadBytes: 10,
            logicalVersionOrdinal: 1,
            parentAddress: null);
        return (store, previous, original);
    }

    private static AbsoluteFrameAddress AppendBase(
        RbfFile file,
        uint objectId,
        int payloadBytes,
        int logicalVersionOrdinal,
        AbsoluteFrameAddress? parentAddress) {
        FrameBuilder frame = new();
        ObjectVersionBuilder version = frame.Add(objectId);
        version.Kind = ObjectVersionKind.Base;
        version.PayloadBytes = payloadBytes;
        version.ReconstructionObjectPayloadBytes = payloadBytes;
        version.ResultBasePayloadBytes = payloadBytes;
        version.LogicalVersionOrdinal = logicalVersionOrdinal;
        version.ParentFrameTicket = ToRelative(file, parentAddress);
        FrameTicket ticket = file.Append(frame.Build());
        return new AbsoluteFrameAddress(file.FileNumber, ticket);
    }

    private static AbsoluteFrameAddress AppendDelta(
        RbfFile file,
        uint objectId,
        int payloadBytes,
        long reconstructionPayloadBytes,
        int resultBasePayloadBytes,
        int expectedParentBasePayloadBytes,
        int logicalVersionOrdinal,
        AbsoluteFrameAddress parentAddress) {
        FrameBuilder frame = new();
        ObjectVersionBuilder version = frame.Add(objectId);
        version.Kind = ObjectVersionKind.Delta;
        version.PayloadBytes = payloadBytes;
        version.ReconstructionObjectPayloadBytes = reconstructionPayloadBytes;
        version.ResultBasePayloadBytes = resultBasePayloadBytes;
        version.ExpectedParentBasePayloadBytes = expectedParentBasePayloadBytes;
        version.LogicalVersionOrdinal = logicalVersionOrdinal;
        version.ParentFrameTicket = ToRelative(file, parentAddress);
        FrameTicket ticket = file.Append(frame.Build());
        return new AbsoluteFrameAddress(file.FileNumber, ticket);
    }

    private static RelativeFrameTicket? ToRelative(
        RbfFile file,
        AbsoluteFrameAddress? parentAddress) =>
        parentAddress is null
            ? null
            : new FileScope(file.FileNumber).Relativize(parentAddress.Value);

    private static IReadOnlyList<LineageSnapshot> CaptureTwoObjectLineages(bool reverse) {
        const uint firstObjectId = 11;
        const uint secondObjectId = 22;
        uint[] objectIds = reverse
            ? [secondObjectId, firstObjectId]
            : [firstObjectId, secondObjectId];

        RbfFileStore store = new();
        RbfFile previous = store.CreateFile();
        FrameBuilder bases = new();
        foreach (uint objectId in objectIds) {
            ObjectVersionBuilder version = bases.Add(objectId);
            version.Kind = ObjectVersionKind.Base;
            version.PayloadBytes = checked((int)objectId);
            version.ReconstructionObjectPayloadBytes = objectId;
            version.ResultBasePayloadBytes = checked((int)objectId);
            version.LogicalVersionOrdinal = 1;
        }

        FrameTicket baseTicket = previous.Append(bases.Build());
        AbsoluteFrameAddress baseAddress = new(previous.FileNumber, baseTicket);

        RbfFile current = store.CreateFile();
        FrameBuilder relays = new();
        foreach (uint objectId in objectIds) {
            ObjectVersionBuilder version = relays.Add(objectId);
            version.Kind = ObjectVersionKind.Delta;
            version.PayloadBytes = 0;
            version.ReconstructionObjectPayloadBytes = objectId;
            version.ResultBasePayloadBytes = checked((int)objectId);
            version.ExpectedParentBasePayloadBytes = checked((int)objectId);
            version.LogicalVersionOrdinal = 1;
            version.ParentFrameTicket = new FileScope(current.FileNumber)
                .Relativize(baseAddress);
        }

        FrameTicket relayTicket = current.Append(relays.Build());
        AbsoluteFrameAddress relayAddress = new(current.FileNumber, relayTicket);

        RbfFile next = store.CreateFile();
        FrameBuilder relocatedBases = new();
        foreach (uint objectId in objectIds) {
            ObjectVersionBuilder version = relocatedBases.Add(objectId);
            version.Kind = ObjectVersionKind.Base;
            version.PayloadBytes = checked((int)objectId);
            version.ReconstructionObjectPayloadBytes = objectId;
            version.ResultBasePayloadBytes = checked((int)objectId);
            version.LogicalVersionOrdinal = 1;
            version.ParentFrameTicket = new FileScope(next.FileNumber)
                .Relativize(relayAddress);
        }

        FrameTicket relocatedTicket = next.Append(relocatedBases.Build());
        AbsoluteFrameAddress relocatedAddress = new(next.FileNumber, relocatedTicket);

        return new[] { firstObjectId, secondObjectId }
            .Select(objectId => Snapshot(
                PhysicalStateOracle.InspectObjectLineage(
                    store,
                    objectId,
                    relocatedAddress)))
            .ToArray();
    }

    private static LineageSnapshot Snapshot(ObjectLineageInspection inspection) => new(
        inspection.ObjectId,
        inspection.HeadState,
        inspection.HeadAddress,
        inspection.RootAddress,
        string.Join(",", inspection.HeadToRootFrameAddresses));

    private sealed record MaintenanceChain(
        RbfFileStore Store,
        AbsoluteFrameAddress OriginalBaseAddress,
        AbsoluteFrameAddress RelayAddress,
        AbsoluteFrameAddress RelocatedBaseAddress);

    private sealed record LineageSnapshot(
        uint ObjectId,
        LogicalObjectState HeadState,
        AbsoluteFrameAddress HeadAddress,
        AbsoluteFrameAddress RootAddress,
        string HeadToRootFrameAddresses);
}
