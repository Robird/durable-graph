using Atelia.TwoLegRotationProbe.Model;
using Atelia.TwoLegRotationProbe.Simulation;
using Atelia.TwoLegRotationProbe.Workloads;

namespace Atelia.TwoLegRotationProbe.Tests;

public sealed class BaseLineageParentLocatorTests {
    private const uint AaObjectId = 1;
    private const uint BaObjectId = 2;
    private const uint BbObjectId = 3;

    [Fact]
    public void Shared_B_anchor_preserves_canonical_AA_and_BA_exact_lineage() {
        CanonicalLayout layout = BuildCanonicalLayout(reverseEntryOrder: false);

        AssertCurrentReconstructionReadsOnlyC(layout);
        AssertBbResolvesExternallyFromC(layout);

        ObjectLineageInspection aa = Inspect(layout, AaObjectId);
        ObjectLineageInspection ba = Inspect(layout, BaObjectId);

        Assert.Equal(new LogicalObjectState(10, 1), aa.HeadState);
        Assert.Equal(layout.A, aa.RootAddress);
        Assert.Equal([layout.C, layout.A], aa.ObjectVersionLineageAddresses);
        AssertLookup(
            Assert.Single(aa.BaseParentLookups),
            ObjectVersionDictionaryLookupDisposition.Found,
            [layout.B, layout.A]);

        Assert.Equal(new LogicalObjectState(22, 2), ba.HeadState);
        Assert.Equal(layout.A, ba.RootAddress);
        Assert.Equal([layout.C, layout.B, layout.A], ba.ObjectVersionLineageAddresses);
        AssertLookup(
            Assert.Single(ba.BaseParentLookups),
            ObjectVersionDictionaryLookupDisposition.Found,
            [layout.B]);
    }

    [Fact]
    public void One_shared_anchor_accepts_new_domain_relocated_and_delta_versions() {
        const uint relocatedId = 10;
        const uint domainBaseId = 11;
        const uint deltaId = 12;
        const uint newId = 13;

        RbfFileStore store = new();
        RbfFile aFile = store.CreateFile();
        ObjectVersionDictionaryBuilder aDictionary = new();
        aDictionary.BindSelf(relocatedId);
        aDictionary.BindSelf(domainBaseId);
        aDictionary.BindSelf(deltaId);
        FrameBuilder aBuilder = new() { ObjectVersionDictionary = aDictionary };
        AddBase(aBuilder, relocatedId, payloadBytes: 10, logicalVersionOrdinal: 1);
        AddBase(aBuilder, domainBaseId, payloadBytes: 20, logicalVersionOrdinal: 1);
        AddBase(aBuilder, deltaId, payloadBytes: 30, logicalVersionOrdinal: 1);
        AbsoluteFrameAddress a = Append(aFile, aBuilder);

        RbfFile bFile = store.CreateFile();
        ObjectVersionDictionaryBuilder bDictionary = new() {
            Kind = ObjectVersionDictionaryKind.Delta,
            ParentRevisionFrameTicket = Previous(a.FrameTicket),
        };
        bDictionary.BindSelf(deltaId);
        FrameBuilder bBuilder = new() { ObjectVersionDictionary = bDictionary };
        AddBase(bBuilder, deltaId, payloadBytes: 30, logicalVersionOrdinal: 1);
        AbsoluteFrameAddress b = Append(bFile, bBuilder);

        RbfFile cFile = store.CreateFile();
        ObjectVersionDictionaryBuilder cDictionary = new() {
            Kind = ObjectVersionDictionaryKind.Base,
            ParentRevisionFrameTicket = Previous(b.FrameTicket),
        };
        cDictionary.BindSelf(relocatedId);
        cDictionary.BindSelf(domainBaseId);
        cDictionary.BindSelf(deltaId);
        cDictionary.BindSelf(newId);
        FrameBuilder cBuilder = new() { ObjectVersionDictionary = cDictionary };
        AddBase(cBuilder, relocatedId, payloadBytes: 10, logicalVersionOrdinal: 1);
        AddBase(cBuilder, domainBaseId, payloadBytes: 25, logicalVersionOrdinal: 2);
        AddDelta(
            cBuilder,
            deltaId,
            payloadBytes: 3,
            reconstructionPayloadBytes: 33,
            resultBasePayloadBytes: 33,
            expectedParentBasePayloadBytes: 30,
            logicalVersionOrdinal: 2,
            Previous(b.FrameTicket));
        AddBase(cBuilder, newId, payloadBytes: 5, logicalVersionOrdinal: 1);
        AbsoluteFrameAddress c = Append(cFile, cBuilder);

        ObjectLineageInspection relocated = Inspect(store, c, relocatedId);
        Assert.Equal([c, a], relocated.ObjectVersionLineageAddresses);
        AssertLookup(
            Assert.Single(relocated.BaseParentLookups),
            ObjectVersionDictionaryLookupDisposition.Found,
            [b, a]);

        ObjectLineageInspection domainBase = Inspect(store, c, domainBaseId);
        Assert.Equal(new LogicalObjectState(25, 2), domainBase.HeadState);
        Assert.Equal([c, a], domainBase.ObjectVersionLineageAddresses);
        AssertLookup(
            Assert.Single(domainBase.BaseParentLookups),
            ObjectVersionDictionaryLookupDisposition.Found,
            [b, a]);

        ObjectLineageInspection delta = Inspect(store, c, deltaId);
        Assert.Equal(new LogicalObjectState(33, 2), delta.HeadState);
        Assert.Equal([c, b, a], delta.ObjectVersionLineageAddresses);
        AssertLookup(
            Assert.Single(delta.BaseParentLookups),
            ObjectVersionDictionaryLookupDisposition.Found,
            [a]);

        ObjectLineageInspection newlyCreated = Inspect(store, c, newId);
        Assert.Equal(c, newlyCreated.RootAddress);
        Assert.Equal([c], newlyCreated.ObjectVersionLineageAddresses);
        AssertLookup(
            Assert.Single(newlyCreated.BaseParentLookups),
            ObjectVersionDictionaryLookupDisposition.AbsentAtBase,
            [b, a]);
    }

