using Atelia.TwoLegRotationProbe.Model;
using Atelia.TwoLegRotationProbe.Simulation;
using Atelia.TwoLegRotationProbe.Workloads;

namespace Atelia.TwoLegRotationProbe.Tests;

public sealed class MaintenanceObjectVersionSemanticsTests {
    private const uint ObjectId = 7;

    [Fact]
    public void Relocated_base_reconstructs_from_C_and_resolves_shared_B_anchor_to_A() {
        RelocationChain chain = CreateRelocationChain();

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
        Assert.Equal([chain.RelocatedBaseAddress], reconstruction.ReconstructionFrameAddresses);
        Assert.Equal(new LogicalObjectState(10, 1), lineage.HeadState);
        Assert.Equal(chain.OriginalBaseAddress, lineage.RootAddress);
        Assert.Equal(
            [chain.RelocatedBaseAddress, chain.OriginalBaseAddress],
            lineage.ObjectVersionLineageAddresses);
        AssertLookup(lineage, [chain.LocatorRevisionAddress, chain.OriginalBaseAddress]);
    }

    [Fact]
    public void Domain_delta_after_relocated_base_advances_logical_version_once() {
        RelocationChain chain = CreateRelocationChain();
        RbfFile next = chain.Store.CreateFile();
        FrameBuilder builder = NewFullRevision(
            Previous(chain.RelocatedBaseAddress.FrameTicket));
        AddDelta(
            builder,
            payloadBytes: 5,
            reconstructionPayloadBytes: 15,
            resultBasePayloadBytes: 15,
            expectedParentBasePayloadBytes: 10,
            logicalVersionOrdinal: 2,
            Previous(chain.RelocatedBaseAddress.FrameTicket));
        AbsoluteFrameAddress domainDelta = Append(next, builder);

        ObjectReconstructionInspection reconstruction =
            PhysicalStateOracle.InspectObjectReconstruction(
                chain.Store,
                ObjectId,
                domainDelta);
        ObjectLineageInspection lineage = PhysicalStateOracle.InspectObjectLineage(
            chain.Store,
            ObjectId,
            domainDelta);

        Assert.Equal(new LogicalObjectState(15, 2), reconstruction.State);
        Assert.Equal(
            [domainDelta, chain.RelocatedBaseAddress],
            reconstruction.ReconstructionFrameAddresses);
        Assert.Equal(
            [domainDelta, chain.RelocatedBaseAddress, chain.OriginalBaseAddress],
            lineage.ObjectVersionLineageAddresses);
        AssertLookup(lineage, [chain.LocatorRevisionAddress, chain.OriginalBaseAddress]);
    }

    [Fact]
    public void Domain_base_after_relocated_base_advances_logical_version_once() {
        RelocationChain chain = CreateRelocationChain();
        RbfFile next = chain.Store.CreateFile();
        FrameBuilder builder = NewFullRevision(
            Previous(chain.RelocatedBaseAddress.FrameTicket));
        AddBase(
            builder,
            payloadBytes: 17,
            logicalVersionOrdinal: 2);
        AbsoluteFrameAddress domainBase = Append(next, builder);

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
        Assert.Equal(
            [domainBase, chain.RelocatedBaseAddress, chain.OriginalBaseAddress],
            lineage.ObjectVersionLineageAddresses);
        Assert.Equal(2, lineage.BaseParentLookups.Count);
        Assert.Equal(
            [chain.RelocatedBaseAddress],
            lineage.BaseParentLookups[0].DictionaryRevisionAddresses);
        Assert.Equal(
            [chain.LocatorRevisionAddress, chain.OriginalBaseAddress],
            lineage.BaseParentLookups[1].DictionaryRevisionAddresses);
    }

