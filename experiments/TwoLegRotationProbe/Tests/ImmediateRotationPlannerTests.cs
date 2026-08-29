using Atelia.TwoLegRotationProbe.Encoding;
using Atelia.TwoLegRotationProbe.Model;
using Atelia.TwoLegRotationProbe.Rotation;
using Atelia.TwoLegRotationProbe.Simulation;
using Atelia.TwoLegRotationProbe.Workloads;

namespace Atelia.TwoLegRotationProbe.Tests;

public sealed class ImmediateRotationPlannerTests {
    private const uint AaObjectId = 11;
    private const uint BaObjectId = 22;
    private const uint BbObjectId = 33;

    [Fact]
    public void Mixed_AA_BA_BB_plan_has_exact_relay_evacuation_and_projected_bindings() {
        SourceFixture source = CreateCanonicalSource();
        StoreSnapshot storeBefore = CaptureStore(source.Store);
        KeyValuePair<uint, AbsoluteFrameAddress>[] stateMapBefore =
            SnapshotMap(source.StateMap);
        KeyValuePair<uint, LogicalObjectState>[] logicalBefore =
            SnapshotLogicalState(source.Store, source.StateMap);

        ImmediateRotationPlan plan = ImmediateRotationPlanner.Create(
            source.Store,
            source.Current.FileNumber,
            source.PublishedRevisionAddress,
            source.StateMap);

        Assert.Equal(source.Previous.FileNumber, plan.PreviousFileNumber);
        Assert.Equal(source.Current.FileNumber, plan.CurrentFileNumber);
        Assert.Equal(source.Current.FileNumber + 1, plan.NextFileNumber);
        Assert.Equal(source.PublishedRevisionAddress, plan.PublishedRevisionAddress);
        Assert.Equal([AaObjectId, BaObjectId], plan.EvacuationObjectIds);
        Assert.Equal([AaObjectId], plan.RelayObjectIds);

        PlannedRevisionV0 relay = Assert.IsType<PlannedRevisionV0>(plan.RelayRevision);
        Assert.Equal(PlannedRevisionV0Role.Relay, relay.Role);
        Assert.Equal(source.Current.FileNumber, relay.FileNumber);
        Assert.Equal(source.Current.TailOffsetBytes, relay.Address.FrameTicket.OffsetBytes);
        Assert.Equal(relay.Address.FrameTicket, relay.Estimate.RbfLayout.Ticket);
        Assert.Equal(new FrameTicket(32, 40), relay.Address.FrameTicket);
        Assert.Equal(0, relay.Estimate.SyntheticObjectPayloadBytes);
        Assert.Equal(4, relay.Estimate.DomainRecordHeaderBytes);
        Assert.Equal(5, relay.Estimate.ObjectVersionDictionaryRecordBytes);
        Assert.Equal(4, relay.Estimate.TailMetaDirectoryBytes);
        Assert.Equal(4, relay.Estimate.AddressTokenBytes);
        Assert.Equal(9, relay.Estimate.PayloadLengthBytes);
        Assert.Equal(40, relay.Estimate.RbfLayout.FrameLengthBytes);
        Assert.Equal(44, relay.Estimate.RbfLayout.AppendLengthBytes);
        AssertEstimateMatchesGrammar(relay);

        ProvisionalDomainRecordInput relayDomain = Assert.Single(
            relay.GrammarInput.DomainRecords);
        Assert.Equal(AaObjectId, relayDomain.ObjectId);
        Assert.Equal(ProvisionalDomainRecordRole.Relay, relayDomain.Role);
        Assert.Equal(0, relayDomain.SyntheticPayloadBytes);
        Assert.Equal(
            source.AaHeadAddress,
            ResolveRequiredParent(relay.FileNumber, relayDomain.ParentFrameTicket));
        Assert.Equal(
            ProvisionalObjectVersionDictionaryKind.Delta,
            relay.GrammarInput.ObjectVersionDictionary.Kind);
        Assert.Empty(relay.GrammarInput.ObjectVersionDictionary.Entries);
        Assert.Equal(
            source.PublishedRevisionAddress,
            ResolveRequiredParent(
                relay.FileNumber,
                relay.GrammarInput.ObjectVersionDictionary.ParentFrameTicket));

        PlannedRevisionV0 evacuation = plan.EvacuationRevision;
        Assert.Equal(PlannedRevisionV0Role.Evacuation, evacuation.Role);
        Assert.Equal(source.Current.FileNumber + 1, evacuation.FileNumber);
        Assert.Equal(RbfV040Layout.InitialTailOffsetBytes, evacuation.Address.FrameTicket.OffsetBytes);
        Assert.Equal(evacuation.Address.FrameTicket, evacuation.Estimate.RbfLayout.Ticket);
        Assert.Equal(new FrameTicket(4, 88), evacuation.Address.FrameTicket);
        Assert.Equal(35, evacuation.Estimate.SyntheticObjectPayloadBytes);
        Assert.Equal(8, evacuation.Estimate.DomainRecordHeaderBytes);
        Assert.Equal(12, evacuation.Estimate.ObjectVersionDictionaryRecordBytes);
        Assert.Equal(6, evacuation.Estimate.TailMetaDirectoryBytes);
        Assert.Equal(10, evacuation.Estimate.AddressTokenBytes);
        Assert.Equal(55, evacuation.Estimate.PayloadLengthBytes);
        Assert.Equal(88, evacuation.Estimate.RbfLayout.FrameLengthBytes);
        Assert.Equal(92, evacuation.Estimate.RbfLayout.AppendLengthBytes);
        AssertEstimateMatchesGrammar(evacuation);

        Assert.Equal(
            [AaObjectId, BaObjectId],
            evacuation.GrammarInput.DomainRecords.Select(static record => record.ObjectId));
        ProvisionalDomainRecordInput aaDomain = evacuation.GrammarInput.DomainRecords[0];
        ProvisionalDomainRecordInput baDomain = evacuation.GrammarInput.DomainRecords[1];
        Assert.Equal(ProvisionalDomainRecordRole.Base, aaDomain.Role);
        Assert.Equal(10, aaDomain.SyntheticPayloadBytes);
        Assert.Equal(relay.Address, ResolveRequiredParent(
            evacuation.FileNumber,
            aaDomain.ParentFrameTicket));
        Assert.Equal(ProvisionalDomainRecordRole.Base, baDomain.Role);
        Assert.Equal(25, baDomain.SyntheticPayloadBytes);
        Assert.Equal(source.BaHeadAddress, ResolveRequiredParent(
            evacuation.FileNumber,
            baDomain.ParentFrameTicket));

        ProvisionalObjectVersionDictionaryInput evacuationOvd =
            evacuation.GrammarInput.ObjectVersionDictionary;
        Assert.Equal(ProvisionalObjectVersionDictionaryKind.Base, evacuationOvd.Kind);
        Assert.Equal(
            relay.Address,
            ResolveRequiredParent(evacuation.FileNumber, evacuationOvd.ParentFrameTicket));
        Assert.Equal(
            [AaObjectId, BaObjectId, BbObjectId],
            evacuationOvd.Entries.Select(static entry => entry.ObjectId));
        Assert.Equal(
            [
                ProvisionalObjectVersionDictionaryBindingKind.Self,
                ProvisionalObjectVersionDictionaryBindingKind.Self,
                ProvisionalObjectVersionDictionaryBindingKind.External,
            ],
            evacuationOvd.Entries.Select(static entry => entry.BindingKind));
        Assert.Equal(
            evacuation.Address,
            ResolveOvdBinding(evacuation, evacuationOvd.Entries[0]));
        Assert.Equal(
            evacuation.Address,
            ResolveOvdBinding(evacuation, evacuationOvd.Entries[1]));
        Assert.Equal(
            source.BbHeadAddress,
            ResolveOvdBinding(evacuation, evacuationOvd.Entries[2]));

        Assert.Equal(
            [
                KeyValuePair.Create(AaObjectId, evacuation.Address),
                KeyValuePair.Create(BaObjectId, evacuation.Address),
                KeyValuePair.Create(BbObjectId, source.BbHeadAddress),
            ],
            SnapshotMap(plan.ProjectedStateMap));
        AssertStoreAndSourceUnchanged(
            source,
            storeBefore,
            stateMapBefore,
            logicalBefore);
    }