    [Fact]
    public void Genesis_and_absent_prior_snapshot_bindings_form_only_V1_roots() {
        RbfFileStore genesisStore = new();
        RbfFile genesisFile = genesisStore.CreateFile();
        FrameBuilder genesisBuilder = NewBaseRevision(AaObjectId);
        AddBase(
            genesisBuilder,
            AaObjectId,
            payloadBytes: 10,
            logicalVersionOrdinal: 1);
        AbsoluteFrameAddress genesis = Append(genesisFile, genesisBuilder);

        ObjectLineageInspection genesisLineage = Inspect(
            genesisStore,
            genesis,
            AaObjectId);
        Assert.Equal(genesis, genesisLineage.RootAddress);
        Assert.Empty(genesisLineage.BaseParentLookups);

        CanonicalLayout absentV1 = BuildAbsentPriorLayout(logicalVersionOrdinal: 1);
        ObjectLineageInspection absentLineage = Inspect(absentV1, AaObjectId);
        Assert.Equal(absentV1.C, absentLineage.RootAddress);
        AssertLookup(
            Assert.Single(absentLineage.BaseParentLookups),
            ObjectVersionDictionaryLookupDisposition.AbsentAtBase,
            [absentV1.B]);

        CanonicalLayout absentV2 = BuildAbsentPriorLayout(logicalVersionOrdinal: 2);
        AssertHeadStillReconstructs(absentV2, AaObjectId, expectedBytes: 10);
        Assert.Throws<InvalidDataException>(() => Inspect(absentV2, AaObjectId));
    }

    [Fact]
    public void Visible_remove_rejects_installed_base_but_not_current_reconstruction() {
        CanonicalLayout removed = BuildCanonicalLayout(
            reverseEntryOrder: false,
            removeAaInB: true);

        AssertHeadStillReconstructs(removed, AaObjectId, expectedBytes: 10);
        Assert.Throws<InvalidDataException>(() => Inspect(removed, AaObjectId));
    }

    [Fact]
    public void Null_missing_non_earlier_and_absent_shared_anchors_fail_lineage_only() {
        CanonicalLayout nullDictionary = BuildMalformedAnchorLayout(
            AnchorKind.NullDictionary);
        CanonicalLayout missing = BuildMalformedAnchorLayout(AnchorKind.Missing);
        CanonicalLayout nonEarlier = BuildMalformedAnchorLayout(AnchorKind.NonEarlier);
        CanonicalLayout absentAnchor = BuildMalformedAnchorLayout(AnchorKind.Absent);

        foreach (CanonicalLayout layout in
            new[] { nullDictionary, missing, nonEarlier, absentAnchor }) {
            AssertHeadStillReconstructs(layout, AaObjectId, expectedBytes: 10);
            Assert.Throws<InvalidDataException>(() => Inspect(layout, AaObjectId));
        }
    }

