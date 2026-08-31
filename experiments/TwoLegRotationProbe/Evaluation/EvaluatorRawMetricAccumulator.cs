using Atelia.TwoLegRotationProbe.Model;
using Atelia.TwoLegRotationProbe.Planning;
using Atelia.TwoLegRotationProbe.Workloads;

namespace Atelia.TwoLegRotationProbe.Evaluation;

/// <summary>
/// Records physical mutations inside caller-declared outer Commit boundaries. Only
/// admitted, realized writes belong here; typed rejection remains an evaluator outcome.
/// </summary>
internal sealed class EvaluatorRawMetricAccumulator {
    private StoreTailSnapshot _lastCompletedStore;
    private CursorValue _lastCompletedCursor;
    private StoreTailSnapshot? _commitStartStore;
    private StoreTailSnapshot? _lastObservedStore;
    private CursorValue? _lastObservedCursor;
    private int _realizedCommitCount;
    private long _workloadPhysicalWriteBytes;
    private long _terminalSettlementPhysicalWriteBytes;
    private long _totalWorkloadDeltaReferencePayloadBytes;
    private long _totalWorkloadBaseReferencePayloadBytes;
    private long _peakCommitWriteBytes;
    private long _maxCurrentFileTailBytes;
    private readonly List<WorkloadColdReadSample> _workloadColdReadSamples = [];

    public EvaluatorRawMetricAccumulator(
        RbfFileStore store,
        ProbeRevisionCursor initialCursor) {
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(initialCursor);

        _lastCompletedStore = StoreTailSnapshot.Capture(store);
        _lastCompletedCursor = CursorValue.Capture(store, initialCursor);
        _maxCurrentFileTailBytes = initialCursor.CurrentFileTailOffsetBytes;
    }

    public void BeginCommit(
        RbfFileStore store,
        ProbeRevisionCursor sourceCursor) {
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(sourceCursor);
        if (_commitStartStore is not null) {
            throw new InvalidOperationException(
                "The preceding evaluator Commit has not ended.");
        }

        StoreTailSnapshot sourceStore = StoreTailSnapshot.Capture(store);
        sourceStore.EnsureExactlyMatches(
            _lastCompletedStore,
            "The Store changed outside an evaluator Commit.");
        CursorValue source = CursorValue.Capture(store, sourceCursor);
        source.EnsureExactlyMatches(
            _lastCompletedCursor,
            "The source cursor does not match the last completed evaluator Commit.");

        _commitStartStore = sourceStore;
        _lastObservedStore = sourceStore;
        _lastObservedCursor = source;
    }

    /// <summary>
    /// Records one accepted Revision inside the open outer Commit. Call this after each
    /// realized append/rotation so Current-tail pressure is not hidden by a later scope shift.
    /// </summary>
    public void ObserveAcceptedRevision(
        RbfFileStore store,
        ProbeRevisionCursor resultCursor) {
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(resultCursor);
        if (_commitStartStore is null ||
            _lastObservedStore is null ||
            _lastObservedCursor is not CursorValue priorCursor) {
            throw new InvalidOperationException(
                "BeginCommit must be called before recording an accepted Revision.");
        }

        StoreTailSnapshot resultStore = StoreTailSnapshot.Capture(store);
        resultStore.EnsureAppendOnlyFrom(_lastObservedStore);
        CursorValue result = CursorValue.Capture(store, resultCursor);
        long sourceCurrentTailAfterWrite = resultStore.GetTail(
            priorCursor.CurrentFileNumber);
        _maxCurrentFileTailBytes = Math.Max(
            _maxCurrentFileTailBytes,
            sourceCurrentTailAfterWrite);
        _maxCurrentFileTailBytes = Math.Max(
            _maxCurrentFileTailBytes,
            result.CurrentFileTailOffsetBytes);

        _lastObservedStore = resultStore;
        _lastObservedCursor = result;
    }

    public void EndTerminalSettlementCommit(
        RbfFileStore store,
        ProbeRevisionCursor resultCursor) {
        long commitWriteBytes = EndCommitCore(store, resultCursor);
        _terminalSettlementPhysicalWriteBytes = checked(
            _terminalSettlementPhysicalWriteBytes + commitWriteBytes);
    }

