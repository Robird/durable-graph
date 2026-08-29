using Atelia.TwoLegRotationProbe.Encoding;
using Atelia.TwoLegRotationProbe.Model;
using Atelia.TwoLegRotationProbe.Rotation;
using Atelia.TwoLegRotationProbe.Simulation;

namespace Atelia.TwoLegRotationProbe.Tests;

public sealed class ImmediateRotationPlannerTests {
    private const uint AaObjectId = 11;
    private const uint BaObjectId = 22;
    private const uint BbObjectId = 33;

    [Fact]
    public void Mixed_AA_BA_BB_plan_uses_one_C_revision_and_B_revision_locators() {
        SourceFixture source = CreateCanonicalSource();
        StoreSnapshot storeBefore = CaptureStore(source.Store);
        KeyValuePair<uint, AbsoluteFrameAddress>[] sourceMap = SnapshotMap(
            ObjectVersionDictionaryReader.MaterializeLive(
                source.Store,
                source.PublishedRevisionAddress).Bindings);

        ImmediateRotationPlan plan = ImmediateRotationPlanner.Create(
            source.Store,
            source.Current.FileNumber,
            source.PublishedRevisionAddress);

        Assert.Equal(source.Previous.FileNumber, plan.PreviousFileNumber);
        Assert.Equal(source.Current.FileNumber, plan.CurrentFileNumber);
        Assert.Equal(source.Current.FileNumber + 1, plan.NextFileNumber);
        Assert.Equal(source.PublishedRevisionAddress, plan.PublishedRevisionAddress);
        Assert.Equal([AaObjectId, BaObjectId], plan.EvacuationObjectIds);

        PlannedRevisionV0 evacuation = plan.EvacuationRevision;
        Assert.Equal(source.Current.FileNumber + 1, evacuation.FileNumber);
        Assert.Equal(RbfV040Layout.InitialTailOffsetBytes, evacuation.Address.FrameTicket.OffsetBytes);
        Assert.Equal(evacuation.Address.FrameTicket, evacuation.Estimate.RbfLayout.Ticket);
        AssertEstimateMatchesFrame(evacuation);

        Assert.Equal(
            [AaObjectId, BaObjectId],
            evacuation.Frame.ObjectVersions.Keys.Order());
        foreach (ObjectVersion version in evacuation.Frame.ObjectVersions.Values) {
            Assert.Equal(ObjectVersionKind.Base, version.Kind);
            Assert.Equal(
                source.PublishedRevisionAddress,
                ResolveRequiredParent(evacuation.FileNumber, version.ParentFrameTicket));
        }

        Assert.Equal(10, evacuation.Frame.ObjectVersions[AaObjectId].PayloadBytes);
        Assert.Equal(25, evacuation.Frame.ObjectVersions[BaObjectId].PayloadBytes);
        Assert.Equal(1, evacuation.Frame.ObjectVersions[AaObjectId].LogicalVersionOrdinal);
        Assert.Equal(2, evacuation.Frame.ObjectVersions[BaObjectId].LogicalVersionOrdinal);

        ObjectVersionDictionary ovd = Assert.IsType<ObjectVersionDictionary>(
            evacuation.Frame.ObjectVersionDictionary);
        Assert.Equal(ObjectVersionDictionaryKind.Base, ovd.Kind);
        Assert.Equal(
            source.PublishedRevisionAddress,
            ResolveRequiredParent(
                evacuation.FileNumber,
                ovd.ParentRevisionFrameTicket));
        Assert.Equal(
            [AaObjectId, BaObjectId, BbObjectId],
            ovd.Entries.Keys.Order());
        Assert.Equal(
            [
                ObjectVersionDictionaryBindingKind.Self,
                ObjectVersionDictionaryBindingKind.Self,
                ObjectVersionDictionaryBindingKind.External,
            ],
            ovd.Entries.OrderBy(static pair => pair.Key)
                .Select(static pair => pair.Value.Kind));
        Assert.Equal(
            source.BbHeadAddress,
            ResolveOvdBinding(evacuation, ovd.Entries[BbObjectId]));
        Assert.Equal(sourceMap, SnapshotMap(
            ObjectVersionDictionaryReader.MaterializeLive(
                source.Store,
                source.PublishedRevisionAddress).Bindings));
        AssertStoreSnapshot(storeBefore, CaptureStore(source.Store));
    }