    [Fact]
    public void Relocated_base_lineage_rejects_hidden_delta_with_impossible_growth() {
        RbfFileStore store = new();
        RbfFile aFile = store.CreateFile();
        FrameBuilder aBuilder = NewFullRevision();
        AddBase(aBuilder, payloadBytes: 10, logicalVersionOrdinal: 1);
        AbsoluteFrameAddress a = Append(aFile, aBuilder);

        RbfFile bFile = store.CreateFile();
        FrameBuilder bBuilder = NewDeltaRevision(Previous(a.FrameTicket));
        bBuilder.ObjectVersionDictionary!.BindSelf(ObjectId);
        AddDelta(
            bBuilder,
            payloadBytes: 1,
            reconstructionPayloadBytes: 11,
            resultBasePayloadBytes: 100,
            expectedParentBasePayloadBytes: 10,
            logicalVersionOrdinal: 2,
            Previous(a.FrameTicket));
        AbsoluteFrameAddress b = Append(bFile, bBuilder);

        RbfFile cFile = store.CreateFile();
        FrameBuilder cBuilder = NewFullRevision(Previous(b.FrameTicket));
        AddBase(
            cBuilder,
            payloadBytes: 100,
            logicalVersionOrdinal: 2);
        AbsoluteFrameAddress c = Append(cFile, cBuilder);

        ObjectReconstructionInspection reconstruction =
            PhysicalStateOracle.InspectObjectReconstruction(store, ObjectId, c);
        Assert.Equal(new LogicalObjectState(100, 2), reconstruction.State);
        Assert.Equal([c], reconstruction.ReconstructionFrameAddresses);
        Assert.Throws<InvalidDataException>(() =>
            PhysicalStateOracle.InspectObjectLineage(store, ObjectId, c));
    }

    [Fact]
    public void Same_version_relocated_base_that_changes_result_fails_lineage() {
        RelocationChain chain = CreateRelocationChain(includeRelocatedBase: false);
        RbfFile cFile = chain.Store.CreateFile();
        FrameBuilder cBuilder = NewFullRevision(
            Previous(chain.LocatorRevisionAddress.FrameTicket));
        AddBase(
            cBuilder,
            payloadBytes: 11,
            logicalVersionOrdinal: 1);
        AbsoluteFrameAddress malformed = Append(cFile, cBuilder);

        ObjectReconstructionInspection reconstruction =
            PhysicalStateOracle.InspectObjectReconstruction(
                chain.Store,
                ObjectId,
                malformed);
        Assert.Equal(new LogicalObjectState(11, 1), reconstruction.State);
        Assert.Equal([malformed], reconstruction.ReconstructionFrameAddresses);
        Assert.Throws<InvalidDataException>(() =>
            PhysicalStateOracle.InspectObjectLineage(chain.Store, ObjectId, malformed));
    }

    [Fact]
    public void Skipped_delta_and_regressed_base_logical_versions_fail_closed() {
        RbfFileStore skippedStore = new();
        RbfFile skippedAFile = skippedStore.CreateFile();
        FrameBuilder skippedABuilder = NewFullRevision();
        AddBase(skippedABuilder, payloadBytes: 10, logicalVersionOrdinal: 1);
        AbsoluteFrameAddress skippedA = Append(skippedAFile, skippedABuilder);
        RbfFile skippedBFile = skippedStore.CreateFile();
        FrameBuilder skippedBBuilder = NewFullRevision(Previous(skippedA.FrameTicket));
        AddDelta(
            skippedBBuilder,
            payloadBytes: 1,
            reconstructionPayloadBytes: 11,
            resultBasePayloadBytes: 11,
            expectedParentBasePayloadBytes: 10,
            logicalVersionOrdinal: 3,
            Previous(skippedA.FrameTicket));
        AbsoluteFrameAddress skipped = Append(skippedBFile, skippedBBuilder);

        Assert.Throws<InvalidDataException>(() =>
            PhysicalStateOracle.InspectObjectReconstruction(skippedStore, ObjectId, skipped));

        RbfFileStore regressedStore = new();
        RbfFile regressedAFile = regressedStore.CreateFile();
        FrameBuilder regressedABuilder = NewFullRevision();
        AddBase(regressedABuilder, payloadBytes: 10, logicalVersionOrdinal: 1);
        AbsoluteFrameAddress regressedA = Append(regressedAFile, regressedABuilder);
        RbfFile regressedBFile = regressedStore.CreateFile();
        FrameBuilder regressedBBuilder = NewDeltaRevision(Previous(regressedA.FrameTicket));
        regressedBBuilder.ObjectVersionDictionary!.BindSelf(ObjectId);
        AddDelta(
            regressedBBuilder,
            payloadBytes: 1,
            reconstructionPayloadBytes: 11,
            resultBasePayloadBytes: 11,
            expectedParentBasePayloadBytes: 10,
            logicalVersionOrdinal: 2,
            Previous(regressedA.FrameTicket));
        AbsoluteFrameAddress regressedB = Append(regressedBFile, regressedBBuilder);
        RbfFile regressedCFile = regressedStore.CreateFile();
        FrameBuilder regressedCBuilder = NewFullRevision(Previous(regressedB.FrameTicket));
        AddBase(
            regressedCBuilder,
            payloadBytes: 11,
            logicalVersionOrdinal: 1);
        AbsoluteFrameAddress regressed = Append(regressedCFile, regressedCBuilder);

        Assert.Throws<InvalidDataException>(() =>
            PhysicalStateOracle.InspectObjectLineage(regressedStore, ObjectId, regressed));
    }