    [Fact]
    public void BA_only_plan_needs_no_relay_and_parents_C_directly_to_old_B_head() {
        SourceFixture source = CreateCanonicalSource();
        Dictionary<uint, AbsoluteFrameAddress> stateMap = new() {
            [BaObjectId] = source.BaHeadAddress,
        };
        StoreSnapshot storeBefore = CaptureStore(source.Store);

        ImmediateRotationPlan plan = ImmediateRotationPlanner.Create(
            source.Store,
            source.Current.FileNumber,
            source.PublishedRevisionAddress,
            stateMap);

        Assert.Equal([BaObjectId], plan.EvacuationObjectIds);
        Assert.Empty(plan.RelayObjectIds);
        Assert.Null(plan.RelayRevision);
        ProvisionalRevisionV0Input grammar = plan.EvacuationRevision.GrammarInput;
        ProvisionalDomainRecordInput domain = Assert.Single(grammar.DomainRecords);
        Assert.Equal(BaObjectId, domain.ObjectId);
        Assert.Equal(ProvisionalDomainRecordRole.Base, domain.Role);
        Assert.Equal(25, domain.SyntheticPayloadBytes);
        Assert.Equal(
            source.BaHeadAddress,
            ResolveRequiredParent(plan.NextFileNumber, domain.ParentFrameTicket));
        Assert.Equal(
            source.PublishedRevisionAddress,
            ResolveRequiredParent(
                plan.NextFileNumber,
                grammar.ObjectVersionDictionary.ParentFrameTicket));
        ProvisionalObjectVersionDictionaryEntry binding = Assert.Single(
            grammar.ObjectVersionDictionary.Entries);
        Assert.Equal(
            plan.EvacuationRevision.Address,
            ResolveOvdBinding(plan.EvacuationRevision, binding));
        Assert.Equal(
            [KeyValuePair.Create(BaObjectId, plan.EvacuationRevision.Address)],
            SnapshotMap(plan.ProjectedStateMap));
        AssertStoreSnapshot(storeBefore, CaptureStore(source.Store));
    }

