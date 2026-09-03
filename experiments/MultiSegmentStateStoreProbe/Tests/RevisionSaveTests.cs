using Atelia.MultiSegmentStateStoreProbe.Model;
using Atelia.MultiSegmentStateStoreProbe.Normalization;
using Atelia.MultiSegmentStateStoreProbe.Planning;
using Atelia.MultiSegmentStateStoreProbe.Reconstruction;
using Atelia.MultiSegmentStateStoreProbe.Storage;
using Atelia.MultiSegmentStateStoreProbe.Workloads;
using ModelState = Atelia.MultiSegmentStateStoreProbe.Model.LogicalObjectState;
using WorkloadState = Atelia.MultiSegmentStateStoreProbe.Workloads.LogicalObjectState;

namespace Atelia.MultiSegmentStateStoreProbe.Tests;

public sealed class RevisionSaveTests {
    private const long OneFramePerSegmentRolloverThresholdBytes = 32;

    [Fact]
    public void Normalization_derives_exact_partitions_and_maps_workload_state_explicitly() {
        RevisionCommitSession session = NewSession();
        PublishedRevisionCommit f1 = CommitGenesis(
            session,
            new CreateObject(1, 10),
            new CreateObject(2, 20),
            new CreateObject(4, 40));
        SaveStep step = new([
            new RemoveObject(1),
            new UpdateObject(2, ResultBasePayloadBytes: 22, DeltaPayloadBytes: 2),
            new CreateObject(3, 30),
        ]);

        NormalizedSaveFacts facts = RevisionSaveNormalizer.Normalize(
            session.Store,
            f1.PublishedHead,
            session.UsedObjectIds,
            step);

        Assert.Equal(f1.PublishedHead, facts.ExpectedParent);
        Assert.Equal([1u, 2u, 4u], facts.ParentLive.Keys);
        Assert.Equal(3u, Assert.Single(facts.Inserts).ObjectId);
        Assert.Equal(2u, Assert.Single(facts.Updates).ObjectId);
        Assert.Equal(1u, Assert.Single(facts.Removes).ObjectId);
        Assert.Equal(4u, Assert.Single(facts.NoChanges).ObjectId);
        Assert.Equal([2u, 3u, 4u], facts.PostLiveStates.Keys);

        WorkloadState workload = WorkloadStateMapper.ToWorkload(
            facts.ParentLive[2].State);
        ModelState model = WorkloadStateMapper.ToModel(workload);
        Assert.Equal(new WorkloadState(20, 1), workload);
        Assert.Equal(new ModelState(value: 0, 20, 1), model);
        Assert.Equal(new ModelState(value: 0, 22, 2),
            facts.PostLiveStates[2]);
    }

    [Fact]
    public void Final_origin_render_keeps_OvdBase_cold_head_external_after_rollover() {
        RevisionCommitSession session = NewSession();
        PublishedRevisionCommit f1 = CommitGenesis(
            session,
            new CreateObject(1, 10),
            new CreateObject(2, 20));
        SaveStep updateHot = new([
            new UpdateObject(2, ResultBasePayloadBytes: 22, DeltaPayloadBytes: 2),
        ]);
        RevisionSaveSelection selection = new(
            ObjectVersionDictionaryKind.Base,
            [new UpdateWriteSelection(2, UpdateWriteMode.Delta)],
            [new NoChangeWriteSelection(1, NoChangeWriteMode.Inherit)]);

        PublishedRevisionCommit f2 = Assert.IsType<PublishedRevisionCommit>(
            session.Commit(updateHot, f1.PublishedHead, selection));

        Assert.Same(f2.Plan.EnvelopePlan, f2.AppendedCandidate.Plan);
        Assert.Equal(2u, f2.AppendedCandidate.FileNumber.Value);
        Assert.All(
            f2.AppendedCandidate.RelativeReferences,
            reference => Assert.Equal(1u, reference.BackwardFileDistance));

        RevisionFrame revision = Assert.IsType<RevisionFrame>(
            f2.AppendedCandidate.RevisionFrame);
        FileScope scope = new(f2.PublishedHead.FileNumber);
        Assert.Equal(
            f1.PublishedHead,
            scope.ResolveEarlier(
                f2.PublishedHead.FrameTicket,
                Assert.IsType<RelativeFrameTicket>(revision.PriorRevision)));
        Assert.Equal(ObjectVersionDictionaryKind.Base,
            revision.ObjectVersionDictionary.Kind);
        Assert.DoesNotContain(1u, revision.ObjectVersions.Keys);
        ObjectVersionDictionaryBinding cold =
            revision.ObjectVersionDictionary.Entries[1];
        Assert.Equal(ObjectVersionDictionaryBindingKind.External, cold.Kind);
        Assert.Equal(
            f1.PublishedHead,
            scope.ResolveEarlier(
                f2.PublishedHead.FrameTicket,
                Assert.IsType<RelativeFrameTicket>(cold.ExternalReference)));

        MaterializedCurrentState current = session.LoadCurrent();
        Assert.Equal(f1.PublishedHead, current.ObjectVersionHeads[1]);
        Assert.Equal(f2.PublishedHead, current.ObjectVersionHeads[2]);
        Assert.Equal(new ModelState(0, 22, 2), current.States[2]);
        AssertWholeSizeConservation(f2.AppendedCandidate);
    }

