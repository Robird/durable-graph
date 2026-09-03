using Atelia.MultiSegmentStateStoreProbe.Model;
using Atelia.MultiSegmentStateStoreProbe.Planning;
using Atelia.MultiSegmentStateStoreProbe.Reconstruction;
using Atelia.MultiSegmentStateStoreProbe.Storage;

namespace Atelia.MultiSegmentStateStoreProbe.Tests;

public sealed class CurrentStateMaterializerTests {
    private const uint ColdObjectId = 1;
    private const uint HotObjectId = 2;
    private const uint RemovedObjectId = 3;

    [Fact]
    public void F1_through_F4_materializes_exact_current_state_across_arbitrary_history() {
        InMemoryFrameCommitSession session = NewOneFramePerSegmentSession();
        LogicalObjectState coldV1 = State(value: 10, bytes: 10, ordinal: 1);
        LogicalObjectState hotV1 = State(value: 20, bytes: 20, ordinal: 1);
        LogicalObjectState removedV1 = State(value: 30, bytes: 5, ordinal: 1);

        PublishedFrameCommit f1 = CommitRevision(session, new(
            priorRevision: null,
            ObjectVersionDictionaryKind.Base,
            [
                OriginFreeObjectVersion.Base(ColdObjectId, coldV1),
                OriginFreeObjectVersion.Base(HotObjectId, hotV1),
                OriginFreeObjectVersion.Base(RemovedObjectId, removedV1),
            ],
            [
                OriginFreeObjectVersionDictionaryEntry.BindSelf(ColdObjectId),
                OriginFreeObjectVersionDictionaryEntry.BindSelf(HotObjectId),
                OriginFreeObjectVersionDictionaryEntry.BindSelf(RemovedObjectId),
            ]));

        LogicalObjectState hotV2 = State(value: 21, bytes: 20, ordinal: 2);
        PublishedFrameCommit f2 = CommitRevision(session, new(
            f1.PublishedHead,
            ObjectVersionDictionaryKind.Delta,
            [OriginFreeObjectVersion.Delta(
                HotObjectId,
                payloadBytes: 1,
                hotV1,
                hotV2,
                f1.PublishedHead)],
            [OriginFreeObjectVersionDictionaryEntry.BindSelf(HotObjectId)]));

        LogicalObjectState hotV3 = State(value: 22, bytes: 22, ordinal: 3);
        PublishedFrameCommit f3 = CommitRevision(session, new(
            f2.PublishedHead,
            ObjectVersionDictionaryKind.Delta,
            [OriginFreeObjectVersion.Delta(
                HotObjectId,
                payloadBytes: 2,
                hotV2,
                hotV3,
                f2.PublishedHead)],
            [
                OriginFreeObjectVersionDictionaryEntry.BindSelf(HotObjectId),
                OriginFreeObjectVersionDictionaryEntry.Remove(RemovedObjectId),
            ]));

        LogicalObjectState hotV4 = State(value: 23, bytes: 22, ordinal: 4);
        PublishedFrameCommit f4 = CommitRevision(session, new(
            f3.PublishedHead,
            ObjectVersionDictionaryKind.Base,
            [OriginFreeObjectVersion.Delta(
                HotObjectId,
                payloadBytes: 1,
                hotV3,
                hotV4,
                f3.PublishedHead)],
            [
                OriginFreeObjectVersionDictionaryEntry.External(
                    ColdObjectId,
                    f1.PublishedHead),
                OriginFreeObjectVersionDictionaryEntry.BindSelf(HotObjectId),
            ]));

        Assert.Equal([1u, 2u, 3u, 4u], new[] { f1, f2, f3, f4 }
            .Select(commit => commit.PublishedHead.FileNumber.Value));
        Assert.Equal(f4.PublishedHead, session.PublishedHead);

        MaterializedCurrentState result = CurrentStateMaterializer.Materialize(
            session.Store,
            f4.PublishedHead);

        Assert.Equal([ColdObjectId, HotObjectId], result.States.Keys);
        Assert.Equal(coldV1, result.States[ColdObjectId]);
        Assert.Equal(hotV4, result.States[HotObjectId]);
        Assert.Equal(f1.PublishedHead, result.ObjectVersionHeads[ColdObjectId]);
        Assert.Equal(f4.PublishedHead, result.ObjectVersionHeads[HotObjectId]);
        Assert.Equal([f4.PublishedHead], result.OvdRevisionAddresses);
        Assert.Equal(
            [f1.PublishedHead],
            result.ObjectReconstructionPaths[ColdObjectId]);
        Assert.Equal(
            [
                f4.PublishedHead,
                f3.PublishedHead,
                f2.PublishedHead,
                f1.PublishedHead,
            ],
            result.ObjectReconstructionPaths[HotObjectId]);

        RevisionFrame renderedF4 = Assert.IsType<RevisionFrame>(
            f4.AppendedCandidate.RevisionFrame);
        AbsoluteFrameAddress resolvedCold = new FileScope(
            f4.PublishedHead.FileNumber).ResolveEarlier(
                f4.PublishedHead.FrameTicket,
                Assert.IsType<RelativeFrameTicket>(renderedF4
                    .ObjectVersionDictionary.Entries[ColdObjectId]
                    .ExternalReference));
        Assert.Equal(f1.PublishedHead, resolvedCold);
        Assert.Equal(3u, renderedF4.ObjectVersionDictionary
            .Entries[ColdObjectId].ExternalReference!.Value.BackwardFileDistance);
        Assert.Equal(ObjectVersionDictionaryKind.Delta,
            f2.AppendedCandidate.RevisionFrame!.ObjectVersionDictionary.Kind);
        Assert.Equal(ObjectVersionDictionaryKind.Delta,
            f3.AppendedCandidate.RevisionFrame!.ObjectVersionDictionary.Kind);
        Assert.Equal(ObjectVersionDictionaryKind.Base,
            renderedF4.ObjectVersionDictionary.Kind);
    }