    [Fact]
    public void BB_only_plan_writes_an_external_full_Ovd_without_domain_records() {
        SourceFixture source = CreateCanonicalSource();
        Dictionary<uint, AbsoluteFrameAddress> stateMap = new() {
            [BbObjectId] = source.BbHeadAddress,
        };

        ImmediateRotationPlan plan = ImmediateRotationPlanner.Create(
            source.Store,
            source.Current.FileNumber,
            source.PublishedRevisionAddress,
            stateMap);

        Assert.Empty(plan.EvacuationObjectIds);
        Assert.Empty(plan.RelayObjectIds);
        Assert.Null(plan.RelayRevision);
        Assert.Empty(plan.EvacuationRevision.GrammarInput.DomainRecords);
        ProvisionalObjectVersionDictionaryInput ovd =
            plan.EvacuationRevision.GrammarInput.ObjectVersionDictionary;
        Assert.Equal(ProvisionalObjectVersionDictionaryKind.Base, ovd.Kind);
        Assert.Equal(
            source.PublishedRevisionAddress,
            ResolveRequiredParent(plan.NextFileNumber, ovd.ParentFrameTicket));
        ProvisionalObjectVersionDictionaryEntry entry = Assert.Single(ovd.Entries);
        Assert.Equal(ProvisionalObjectVersionDictionaryBindingKind.External, entry.BindingKind);
        Assert.Equal(
            source.BbHeadAddress,
            ResolveOvdBinding(plan.EvacuationRevision, entry));
        Assert.Equal(
            [KeyValuePair.Create(BbObjectId, source.BbHeadAddress)],
            SnapshotMap(plan.ProjectedStateMap));
        AssertEstimateMatchesGrammar(plan.EvacuationRevision);
    }

    [Fact]
    public void Empty_graph_still_writes_a_legal_empty_full_Ovd() {
        SourceFixture source = CreateCanonicalSource();
        Dictionary<uint, AbsoluteFrameAddress> stateMap = [];

        ImmediateRotationPlan plan = ImmediateRotationPlanner.Create(
            source.Store,
            source.Current.FileNumber,
            source.PublishedRevisionAddress,
            stateMap);

        Assert.Empty(plan.EvacuationObjectIds);
        Assert.Empty(plan.RelayObjectIds);
        Assert.Null(plan.RelayRevision);
        Assert.Empty(plan.EvacuationRevision.GrammarInput.DomainRecords);
        Assert.Empty(plan.EvacuationRevision.GrammarInput.ObjectVersionDictionary.Entries);
        Assert.Equal(
            source.PublishedRevisionAddress,
            ResolveRequiredParent(
                plan.NextFileNumber,
                plan.EvacuationRevision.GrammarInput.ObjectVersionDictionary.ParentFrameTicket));
        Assert.Empty(plan.ProjectedStateMap);
        AssertEstimateMatchesGrammar(plan.EvacuationRevision);
    }