    [Fact]
    public void Ovd_mode_is_independent_from_object_representation_selection() {
        RevisionCommitSession session = NewSession();
        PublishedRevisionCommit f1 = CommitGenesis(
            session,
            new CreateObject(1, 10),
            new CreateObject(2, 20));
        NormalizedSaveFacts facts = RevisionSaveNormalizer.Normalize(
            session.Store,
            f1.PublishedHead,
            session.UsedObjectIds,
            new SaveStep([
                new UpdateObject(2, ResultBasePayloadBytes: 21, DeltaPayloadBytes: 1),
            ]));
        UpdateWriteSelection update = new(2, UpdateWriteMode.Delta);
        NoChangeWriteSelection noChange = new(1, NoChangeWriteMode.Inherit);

        RevisionPlan basePlan = RevisionPlanner.Create(
            facts,
            new RevisionSaveSelection(
                ObjectVersionDictionaryKind.Base,
                [update],
                [noChange]));
        RevisionPlan deltaPlan = RevisionPlanner.Create(
            facts,
            new RevisionSaveSelection(
                ObjectVersionDictionaryKind.Delta,
                [update],
                [noChange]));
        RevisionPlan baseObjectPlan = RevisionPlanner.Create(
            facts,
            new RevisionSaveSelection(
                ObjectVersionDictionaryKind.Delta,
                [new UpdateWriteSelection(2, UpdateWriteMode.Base)],
                [noChange]));

        Assert.Equal(ObjectVersionKind.Delta,
            Assert.Single(basePlan.FramePlan.ObjectVersions).Kind);
        Assert.Equal(ObjectVersionKind.Delta,
            Assert.Single(deltaPlan.FramePlan.ObjectVersions).Kind);
        Assert.Equal(ObjectVersionKind.Base,
            Assert.Single(baseObjectPlan.FramePlan.ObjectVersions).Kind);
        Assert.Null(Assert.Single(baseObjectPlan.FramePlan.ObjectVersions).DeltaParent);
        Assert.Equal(f1.PublishedHead, basePlan.FramePlan.PriorRevision);
        Assert.Equal(f1.PublishedHead, deltaPlan.FramePlan.PriorRevision);
        Assert.Equal(
            [
                ObjectVersionDictionaryBindingKind.External,
                ObjectVersionDictionaryBindingKind.BindSelf,
            ],
            basePlan.FramePlan.OvdEntries.Select(static entry => entry.Kind));
        Assert.Equal(
            [ObjectVersionDictionaryBindingKind.BindSelf],
            deltaPlan.FramePlan.OvdEntries.Select(static entry => entry.Kind));
    }

    [Fact]
    public void Stale_parent_is_rechecked_immediately_before_append_without_mutation() {
        RevisionCommitSession session = NewSession();
        PublishedRevisionCommit f1 = CommitGenesis(session, new CreateObject(1, 10));
        StoreSnapshot before = Snapshot(session);
        SaveStep step = new([
            new UpdateObject(1, ResultBasePayloadBytes: 11, DeltaPayloadBytes: 1),
        ]);
        RevisionSaveSelection selection = new(
            ObjectVersionDictionaryKind.Delta,
            [new UpdateWriteSelection(1, UpdateWriteMode.Delta)],
            []);

        RejectedRevisionCommit initiallyStale =
            Assert.IsType<RejectedRevisionCommit>(session.Commit(
                step,
                expectedParent: null,
                selection));
        Assert.Equal(RevisionCommitRejectionKind.StaleParent, initiallyStale.Kind);
        Assert.Equal(before, Snapshot(session));

        RejectedRevisionCommit becameStale =
            Assert.IsType<RejectedRevisionCommit>(session.Commit(
                step,
                f1.PublishedHead,
                selection,
                new RevisionCommitHooks {
                    ObserveHeadBeforeAppend = static () => null,
                }));
        Assert.Equal(RevisionCommitRejectionKind.StaleParent, becameStale.Kind);
        Assert.Equal(before, Snapshot(session));
    }

