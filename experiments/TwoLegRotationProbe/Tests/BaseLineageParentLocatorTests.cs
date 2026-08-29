using Atelia.TwoLegRotationProbe.Model;
using Atelia.TwoLegRotationProbe.Simulation;
using Atelia.TwoLegRotationProbe.Workloads;

namespace Atelia.TwoLegRotationProbe.Tests;

public sealed class BaseLineageParentLocatorTests {
    private const uint AaObjectId = 1;
    private const uint BaObjectId = 2;
    private const uint BbObjectId = 3;

    [Fact]
    public void Hybrid_relay_and_relay_free_layouts_preserve_heads_and_roots_but_not_exact_hops() {
        CanonicalLayout hybrid = BuildCanonicalLayout(
            includeRelay: true,
            bindAaInRelay: true,
            reverseEntryOrder: false);
        CanonicalLayout relayFree = BuildCanonicalLayout(
            includeRelay: false,
            bindAaInRelay: false,
            reverseEntryOrder: false);

        AssertCurrentReconstructionReadsOnlyC(hybrid);
        AssertCurrentReconstructionReadsOnlyC(relayFree);
        AssertBbResolvesExternallyFromC(hybrid);
        AssertBbResolvesExternallyFromC(relayFree);

        RevisionLocatorLineageInspection hybridAa = Inspect(hybrid, AaObjectId);
        RevisionLocatorLineageInspection relayFreeAa = Inspect(relayFree, AaObjectId);
        RevisionLocatorLineageInspection hybridBa = Inspect(hybrid, BaObjectId);
        RevisionLocatorLineageInspection relayFreeBa = Inspect(relayFree, BaObjectId);

        Assert.Equal(relayFreeAa.HeadState, hybridAa.HeadState);
        Assert.Equal(relayFreeAa.RootAddress, hybridAa.RootAddress);
        Assert.Equal(relayFreeBa.HeadState, hybridBa.HeadState);
        Assert.Equal(relayFreeBa.RootAddress, hybridBa.RootAddress);

        Assert.Equal(
            [hybrid.C, hybrid.Relay!.Value, hybrid.A],
            hybridAa.ObjectVersionLineageAddresses);
        AssertLookup(hybridAa, [hybrid.Relay.Value]);
        Assert.Equal(
            [relayFree.C, relayFree.A],
            relayFreeAa.ObjectVersionLineageAddresses);
        AssertLookup(relayFreeAa, [relayFree.B, relayFree.A]);

        Assert.Equal(
            [hybrid.C, hybrid.B, hybrid.A],
            hybridBa.ObjectVersionLineageAddresses);
        AssertLookup(hybridBa, [hybrid.Relay.Value, hybrid.B]);
        Assert.Equal(
            [relayFree.C, relayFree.B, relayFree.A],
            relayFreeBa.ObjectVersionLineageAddresses);
        AssertLookup(relayFreeBa, [relayFree.B]);
    }

    [Fact]
    public void Relay_record_without_an_OVD_self_binding_is_skipped() {
        CanonicalLayout layout = BuildCanonicalLayout(
            includeRelay: true,
            bindAaInRelay: false,
            reverseEntryOrder: false);

        RevisionLocatorLineageInspection lineage = Inspect(layout, AaObjectId);

        Assert.Equal([layout.C, layout.A], lineage.ObjectVersionLineageAddresses);
        AssertLookup(lineage, [layout.Relay!.Value, layout.B, layout.A]);
        Assert.DoesNotContain(layout.Relay.Value, lineage.ObjectVersionLineageAddresses);
    }

    [Fact]
    public void Removed_and_absent_locator_results_fail_lineage_but_not_reconstruction() {
        CanonicalLayout removed = BuildCanonicalLayout(
            includeRelay: true,
            bindAaInRelay: false,
            reverseEntryOrder: false,
            removeAaInRelay: true);
        CanonicalLayout absent = BuildAbsentLocatorLayout();

        AssertHeadStillReconstructs(removed, AaObjectId, expectedBytes: 10);
        AssertHeadStillReconstructs(absent, AaObjectId, expectedBytes: 10);

        Assert.Throws<InvalidDataException>(() => Inspect(removed, AaObjectId));
        Assert.Throws<InvalidDataException>(() => Inspect(absent, AaObjectId));
    }