    /// <summary>
    /// Closes one successful outer workload Save and records one empty-cache load of
    /// its accepted PublishedRevision. Bootstrap and evaluator terminal settlement use
    /// <see cref="EndTerminalSettlementCommit"/> instead and therefore do not enter
    /// this read schedule or the workload reference payload totals.
    /// </summary>
    public void EndWorkloadCommit(
        RbfFileStore store,
        ProbeRevisionCursor resultCursor,
        NormalizedSaveFacts facts) {
        ArgumentNullException.ThrowIfNull(facts);

        long postLiveGraphBasePayloadBytes = SumPostLiveBasePayloadBytes(facts);
        (long deltaReferencePayloadBytes, long baseReferencePayloadBytes) =
            SumWorkloadReferencePayloadBytes(facts);
        long commitWriteBytes = EndCommitCore(store, resultCursor);

        FinalColdHeadReadObservation coldRead = FinalColdHeadReadMeasurer.Measure(
            store,
            resultCursor.PublishedRevisionAddress);
        if (coldRead.PostLiveBasePayloadBytes != postLiveGraphBasePayloadBytes) {
            throw new InvalidDataException(
                "The accepted workload head does not match its normalized post-live Base bytes.");
        }

        _workloadPhysicalWriteBytes = checked(
            _workloadPhysicalWriteBytes + commitWriteBytes);
        _totalWorkloadDeltaReferencePayloadBytes = checked(
            _totalWorkloadDeltaReferencePayloadBytes + deltaReferencePayloadBytes);
        _totalWorkloadBaseReferencePayloadBytes = checked(
            _totalWorkloadBaseReferencePayloadBytes + baseReferencePayloadBytes);
        _workloadColdReadSamples.Add(new WorkloadColdReadSample(
            workloadSaveOrdinal: _workloadColdReadSamples.Count,
            coldRead,
            coldRead.PostLiveBasePayloadBytes));
    }

    private long EndCommitCore(
        RbfFileStore store,
        ProbeRevisionCursor resultCursor) {
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(resultCursor);
        if (_commitStartStore is null ||
            _lastObservedStore is null ||
            _lastObservedCursor is not CursorValue observedCursor) {
            throw new InvalidOperationException("No evaluator Commit is open.");
        }

        StoreTailSnapshot resultStore = StoreTailSnapshot.Capture(store);
        resultStore.EnsureExactlyMatches(
            _lastObservedStore,
            "The Store changed after the last accepted Revision checkpoint.");
        CursorValue result = CursorValue.Capture(store, resultCursor);
        result.EnsureExactlyMatches(
            observedCursor,
            "The Commit result cursor differs from its last accepted checkpoint.");
        long commitWriteBytes = checked(
            _lastObservedStore.TotalTailOffsetBytes -
            _commitStartStore.TotalTailOffsetBytes);
        if (commitWriteBytes <= 0) {
            throw new InvalidOperationException(
                "An admitted evaluator Commit must contain a realized physical write.");
        }

        _realizedCommitCount = checked(_realizedCommitCount + 1);
        _peakCommitWriteBytes = Math.Max(
            _peakCommitWriteBytes,
            commitWriteBytes);
        _lastCompletedStore = _lastObservedStore;
        _lastCompletedCursor = result;
        ClearOpenCommit();
        return commitWriteBytes;
    }

    /// <summary>
    /// Closes a caller-declared Commit that produced a typed non-realized outcome.
    /// The Store and cursor must remain exactly unchanged, and no metric is recorded.
    /// </summary>
    public void CancelUnrealizedCommit(
        RbfFileStore store,
        ProbeRevisionCursor sourceCursor) {
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(sourceCursor);
        if (_commitStartStore is null) {
            throw new InvalidOperationException("No evaluator Commit is open.");
        }

        StoreTailSnapshot unchangedStore = StoreTailSnapshot.Capture(store);
        unchangedStore.EnsureExactlyMatches(
            _commitStartStore,
            "A non-realized evaluator outcome mutated the Store.");
        CursorValue unchangedCursor = CursorValue.Capture(store, sourceCursor);
        unchangedCursor.EnsureExactlyMatches(
            _lastCompletedCursor,
            "A non-realized evaluator outcome changed the cursor.");
        ClearOpenCommit();
    }

    public EvaluatorRawMetrics Complete(
        RbfFileStore store,
        ProbeRevisionCursor finalCursor) {
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(finalCursor);
        ValidateClosedBoundary(store, finalCursor);

        FinalColdHeadReadObservation coldRead = FinalColdHeadReadMeasurer.Measure(
            store,
            finalCursor.PublishedRevisionAddress);
        return new EvaluatorRawMetrics(
            _realizedCommitCount,
            _workloadPhysicalWriteBytes,
            _terminalSettlementPhysicalWriteBytes,
            _totalWorkloadDeltaReferencePayloadBytes,
            _totalWorkloadBaseReferencePayloadBytes,
            _peakCommitWriteBytes,
            _maxCurrentFileTailBytes,
            _workloadColdReadSamples,
            coldRead);
    }

