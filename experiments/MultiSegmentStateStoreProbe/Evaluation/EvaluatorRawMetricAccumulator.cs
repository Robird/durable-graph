using Atelia.MultiSegmentStateStoreProbe.Normalization;
using Atelia.MultiSegmentStateStoreProbe.Storage;

namespace Atelia.MultiSegmentStateStoreProbe.Evaluation;

internal sealed class EvaluatorRawMetricAccumulator {
    private StoreTailSnapshot _lastAcceptedStore;
    private int _admittedSaveCount;
    private long _workloadPhysicalWriteBytes;
    private long _peakWorkloadSaveWriteBytes;
    private long _maxSegmentTailBytes;
    private long _deltaReferencePayloadBytes;
    private long _baseReferencePayloadBytes;
    private readonly List<WorkloadColdReadSample> _coldReadSamples = [];

    public EvaluatorRawMetricAccumulator(InMemorySegmentStore store) {
        ArgumentNullException.ThrowIfNull(store);
        _lastAcceptedStore = StoreTailSnapshot.Capture(store);
        _maxSegmentTailBytes = _lastAcceptedStore.MaxTailBytes;
    }

    public void RecordAcceptedSave(
        InMemorySegmentStore store,
        NormalizedSaveFacts facts,
        Model.AbsoluteFrameAddress publishedHead) {
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(facts);
        StoreTailSnapshot result = StoreTailSnapshot.Capture(store);
        result.EnsureAppendOnlyFrom(_lastAcceptedStore);
        long writeBytes = checked(
            result.TotalTailBytes - _lastAcceptedStore.TotalTailBytes);
        if (writeBytes <= 0) {
            throw new InvalidDataException("An admitted Save must append physical bytes.");
        }

        ColdHeadReadObservation coldRead = ColdHeadReadMeasurer.Measure(
            store,
            publishedHead);
        long factsLiveBaseBytes = facts.PostLiveStates.Values.Sum(
            static state => (long)state.BasePayloadBytes);
        if (coldRead.PostLiveBasePayloadBytes != factsLiveBaseBytes) {
            throw new InvalidDataException(
                "Accepted head and normalized post-live Base bytes diverged.");
        }

        (long deltaReference, long baseReference) = GetReferences(facts);
        _workloadPhysicalWriteBytes = checked(
            _workloadPhysicalWriteBytes + writeBytes);
        _peakWorkloadSaveWriteBytes = Math.Max(_peakWorkloadSaveWriteBytes, writeBytes);
        _maxSegmentTailBytes = Math.Max(_maxSegmentTailBytes, result.MaxTailBytes);
        _deltaReferencePayloadBytes = checked(
            _deltaReferencePayloadBytes + deltaReference);
        _baseReferencePayloadBytes = checked(
            _baseReferencePayloadBytes + baseReference);
        _coldReadSamples.Add(new WorkloadColdReadSample(_admittedSaveCount, coldRead));
        _admittedSaveCount = checked(_admittedSaveCount + 1);
        _lastAcceptedStore = result;
    }

    public void ValidateRejectedSave(InMemorySegmentStore store) {
        ArgumentNullException.ThrowIfNull(store);
        StoreTailSnapshot.Capture(store).EnsureExactlyMatches(
            _lastAcceptedStore,
            "A rejected Save mutated the Store.");
    }

    public EvaluatorRawMetrics Complete(InMemorySegmentStore store) {
        ValidateRejectedSave(store);
        return new EvaluatorRawMetrics(
            _admittedSaveCount,
            _workloadPhysicalWriteBytes,
            _peakWorkloadSaveWriteBytes,
            _maxSegmentTailBytes,
            _deltaReferencePayloadBytes,
            _baseReferencePayloadBytes,
            _coldReadSamples);
    }

    private static (long Delta, long Base) GetReferences(NormalizedSaveFacts facts) {
        long delta = 0;
        long @base = 0;
        foreach (NormalizedInsertFact insert in facts.Inserts) {
            delta = checked(delta + insert.ResultState.BasePayloadBytes);
            @base = checked(@base + insert.ResultState.BasePayloadBytes);
        }

        foreach (NormalizedUpdateFact update in facts.Updates) {
            delta = checked(delta + update.DeltaPayloadBytes);
            @base = checked(@base + update.ResultState.BasePayloadBytes);
        }

        return (delta, @base);
    }

    private sealed class StoreTailSnapshot {
        private readonly KeyValuePair<uint, long>[] _tails;

        private StoreTailSnapshot(KeyValuePair<uint, long>[] tails) {
            _tails = tails;
            TotalTailBytes = tails.Sum(static pair => pair.Value);
            MaxTailBytes = tails.Length == 0
                ? 0
                : tails.Max(static pair => pair.Value);
        }

        public long TotalTailBytes { get; }

        public long MaxTailBytes { get; }

        public static StoreTailSnapshot Capture(InMemorySegmentStore store) => new(
            store.Segments
                .Select(static segment => new KeyValuePair<uint, long>(
                    segment.FileNumber.Value,
                    segment.TailOffsetBytes))
                .ToArray());

        public void EnsureAppendOnlyFrom(StoreTailSnapshot source) {
            if (_tails.Length < source._tails.Length) {
                throw new InvalidDataException("An evaluator Save removed a Segment.");
            }

            for (int index = 0; index < source._tails.Length; index++) {
                if (_tails[index].Key != source._tails[index].Key ||
                    _tails[index].Value < source._tails[index].Value) {
                    throw new InvalidDataException(
                        "An evaluator Save renumbered or shortened a Segment.");
                }
            }
        }

        public void EnsureExactlyMatches(StoreTailSnapshot expected, string message) {
            if (!_tails.SequenceEqual(expected._tails)) {
                throw new InvalidDataException(message);
            }
        }
    }
}