    [Fact]
    public void Append_success_before_publish_failure_leaves_an_unreachable_candidate() {
        RevisionCommitSession session = NewSession();
        PublishedRevisionCommit f1 = CommitGenesis(session, new CreateObject(1, 10));
        ModelState oldState = session.LoadCurrent().States[1];
        SaveStep update = new([
            new UpdateObject(1, ResultBasePayloadBytes: 11, DeltaPayloadBytes: 1),
        ]);
        RevisionSaveSelection selection = new(
            ObjectVersionDictionaryKind.Delta,
            [new UpdateWriteSelection(1, UpdateWriteMode.Delta)],
            []);

        AppendedUnpublishedRevisionCommit orphan =
            Assert.IsType<AppendedUnpublishedRevisionCommit>(session.Commit(
                update,
                f1.PublishedHead,
                selection,
                new RevisionCommitHooks {
                    FailAfterAppendBeforePublish = true,
                }));

        Assert.Equal(f1.PublishedHead, session.PublishedHead);
        Assert.Equal(f1.PublishedHead, orphan.RetainedPublishedHead);
        Assert.Same(orphan.AppendedCandidate,
            session.Store.Read(orphan.AppendedCandidate.Address));
        Assert.NotEqual(session.PublishedHead, orphan.AppendedCandidate.Address);
        Assert.Equal(oldState, session.LoadCurrent().States[1]);
    }

    [Fact]
    public void Cache_install_failure_keeps_new_head_authoritative_and_reconstructible() {
        RevisionCommitSession session = NewSession();
        PublishedRevisionCommit f1 = CommitGenesis(session, new CreateObject(1, 10));
        RevisionSaveSelection selection = new(
            ObjectVersionDictionaryKind.Delta,
            [new UpdateWriteSelection(1, UpdateWriteMode.Delta)],
            []);

        PublishedRevisionCommit f2 = Assert.IsType<PublishedRevisionCommit>(
            session.Commit(
                new SaveStep([
                    new UpdateObject(1, ResultBasePayloadBytes: 11, DeltaPayloadBytes: 1),
                ]),
                f1.PublishedHead,
                selection,
                new RevisionCommitHooks {
                    FailCacheInstallation = true,
                }));

        Assert.False(f2.CacheInstalled);
        Assert.Null(session.CachedState);
        Assert.Equal(f2.PublishedHead, session.PublishedHead);
        Assert.Equal(new ModelState(0, 11, 2), session.LoadCurrent().States[1]);

        PublishedRevisionCommit f3 = Assert.IsType<PublishedRevisionCommit>(
            session.Commit(
                new SaveStep([
                    new UpdateObject(1, ResultBasePayloadBytes: 12, DeltaPayloadBytes: 1),
                ]),
                f2.PublishedHead,
                selection));
        Assert.True(f3.CacheInstalled);
        Assert.Equal(new ModelState(0, 12, 3), f3.CachedState!.States[1]);
    }

    [Fact]
    public void SameStateRebase_keeps_ordinal_and_invalid_or_oversize_input_is_non_mutating() {
        RevisionCommitSession session = NewSession();
        PublishedRevisionCommit f1 = CommitGenesis(session, new CreateObject(1, 10));
        PublishedRevisionCommit f2 = Assert.IsType<PublishedRevisionCommit>(
            session.Commit(
                new SaveStep([new CreateObject(2, 5)]),
                f1.PublishedHead,
                new RevisionSaveSelection(
                    ObjectVersionDictionaryKind.Delta,
                    [],
                    [new NoChangeWriteSelection(
                        1,
                        NoChangeWriteMode.SameStateRebase)])));

        RevisionFrame revision = f2.AppendedCandidate.RevisionFrame!;
        Assert.Equal(ObjectVersionKind.Base, revision.ObjectVersions[1].Kind);
        Assert.Equal(1, revision.ObjectVersions[1].ResultState.LogicalVersionOrdinal);
        Assert.Equal([f2.PublishedHead],
            session.LoadCurrent().ObjectReconstructionPaths[1]);

        StoreSnapshot beforeInvalid = Snapshot(session);
        RejectedRevisionCommit invalid = Assert.IsType<RejectedRevisionCommit>(
            session.Commit(
                new SaveStep([new UpdateObject(999, 1, 1)]),
                f2.PublishedHead,
                new RevisionSaveSelection(
                    ObjectVersionDictionaryKind.Delta,
                    [new UpdateWriteSelection(999, UpdateWriteMode.Delta)],
                    [
                        new NoChangeWriteSelection(1, NoChangeWriteMode.Inherit),
                        new NoChangeWriteSelection(2, NoChangeWriteMode.Inherit),
                    ])));
        Assert.Equal(RevisionCommitRejectionKind.InvalidInput, invalid.Kind);
        Assert.Equal(beforeInvalid, Snapshot(session));

        RevisionCommitSession empty = NewSession();
        RejectedRevisionCommit oversize = Assert.IsType<RejectedRevisionCommit>(
            empty.Commit(
                new SaveStep([new CreateObject(1, int.MaxValue)]),
                expectedParent: null,
                EmptySelection(ObjectVersionDictionaryKind.Base)));
        Assert.Equal(RevisionCommitRejectionKind.Capacity, oversize.Kind);
        Assert.Null(empty.PublishedHead);
        Assert.Equal(0, empty.Store.SegmentCount);
    }

