using Atelia.MultiSegmentStateStoreProbe.Model;
using Atelia.MultiSegmentStateStoreProbe.Planning;
using Atelia.MultiSegmentStateStoreProbe.Reconstruction;
using Atelia.MultiSegmentStateStoreProbe.Storage;

namespace Atelia.MultiSegmentStateStoreProbe.Tests;

public sealed class ObjectLineageReaderTests {
    private const uint ObjectId = 1;

    [Fact]
    public void Domain_Base_uses_shared_prior_OVD_then_Delta_uses_exact_parent() {
        InMemoryFrameCommitSession session = NewOneFramePerSegmentSession();
        LogicalObjectState v1 = State(10, 10, 1);
        PublishedFrameCommit f1 = CommitRevision(session, new(
            priorRevision: null,
            ObjectVersionDictionaryKind.Base,
            [OriginFreeObjectVersion.Base(ObjectId, v1)],
            [OriginFreeObjectVersionDictionaryEntry.BindSelf(ObjectId)]));

        LogicalObjectState v2 = State(11, 10, 2);
        PublishedFrameCommit f2 = CommitRevision(session, new(
            f1.PublishedHead,
            ObjectVersionDictionaryKind.Delta,
            [OriginFreeObjectVersion.Delta(
                ObjectId,
                payloadBytes: 1,
                v1,
                v2,
                f1.PublishedHead)],
            [OriginFreeObjectVersionDictionaryEntry.BindSelf(ObjectId)]));

        LogicalObjectState v3 = State(12, 12, 3);
        PublishedFrameCommit f3 = CommitRevision(session, new(
            f2.PublishedHead,
            ObjectVersionDictionaryKind.Delta,
            [OriginFreeObjectVersion.Base(ObjectId, v3)],
            [OriginFreeObjectVersionDictionaryEntry.BindSelf(ObjectId)]));

        ObjectLineage lineage = ObjectLineageReader.Read(
            session.Store,
            ObjectId,
            f3.PublishedHead);

        Assert.Equal(ObjectId, lineage.ObjectId);
        Assert.Equal(
            [f3.PublishedHead, f2.PublishedHead, f1.PublishedHead],
            lineage.Entries.Select(static entry => entry.Address));
        Assert.Equal(
            [3, 2, 1],
            lineage.Entries.Select(static entry =>
                entry.State.LogicalVersionOrdinal));
        Assert.Equal(
            [ObjectVersionKind.Base, ObjectVersionKind.Delta, ObjectVersionKind.Base],
            lineage.Entries.Select(static entry => entry.Kind));
    }

    [Fact]
    public void SameStateRebase_preserves_ordinal_and_exact_state() {
        InMemoryFrameCommitSession session = NewOneFramePerSegmentSession();
        LogicalObjectState state = State(10, 10, 1);
        PublishedFrameCommit f1 = CommitRevision(session, new(
            priorRevision: null,
            ObjectVersionDictionaryKind.Base,
            [OriginFreeObjectVersion.Base(ObjectId, state)],
            [OriginFreeObjectVersionDictionaryEntry.BindSelf(ObjectId)]));
        PublishedFrameCommit f2 = CommitRevision(session, new(
            f1.PublishedHead,
            ObjectVersionDictionaryKind.Delta,
            [OriginFreeObjectVersion.Base(ObjectId, state)],
            [OriginFreeObjectVersionDictionaryEntry.BindSelf(ObjectId)]));

        ObjectLineage lineage = ObjectLineageReader.Read(
            session.Store,
            ObjectId,
            f2.PublishedHead);

        Assert.Equal(
            [f2.PublishedHead, f1.PublishedHead],
            lineage.Entries.Select(static entry => entry.Address));
        Assert.Equal(
            [1, 1],
            lineage.Entries.Select(static entry =>
                entry.State.LogicalVersionOrdinal));
        Assert.All(lineage.Entries, entry => Assert.Equal(state, entry.State));
    }

    [Fact]
    public void Non_genesis_OVD_Base_absence_allows_an_ordinal_one_Insert_root() {
        InMemoryFrameCommitSession session = NewOneFramePerSegmentSession();
        PublishedFrameCommit f1 = CommitRevision(session, new(
            priorRevision: null,
            ObjectVersionDictionaryKind.Base,
            [OriginFreeObjectVersion.Base(2, State(20, 5, 1))],
            [OriginFreeObjectVersionDictionaryEntry.BindSelf(2)]));
        LogicalObjectState inserted = State(10, 10, 1);
        PublishedFrameCommit f2 = CommitRevision(session, new(
            f1.PublishedHead,
            ObjectVersionDictionaryKind.Base,
            [OriginFreeObjectVersion.Base(ObjectId, inserted)],
            [OriginFreeObjectVersionDictionaryEntry.BindSelf(ObjectId)]));

        ObjectLineage lineage = ObjectLineageReader.Read(
            session.Store,
            ObjectId,
            f2.PublishedHead);

        ObjectLineageEntry root = Assert.Single(lineage.Entries);
        Assert.Equal(f2.PublishedHead, root.Address);
        Assert.Equal(1, root.State.LogicalVersionOrdinal);
    }