    [Fact]
    public void Plan_is_deterministic_across_StateMap_insertion_order() {
        SourceFixture source = CreateCanonicalSource();
        Dictionary<uint, AbsoluteFrameAddress> reverse = new() {
            [BbObjectId] = source.BbHeadAddress,
            [BaObjectId] = source.BaHeadAddress,
            [AaObjectId] = source.AaHeadAddress,
        };

        ImmediateRotationPlan first = ImmediateRotationPlanner.Create(
            source.Store,
            source.Current.FileNumber,
            source.PublishedRevisionAddress,
            source.StateMap);
        ImmediateRotationPlan second = ImmediateRotationPlanner.Create(
            source.Store,
            source.Current.FileNumber,
            source.PublishedRevisionAddress,
            reverse);

        Assert.Equal(first.EvacuationObjectIds, second.EvacuationObjectIds);
        Assert.Equal(first.RelayObjectIds, second.RelayObjectIds);
        AssertPlannedRevisionEqual(first.RelayRevision!, second.RelayRevision!);
        AssertPlannedRevisionEqual(first.EvacuationRevision, second.EvacuationRevision);
        Assert.Equal(SnapshotMap(first.ProjectedStateMap), SnapshotMap(second.ProjectedStateMap));
    }

    [Fact]
    public void C_overflow_after_relay_preflight_is_non_mutating_and_retryable() {
        SourceFixture source = CreateCanonicalSource(
            aaPayloadBytes: RbfV040Layout.MaxPayloadAndTailMetaLengthBytes);
        StoreSnapshot storeBefore = CaptureStore(source.Store);
        KeyValuePair<uint, AbsoluteFrameAddress>[] stateMapBefore =
            SnapshotMap(source.StateMap);
        KeyValuePair<uint, LogicalObjectState>[] logicalBefore =
            SnapshotLogicalState(source.Store, source.StateMap);

        Assert.Throws<ArgumentOutOfRangeException>(() => ImmediateRotationPlanner.Create(
            source.Store,
            source.Current.FileNumber,
            source.PublishedRevisionAddress,
            source.StateMap));

        AssertStoreAndSourceUnchanged(
            source,
            storeBefore,
            stateMapBefore,
            logicalBefore);

        Dictionary<uint, AbsoluteFrameAddress> legalRetry = new() {
            [BbObjectId] = source.BbHeadAddress,
        };
        ImmediateRotationPlan retry = ImmediateRotationPlanner.Create(
            source.Store,
            source.Current.FileNumber,
            source.PublishedRevisionAddress,
            legalRetry);
        Assert.Null(retry.RelayRevision);
        Assert.Empty(retry.EvacuationObjectIds);
        Assert.Equal(
            [KeyValuePair.Create(BbObjectId, source.BbHeadAddress)],
            SnapshotMap(retry.ProjectedStateMap));
        AssertStoreSnapshot(storeBefore, CaptureStore(source.Store));
    }

    [Fact]
    public void B_relay_TailMeta_overflow_is_non_mutating_and_retryable() {
        const int objectCount = 15_000;
        RbfFileStore store = new();
        RbfFile previous = store.CreateFile();
        FrameBuilder previousFrame = new();
        Dictionary<uint, AbsoluteFrameAddress> stateMap = new(objectCount);
        for (uint objectId = 1; objectId <= objectCount; objectId++) {
            AddBase(previousFrame, objectId, payloadBytes: 0);
        }

        FrameTicket previousTicket = previous.Append(previousFrame.Build());
        AbsoluteFrameAddress previousAddress = new(previous.FileNumber, previousTicket);
        for (uint objectId = 1; objectId <= objectCount; objectId++) {
            stateMap.Add(objectId, previousAddress);
        }

        RbfFile current = store.CreateFile();
        AbsoluteFrameAddress published = new(
            current.FileNumber,
            current.Append(new FrameBuilder().Build()));
        StoreSnapshot storeBefore = CaptureStore(store);
        KeyValuePair<uint, AbsoluteFrameAddress>[] stateMapBefore = SnapshotMap(stateMap);

        ArgumentOutOfRangeException exception = Assert.Throws<ArgumentOutOfRangeException>(
            () => ImmediateRotationPlanner.Create(
                store,
                current.FileNumber,
                published,
                stateMap));

        Assert.Equal("tailMetaLengthBytes", exception.ParamName);
        AssertStoreSnapshot(storeBefore, CaptureStore(store));
        Assert.Equal(stateMapBefore, SnapshotMap(stateMap));

        ImmediateRotationPlan retry = ImmediateRotationPlanner.Create(
            store,
            current.FileNumber,
            published,
            new Dictionary<uint, AbsoluteFrameAddress>());
        Assert.Null(retry.RelayRevision);
        Assert.Empty(retry.ProjectedStateMap);
        AssertStoreSnapshot(storeBefore, CaptureStore(store));
    }