    [Fact]
    public void Ovd_canonical_binding_modes_reject_External_in_Delta_and_Remove_in_Base() {
        RelativeFrameTicket reference = new(
            backwardFileDistance: 1,
            new FrameTicket(4, 24));

        Assert.Throws<ArgumentException>(() => new ObjectVersionDictionary(
            ObjectVersionDictionaryKind.Delta,
            [new KeyValuePair<uint, ObjectVersionDictionaryBinding>(
                1,
                ObjectVersionDictionaryBinding.External(reference))]));
        Assert.Throws<ArgumentException>(() => new ObjectVersionDictionary(
            ObjectVersionDictionaryKind.Base,
            [new KeyValuePair<uint, ObjectVersionDictionaryBinding>(
                1,
                ObjectVersionDictionaryBinding.Remove())]));
    }

    [Fact]
    public void Ovd_Delta_without_shared_PriorRevision_fails_closed() {
        InMemoryFrameCommitSession session = NewOneFramePerSegmentSession();
        PublishedFrameCommit head = CommitRevision(session, new(
            priorRevision: null,
            ObjectVersionDictionaryKind.Delta,
            [],
            []));

        Assert.Throws<InvalidDataException>(() =>
            CurrentStateMaterializer.Materialize(session.Store, head.PublishedHead));
    }

    [Fact]
    public void Missing_current_required_External_dependency_fails_closed() {
        InMemoryFrameCommitSession session = NewOneFramePerSegmentSession();
        PublishedFrameCommit f1 = CommitRevision(session, EmptyBase());
        AbsoluteFrameAddress missing = new(
            f1.PublishedHead.FileNumber,
            new FrameTicket(8, 24));
        PublishedFrameCommit f2 = CommitRevision(session, new(
            f1.PublishedHead,
            ObjectVersionDictionaryKind.Base,
            [],
            [OriginFreeObjectVersionDictionaryEntry.External(1, missing)]));

        Assert.Throws<InvalidDataException>(() =>
            CurrentStateMaterializer.Materialize(session.Store, f2.PublishedHead));
    }