    [Fact]
    public void Null_missing_and_non_earlier_locator_frames_fail_lineage_but_not_reconstruction() {
        CanonicalLayout nullDictionary = BuildMalformedLocatorLayout(
            locatorKind: LocatorKind.NullDictionary);
        CanonicalLayout missing = BuildMalformedLocatorLayout(locatorKind: LocatorKind.Missing);
        CanonicalLayout nonEarlier = BuildMalformedLocatorLayout(
            locatorKind: LocatorKind.NonEarlier);

        AssertHeadStillReconstructs(nullDictionary, AaObjectId, expectedBytes: 10);
        AssertHeadStillReconstructs(missing, AaObjectId, expectedBytes: 10);
        AssertHeadStillReconstructs(nonEarlier, AaObjectId, expectedBytes: 10);

        Assert.Throws<InvalidDataException>(() => Inspect(nullDictionary, AaObjectId));
        Assert.Throws<InvalidDataException>(() => Inspect(missing, AaObjectId));
        Assert.Throws<InvalidDataException>(() => Inspect(nonEarlier, AaObjectId));
    }

    [Fact]
    public void Revision_locator_inspection_is_frozen_repeatable_and_entry_order_independent() {
        CanonicalLayout forward = BuildCanonicalLayout(
            includeRelay: true,
            bindAaInRelay: true,
            reverseEntryOrder: false);
        CanonicalLayout reverse = BuildCanonicalLayout(
            includeRelay: true,
            bindAaInRelay: true,
            reverseEntryOrder: true);

        RevisionLocatorLineageInspection first = Inspect(forward, BaObjectId);
        RevisionLocatorLineageInspection repeated = Inspect(forward, BaObjectId);
        RevisionLocatorLineageInspection reordered = Inspect(reverse, BaObjectId);

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
        bool includeRelay,
        bool bindAaInRelay,
        bool reverseEntryOrder,
        bool removeAaInRelay = false) {
        RbfFileStore store = new();
        RbfFile aFile = store.CreateFile();
        ObjectVersionDictionaryBuilder aDictionary = new();
        BindSelf(aDictionary, reverseEntryOrder, AaObjectId, BaObjectId);
        FrameBuilder aBuilder = new() { ObjectVersionDictionary = aDictionary };
        AddBase(aBuilder, AaObjectId, payloadBytes: 10, logicalVersionOrdinal: 1, null);
        AddBase(aBuilder, BaObjectId, payloadBytes: 20, logicalVersionOrdinal: 1, null);
        AbsoluteFrameAddress a = Append(aFile, aBuilder);

        RbfFile bFile = store.CreateFile();
        ObjectVersionDictionaryBuilder bDictionary = new() {
            Kind = ObjectVersionDictionaryKind.Delta,
            ParentRevisionFrameTicket = Previous(a.FrameTicket),
        };
        BindSelf(bDictionary, reverseEntryOrder, BaObjectId, BbObjectId);
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
        AddBase(bBuilder, BbObjectId, payloadBytes: 30, logicalVersionOrdinal: 1, null);
        AbsoluteFrameAddress b = Append(bFile, bBuilder);

        AbsoluteFrameAddress? relay = null;
        if (includeRelay) {
            ObjectVersionDictionaryBuilder relayDictionary = new() {
                Kind = ObjectVersionDictionaryKind.Delta,
                ParentRevisionFrameTicket = Current(b.FrameTicket),
            };
            if (bindAaInRelay) {
                relayDictionary.BindSelf(AaObjectId);
            } else if (removeAaInRelay) {
                relayDictionary.Remove(AaObjectId);
            }

            FrameBuilder relayBuilder = new() {
                ObjectVersionDictionary = relayDictionary,
            };
            AddDelta(
                relayBuilder,
                AaObjectId,
                payloadBytes: 0,
                reconstructionPayloadBytes: 10,
                resultBasePayloadBytes: 10,
                expectedParentBasePayloadBytes: 10,
                logicalVersionOrdinal: 1,
                Previous(a.FrameTicket));
            relay = Append(bFile, relayBuilder);
        }

        RbfFile cFile = store.CreateFile();
        RelativeFrameTicket locator = Previous((relay ?? b).FrameTicket);
        ObjectVersionDictionaryBuilder cDictionary = new() {
            Kind = ObjectVersionDictionaryKind.Base,
            ParentRevisionFrameTicket = locator,
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
        AddBase(cBuilder, AaObjectId, payloadBytes: 10, logicalVersionOrdinal: 1, locator);
        AddBase(cBuilder, BaObjectId, payloadBytes: 22, logicalVersionOrdinal: 2, locator);
        AbsoluteFrameAddress c = Append(cFile, cBuilder);

        return new CanonicalLayout(store, a, b, relay, c);
    }

    private static CanonicalLayout BuildAbsentLocatorLayout() {
        RbfFileStore store = new();
        RbfFile aFile = store.CreateFile();
        FrameBuilder aBuilder = new() {
            ObjectVersionDictionary = new ObjectVersionDictionaryBuilder(),
        };
        AbsoluteFrameAddress a = Append(aFile, aBuilder);
        RbfFile bFile = store.CreateFile();
        FrameBuilder bBuilder = new() {
            ObjectVersionDictionary = new ObjectVersionDictionaryBuilder(),
        };
        AbsoluteFrameAddress b = Append(bFile, bBuilder);
        RbfFile cFile = store.CreateFile();
        RelativeFrameTicket locator = Previous(b.FrameTicket);
        ObjectVersionDictionaryBuilder cDictionary = new() {
            Kind = ObjectVersionDictionaryKind.Base,
            ParentRevisionFrameTicket = locator,
        };
        cDictionary.BindSelf(AaObjectId);
        FrameBuilder cBuilder = new() { ObjectVersionDictionary = cDictionary };
        AddBase(
            cBuilder,
            AaObjectId,
            payloadBytes: 10,
            logicalVersionOrdinal: 1,
            locator);
        AbsoluteFrameAddress c = Append(cFile, cBuilder);
        return new CanonicalLayout(store, a, b, null, c);
    }

    private static CanonicalLayout BuildMalformedLocatorLayout(LocatorKind locatorKind) {
        RbfFileStore store = new();
        RbfFile aFile = store.CreateFile();
        AbsoluteFrameAddress a = Append(aFile, new FrameBuilder());
        RbfFile bFile = store.CreateFile();
        AbsoluteFrameAddress b = Append(bFile, new FrameBuilder());
        RbfFile cFile = store.CreateFile();
        RelativeFrameTicket locator = locatorKind switch {
            LocatorKind.NullDictionary => Previous(b.FrameTicket),
            LocatorKind.Missing => Previous(new FrameTicket(100, 24)),
            LocatorKind.NonEarlier => Current(new FrameTicket(4, 24)),
            _ => throw new ArgumentOutOfRangeException(nameof(locatorKind)),
        };
        ObjectVersionDictionaryBuilder cDictionary = new() {
            Kind = ObjectVersionDictionaryKind.Base,
            ParentRevisionFrameTicket = locator,
        };
        cDictionary.BindSelf(AaObjectId);
        FrameBuilder cBuilder = new() { ObjectVersionDictionary = cDictionary };
        AddBase(
            cBuilder,
            AaObjectId,
            payloadBytes: 10,
            logicalVersionOrdinal: 1,
            locator);
        AbsoluteFrameAddress c = Append(cFile, cBuilder);
        return new CanonicalLayout(store, a, b, null, c);
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

    private static RevisionLocatorLineageInspection Inspect(
        CanonicalLayout layout,
        uint objectId) {
        AbsoluteFrameAddress currentHead = LookupCurrentHead(layout, objectId);
        return PhysicalStateOracle.InspectObjectLineageViaRevisionLocator(
            layout.Store,
            objectId,
            currentHead);
    }

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
        RevisionLocatorLineageInspection lineage,
        AbsoluteFrameAddress[] expectedRevisionReads) {
        ObjectVersionDictionaryLookupInspection lookup =
            Assert.Single(lineage.BaseParentLookups);
        Assert.Equal(ObjectVersionDictionaryLookupDisposition.Found, lookup.Disposition);
        Assert.Equal(expectedRevisionReads, lookup.DictionaryRevisionAddresses);
    }

    private static void AssertEquivalent(
        RevisionLocatorLineageInspection expected,
        RevisionLocatorLineageInspection actual) {
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
        int logicalVersionOrdinal,
        RelativeFrameTicket? parent) {
        ObjectVersionBuilder version = builder.Add(objectId);
        version.Kind = ObjectVersionKind.Base;
        version.PayloadBytes = payloadBytes;
        version.ReconstructionObjectPayloadBytes = payloadBytes;
        version.ResultBasePayloadBytes = payloadBytes;
        version.LogicalVersionOrdinal = logicalVersionOrdinal;
        version.ParentFrameTicket = parent;
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
        version.ParentFrameTicket = parent;
    }

    private static AbsoluteFrameAddress Append(RbfFile file, FrameBuilder builder) =>
        new(file.FileNumber, file.Append(builder.Build()));

    private static RelativeFrameTicket Current(FrameTicket ticket) =>
        new(IsPreviousFile: false, ticket);

    private static RelativeFrameTicket Previous(FrameTicket ticket) =>
        new(IsPreviousFile: true, ticket);

    private sealed record CanonicalLayout(
        RbfFileStore Store,
        AbsoluteFrameAddress A,
        AbsoluteFrameAddress B,
        AbsoluteFrameAddress? Relay,
        AbsoluteFrameAddress C);

    private enum LocatorKind {
        NullDictionary,
        Missing,
        NonEarlier,
    }
}