    private static long SumPostLiveBasePayloadBytes(NormalizedSaveFacts facts) {
        long result = 0;
        foreach (LogicalObjectState state in facts.PostLiveStates.Values) {
            result = checked(result + state.BasePayloadBytes);
        }

        return result;
    }

    private static (long Delta, long Base) SumWorkloadReferencePayloadBytes(
        NormalizedSaveFacts facts) {
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

    /// <summary>
    /// Verifies that no unaccounted mutation occurred at the current run boundary.
    /// This does not produce metrics or claim that the evaluation horizon is closed.
    /// </summary>
    public void ValidateClosedBoundary(
        RbfFileStore store,
        ProbeRevisionCursor cursor) {
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(cursor);
        if (_commitStartStore is not null) {
            throw new InvalidOperationException(
                "The evaluator run boundary cannot be inspected inside an open Commit.");
        }

        StoreTailSnapshot finalStore = StoreTailSnapshot.Capture(store);
        finalStore.EnsureExactlyMatches(
            _lastCompletedStore,
            "The Store changed after the last completed evaluator Commit.");
        CursorValue final = CursorValue.Capture(store, cursor);
        final.EnsureExactlyMatches(
            _lastCompletedCursor,
            "The final cursor does not match the last completed evaluator Commit.");
    }

    private void ClearOpenCommit() {
        _commitStartStore = null;
        _lastObservedStore = null;
        _lastObservedCursor = null;
    }

    private readonly record struct CursorValue(
        uint PreviousFileNumber,
        uint CurrentFileNumber,
        AbsoluteFrameAddress PublishedRevisionAddress,
        long CurrentFileTailOffsetBytes) {
        public static CursorValue Capture(
            RbfFileStore store,
            ProbeRevisionCursor cursor) {
            uint currentFileNumber = cursor.FileScope.CurrentFileNumber;
            uint previousFileNumber = cursor.FileScope.PreviousFileNumber
                ?? throw new InvalidDataException(
                    "Evaluator metrics require a two-file scope.");
            if (currentFileNumber != checked(previousFileNumber + 1) ||
                currentFileNumber != (uint)store.FileCount) {
                throw new InvalidDataException(
                    "The evaluator cursor must name the Store's highest adjacent two-file scope.");
            }

            RbfFile current = store.GetFile(currentFileNumber);
            if (current.TailOffsetBytes != cursor.CurrentFileTailOffsetBytes) {
                throw new InvalidDataException(
                    "The evaluator cursor Current tail does not match the Store.");
            }

            try {
                _ = store.ReadFrame(cursor.PublishedRevisionAddress);
            } catch (Exception exception) when (
                exception is ArgumentOutOfRangeException or KeyNotFoundException) {
                throw new InvalidDataException(
                    "The evaluator cursor PublishedRevision is not readable.",
                    exception);
            }

            return new CursorValue(
                previousFileNumber,
                currentFileNumber,
                cursor.PublishedRevisionAddress,
                cursor.CurrentFileTailOffsetBytes);
        }

        public void EnsureExactlyMatches(CursorValue expected, string message) {
            if (this != expected) {
                throw new InvalidDataException(message);
            }
        }
    }

    private sealed class StoreTailSnapshot {
        private readonly long[] _tails;

        private StoreTailSnapshot(long[] tails) {
            _tails = tails;
            long total = 0;
            foreach (long tail in tails) {
                total = checked(total + tail);
            }

            TotalTailOffsetBytes = total;
        }

        public long TotalTailOffsetBytes { get; }

        public static StoreTailSnapshot Capture(RbfFileStore store) {
            long[] tails = Enumerable.Range(1, store.FileCount)
                .Select(index => store.GetFile((uint)index).TailOffsetBytes)
                .ToArray();
            return new StoreTailSnapshot(tails);
        }

        public long GetTail(uint fileNumber) {
            if (fileNumber == 0 || fileNumber > _tails.Length) {
                throw new InvalidDataException(
                    $"Evaluator Store snapshot has no file {fileNumber}.");
            }

            return _tails[checked((int)fileNumber - 1)];
        }

        public void EnsureAppendOnlyFrom(StoreTailSnapshot source) {
            if (_tails.Length < source._tails.Length) {
                throw new InvalidDataException(
                    "An evaluator Commit removed an RBF file.");
            }

            for (int index = 0; index < source._tails.Length; index++) {
                if (_tails[index] < source._tails[index]) {
                    throw new InvalidDataException(
                        $"Evaluator Commit shortened RBF file {index + 1}.");
                }
            }
        }

        public void EnsureExactlyMatches(
            StoreTailSnapshot expected,
            string message) {
            if (!_tails.SequenceEqual(expected._tails)) {
                throw new InvalidDataException(message);
            }
        }
    }
}
