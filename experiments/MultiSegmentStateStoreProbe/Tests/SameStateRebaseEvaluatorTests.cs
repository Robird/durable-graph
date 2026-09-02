using Atelia.MultiSegmentStateStoreProbe.Evaluation;
using Atelia.MultiSegmentStateStoreProbe.Model;
using Atelia.MultiSegmentStateStoreProbe.Normalization;
using Atelia.MultiSegmentStateStoreProbe.Planning;
using Atelia.MultiSegmentStateStoreProbe.Policies;
using Atelia.MultiSegmentStateStoreProbe.Reconstruction;
using Atelia.MultiSegmentStateStoreProbe.Storage;
using Atelia.MultiSegmentStateStoreProbe.Workloads;

namespace Atelia.MultiSegmentStateStoreProbe.Tests;

public sealed class SameStateRebaseEvaluatorTests {
    private const uint ColdObjectId = 1;
    private const uint ClockObjectId = 2;

    [Fact]
    public void One_adaptive_SameStateRebase_trades_one_write_for_lower_future_cold_reads() {
        SameStateRun inherit = Run(adaptiveAfterPrehistory: false);
        SameStateRun adaptive = Run(adaptiveAfterPrehistory: true);

        Assert.Equal(220, adaptive.FirstColdHistoryBytes);
        Assert.Equal([ColdObjectId], adaptive.RebaseIdsByLaterSave[0]);
        Assert.Equal(100, adaptive.SecondColdHistoryBytes);
        Assert.Empty(adaptive.RebaseIdsByLaterSave[1]);
        Assert.Empty(adaptive.RebaseIdsByLaterSave[2]);
        _ = Assert.Single(
            adaptive.RebaseIdsByLaterSave,
            static ids => ids.Contains(ColdObjectId));
        Assert.Equal(3, adaptive.ColdOrdinalAfterRebase);
        Assert.Equal(inherit.ColdOrdinalAfterRebase, adaptive.ColdOrdinalAfterRebase);
        Assert.True(adaptive.WriteBytes[3] > inherit.WriteBytes[3]);
        Assert.True(
            adaptive.Metrics.WorkloadPhysicalWriteBytes >
            inherit.Metrics.WorkloadPhysicalWriteBytes);
        Assert.True(
            adaptive.Metrics.ColdReadSamples.Skip(4)
                .Sum(static sample => sample.ColdRead.UniqueFrameBytes) <
            inherit.Metrics.ColdReadSamples.Skip(4)
                .Sum(static sample => sample.ColdRead.UniqueFrameBytes));
    }

    private static SameStateRun Run(bool adaptiveAfterPrehistory) {
        SaveStep[] steps = [
            new SaveStep([
                new CreateObject(ColdObjectId, 100),
                new CreateObject(ClockObjectId, 1),
            ]),
            new SaveStep([new UpdateObject(ColdObjectId, 100, 60)]),
            new SaveStep([new UpdateObject(ColdObjectId, 100, 60)]),
            new SaveStep([new UpdateObject(ClockObjectId, 1, 1)]),
            new SaveStep([new UpdateObject(ClockObjectId, 1, 1)]),
            new SaveStep([new UpdateObject(ClockObjectId, 1, 1)]),
        ];
        InMemorySegmentStore store = new();
        RevisionCommitSession session = new(store, targetFileBytes: 16_384);
        EvaluatorRawMetricAccumulator evaluator = new(store);
        ReadAmplificationBaseBudgetPolicyParameters parameters = new(2m, 1m);
        List<long> writes = [];
        List<uint[]> rebaseIds = [];
        long firstColdHistory = -1;
        long secondColdHistory = -1;
        int coldOrdinalAfterRebase = -1;

        for (int index = 0; index < steps.Length; index++) {
            SaveStep step = steps[index];
            NormalizedSaveFacts facts = RevisionSaveNormalizer.Normalize(
                store,
                session.PublishedHead,
                session.UsedObjectIds,
                step);
            RevisionSaveSelection selection;
            if (index <= 2) {
                selection = FixedSelection(facts, UpdateWriteMode.Delta);
            } else if (adaptiveAfterPrehistory) {
                AdaptedReadAmplificationSelection adapted =
                    ReadAmplificationPolicyAdapter.Select(
                        store,
                        facts,
                        parameters,
                        ObjectVersionDictionaryKind.Base);
                ReadAmplificationBaseBudgetPolicyFact cold = adapted.PolicyInput.Facts
                    .Single(fact => fact.ObjectId == ColdObjectId);
                if (index == 3) {
                    firstColdHistory = cold.SourceReconstructionPayloadBytes!.Value;
                } else if (index == 4) {
                    secondColdHistory = cold.SourceReconstructionPayloadBytes!.Value;
                }

                rebaseIds.Add(adapted.PolicySelection.SameStateRebaseObjectIds.ToArray());
                selection = adapted.RevisionSelection;
            } else {
                selection = FixedSelection(facts, UpdateWriteMode.Base);
            }

            long before = store.Segments.Sum(static segment => segment.TailOffsetBytes);
            PublishedRevisionCommit published = Assert.IsType<PublishedRevisionCommit>(
                session.Commit(step, session.PublishedHead, selection));
            writes.Add(
                store.Segments.Sum(static segment => segment.TailOffsetBytes) - before);
            evaluator.RecordAcceptedSave(store, facts, published.PublishedHead);
            if (index == 3) {
                MaterializedCurrentState current = session.LoadCurrent();
                coldOrdinalAfterRebase = current.States[ColdObjectId].LogicalVersionOrdinal;
            }
        }

        return new SameStateRun(
            evaluator.Complete(store),
            writes,
            rebaseIds,
            firstColdHistory,
            secondColdHistory,
            coldOrdinalAfterRebase);
    }

    private static RevisionSaveSelection FixedSelection(
        NormalizedSaveFacts facts,
        UpdateWriteMode updateMode) => new(
        ObjectVersionDictionaryKind.Base,
        facts.Updates.Select(update => new UpdateWriteSelection(
            update.ObjectId,
            updateMode)),
        facts.NoChanges.Select(static noChange => new NoChangeWriteSelection(
            noChange.ObjectId,
            NoChangeWriteMode.Inherit)));

    private sealed record SameStateRun(
        EvaluatorRawMetrics Metrics,
        IReadOnlyList<long> WriteBytes,
        IReadOnlyList<uint[]> RebaseIdsByLaterSave,
        long FirstColdHistoryBytes,
        long SecondColdHistoryBytes,
        int ColdOrdinalAfterRebase);
}
