using Atelia.TwoLegRotationProbe.Encoding;
using Atelia.TwoLegRotationProbe.Model;
using Atelia.TwoLegRotationProbe.Rotation;
using Atelia.TwoLegRotationProbe.Simulation;
using Atelia.TwoLegRotationProbe.Workloads;

namespace Atelia.TwoLegRotationProbe.Tests;

public sealed class ImmediateRotationAppenderTests {
    private const uint AaObjectId = 11;
    private const uint BaObjectId = 22;
    private const uint BbObjectId = 33;

    [Fact]
    public void Canonical_plan_appends_one_runtime_C_revision_and_closes_all_oracles() {
        SourceFixture source = CreateCanonicalSource();
        ObjectVersionDictionaryMaterializationInspection sourceOvd =
            ObjectVersionDictionaryReader.MaterializeLive(
                source.Store,
                source.PublishedRevisionAddress);
        IReadOnlyDictionary<uint, LogicalObjectState> sourceState =
            PhysicalStateOracle.Materialize(source.Store, sourceOvd.Bindings);
        StoreSnapshot before = CaptureStore(source.Store);
        ImmediateRotationPlan plan = ImmediateRotationPlanner.Create(
            source.Store,
            source.Current.FileNumber,
            source.PublishedRevisionAddress);

        AbsoluteFrameAddress appended = ImmediateRotationAppender.AppendToFreshNextFile(
            source.Store,
            plan);

        Assert.Equal(plan.EvacuationRevision.Address, appended);
        Assert.Equal(3, source.Store.FileCount);
        Assert.Equal(before.Files, CaptureStore(source.Store).Files.Take(2));
        RbfFile next = source.Store.GetFile(plan.NextFileNumber);
        Assert.Equal(1, next.FrameCount);
        Assert.Equal(plan.EvacuationRevision.Estimate.RbfLayout.TailOffsetAfterBytes,
            next.TailOffsetBytes);
        Assert.Same(
            plan.EvacuationRevision.Frame,
            source.Store.ReadFrame(appended));
        Assert.Equal(
            plan.EvacuationRevision.Estimate.RbfLayout,
            source.Store.ReadLayout(appended));
        AssertEquivalentEstimate(
            plan.EvacuationRevision.Estimate,
            ProvisionalRevisionV0Estimator.Estimate(
                source.Store.ReadFrame(appended),
                appended.FrameTicket.OffsetBytes));

        ObjectVersionDictionaryMaterializationInspection currentOvd =
            ObjectVersionDictionaryReader.MaterializeLive(source.Store, appended);
        Assert.Equal([appended], currentOvd.DictionaryRevisionAddresses);
        Assert.Equal(
            [
                KeyValuePair.Create(AaObjectId, appended),
                KeyValuePair.Create(BaObjectId, appended),
                KeyValuePair.Create(BbObjectId, source.BbHeadAddress),
            ],
            currentOvd.Bindings.ToArray());
        AssertExactState(
            sourceState,
            PhysicalStateOracle.Materialize(source.Store, currentOvd.Bindings));

        Assert.Equal(
            [appended],
            PhysicalStateOracle.InspectObjectReconstruction(
                source.Store,
                AaObjectId,
                currentOvd.Bindings[AaObjectId]).ReconstructionFrameAddresses);
        Assert.Equal(
            [appended],
            PhysicalStateOracle.InspectObjectReconstruction(
                source.Store,
                BaObjectId,
                currentOvd.Bindings[BaObjectId]).ReconstructionFrameAddresses);
        Assert.Equal(
            [source.BbHeadAddress],
            PhysicalStateOracle.InspectObjectReconstruction(
                source.Store,
                BbObjectId,
                currentOvd.Bindings[BbObjectId]).ReconstructionFrameAddresses);

        ObjectLineageInspection aaLineage = PhysicalStateOracle.InspectObjectLineage(
            source.Store,
            AaObjectId,
            currentOvd.Bindings[AaObjectId]);
        Assert.Equal(new LogicalObjectState(10, 1), aaLineage.HeadState);
        Assert.Equal(source.PreviousRevisionAddress, aaLineage.RootAddress);
        Assert.Equal(
            [appended, source.PreviousRevisionAddress],
            aaLineage.ObjectVersionLineageAddresses);
        Assert.Equal(
            [source.PublishedRevisionAddress, source.PreviousRevisionAddress],
            Assert.Single(aaLineage.BaseParentLookups).DictionaryRevisionAddresses);

        ObjectLineageInspection baLineage = PhysicalStateOracle.InspectObjectLineage(
            source.Store,
            BaObjectId,
            currentOvd.Bindings[BaObjectId]);
        Assert.Equal(new LogicalObjectState(25, 2), baLineage.HeadState);
        Assert.Equal(source.PreviousRevisionAddress, baLineage.RootAddress);
        Assert.Equal(
            [appended, source.PublishedRevisionAddress, source.PreviousRevisionAddress],
            baLineage.ObjectVersionLineageAddresses);
        Assert.Equal(
            [source.PublishedRevisionAddress],
            Assert.Single(baLineage.BaseParentLookups).DictionaryRevisionAddresses);
    }