    [Fact]
    public void Published_OVD_is_the_only_source_of_live_bindings() {
        SourceFixture source = CreateCanonicalSource(removeAaInPublishedRevision: true);

        ImmediateRotationPlan plan = ImmediateRotationPlanner.Create(
            source.Store,
            source.Current.FileNumber,
            source.PublishedRevisionAddress);

        Assert.Equal([BaObjectId], plan.EvacuationObjectIds);
        ObjectVersionDictionary plannedOvd = Assert.IsType<ObjectVersionDictionary>(
            plan.EvacuationRevision.Frame.ObjectVersionDictionary);
        Assert.Equal([BaObjectId, BbObjectId], plannedOvd.Entries.Keys.Order());
        Assert.DoesNotContain(AaObjectId, plannedOvd.Entries.Keys);
        Assert.Equal(
            [BaObjectId],
            plan.EvacuationRevision.Frame.ObjectVersions.Keys.Order());
    }

    [Fact]
    public void Empty_published_graph_still_plans_a_legal_empty_full_OVD() {
        RbfFileStore store = new();
        _ = store.CreateFile();
        RbfFile current = store.CreateFile();
        FrameBuilder publishedBuilder = new() {
            ObjectVersionDictionary = new ObjectVersionDictionaryBuilder(),
        };
        AbsoluteFrameAddress published = Append(current, publishedBuilder);

        ImmediateRotationPlan plan = ImmediateRotationPlanner.Create(
            store,
            current.FileNumber,
            published);

        Assert.Empty(plan.EvacuationObjectIds);
        Assert.Empty(plan.EvacuationRevision.Frame.ObjectVersions);
        ObjectVersionDictionary plannedOvd = Assert.IsType<ObjectVersionDictionary>(
            plan.EvacuationRevision.Frame.ObjectVersionDictionary);
        Assert.Empty(plannedOvd.Entries);
        Assert.Equal(
            published,
            ResolveRequiredParent(
                plan.NextFileNumber,
                plannedOvd.ParentRevisionFrameTicket));
        AssertEstimateMatchesFrame(plan.EvacuationRevision);
    }

    [Fact]
    public void Plan_is_deterministic_across_OVD_entry_insertion_order() {
        SourceFixture forward = CreateCanonicalSource(reverseOvdEntryOrder: false);
        SourceFixture reverse = CreateCanonicalSource(reverseOvdEntryOrder: true);

        ImmediateRotationPlan first = ImmediateRotationPlanner.Create(
            forward.Store,
            forward.Current.FileNumber,
            forward.PublishedRevisionAddress);
        ImmediateRotationPlan second = ImmediateRotationPlanner.Create(
            reverse.Store,
            reverse.Current.FileNumber,
            reverse.PublishedRevisionAddress);

        Assert.Equal(first.EvacuationObjectIds, second.EvacuationObjectIds);
        AssertPlannedRevisionEqual(first.EvacuationRevision, second.EvacuationRevision);
    }

    [Fact]
    public void C_capacity_overflow_is_non_mutating_and_repeatable() {
        SourceFixture source = CreateCanonicalSource(
            aaPayloadBytes: RbfV040Layout.MaxPayloadAndTailMetaLengthBytes);
        StoreSnapshot storeBefore = CaptureStore(source.Store);

        Assert.Throws<ArgumentOutOfRangeException>(() => ImmediateRotationPlanner.Create(
            source.Store,
            source.Current.FileNumber,
            source.PublishedRevisionAddress));
        AssertStoreSnapshot(storeBefore, CaptureStore(source.Store));

        Assert.Throws<ArgumentOutOfRangeException>(() => ImmediateRotationPlanner.Create(
            source.Store,
            source.Current.FileNumber,
            source.PublishedRevisionAddress));
        AssertStoreSnapshot(storeBefore, CaptureStore(source.Store));
    }