    [Fact]
    public void Source_heads_and_bases_outside_the_file_pair_fail_without_mutation() {
        RbfFileStore store = new();
        RbfFile old = store.CreateFile();
        FrameTicket oldBaseTicket = AppendBase(old, AaObjectId, payloadBytes: 10);
        RbfFile previous = store.CreateFile();
        FrameTicket previousDeltaTicket = AppendDelta(
            previous,
            AaObjectId,
            new RelativeFrameTicket(true, oldBaseTicket),
            parentBasePayloadBytes: 10,
            resultBasePayloadBytes: 12,
            deltaPayloadBytes: 2,
            reconstructionPayloadBytes: 12,
            logicalVersionOrdinal: 2);
        RbfFile current = store.CreateFile();
        FrameTicket publishedTicket = current.Append(new FrameBuilder().Build());
        AbsoluteFrameAddress published = new(current.FileNumber, publishedTicket);
        StoreSnapshot before = CaptureStore(store);

        Dictionary<uint, AbsoluteFrameAddress> headOutsidePair = new() {
            [AaObjectId] = new(old.FileNumber, oldBaseTicket),
        };
        Assert.Throws<InvalidDataException>(() => ImmediateRotationPlanner.Create(
            store,
            current.FileNumber,
            published,
            headOutsidePair));
        AssertStoreSnapshot(before, CaptureStore(store));

        Dictionary<uint, AbsoluteFrameAddress> baseOutsidePair = new() {
            [AaObjectId] = new(previous.FileNumber, previousDeltaTicket),
        };
        Assert.Throws<InvalidDataException>(() => ImmediateRotationPlanner.Create(
            store,
            current.FileNumber,
            published,
            baseOutsidePair));
        AssertStoreSnapshot(before, CaptureStore(store));
    }

    [Fact]
    public void Invalid_published_revision_address_fails_without_mutation() {
        SourceFixture source = CreateCanonicalSource();
        StoreSnapshot before = CaptureStore(source.Store);
        KeyValuePair<uint, AbsoluteFrameAddress>[] stateMapBefore =
            SnapshotMap(source.StateMap);
        KeyValuePair<uint, LogicalObjectState>[] logicalBefore =
            SnapshotLogicalState(source.Store, source.StateMap);
        AbsoluteFrameAddress missingCurrentFrame = new(
            source.Current.FileNumber,
            new FrameTicket(source.Current.TailOffsetBytes, RbfV040Layout.MinFrameLengthBytes));

        Assert.Throws<InvalidDataException>(() => ImmediateRotationPlanner.Create(
            source.Store,
            source.Current.FileNumber,
            missingCurrentFrame,
            source.StateMap));

        AssertStoreAndSourceUnchanged(
            source,
            before,
            stateMapBefore,
            logicalBefore);
    }

    private static SourceFixture CreateCanonicalSource(int aaPayloadBytes = 10) {
        RbfFileStore store = new();
        RbfFile previous = store.CreateFile();
        FrameBuilder previousFrame = new();
        AddBase(previousFrame, AaObjectId, aaPayloadBytes);
        AddBase(previousFrame, BaObjectId, payloadBytes: 20);
        FrameTicket previousTicket = previous.Append(previousFrame.Build());

        RbfFile current = store.CreateFile();
        FrameBuilder currentFrame = new();
        AddDelta(
            currentFrame,
            BaObjectId,
            new RelativeFrameTicket(true, previousTicket),
            parentBasePayloadBytes: 20,
            resultBasePayloadBytes: 25,
            deltaPayloadBytes: 5,
            reconstructionPayloadBytes: 25,
            logicalVersionOrdinal: 2);
        AddBase(currentFrame, BbObjectId, payloadBytes: 30);
        FrameTicket currentTicket = current.Append(currentFrame.Build());

        AbsoluteFrameAddress aaHead = new(previous.FileNumber, previousTicket);
        AbsoluteFrameAddress baHead = new(current.FileNumber, currentTicket);
        AbsoluteFrameAddress bbHead = new(current.FileNumber, currentTicket);
        Dictionary<uint, AbsoluteFrameAddress> stateMap = new() {
            [AaObjectId] = aaHead,
            [BaObjectId] = baHead,
            [BbObjectId] = bbHead,
        };
        return new SourceFixture(
            store,
            previous,
            current,
            new AbsoluteFrameAddress(current.FileNumber, currentTicket),
            aaHead,
            baHead,
            bbHead,
            stateMap);
    }