    [Fact]
    public void Empty_graph_appends_a_legal_runtime_C_revision() {
        RbfFileStore store = new();
        _ = store.CreateFile();
        RbfFile current = store.CreateFile();
        AbsoluteFrameAddress published = Append(
            current,
            new FrameBuilder {
                ObjectVersionDictionary = new ObjectVersionDictionaryBuilder(),
            });
        ImmediateRotationPlan plan = ImmediateRotationPlanner.Create(
            store,
            current.FileNumber,
            published);

        AbsoluteFrameAddress appended = ImmediateRotationAppender.AppendToFreshNextFile(
            store,
            plan);

        ObjectVersionDictionaryMaterializationInspection materialized =
            ObjectVersionDictionaryReader.MaterializeLive(store, appended);
        Assert.Empty(materialized.Bindings);
        Assert.Equal([appended], materialized.DictionaryRevisionAddresses);
        Assert.Empty(store.ReadFrame(appended).ObjectVersions);
    }

    [Fact]
    public void Reapply_or_preexisting_C_fails_without_further_mutation() {
        SourceFixture appliedSource = CreateCanonicalSource();
        ImmediateRotationPlan appliedPlan = ImmediateRotationPlanner.Create(
            appliedSource.Store,
            appliedSource.Current.FileNumber,
            appliedSource.PublishedRevisionAddress);
        _ = ImmediateRotationAppender.AppendToFreshNextFile(
            appliedSource.Store,
            appliedPlan);
        StoreSnapshot afterFirstApply = CaptureStore(appliedSource.Store);

        Assert.Throws<InvalidDataException>(() =>
            ImmediateRotationAppender.AppendToFreshNextFile(
                appliedSource.Store,
                appliedPlan));
        AssertStoreSnapshot(afterFirstApply, CaptureStore(appliedSource.Store));

        SourceFixture precreatedSource = CreateCanonicalSource();
        ImmediateRotationPlan precreatedPlan = ImmediateRotationPlanner.Create(
            precreatedSource.Store,
            precreatedSource.Current.FileNumber,
            precreatedSource.PublishedRevisionAddress);
        _ = precreatedSource.Store.CreateFile();
        StoreSnapshot beforeRejectedApply = CaptureStore(precreatedSource.Store);

        Assert.Throws<InvalidDataException>(() =>
            ImmediateRotationAppender.AppendToFreshNextFile(
                precreatedSource.Store,
                precreatedPlan));
        AssertStoreSnapshot(beforeRejectedApply, CaptureStore(precreatedSource.Store));
    }

    [Fact]
    public void Missing_source_revision_fails_before_creating_C() {
        SourceFixture source = CreateCanonicalSource();
        ImmediateRotationPlan plan = ImmediateRotationPlanner.Create(
            source.Store,
            source.Current.FileNumber,
            source.PublishedRevisionAddress);
        RbfFileStore wrongStore = new();
        _ = wrongStore.CreateFile();
        _ = wrongStore.CreateFile();
        StoreSnapshot before = CaptureStore(wrongStore);

        Assert.Throws<InvalidDataException>(() =>
            ImmediateRotationAppender.AppendToFreshNextFile(wrongStore, plan));

        AssertStoreSnapshot(before, CaptureStore(wrongStore));
    }