    [Fact]
    public void Delta_parent_requires_the_exact_same_ObjectId_and_parent_state() {
        InMemoryFrameCommitSession wrongIdSession = NewOneFramePerSegmentSession();
        LogicalObjectState object2V1 = State(20, 10, 1);
        PublishedFrameCommit wrongIdF1 = CommitRevision(wrongIdSession, new(
            priorRevision: null,
            ObjectVersionDictionaryKind.Base,
            [OriginFreeObjectVersion.Base(2, object2V1)],
            [OriginFreeObjectVersionDictionaryEntry.BindSelf(2)]));
        PublishedFrameCommit wrongIdF2 = CommitRevision(wrongIdSession, new(
            wrongIdF1.PublishedHead,
            ObjectVersionDictionaryKind.Delta,
            [OriginFreeObjectVersion.Delta(
                objectId: 1,
                payloadBytes: 1,
                State(10, 10, 1),
                State(11, 10, 2),
                wrongIdF1.PublishedHead)],
            [OriginFreeObjectVersionDictionaryEntry.BindSelf(1)]));

        Assert.Throws<InvalidDataException>(() => CurrentStateMaterializer.Materialize(
            wrongIdSession.Store,
            wrongIdF2.PublishedHead));

        InMemoryFrameCommitSession wrongStateSession = NewOneFramePerSegmentSession();
        LogicalObjectState actualV1 = State(10, 10, 1);
        PublishedFrameCommit wrongStateF1 = CommitRevision(wrongStateSession, new(
            priorRevision: null,
            ObjectVersionDictionaryKind.Base,
            [OriginFreeObjectVersion.Base(1, actualV1)],
            [OriginFreeObjectVersionDictionaryEntry.BindSelf(1)]));
        PublishedFrameCommit wrongStateF2 = CommitRevision(wrongStateSession, new(
            wrongStateF1.PublishedHead,
            ObjectVersionDictionaryKind.Delta,
            [OriginFreeObjectVersion.Delta(
                objectId: 1,
                payloadBytes: 1,
                expectedParentState: State(10, 10, 2),
                resultState: State(11, 10, 3),
                wrongStateF1.PublishedHead)],
            [OriginFreeObjectVersionDictionaryEntry.BindSelf(1)]));

        Assert.Throws<InvalidDataException>(() => CurrentStateMaterializer.Materialize(
            wrongStateSession.Store,
            wrongStateF2.PublishedHead));
    }

    [Fact]
    public void Base_PriorRevision_is_validated_but_missing_lineage_target_is_not_read() {
        InMemoryFrameCommitSession session = NewOneFramePerSegmentSession();
        PublishedFrameCommit f1 = CommitRevision(session, EmptyBase());
        AbsoluteFrameAddress missingLineageTarget = new(
            f1.PublishedHead.FileNumber,
            new FrameTicket(8, 24));
        LogicalObjectState current = State(42, 12, 2);
        PublishedFrameCommit f2 = CommitRevision(session, new(
            missingLineageTarget,
            ObjectVersionDictionaryKind.Base,
            [OriginFreeObjectVersion.Base(1, current)],
            [OriginFreeObjectVersionDictionaryEntry.BindSelf(1)]));

        MaterializedCurrentState result = CurrentStateMaterializer.Materialize(
            session.Store,
            f2.PublishedHead);

        Assert.Equal(current, Assert.Single(result.States).Value);
        Assert.Equal([f2.PublishedHead], result.OvdRevisionAddresses);
    }

    [Fact]
    public void Base_PriorRevision_underflow_and_same_frame_back_edge_fail_closed() {
        InMemorySegmentStore underflowStore = new();
        AbsoluteFrameAddress underflowHead = AppendMalformedRevision(
            underflowStore,
            (candidate) => new RevisionFrame(
                new RelativeFrameTicket(1, candidate.Layout.Ticket),
                EmptyDictionary(ObjectVersionDictionaryKind.Base),
                []));
        Assert.Throws<InvalidDataException>(() =>
            CurrentStateMaterializer.Materialize(underflowStore, underflowHead));

        InMemorySegmentStore sameFrameStore = new();
        AbsoluteFrameAddress sameFrameHead = AppendMalformedRevision(
            sameFrameStore,
            (candidate) => new RevisionFrame(
                new RelativeFrameTicket(0, candidate.Layout.Ticket),
                EmptyDictionary(ObjectVersionDictionaryKind.Base),
                []));
        Assert.Throws<InvalidDataException>(() =>
            CurrentStateMaterializer.Materialize(sameFrameStore, sameFrameHead));
    }

    [Fact]
    public void Object_Delta_same_frame_parent_fails_closed_at_the_reader() {
        InMemorySegmentStore store = new();
        AbsoluteFrameAddress head = AppendMalformedRevision(
            store,
            candidate => {
                LogicalObjectState parent = State(10, 10, 1);
                ObjectVersion version = new(
                    objectId: 1,
                    ObjectVersionKind.Delta,
                    payloadBytes: 1,
                    resultState: State(11, 10, 2),
                    expectedParentState: parent,
                    deltaParent: new RelativeFrameTicket(
                        0,
                        candidate.Layout.Ticket));
                ObjectVersionDictionary dictionary = new(
                    ObjectVersionDictionaryKind.Base,
                    [new KeyValuePair<uint, ObjectVersionDictionaryBinding>(
                        1,
                        ObjectVersionDictionaryBinding.BindSelf())]);
                return new RevisionFrame(null, dictionary, [version]);
            });

        Assert.Throws<InvalidDataException>(() =>
            CurrentStateMaterializer.Materialize(store, head));
    }