    private static FrameTicket AppendBase(
        RbfFile file,
        uint objectId,
        int payloadBytes) {
        FrameBuilder frame = new();
        AddBase(frame, objectId, payloadBytes);
        return file.Append(frame.Build());
    }

    private static FrameTicket AppendDelta(
        RbfFile file,
        uint objectId,
        RelativeFrameTicket parent,
        int parentBasePayloadBytes,
        int resultBasePayloadBytes,
        int deltaPayloadBytes,
        long reconstructionPayloadBytes,
        int logicalVersionOrdinal) {
        FrameBuilder frame = new();
        AddDelta(
            frame,
            objectId,
            parent,
            parentBasePayloadBytes,
            resultBasePayloadBytes,
            deltaPayloadBytes,
            reconstructionPayloadBytes,
            logicalVersionOrdinal);
        return file.Append(frame.Build());
    }

    private static void AddBase(FrameBuilder frame, uint objectId, int payloadBytes) {
        ObjectVersionBuilder version = frame.Add(objectId);
        version.Kind = ObjectVersionKind.Base;
        version.PayloadBytes = payloadBytes;
        version.ReconstructionObjectPayloadBytes = payloadBytes;
        version.ResultBasePayloadBytes = payloadBytes;
        version.LogicalVersionOrdinal = 1;
    }

    private static void AddDelta(
        FrameBuilder frame,
        uint objectId,
        RelativeFrameTicket parent,
        int parentBasePayloadBytes,
        int resultBasePayloadBytes,
        int deltaPayloadBytes,
        long reconstructionPayloadBytes,
        int logicalVersionOrdinal) {
        ObjectVersionBuilder version = frame.Add(objectId);
        version.Kind = ObjectVersionKind.Delta;
        version.PayloadBytes = deltaPayloadBytes;
        version.ReconstructionObjectPayloadBytes = reconstructionPayloadBytes;
        version.ResultBasePayloadBytes = resultBasePayloadBytes;
        version.ExpectedParentBasePayloadBytes = parentBasePayloadBytes;
        version.LogicalVersionOrdinal = logicalVersionOrdinal;
        version.ParentFrameTicket = parent;
    }

    private static AbsoluteFrameAddress ResolveRequiredParent(
        uint originFileNumber,
        RelativeFrameTicket? relative) =>
        new FileScope(originFileNumber).Resolve(Assert.IsType<RelativeFrameTicket>(relative));

    private static AbsoluteFrameAddress ResolveOvdBinding(
        PlannedRevisionV0 revision,
        ProvisionalObjectVersionDictionaryEntry entry) {
        ulong token = entry.BindingKind switch {
            ProvisionalObjectVersionDictionaryBindingKind.Self =>
                ProvisionalObjectVersionDictionaryBinding.EncodeSelf(),
            ProvisionalObjectVersionDictionaryBindingKind.External =>
                ProvisionalObjectVersionDictionaryBinding.EncodeExternal(
                    Assert.IsType<RelativeFrameTicket>(entry.ExternalFrameTicket)),
            _ => throw new Xunit.Sdk.XunitException(
                $"Binding {entry.BindingKind} cannot resolve to a live address."),
        };
        return ProvisionalObjectVersionDictionaryBinding.Resolve(
            token,
            revision.FileNumber,
            revision.Address.FrameTicket);
    }