    [Fact]
    public void Missing_Base_lineage_anchor_fails_query_but_not_current_load() {
        InMemoryFrameCommitSession session = NewOneFramePerSegmentSession();
        PublishedFrameCommit f1 = CommitRevision(session, new(
            priorRevision: null,
            ObjectVersionDictionaryKind.Base,
            [],
            []));
        AbsoluteFrameAddress missingPrior = new(
            f1.PublishedHead.FileNumber,
            new FrameTicket(8, 24));
        LogicalObjectState current = State(10, 10, 1);
        PublishedFrameCommit f2 = CommitRevision(session, new(
            missingPrior,
            ObjectVersionDictionaryKind.Base,
            [OriginFreeObjectVersion.Base(ObjectId, current)],
            [OriginFreeObjectVersionDictionaryEntry.BindSelf(ObjectId)]));

        MaterializedCurrentState loaded = CurrentStateMaterializer.Materialize(
            session.Store,
            f2.PublishedHead);

        Assert.Equal(current, loaded.States[ObjectId]);
        Assert.Throws<InvalidDataException>(() => ObjectLineageReader.Read(
            session.Store,
            ObjectId,
            f2.PublishedHead));
        Assert.Equal(current, loaded.States[ObjectId]);
    }

    [Fact]
    public void Removed_prior_ObjectId_cannot_restart_a_lineage() {
        InMemoryFrameCommitSession session = NewOneFramePerSegmentSession();
        PublishedFrameCommit f1 = CommitRevision(session, new(
            priorRevision: null,
            ObjectVersionDictionaryKind.Base,
            [OriginFreeObjectVersion.Base(ObjectId, State(10, 10, 1))],
            [OriginFreeObjectVersionDictionaryEntry.BindSelf(ObjectId)]));
        PublishedFrameCommit f2 = CommitRevision(session, new(
            f1.PublishedHead,
            ObjectVersionDictionaryKind.Delta,
            [],
            [OriginFreeObjectVersionDictionaryEntry.Remove(ObjectId)]));
        PublishedFrameCommit f3 = CommitRevision(session, new(
            f2.PublishedHead,
            ObjectVersionDictionaryKind.Base,
            [OriginFreeObjectVersion.Base(ObjectId, State(30, 10, 1))],
            [OriginFreeObjectVersionDictionaryEntry.BindSelf(ObjectId)]));

        MaterializedCurrentState loaded = CurrentStateMaterializer.Materialize(
            session.Store,
            f3.PublishedHead);
        Assert.Equal(30, loaded.States[ObjectId].Value);

        Assert.Throws<InvalidDataException>(() => ObjectLineageReader.Read(
            session.Store,
            ObjectId,
            f3.PublishedHead));
    }

    [Fact]
    public void Domain_Base_must_advance_prior_ordinal_exactly_once() {
        InMemoryFrameCommitSession session = NewOneFramePerSegmentSession();
        PublishedFrameCommit f1 = CommitRevision(session, new(
            priorRevision: null,
            ObjectVersionDictionaryKind.Base,
            [OriginFreeObjectVersion.Base(ObjectId, State(10, 10, 1))],
            [OriginFreeObjectVersionDictionaryEntry.BindSelf(ObjectId)]));
        PublishedFrameCommit f2 = CommitRevision(session, new(
            f1.PublishedHead,
            ObjectVersionDictionaryKind.Delta,
            [OriginFreeObjectVersion.Base(ObjectId, State(12, 10, 3))],
            [OriginFreeObjectVersionDictionaryEntry.BindSelf(ObjectId)]));

        Assert.Throws<InvalidDataException>(() => ObjectLineageReader.Read(
            session.Store,
            ObjectId,
            f2.PublishedHead));
    }

    [Fact]
    public void Later_Revision_without_PriorRevision_is_not_a_second_genesis() {
        InMemoryFrameCommitSession session = new(
            new InMemorySegmentStore(),
            rolloverThresholdBytes: 1_000_000);
        _ = CommitRevision(session, new(
            priorRevision: null,
            ObjectVersionDictionaryKind.Base,
            [],
            []));
        PublishedFrameCommit malformed = CommitRevision(session, new(
            priorRevision: null,
            ObjectVersionDictionaryKind.Base,
            [OriginFreeObjectVersion.Base(ObjectId, State(10, 10, 1))],
            [OriginFreeObjectVersionDictionaryEntry.BindSelf(ObjectId)]));

        MaterializedCurrentState loaded = CurrentStateMaterializer.Materialize(
            session.Store,
            malformed.PublishedHead);
        Assert.Equal(10, loaded.States[ObjectId].Value);

        Assert.Throws<InvalidDataException>(() => ObjectLineageReader.Read(
            session.Store,
            ObjectId,
            malformed.PublishedHead));
    }

