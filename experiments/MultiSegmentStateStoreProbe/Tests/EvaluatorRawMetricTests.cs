using Atelia.MultiSegmentStateStoreProbe.Evaluation;
using Atelia.MultiSegmentStateStoreProbe.Model;
using Atelia.MultiSegmentStateStoreProbe.Normalization;
using Atelia.MultiSegmentStateStoreProbe.Planning;
using Atelia.MultiSegmentStateStoreProbe.Storage;
using Atelia.MultiSegmentStateStoreProbe.Workloads;

namespace Atelia.MultiSegmentStateStoreProbe.Tests;

public sealed class EvaluatorRawMetricTests {
    [Fact]
    public void Shared_Revision_Frame_is_counted_once_across_OVD_and_objects() {
        InMemorySegmentStore store = new();
        RevisionCommitSession session = new(store, targetFileBytes: 4096);
        SaveStep step = new([
            new CreateObject(1, 10),
            new CreateObject(2, 20),
        ]);
        NormalizedSaveFacts facts = Normalize(session, step);
        PublishedRevisionCommit published = Commit(
            session,
            step,
            MultiSegmentBenchmarkingSelection(facts, UpdateWriteMode.Base));

        ColdHeadReadObservation observed = ColdHeadReadMeasurer.Measure(
            store,
            published.PublishedHead);

        Assert.Equal([published.PublishedHead], observed.OvdFrameAddresses);
        Assert.Equal([published.PublishedHead], observed.ObjectFrameAddresses);
        Assert.Equal([published.PublishedHead], observed.UniqueFrameAddresses);
        Assert.Equal(published.PublishedHead.FrameTicket.LengthBytes, observed.UniqueFrameBytes);
        Assert.Equal(30, observed.PostLiveBasePayloadBytes);
    }

    [Fact]
    public void Ovd_observation_tracks_only_frames_required_by_final_live_bindings() {
        InMemorySegmentStore store = new();
        RevisionCommitSession session = new(store, targetFileBytes: 128);
        SaveStep insert = new([new CreateObject(1, 10)]);
        NormalizedSaveFacts insertFacts = Normalize(session, insert);
        PublishedRevisionCommit f1 = Commit(
            session,
            insert,
            MultiSegmentBenchmarkingSelection(insertFacts, UpdateWriteMode.Base));

        SaveStep noChange = new([new CreateObject(2, 5)]);
        NormalizedSaveFacts noChangeFacts = Normalize(session, noChange);
        PublishedRevisionCommit f2 = Commit(
            session,
            noChange,
            MultiSegmentBenchmarkingSelection(noChangeFacts, UpdateWriteMode.Base));
        ColdHeadReadObservation liveExternal = ColdHeadReadMeasurer.Measure(
            store,
            f2.PublishedHead);
        Assert.Equal(
            [f1.PublishedHead, f2.PublishedHead],
            liveExternal.OvdFrameAddresses);

        SaveStep remove = new([new RemoveObject(1)]);
        NormalizedSaveFacts removeFacts = Normalize(session, remove);
        PublishedRevisionCommit f3 = Assert.IsType<PublishedRevisionCommit>(
            session.Commit(
                remove,
                f2.PublishedHead,
                new RevisionSaveSelection(
                    ObjectVersionDictionaryKind.Delta,
                    updates: [],
                    noChanges: [new NoChangeWriteSelection(
                        2,
                        NoChangeWriteMode.Inherit)])));
        ColdHeadReadObservation removedExternal = ColdHeadReadMeasurer.Measure(
            store,
            f3.PublishedHead);

        Assert.Equal(
            [f2.PublishedHead, f3.PublishedHead],
            removedExternal.OvdFrameAddresses);
        Assert.Equal(
            [f2.PublishedHead],
            removedExternal.ObjectFrameAddresses);
        Assert.Equal(
            f2.PublishedHead.FrameTicket.LengthBytes +
                f3.PublishedHead.FrameTicket.LengthBytes,
            removedExternal.UniqueFrameBytes);
    }