    [Fact]
    public void Shared_anchor_inspection_is_frozen_repeatable_and_entry_order_independent() {
        CanonicalLayout forward = BuildCanonicalLayout(reverseEntryOrder: false);
        CanonicalLayout reverse = BuildCanonicalLayout(reverseEntryOrder: true);

        ObjectLineageInspection first = Inspect(forward, BaObjectId);
        ObjectLineageInspection repeated = Inspect(forward, BaObjectId);
        ObjectLineageInspection reordered = Inspect(reverse, BaObjectId);

        AssertEquivalent(first, repeated);
        AssertEquivalent(first, reordered);

        IList<AbsoluteFrameAddress> exactAddresses =
            Assert.IsAssignableFrom<IList<AbsoluteFrameAddress>>(
                first.ObjectVersionLineageAddresses);
        Assert.True(exactAddresses.IsReadOnly);
        Assert.Throws<NotSupportedException>(() => exactAddresses.Add(forward.A));

        IList<ObjectVersionDictionaryLookupInspection> lookups =
            Assert.IsAssignableFrom<IList<ObjectVersionDictionaryLookupInspection>>(
                first.BaseParentLookups);
        Assert.True(lookups.IsReadOnly);
        Assert.Throws<NotSupportedException>(() => lookups.Add(first.BaseParentLookups[0]));
    }

    private static CanonicalLayout BuildCanonicalLayout(
        bool reverseEntryOrder,
        bool removeAaInB = false) {
        RbfFileStore store = new();
        RbfFile aFile = store.CreateFile();
        ObjectVersionDictionaryBuilder aDictionary = new();
        BindSelf(aDictionary, reverseEntryOrder, AaObjectId, BaObjectId);
        FrameBuilder aBuilder = new() { ObjectVersionDictionary = aDictionary };
        AddBase(aBuilder, AaObjectId, payloadBytes: 10, logicalVersionOrdinal: 1);
        AddBase(aBuilder, BaObjectId, payloadBytes: 20, logicalVersionOrdinal: 1);
        AbsoluteFrameAddress a = Append(aFile, aBuilder);

        RbfFile bFile = store.CreateFile();
        ObjectVersionDictionaryBuilder bDictionary = new() {
            Kind = ObjectVersionDictionaryKind.Delta,
            ParentRevisionFrameTicket = Previous(a.FrameTicket),
        };
        BindSelf(bDictionary, reverseEntryOrder, BaObjectId, BbObjectId);
        if (removeAaInB) {
            bDictionary.Remove(AaObjectId);
        }

        FrameBuilder bBuilder = new() { ObjectVersionDictionary = bDictionary };
        AddDelta(
            bBuilder,
            BaObjectId,
            payloadBytes: 2,
            reconstructionPayloadBytes: 22,
            resultBasePayloadBytes: 22,
            expectedParentBasePayloadBytes: 20,
            logicalVersionOrdinal: 2,
            Previous(a.FrameTicket));
        AddBase(bBuilder, BbObjectId, payloadBytes: 30, logicalVersionOrdinal: 1);
        AbsoluteFrameAddress b = Append(bFile, bBuilder);

        RbfFile cFile = store.CreateFile();
        ObjectVersionDictionaryBuilder cDictionary = new() {
            Kind = ObjectVersionDictionaryKind.Base,
            ParentRevisionFrameTicket = Previous(b.FrameTicket),
        };
        if (reverseEntryOrder) {
            cDictionary.BindExternal(BbObjectId, Previous(b.FrameTicket));
            cDictionary.BindSelf(BaObjectId);
            cDictionary.BindSelf(AaObjectId);
        } else {
            cDictionary.BindSelf(AaObjectId);
            cDictionary.BindSelf(BaObjectId);
            cDictionary.BindExternal(BbObjectId, Previous(b.FrameTicket));
        }

        FrameBuilder cBuilder = new() { ObjectVersionDictionary = cDictionary };
        AddBase(cBuilder, AaObjectId, payloadBytes: 10, logicalVersionOrdinal: 1);
        AddBase(cBuilder, BaObjectId, payloadBytes: 22, logicalVersionOrdinal: 2);
        AbsoluteFrameAddress c = Append(cFile, cBuilder);

        return new CanonicalLayout(store, a, b, c);
    }

