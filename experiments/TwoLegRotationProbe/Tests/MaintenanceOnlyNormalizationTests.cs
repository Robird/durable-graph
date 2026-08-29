using Atelia.TwoLegRotationProbe.Encoding;
using Atelia.TwoLegRotationProbe.Model;
using Atelia.TwoLegRotationProbe.Planning;
using Atelia.TwoLegRotationProbe.Workloads;

namespace Atelia.TwoLegRotationProbe.Tests;

public sealed class MaintenanceOnlyNormalizationTests {
    [Fact]
    public void Nonempty_parent_becomes_canonical_all_NoChange_facts() {
        const uint previousObjectId = 10;
        const uint currentObjectId = 20;
        RbfFileStore store = new();
        RbfFile previous = store.CreateFile();
        FrameBuilder previousBuilder = NewRevisionBuilder();
        AddBase(previousBuilder, previousObjectId, payloadBytes: 10);
        previousBuilder.ObjectVersionDictionary!.BindSelf(previousObjectId);
        AbsoluteFrameAddress previousRevision = AppendRevision(
            previous,
            previousBuilder);

        RbfFile current = store.CreateFile();
        FrameBuilder publishedBuilder = NewRevisionBuilder(
            Previous(previousRevision.FrameTicket));
        AddBase(publishedBuilder, currentObjectId, payloadBytes: 20);
        publishedBuilder.ObjectVersionDictionary!.BindSelf(currentObjectId);
        AbsoluteFrameAddress publishedRevision = AppendRevision(
            current,
            publishedBuilder);

        NormalizedSaveFacts facts = SaveStepNormalizer.NormalizeMaintenanceOnly(
            store,
            current.FileNumber,
            publishedRevision);

        Assert.Equal(previous.FileNumber, facts.PreviousFileNumber);
        Assert.Equal(current.FileNumber, facts.CurrentFileNumber);
        Assert.Equal(publishedRevision, facts.PublishedRevisionAddress);
        Assert.Empty(facts.Inserts);
        Assert.Empty(facts.Updates);
        Assert.Empty(facts.Removes);
        Assert.Equal(
            [previousObjectId, currentObjectId],
            facts.AllFacts.Select(static fact => fact.ObjectId));
        Assert.Equal(
            [previousObjectId, currentObjectId],
            facts.NoChanges.Select(static fact => fact.ObjectId));
        Assert.All(facts.AllFacts, static fact =>
            Assert.IsType<NormalizedNoChangeFact>(fact));
        AssertExactState(facts.ParentLiveStates, facts.PostLiveStates);
        Assert.Equal(
            previousRevision,
            facts.ParentLive[previousObjectId].HeadAddress);
        Assert.Equal(
            publishedRevision,
            facts.ParentLive[currentObjectId].HeadAddress);
    }

    [Fact]
    public void Empty_parent_produces_empty_maintenance_facts() {
        RbfFileStore store = new();
        RbfFile previous = store.CreateFile();
        AbsoluteFrameAddress previousRevision = AppendRevision(
            previous,
            NewRevisionBuilder());
        RbfFile current = store.CreateFile();
        AbsoluteFrameAddress publishedRevision = AppendRevision(
            current,
            NewRevisionBuilder(Previous(previousRevision.FrameTicket)));

        NormalizedSaveFacts facts = SaveStepNormalizer.NormalizeMaintenanceOnly(
            store,
            current.FileNumber,
            publishedRevision);

        Assert.Empty(facts.ParentLive);
        Assert.Empty(facts.AllFacts);
        Assert.Empty(facts.Inserts);
        Assert.Empty(facts.Updates);
        Assert.Empty(facts.Removes);
        Assert.Empty(facts.NoChanges);
        Assert.Empty(facts.ParentLiveStates);
        Assert.Empty(facts.PostLiveStates);
    }

    [Fact]
    public void OVD_chain_outside_AB_fails_without_mutating_store() {
        RbfFileStore store = new();
        RbfFile older = store.CreateFile();
        AbsoluteFrameAddress olderRevision = AppendRevision(
            older,
            NewRevisionBuilder());
        RbfFile previous = store.CreateFile();
        AbsoluteFrameAddress previousRevision = AppendRevision(
            previous,
            NewRevisionBuilder(Previous(olderRevision.FrameTicket)));
        RbfFile current = store.CreateFile();
        AbsoluteFrameAddress publishedRevision = AppendRevision(
            current,
            NewRevisionBuilder(Previous(previousRevision.FrameTicket)));
        StoreSnapshot before = CaptureStore(store);

        InvalidDataException exception = Assert.Throws<InvalidDataException>(() =>
            SaveStepNormalizer.NormalizeMaintenanceOnly(
                store,
                current.FileNumber,
                publishedRevision));

        Assert.Contains("OVD chain", exception.Message, StringComparison.Ordinal);
        AssertStoreSnapshot(before, CaptureStore(store));
    }

    private static FrameBuilder NewRevisionBuilder(
        RelativeFrameTicket? parent = null) {
        ObjectVersionDictionaryBuilder dictionary = new();
        if (parent is RelativeFrameTicket parentRevision) {
            dictionary.Kind = ObjectVersionDictionaryKind.Delta;
            dictionary.ParentRevisionFrameTicket = parentRevision;
        }

        return new FrameBuilder() { ObjectVersionDictionary = dictionary };
    }

    private static void AddBase(
        FrameBuilder frame,
        uint objectId,
        int payloadBytes) {
        RevisionCandidateRecordBuilder.AddBase(
            frame,
            objectId,
            new LogicalObjectState(payloadBytes, LogicalVersionOrdinal: 1));
    }

    private static AbsoluteFrameAddress AppendRevision(
        RbfFile file,
        FrameBuilder builder) {
        Frame frame = builder.Build();
        ProvisionalRevisionV0Estimate estimate =
            ProvisionalRevisionV0Estimator.Estimate(frame, file.TailOffsetBytes);
        FrameTicket ticket = file.Append(
            frame,
            estimate.RbfLayout.PayloadLengthBytes,
            estimate.RbfLayout.TailMetaLengthBytes);
        Assert.Equal(estimate.RbfLayout.Ticket, ticket);
        return new AbsoluteFrameAddress(file.FileNumber, ticket);
    }

    private static RelativeFrameTicket Previous(FrameTicket ticket) =>
        new(IsPreviousFile: true, ticket);

    private static void AssertExactState(
        IReadOnlyDictionary<uint, LogicalObjectState> expected,
        IReadOnlyDictionary<uint, LogicalObjectState> actual) {
        Assert.Equal(expected.Count, actual.Count);
        foreach ((uint objectId, LogicalObjectState expectedState) in expected) {
            Assert.True(actual.TryGetValue(
                objectId,
                out LogicalObjectState actualState));
            Assert.Equal(expectedState, actualState);
        }
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

    private static void AssertStoreSnapshot(
        StoreSnapshot expected,
        StoreSnapshot actual) {
        Assert.Equal(expected.FileCount, actual.FileCount);
        Assert.Equal(expected.Files, actual.Files);
    }

    private sealed record StoreSnapshot(
        int FileCount,
        IReadOnlyList<FileSnapshot> Files);

    private sealed record FileSnapshot(
        uint FileNumber,
        int FrameCount,
        long TailOffsetBytes);
}