    [Fact]
    public void Malformed_cycle_shape_is_rejected_by_the_strictly_earlier_gate() {
        InMemorySegmentStore store = new();
        AbsoluteFrameAddress head = AppendMalformedRevision(
            store,
            (candidate) => new RevisionFrame(
                new RelativeFrameTicket(0, candidate.Layout.Ticket),
                EmptyDictionary(ObjectVersionDictionaryKind.Delta),
                []));

        Assert.Throws<InvalidDataException>(() =>
            CurrentStateMaterializer.Materialize(store, head));
    }

    [Fact]
    public void Non_Revision_Frame_at_the_exact_head_fails_closed() {
        InMemoryFrameCommitSession session = NewOneFramePerSegmentSession();
        PublishedFrameCommit untyped = Assert.IsType<PublishedFrameCommit>(
            session.Commit(new OriginFreeFramePlan(0)));

        Assert.Null(untyped.AppendedCandidate.RevisionFrame);
        Assert.Throws<InvalidDataException>(() => CurrentStateMaterializer.Materialize(
            session.Store,
            untyped.PublishedHead));
    }

    [Fact]
    public void Failed_materialization_never_replaces_a_previously_exposed_complete_state() {
        InMemoryFrameCommitSession session = NewOneFramePerSegmentSession();
        LogicalObjectState object1 = State(10, 10, 1);
        PublishedFrameCommit f1 = CommitRevision(session, new(
            priorRevision: null,
            ObjectVersionDictionaryKind.Base,
            [OriginFreeObjectVersion.Base(1, object1)],
            [OriginFreeObjectVersionDictionaryEntry.BindSelf(1)]));
        MaterializedCurrentState exposed = CurrentStateMaterializer.Materialize(
            session.Store,
            f1.PublishedHead);

        AbsoluteFrameAddress missing = new(
            f1.PublishedHead.FileNumber,
            new FrameTicket(8, 24));
        PublishedFrameCommit f2 = CommitRevision(session, new(
            f1.PublishedHead,
            ObjectVersionDictionaryKind.Base,
            [],
            [
                OriginFreeObjectVersionDictionaryEntry.External(1, f1.PublishedHead),
                OriginFreeObjectVersionDictionaryEntry.External(2, missing),
            ]));

        Assert.Throws<InvalidDataException>(() => exposed =
            CurrentStateMaterializer.Materialize(session.Store, f2.PublishedHead));
        Assert.Equal(f1.PublishedHead, exposed.PublishedHead);
        Assert.Equal(object1, Assert.Single(exposed.States).Value);
    }

    private static InMemoryFrameCommitSession NewOneFramePerSegmentSession() => new(
        new InMemorySegmentStore(),
        rolloverThresholdBytes: 32);

    private static OriginFreeRevisionFramePlan EmptyBase() => new(
        priorRevision: null,
        ObjectVersionDictionaryKind.Base,
        [],
        []);

    private static ObjectVersionDictionary EmptyDictionary(
        ObjectVersionDictionaryKind kind) => new(kind, []);

    private static LogicalObjectState State(int value, int bytes, int ordinal) =>
        new(value, bytes, ordinal);

    private static PublishedFrameCommit CommitRevision(
        InMemoryFrameCommitSession session,
        OriginFreeRevisionFramePlan revision) =>
        Assert.IsType<PublishedFrameCommit>(
            session.Commit(new OriginFreeFramePlan(revision)));

    private static AbsoluteFrameAddress AppendMalformedRevision(
        InMemorySegmentStore store,
        Func<RenderedFrameCandidate, RevisionFrame> createRevision) {
        FileNumber fileNumber = store.AppendFileNumber;
        long frameStart = store.CurrentSegment?.TailOffsetBytes ??
            ProvisionalFrameEnvelopeEstimator.InitialTailOffsetBytes;
        RenderedFrameCandidate envelope = FramePlanRenderer.RenderAndMeasure(
            new OriginFreeFramePlan(0),
            fileNumber,
            frameStart);
        RenderedFrameCandidate malformed = new(
            envelope.Plan,
            envelope.FileNumber,
            envelope.RelativeReferences,
            envelope.EncodedReferenceBytes.ToArray(),
            envelope.Layout,
            createRevision(envelope));
        store.Append(malformed);
        return malformed.Address;
    }
}