    private static CanonicalLayout BuildAbsentPriorLayout(int logicalVersionOrdinal) {
        RbfFileStore store = new();
        RbfFile priorFile = store.CreateFile();
        AbsoluteFrameAddress prior = Append(
            priorFile,
            new FrameBuilder {
                ObjectVersionDictionary = new ObjectVersionDictionaryBuilder(),
            });

        RbfFile currentFile = store.CreateFile();
        FrameBuilder currentBuilder = NewBaseRevision(
            AaObjectId,
            Previous(prior.FrameTicket));
        AddBase(
            currentBuilder,
            AaObjectId,
            payloadBytes: 10,
            logicalVersionOrdinal);
        AbsoluteFrameAddress current = Append(currentFile, currentBuilder);
        return new CanonicalLayout(store, prior, prior, current);
    }

    private static CanonicalLayout BuildMalformedAnchorLayout(AnchorKind anchorKind) {
        RbfFileStore store = new();
        RbfFile aFile = store.CreateFile();
        AbsoluteFrameAddress a = Append(aFile, new FrameBuilder());
        RbfFile bFile = store.CreateFile();
        AbsoluteFrameAddress b = Append(bFile, new FrameBuilder());
        RbfFile cFile = store.CreateFile();

        FrameBuilder cBuilder;
        if (anchorKind == AnchorKind.Absent) {
            cBuilder = NewBaseRevision(AaObjectId);
        } else {
            RelativeFrameTicket anchor = anchorKind switch {
                AnchorKind.NullDictionary => Previous(b.FrameTicket),
                AnchorKind.Missing => Previous(new FrameTicket(100, 24)),
                AnchorKind.NonEarlier => Current(new FrameTicket(4, 24)),
                _ => throw new ArgumentOutOfRangeException(nameof(anchorKind)),
            };
            cBuilder = NewBaseRevision(AaObjectId, anchor);
        }

        AddBase(
            cBuilder,
            AaObjectId,
            payloadBytes: 10,
            logicalVersionOrdinal: 2);
        AbsoluteFrameAddress c = Append(cFile, cBuilder);
        return new CanonicalLayout(store, a, b, c);
    }

    private static FrameBuilder NewBaseRevision(
        uint objectId,
        RelativeFrameTicket? priorSnapshot = null) {
        ObjectVersionDictionaryBuilder dictionary = new() {
            Kind = ObjectVersionDictionaryKind.Base,
            ParentRevisionFrameTicket = priorSnapshot,
        };
        dictionary.BindSelf(objectId);
        return new FrameBuilder { ObjectVersionDictionary = dictionary };
    }

    private static void AssertCurrentReconstructionReadsOnlyC(CanonicalLayout layout) {
        AssertHeadStillReconstructs(layout, AaObjectId, expectedBytes: 10);
        AssertHeadStillReconstructs(layout, BaObjectId, expectedBytes: 22);
    }

    private static void AssertHeadStillReconstructs(
        CanonicalLayout layout,
        uint objectId,
        int expectedBytes) {
        AbsoluteFrameAddress currentHead = LookupCurrentHead(layout, objectId);
        ObjectReconstructionInspection reconstruction =
            PhysicalStateOracle.InspectObjectReconstruction(
                layout.Store,
                objectId,
                currentHead);
        Assert.Equal(expectedBytes, reconstruction.State.BasePayloadBytes);
        Assert.Equal([currentHead], reconstruction.ReconstructionFrameAddresses);
    }

    private static ObjectLineageInspection Inspect(
        CanonicalLayout layout,
        uint objectId) {
        AbsoluteFrameAddress currentHead = LookupCurrentHead(layout, objectId);
        return PhysicalStateOracle.InspectObjectLineage(
            layout.Store,
            objectId,
            currentHead);
    }

    private static ObjectLineageInspection Inspect(
        RbfFileStore store,
        AbsoluteFrameAddress head,
        uint objectId) =>
        PhysicalStateOracle.InspectObjectLineage(store, objectId, head);