    [Fact]
    public void Atomic_first_frame_creation_rejects_wrong_layout_without_an_empty_file() {
        RbfFileStore store = new();
        _ = store.CreateFile();
        Frame frame = new FrameBuilder {
            ObjectVersionDictionary = new ObjectVersionDictionaryBuilder(),
        }.Build();
        RbfFrameLayoutEstimate wrongStart = RbfV040Layout.Estimate(
            frameStartOffsetBytes: 8,
            payloadLengthBytes: 1,
            tailMetaLengthBytes: 0);

        Assert.Throws<InvalidDataException>(() =>
            store.CreateFileWithFirstFrame(frame, wrongStart));

        Assert.Equal(1, store.FileCount);
        Assert.Equal(0, store.GetFile(1).FrameCount);
    }

    private static SourceFixture CreateCanonicalSource() {
        RbfFileStore store = new();
        RbfFile previous = store.CreateFile();
        ObjectVersionDictionaryBuilder previousOvd = new();
        previousOvd.BindSelf(AaObjectId);
        previousOvd.BindSelf(BaObjectId);
        FrameBuilder previousBuilder = new() { ObjectVersionDictionary = previousOvd };
        AddBase(previousBuilder, AaObjectId, payloadBytes: 10);
        AddBase(previousBuilder, BaObjectId, payloadBytes: 20);
        AbsoluteFrameAddress previousAddress = Append(previous, previousBuilder);

        RbfFile current = store.CreateFile();
        ObjectVersionDictionaryBuilder currentOvd = new() {
            Kind = ObjectVersionDictionaryKind.Delta,
            ParentRevisionFrameTicket = Previous(previousAddress.FrameTicket),
        };
        currentOvd.BindSelf(BaObjectId);
        currentOvd.BindSelf(BbObjectId);
        FrameBuilder currentBuilder = new() { ObjectVersionDictionary = currentOvd };
        AddDelta(
            currentBuilder,
            BaObjectId,
            Previous(previousAddress.FrameTicket),
            parentBasePayloadBytes: 20,
            resultBasePayloadBytes: 25,
            deltaPayloadBytes: 5,
            reconstructionPayloadBytes: 25,
            logicalVersionOrdinal: 2);
        AddBase(currentBuilder, BbObjectId, payloadBytes: 30);
        AbsoluteFrameAddress currentAddress = Append(current, currentBuilder);
        return new SourceFixture(
            store,
            previous,
            current,
            previousAddress,
            currentAddress,
            currentAddress);
    }

    private static void AddBase(
        FrameBuilder frame,
        uint objectId,
        int payloadBytes) {
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

    private static void AssertExactState(
        IReadOnlyDictionary<uint, LogicalObjectState> expected,
        IReadOnlyDictionary<uint, LogicalObjectState> actual) {
        Assert.Equal(expected.Count, actual.Count);
        foreach ((uint objectId, LogicalObjectState expectedState) in expected) {
            Assert.True(actual.TryGetValue(objectId, out LogicalObjectState actualState));
            Assert.Equal(expectedState, actualState);
        }
    }

    private static void AssertEquivalentEstimate(
        ProvisionalRevisionV0Estimate expected,
        ProvisionalRevisionV0Estimate actual) {
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

    private static AbsoluteFrameAddress Append(RbfFile file, FrameBuilder builder) =>
        new(file.FileNumber, file.Append(builder.Build()));

    private static RelativeFrameTicket Previous(FrameTicket ticket) =>
        new(IsPreviousFile: true, ticket);

    private static StoreSnapshot CaptureStore(RbfFileStore store) => new(
        store.FileCount,
        Enumerable.Range(1, store.FileCount)
            .Select(index => store.GetFile((uint)index))
            .Select(static file => new FileSnapshot(
                file.FileNumber,
                file.FrameCount,
                file.TailOffsetBytes))
            .ToArray());

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
        AbsoluteFrameAddress PreviousRevisionAddress,
        AbsoluteFrameAddress PublishedRevisionAddress,
        AbsoluteFrameAddress BbHeadAddress);

    private sealed record StoreSnapshot(int FileCount, IReadOnlyList<FileSnapshot> Files);

    private sealed record FileSnapshot(uint FileNumber, int FrameCount, long TailOffsetBytes);
}