    [Fact]
    public void B_tail_beyond_relative_start_capacity_does_not_create_a_planning_debt() {
        SourceFixture source = CreateCanonicalSource();
        while (source.Current.TailOffsetBytes <=
            RbfV040Layout.MaxDurableGraphRelativeFrameStartOffsetBytes) {
            _ = source.Current.Append(
                new FrameBuilder().Build(),
                RbfV040Layout.MaxPayloadAndTailMetaLengthBytes);
        }

        StoreSnapshot storeBefore = CaptureStore(source.Store);

        ImmediateRotationPlan plan = ImmediateRotationPlanner.Create(
            source.Store,
            source.Current.FileNumber,
            source.PublishedRevisionAddress);

        Assert.Equal([AaObjectId, BaObjectId], plan.EvacuationObjectIds);
        Assert.Equal(source.Current.FileNumber + 1, plan.EvacuationRevision.FileNumber);
        AssertStoreSnapshot(storeBefore, CaptureStore(source.Store));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Source_head_or_reconstruction_outside_A_B_fails_without_mutation(
        bool exposeOldHead) {
        (RbfFileStore store, RbfFile current, AbsoluteFrameAddress published) =
            CreateOutOfScopeSource(exposeOldHead);
        StoreSnapshot storeBefore = CaptureStore(store);

        Assert.Throws<InvalidDataException>(() => ImmediateRotationPlanner.Create(
            store,
            current.FileNumber,
            published));

        AssertStoreSnapshot(storeBefore, CaptureStore(store));
    }

    [Fact]
    public void Invalid_published_revision_address_fails_without_mutation() {
        SourceFixture source = CreateCanonicalSource();
        StoreSnapshot before = CaptureStore(source.Store);
        AbsoluteFrameAddress missingCurrentFrame = new(
            source.Current.FileNumber,
            new FrameTicket(source.Current.TailOffsetBytes, RbfV040Layout.MinFrameLengthBytes));

        Assert.Throws<InvalidDataException>(() => ImmediateRotationPlanner.Create(
            source.Store,
            source.Current.FileNumber,
            missingCurrentFrame));

        AssertStoreSnapshot(before, CaptureStore(source.Store));
    }

    private static SourceFixture CreateCanonicalSource(
        int aaPayloadBytes = 10,
        bool reverseOvdEntryOrder = false,
        bool removeAaInPublishedRevision = false) {
        RbfFileStore store = new();
        RbfFile previous = store.CreateFile();
        ObjectVersionDictionaryBuilder previousOvd = new();
        if (reverseOvdEntryOrder) {
            previousOvd.BindSelf(BaObjectId);
            previousOvd.BindSelf(AaObjectId);
        } else {
            previousOvd.BindSelf(AaObjectId);
            previousOvd.BindSelf(BaObjectId);
        }

        FrameBuilder previousFrame = new() { ObjectVersionDictionary = previousOvd };
        AddBase(previousFrame, AaObjectId, aaPayloadBytes);
        AddBase(previousFrame, BaObjectId, payloadBytes: 20);
        AbsoluteFrameAddress previousAddress = Append(previous, previousFrame);

        RbfFile current = store.CreateFile();
        ObjectVersionDictionaryBuilder currentOvd = new() {
            Kind = ObjectVersionDictionaryKind.Delta,
            ParentRevisionFrameTicket = Previous(previousAddress.FrameTicket),
        };
        if (removeAaInPublishedRevision) {
            currentOvd.Remove(AaObjectId);
        }

        if (reverseOvdEntryOrder) {
            currentOvd.BindSelf(BbObjectId);
            currentOvd.BindSelf(BaObjectId);
        } else {
            currentOvd.BindSelf(BaObjectId);
            currentOvd.BindSelf(BbObjectId);
        }

        FrameBuilder currentFrame = new() { ObjectVersionDictionary = currentOvd };
        AddDelta(
            currentFrame,
            BaObjectId,
            Previous(previousAddress.FrameTicket),
            parentBasePayloadBytes: 20,
            resultBasePayloadBytes: 25,
            deltaPayloadBytes: 5,
            reconstructionPayloadBytes: 25,
            logicalVersionOrdinal: 2);
        AddBase(currentFrame, BbObjectId, payloadBytes: 30);
        AbsoluteFrameAddress currentAddress = Append(current, currentFrame);

        return new SourceFixture(
            store,
            previous,
            current,
            currentAddress,
            currentAddress);
    }

    private static (RbfFileStore Store, RbfFile Current, AbsoluteFrameAddress Published)
        CreateOutOfScopeSource(bool exposeOldHead) {
        RbfFileStore store = new();
        RbfFile old = store.CreateFile();
        ObjectVersionDictionaryBuilder oldOvd = new();
        oldOvd.BindSelf(AaObjectId);
        FrameBuilder oldBuilder = new() { ObjectVersionDictionary = oldOvd };
        AddBase(oldBuilder, AaObjectId, payloadBytes: 10);
        AbsoluteFrameAddress oldAddress = Append(old, oldBuilder);

        RbfFile previous = store.CreateFile();
        ObjectVersionDictionaryBuilder previousOvd = new();
        FrameBuilder previousBuilder = new() { ObjectVersionDictionary = previousOvd };
        if (exposeOldHead) {
            previousOvd.BindExternal(AaObjectId, Previous(oldAddress.FrameTicket));
        } else {
            previousOvd.BindSelf(AaObjectId);
            AddDelta(
                previousBuilder,
                AaObjectId,
                Previous(oldAddress.FrameTicket),
                parentBasePayloadBytes: 10,
                resultBasePayloadBytes: 12,
                deltaPayloadBytes: 2,
                reconstructionPayloadBytes: 12,
                logicalVersionOrdinal: 2);
        }

        AbsoluteFrameAddress previousAddress = Append(previous, previousBuilder);
        RbfFile current = store.CreateFile();
        ObjectVersionDictionaryBuilder currentOvd = new() {
            Kind = ObjectVersionDictionaryKind.Delta,
            ParentRevisionFrameTicket = Previous(previousAddress.FrameTicket),
        };
        FrameBuilder currentBuilder = new() { ObjectVersionDictionary = currentOvd };
        AbsoluteFrameAddress published = Append(current, currentBuilder);
        return (store, current, published);
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

    private static AbsoluteFrameAddress Append(RbfFile file, FrameBuilder builder) =>
        new(file.FileNumber, file.Append(builder.Build()));

    private static RelativeFrameTicket Previous(FrameTicket ticket) =>
        new(IsPreviousFile: true, ticket);

    private static AbsoluteFrameAddress ResolveRequiredParent(
        uint originFileNumber,
        RelativeFrameTicket? relative) =>
        new FileScope(originFileNumber).Resolve(Assert.IsType<RelativeFrameTicket>(relative));

    private static AbsoluteFrameAddress ResolveOvdBinding(
        PlannedRevisionV0 revision,
        ObjectVersionDictionaryBinding binding) {
        return binding.Kind switch {
            ObjectVersionDictionaryBindingKind.Self => revision.Address,
            ObjectVersionDictionaryBindingKind.External =>
                new FileScope(revision.FileNumber).Resolve(
                    Assert.IsType<RelativeFrameTicket>(binding.ExternalFrameTicket)),
            _ => throw new Xunit.Sdk.XunitException(
                $"Binding {binding.Kind} cannot resolve to a live address."),
        };
    }

    private static void AssertEstimateMatchesFrame(PlannedRevisionV0 revision) {
        ProvisionalRevisionV0Estimate actual = revision.Estimate;
        ProvisionalRevisionV0Estimate expected = ProvisionalRevisionV0Estimator.Estimate(
            revision.Frame,
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
        Assert.Equal(expected.FileNumber, actual.FileNumber);
        Assert.Equal(expected.Address, actual.Address);
        Assert.Equal(
            DescribeObjectVersions(expected.Frame),
            DescribeObjectVersions(actual.Frame));
        ObjectVersionDictionary expectedOvd = Assert.IsType<ObjectVersionDictionary>(
            expected.Frame.ObjectVersionDictionary);
        ObjectVersionDictionary actualOvd = Assert.IsType<ObjectVersionDictionary>(
            actual.Frame.ObjectVersionDictionary);
        Assert.Equal(expectedOvd.Kind, actualOvd.Kind);
        Assert.Equal(
            expectedOvd.ParentRevisionFrameTicket,
            actualOvd.ParentRevisionFrameTicket);
        Assert.Equal(
            expectedOvd.Entries.OrderBy(static pair => pair.Key),
            actualOvd.Entries.OrderBy(static pair => pair.Key));
        Assert.Equal(expected.Estimate.RbfLayout, actual.Estimate.RbfLayout);
    }

    private static object[] DescribeObjectVersions(Frame frame) => frame.ObjectVersions
        .OrderBy(static pair => pair.Key)
        .Select(static pair => (object)new {
            ObjectId = pair.Key,
            pair.Value.Kind,
            pair.Value.PayloadBytes,
            pair.Value.ReconstructionObjectPayloadBytes,
            pair.Value.ResultBasePayloadBytes,
            pair.Value.ExpectedParentBasePayloadBytes,
            pair.Value.LogicalVersionOrdinal,
            pair.Value.ParentFrameTicket,
        })
        .ToArray();

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

    private static void AssertStoreSnapshot(StoreSnapshot expected, StoreSnapshot actual) {
        Assert.Equal(expected.FileCount, actual.FileCount);
        Assert.Equal(expected.Files, actual.Files);
    }

    private sealed record SourceFixture(
        RbfFileStore Store,
        RbfFile Previous,
        RbfFile Current,
        AbsoluteFrameAddress PublishedRevisionAddress,
        AbsoluteFrameAddress BbHeadAddress);

    private sealed record StoreSnapshot(int FileCount, IReadOnlyList<FileSnapshot> Files);

    private sealed record FileSnapshot(uint FileNumber, int FrameCount, long TailOffsetBytes);
}