    [Fact]
    public void Duplicate_change_and_ObjectId_reuse_are_rejected() {
        Assert.Throws<ArgumentException>(() => new SaveStep([
            new CreateObject(1, 1),
            new RemoveObject(1),
        ]));

        RevisionCommitSession session = NewSession();
        PublishedRevisionCommit f1 = CommitGenesis(session, new CreateObject(1, 10));
        PublishedRevisionCommit removed = Assert.IsType<PublishedRevisionCommit>(
            session.Commit(
                new SaveStep([new RemoveObject(1)]),
                f1.PublishedHead,
                EmptySelection(ObjectVersionDictionaryKind.Delta)));
        StoreSnapshot beforeReuse = Snapshot(session);
        RejectedRevisionCommit reuse = Assert.IsType<RejectedRevisionCommit>(
            session.Commit(
                new SaveStep([new CreateObject(1, 10)]),
                removed.PublishedHead,
                EmptySelection(ObjectVersionDictionaryKind.Delta)));

        Assert.Equal(RevisionCommitRejectionKind.InvalidInput, reuse.Kind);
        Assert.Equal(beforeReuse, Snapshot(session));

        Assert.Throws<InvalidOperationException>(() =>
            new RevisionCommitSession(
                session.Store,
                OneFramePerSegmentRolloverThresholdBytes));
    }

    private static RevisionCommitSession NewSession() => new(
        new InMemorySegmentStore(),
        OneFramePerSegmentRolloverThresholdBytes);

    private static PublishedRevisionCommit CommitGenesis(
        RevisionCommitSession session,
        params CreateObject[] creates) => Assert.IsType<PublishedRevisionCommit>(
            session.Commit(
                new SaveStep(creates),
                expectedParent: null,
                EmptySelection(ObjectVersionDictionaryKind.Base)));

    private static RevisionSaveSelection EmptySelection(
        ObjectVersionDictionaryKind kind) => new(kind, [], []);

    private static void AssertWholeSizeConservation(RenderedFrameCandidate candidate) {
        Assert.Equal(
            candidate.SyntheticObjectPayloadBytes +
                candidate.SemanticMetadataPayloadBytes +
                candidate.EncodedReferenceLengthBytes,
            candidate.Layout.PayloadLengthBytes);
        Assert.Equal(candidate.Plan.TailMetadataBytes,
            candidate.Layout.TailMetadataLengthBytes);
        Assert.Equal(
            ProvisionalFrameEnvelopeEstimator.FrameFixedBytes +
                candidate.Layout.PayloadLengthBytes +
                candidate.Layout.TailMetadataLengthBytes +
                candidate.Layout.PaddingLengthBytes,
            candidate.Layout.FrameLengthBytes);
        Assert.Equal(
            candidate.Layout.FrameLengthBytes +
                ProvisionalFrameEnvelopeEstimator.TrailingFenceBytes,
            candidate.Layout.AppendLengthBytes);
    }

    private static StoreSnapshot Snapshot(RevisionCommitSession session) => new(
        session.PublishedHead,
        session.Store.SegmentCount,
        session.Store.CurrentSegment?.FrameCount ?? 0,
        session.Store.CurrentSegment?.TailOffsetBytes ??
            ProvisionalFrameEnvelopeEstimator.InitialTailOffsetBytes);

    private sealed record StoreSnapshot(
        AbsoluteFrameAddress? PublishedHead,
        int SegmentCount,
        int CurrentFrameCount,
        long CurrentTailBytes);
}