    private static void AssertEstimateMatchesGrammar(PlannedRevisionV0 revision) {
        ProvisionalRevisionV0Estimate actual = revision.Estimate;
        ProvisionalRevisionV0Estimate expected = ProvisionalRevisionV0Estimator.Estimate(
            revision.GrammarInput,
            revision.Address.FrameTicket.OffsetBytes);

        Assert.Equal(expected.DomainRecords, actual.DomainRecords);
        Assert.Equal(expected.SyntheticObjectPayloadBytes, actual.SyntheticObjectPayloadBytes);
        Assert.Equal(expected.DomainRecordHeaderBytes, actual.DomainRecordHeaderBytes);
        Assert.Equal(
            expected.ObjectVersionDictionaryRecordBytes,
            actual.ObjectVersionDictionaryRecordBytes);
        Assert.Equal(expected.TailMetaDirectoryBytes, actual.TailMetaDirectoryBytes);
        Assert.Equal(expected.AddressTokenBytes, actual.AddressTokenBytes);
        Assert.Equal(
            expected.ObjectVersionDictionaryPayloadOffsetBytes,
            actual.ObjectVersionDictionaryPayloadOffsetBytes);
        Assert.Equal(expected.RbfLayout, actual.RbfLayout);
    }

    private static void AssertPlannedRevisionEqual(
        PlannedRevisionV0 expected,
        PlannedRevisionV0 actual) {
        Assert.Equal(expected.Role, actual.Role);
        Assert.Equal(expected.FileNumber, actual.FileNumber);
        Assert.Equal(expected.Address, actual.Address);
        Assert.Equal(expected.GrammarInput.DomainRecords, actual.GrammarInput.DomainRecords);
        Assert.Equal(
            expected.GrammarInput.ObjectVersionDictionary.Kind,
            actual.GrammarInput.ObjectVersionDictionary.Kind);
        Assert.Equal(
            expected.GrammarInput.ObjectVersionDictionary.ParentFrameTicket,
            actual.GrammarInput.ObjectVersionDictionary.ParentFrameTicket);
        Assert.Equal(
            expected.GrammarInput.ObjectVersionDictionary.Entries,
            actual.GrammarInput.ObjectVersionDictionary.Entries);
        Assert.Equal(expected.Estimate.RbfLayout, actual.Estimate.RbfLayout);
    }

    private static StoreSnapshot CaptureStore(RbfFileStore store) => new(
        store.FileCount,
        Enumerable.Range(1, store.FileCount)
            .Select(index => store.GetFile((uint)index))
            .Select(static file => new FileSnapshot(
                file.FileNumber,
                file.FrameCount,
                file.TailOffsetBytes))
            .ToArray());

    private static KeyValuePair<uint, AbsoluteFrameAddress>[] SnapshotMap(
        IReadOnlyDictionary<uint, AbsoluteFrameAddress> map) =>
        map.OrderBy(static pair => pair.Key).ToArray();

    private static KeyValuePair<uint, LogicalObjectState>[] SnapshotLogicalState(
        RbfFileStore store,
        IReadOnlyDictionary<uint, AbsoluteFrameAddress> map) =>
        PhysicalStateOracle.Materialize(store, map)
            .OrderBy(static pair => pair.Key)
            .ToArray();

    private static void AssertStoreAndSourceUnchanged(
        SourceFixture source,
        StoreSnapshot expectedStore,
        KeyValuePair<uint, AbsoluteFrameAddress>[] expectedStateMap,
        KeyValuePair<uint, LogicalObjectState>[] expectedLogicalState) {
        AssertStoreSnapshot(expectedStore, CaptureStore(source.Store));
        Assert.Equal(expectedStateMap, SnapshotMap(source.StateMap));
        Assert.Equal(expectedLogicalState, SnapshotLogicalState(source.Store, source.StateMap));
    }

    private static void AssertStoreSnapshot(
        StoreSnapshot expected,
        StoreSnapshot actual) {
        Assert.Equal(expected.FileCount, actual.FileCount);
        Assert.Equal(expected.Files, actual.Files);
    }

    private sealed record SourceFixture(
        RbfFileStore Store,
        RbfFile Previous,
        RbfFile Current,
        AbsoluteFrameAddress PublishedRevisionAddress,
        AbsoluteFrameAddress AaHeadAddress,
        AbsoluteFrameAddress BaHeadAddress,
        AbsoluteFrameAddress BbHeadAddress,
        Dictionary<uint, AbsoluteFrameAddress> StateMap);

    private sealed record StoreSnapshot(int FileCount, IReadOnlyList<FileSnapshot> Files);

    private sealed record FileSnapshot(uint FileNumber, int FrameCount, long TailOffsetBytes);
}