    private static AbsoluteFrameAddress LookupCurrentHead(
        CanonicalLayout layout,
        uint objectId) {
        ObjectVersionDictionaryLookupInspection lookup =
            ObjectVersionDictionaryReader.LookupLive(layout.Store, layout.C, objectId);
        Assert.Equal(ObjectVersionDictionaryLookupDisposition.Found, lookup.Disposition);
        Assert.Equal([layout.C], lookup.DictionaryRevisionAddresses);
        return Assert.IsType<AbsoluteFrameAddress>(lookup.ResolvedObjectVersionAddress);
    }

    private static void AssertBbResolvesExternallyFromC(CanonicalLayout layout) {
        AbsoluteFrameAddress currentHead = LookupCurrentHead(layout, BbObjectId);
        Assert.Equal(layout.B, currentHead);

        ObjectReconstructionInspection reconstruction =
            PhysicalStateOracle.InspectObjectReconstruction(
                layout.Store,
                BbObjectId,
                currentHead);
        Assert.Equal(new LogicalObjectState(30, 1), reconstruction.State);
        Assert.Equal([layout.B], reconstruction.ReconstructionFrameAddresses);
    }

    private static void AssertLookup(
        ObjectVersionDictionaryLookupInspection lookup,
        ObjectVersionDictionaryLookupDisposition expectedDisposition,
        AbsoluteFrameAddress[] expectedRevisionReads) {
        Assert.Equal(expectedDisposition, lookup.Disposition);
        Assert.Equal(expectedRevisionReads, lookup.DictionaryRevisionAddresses);
    }

    private static void AssertEquivalent(
        ObjectLineageInspection expected,
        ObjectLineageInspection actual) {
        Assert.Equal(expected.ObjectId, actual.ObjectId);
        Assert.Equal(expected.HeadState, actual.HeadState);
        Assert.Equal(expected.HeadAddress, actual.HeadAddress);
        Assert.Equal(expected.RootAddress, actual.RootAddress);
        Assert.Equal(
            expected.ObjectVersionLineageAddresses,
            actual.ObjectVersionLineageAddresses);
        Assert.Equal(expected.BaseParentLookups.Count, actual.BaseParentLookups.Count);
        for (int index = 0; index < expected.BaseParentLookups.Count; index++) {
            Assert.Equal(
                expected.BaseParentLookups[index].Disposition,
                actual.BaseParentLookups[index].Disposition);
            Assert.Equal(
                expected.BaseParentLookups[index].DictionaryRevisionAddresses,
                actual.BaseParentLookups[index].DictionaryRevisionAddresses);
            Assert.Equal(
                expected.BaseParentLookups[index].ResolvedObjectVersionAddress,
                actual.BaseParentLookups[index].ResolvedObjectVersionAddress);
        }
    }

    private static void BindSelf(
        ObjectVersionDictionaryBuilder dictionary,
        bool reverse,
        uint first,
        uint second) {
        if (reverse) {
            dictionary.BindSelf(second);
            dictionary.BindSelf(first);
        } else {
            dictionary.BindSelf(first);
            dictionary.BindSelf(second);
        }
    }

    private static void AddBase(
        FrameBuilder builder,
        uint objectId,
        int payloadBytes,
        int logicalVersionOrdinal) {
        ObjectVersionBuilder version = builder.Add(objectId);
        version.Kind = ObjectVersionKind.Base;
        version.PayloadBytes = payloadBytes;
        version.ReconstructionObjectPayloadBytes = payloadBytes;
        version.ResultBasePayloadBytes = payloadBytes;
        version.LogicalVersionOrdinal = logicalVersionOrdinal;
    }

    private static void AddDelta(
        FrameBuilder builder,
        uint objectId,
        int payloadBytes,
        long reconstructionPayloadBytes,
        int resultBasePayloadBytes,
        int expectedParentBasePayloadBytes,
        int logicalVersionOrdinal,
        RelativeFrameTicket parent) {
        ObjectVersionBuilder version = builder.Add(objectId);
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

    private static RelativeFrameTicket Current(FrameTicket ticket) =>
        new(IsPreviousFile: false, ticket);

    private sealed record CanonicalLayout(
        RbfFileStore Store,
        AbsoluteFrameAddress A,
        AbsoluteFrameAddress B,
        AbsoluteFrameAddress C);

    private enum AnchorKind {
        NullDictionary,
        Missing,
        NonEarlier,
        Absent,
    }
}