    [Fact]
    public void Accumulator_reports_exact_W_P_F_R_L_and_payload_references() {
        InMemorySegmentStore store = new();
        RevisionCommitSession session = new(store, targetFileBytes: 128);
        EvaluatorRawMetricAccumulator evaluator = new(store);
        SaveStep[] steps = [
            new SaveStep([new CreateObject(1, 10), new CreateObject(2, 20)]),
            new SaveStep([new UpdateObject(1, 12, 2), new RemoveObject(2)]),
        ];
        List<long> writes = [];

        foreach (SaveStep step in steps) {
            long before = TotalTailBytes(store);
            NormalizedSaveFacts facts = Normalize(session, step);
            PublishedRevisionCommit published = Commit(
                session,
                step,
                MultiSegmentBenchmarkingSelection(facts, UpdateWriteMode.Delta));
            writes.Add(TotalTailBytes(store) - before);
            evaluator.RecordAcceptedSave(store, facts, published.PublishedHead);
        }

        EvaluatorRawMetrics metrics = evaluator.Complete(store);

        Assert.Equal(2, metrics.AdmittedSaveCount);
        Assert.Equal(writes.Sum(), metrics.WorkloadPhysicalWriteBytes);
        Assert.Equal(writes.Max(), metrics.PeakWorkloadSaveWriteBytes);
        Assert.Equal(
            store.Segments.Max(static segment => segment.TailOffsetBytes),
            metrics.MaxSegmentTailBytes);
        Assert.Equal(32, metrics.TotalWorkloadDeltaReferencePayloadBytes);
        Assert.Equal(42, metrics.TotalWorkloadBaseReferencePayloadBytes);
        Assert.Equal(
            metrics.ColdReadSamples.Sum(static sample => sample.ColdRead.UniqueFrameBytes),
            metrics.TotalWorkloadColdReadBytes);
        Assert.Equal(42, metrics.TotalWorkloadLogicalBasePayloadBytes);
    }

    [Fact]
    public void Typed_rejection_adds_no_penalty_or_sample() {
        InMemorySegmentStore store = new();
        RevisionCommitSession session = new(store, targetFileBytes: 4096);
        EvaluatorRawMetricAccumulator evaluator = new(store);
        SaveStep step = new([new CreateObject(1, 10)]);
        RevisionSaveSelection invalidSelection = new(
            ObjectVersionDictionaryKind.Base,
            updates: [],
            noChanges: [new NoChangeWriteSelection(1, NoChangeWriteMode.Inherit)]);

        Assert.IsType<RejectedRevisionCommit>(session.Commit(
            step,
            session.PublishedHead,
            invalidSelection));
        evaluator.ValidateRejectedSave(store);
        EvaluatorRawMetrics metrics = evaluator.Complete(store);

        Assert.Equal(0, metrics.AdmittedSaveCount);
        Assert.Equal(0, metrics.WorkloadPhysicalWriteBytes);
        Assert.Empty(metrics.ColdReadSamples);
    }

    [Fact]
    public void Empty_live_graph_keeps_R_but_makes_aggregate_R_over_L_undefined() {
        InMemorySegmentStore store = new();
        RevisionCommitSession session = new(store, targetFileBytes: 4096);
        SaveStep insert = new([new CreateObject(1, 10)]);
        NormalizedSaveFacts insertFacts = Normalize(session, insert);
        _ = Commit(
            session,
            insert,
            MultiSegmentBenchmarkingSelection(insertFacts, UpdateWriteMode.Base));
        EvaluatorRawMetricAccumulator evaluator = new(store);

        SaveStep remove = new([new RemoveObject(1)]);
        NormalizedSaveFacts removeFacts = Normalize(session, remove);
        PublishedRevisionCommit published = Commit(
            session,
            remove,
            MultiSegmentBenchmarkingSelection(removeFacts, UpdateWriteMode.Base));
        evaluator.RecordAcceptedSave(store, removeFacts, published.PublishedHead);
        EvaluatorRawMetrics metrics = evaluator.Complete(store);

        Assert.True(metrics.TotalWorkloadColdReadBytes > 0);
        Assert.Equal(0, metrics.TotalWorkloadLogicalBasePayloadBytes);
        Assert.False(metrics.IsAggregateReadAmplificationDefined);
        Assert.Equal(0, metrics.TotalWorkloadDeltaReferencePayloadBytes);
        Assert.Equal(0, metrics.TotalWorkloadBaseReferencePayloadBytes);
    }

    private static RevisionSaveSelection MultiSegmentBenchmarkingSelection(
        NormalizedSaveFacts facts,
        UpdateWriteMode mode) => new(
        ObjectVersionDictionaryKind.Base,
        facts.Updates.Select(update => new UpdateWriteSelection(update.ObjectId, mode)),
        facts.NoChanges.Select(static noChange => new NoChangeWriteSelection(
            noChange.ObjectId,
            NoChangeWriteMode.Inherit)));

    private static NormalizedSaveFacts Normalize(
        RevisionCommitSession session,
        SaveStep step) => RevisionSaveNormalizer.Normalize(
        session.Store,
        session.PublishedHead,
        session.UsedObjectIds,
        step);

    private static PublishedRevisionCommit Commit(
        RevisionCommitSession session,
        SaveStep step,
        RevisionSaveSelection selection) => Assert.IsType<PublishedRevisionCommit>(
        session.Commit(step, session.PublishedHead, selection));

    private static long TotalTailBytes(InMemorySegmentStore store) =>
        store.Segments.Sum(static segment => segment.TailOffsetBytes);
}