    private static RelocationChain CreateRelocationChain(bool includeRelocatedBase = true) {
        RbfFileStore store = new();
        RbfFile aFile = store.CreateFile();
        FrameBuilder aBuilder = NewFullRevision();
        AddBase(aBuilder, payloadBytes: 10, logicalVersionOrdinal: 1);
        AbsoluteFrameAddress a = Append(aFile, aBuilder);

        RbfFile bFile = store.CreateFile();
        FrameBuilder bBuilder = NewDeltaRevision(Previous(a.FrameTicket));
        AbsoluteFrameAddress b = Append(bFile, bBuilder);
        if (!includeRelocatedBase) {
            return new RelocationChain(store, a, b, default);
        }

        RbfFile cFile = store.CreateFile();
        FrameBuilder cBuilder = NewFullRevision(Previous(b.FrameTicket));
        AddBase(
            cBuilder,
            payloadBytes: 10,
            logicalVersionOrdinal: 1);
        AbsoluteFrameAddress c = Append(cFile, cBuilder);
        return new RelocationChain(store, a, b, c);
    }

    private static FrameBuilder NewFullRevision(
        RelativeFrameTicket? priorSnapshot = null) {
        ObjectVersionDictionaryBuilder dictionary = new() {
            ParentRevisionFrameTicket = priorSnapshot,
        };
        dictionary.BindSelf(ObjectId);
        return new FrameBuilder { ObjectVersionDictionary = dictionary };
    }

    private static FrameBuilder NewDeltaRevision(RelativeFrameTicket parent) => new() {
        ObjectVersionDictionary = new ObjectVersionDictionaryBuilder {
            Kind = ObjectVersionDictionaryKind.Delta,
            ParentRevisionFrameTicket = parent,
        },
    };

    private static void AddBase(
        FrameBuilder builder,
        int payloadBytes,
        int logicalVersionOrdinal) {
        ObjectVersionBuilder version = builder.Add(ObjectId);
        version.Kind = ObjectVersionKind.Base;
        version.PayloadBytes = payloadBytes;
        version.ReconstructionObjectPayloadBytes = payloadBytes;
        version.ResultBasePayloadBytes = payloadBytes;
        version.LogicalVersionOrdinal = logicalVersionOrdinal;
    }

    private static void AddDelta(
        FrameBuilder builder,
        int payloadBytes,
        long reconstructionPayloadBytes,
        int resultBasePayloadBytes,
        int expectedParentBasePayloadBytes,
        int logicalVersionOrdinal,
        RelativeFrameTicket parent) {
        ObjectVersionBuilder version = builder.Add(ObjectId);
        version.Kind = ObjectVersionKind.Delta;
        version.PayloadBytes = payloadBytes;
        version.ReconstructionObjectPayloadBytes = reconstructionPayloadBytes;
        version.ResultBasePayloadBytes = resultBasePayloadBytes;
        version.ExpectedParentBasePayloadBytes = expectedParentBasePayloadBytes;
        version.LogicalVersionOrdinal = logicalVersionOrdinal;
        version.DeltaParentFrameTicket = parent;
    }

    private static AbsoluteFrameAddress Append(RbfFile file, FrameBuilder builder) =>
        new(file.FileNumber, file.Append(builder.Build()));

    private static RelativeFrameTicket Previous(FrameTicket ticket) =>
        new(IsPreviousFile: true, ticket);

    private static void AssertLookup(
        ObjectLineageInspection lineage,
        AbsoluteFrameAddress[] expectedRevisionReads) {
        ObjectVersionDictionaryLookupInspection lookup =
            Assert.Single(lineage.BaseParentLookups);
        Assert.Equal(ObjectVersionDictionaryLookupDisposition.Found, lookup.Disposition);
        Assert.Equal(expectedRevisionReads, lookup.DictionaryRevisionAddresses);
    }

    private sealed record RelocationChain(
        RbfFileStore Store,
        AbsoluteFrameAddress OriginalBaseAddress,
        AbsoluteFrameAddress LocatorRevisionAddress,
        AbsoluteFrameAddress RelocatedBaseAddress);
}