    [Fact]
    public void Same_ordinal_Base_that_changes_state_is_not_a_SameStateRebase() {
        InMemoryFrameCommitSession session = NewOneFramePerSegmentSession();
        PublishedFrameCommit f1 = CommitRevision(session, new(
            priorRevision: null,
            ObjectVersionDictionaryKind.Base,
            [OriginFreeObjectVersion.Base(ObjectId, State(10, 10, 1))],
            [OriginFreeObjectVersionDictionaryEntry.BindSelf(ObjectId)]));
        PublishedFrameCommit f2 = CommitRevision(session, new(
            f1.PublishedHead,
            ObjectVersionDictionaryKind.Delta,
            [OriginFreeObjectVersion.Base(ObjectId, State(11, 10, 1))],
            [OriginFreeObjectVersionDictionaryEntry.BindSelf(ObjectId)]));

        Assert.Throws<InvalidDataException>(() => ObjectLineageReader.Read(
            session.Store,
            ObjectId,
            f2.PublishedHead));
    }

    [Fact]
    public void Genesis_and_Insert_roots_both_require_ordinal_one() {
        InMemoryFrameCommitSession genesisSession = NewOneFramePerSegmentSession();
        PublishedFrameCommit badGenesis = CommitRevision(genesisSession, new(
            priorRevision: null,
            ObjectVersionDictionaryKind.Base,
            [OriginFreeObjectVersion.Base(ObjectId, State(10, 10, 2))],
            [OriginFreeObjectVersionDictionaryEntry.BindSelf(ObjectId)]));
        Assert.Throws<InvalidDataException>(() => ObjectLineageReader.Read(
            genesisSession.Store,
            ObjectId,
            badGenesis.PublishedHead));

        InMemoryFrameCommitSession insertSession = NewOneFramePerSegmentSession();
        PublishedFrameCommit prior = CommitRevision(insertSession, new(
            priorRevision: null,
            ObjectVersionDictionaryKind.Base,
            [],
            []));
        PublishedFrameCommit badInsert = CommitRevision(insertSession, new(
            prior.PublishedHead,
            ObjectVersionDictionaryKind.Base,
            [OriginFreeObjectVersion.Base(ObjectId, State(10, 10, 2))],
            [OriginFreeObjectVersionDictionaryEntry.BindSelf(ObjectId)]));
        Assert.Throws<InvalidDataException>(() => ObjectLineageReader.Read(
            insertSession.Store,
            ObjectId,
            badInsert.PublishedHead));
    }

    [Fact]
    public void Prior_OVD_lookup_does_not_reconstruct_an_unrelated_broken_object() {
        InMemoryFrameCommitSession session = NewOneFramePerSegmentSession();
        PublishedFrameCommit f0 = CommitRevision(session, new(
            priorRevision: null,
            ObjectVersionDictionaryKind.Base,
            [],
            []));
        LogicalObjectState objectV1 = State(10, 10, 1);
        PublishedFrameCommit f1 = CommitRevision(session, new(
            f0.PublishedHead,
            ObjectVersionDictionaryKind.Base,
            [
                OriginFreeObjectVersion.Base(ObjectId, objectV1),
                OriginFreeObjectVersion.Delta(
                    objectId: 2,
                    payloadBytes: 1,
                    expectedParentState: State(20, 10, 1),
                    resultState: State(21, 10, 2),
                    f0.PublishedHead),
            ],
            [
                OriginFreeObjectVersionDictionaryEntry.BindSelf(ObjectId),
                OriginFreeObjectVersionDictionaryEntry.BindSelf(2),
            ]));
        Assert.Throws<InvalidDataException>(() => CurrentStateMaterializer.Materialize(
            session.Store,
            f1.PublishedHead));

        LogicalObjectState objectV2 = State(11, 10, 2);
        PublishedFrameCommit f2 = CommitRevision(session, new(
            f1.PublishedHead,
            ObjectVersionDictionaryKind.Base,
            [OriginFreeObjectVersion.Base(ObjectId, objectV2)],
            [OriginFreeObjectVersionDictionaryEntry.BindSelf(ObjectId)]));

        ObjectLineage lineage = ObjectLineageReader.Read(
            session.Store,
            ObjectId,
            f2.PublishedHead);

        Assert.Equal(
            [f2.PublishedHead, f1.PublishedHead],
            lineage.Entries.Select(static entry => entry.Address));
        Assert.Equal(
            [2, 1],
            lineage.Entries.Select(static entry =>
                entry.State.LogicalVersionOrdinal));
    }

    private static InMemoryFrameCommitSession NewOneFramePerSegmentSession() => new(
        new InMemorySegmentStore(),
        rolloverThresholdBytes: 32);

    private static LogicalObjectState State(int value, int bytes, int ordinal) =>
        new(value, bytes, ordinal);

    private static PublishedFrameCommit CommitRevision(
        InMemoryFrameCommitSession session,
        OriginFreeRevisionFramePlan revision) =>
        Assert.IsType<PublishedFrameCommit>(
            session.Commit(new OriginFreeFramePlan(revision)));
}
