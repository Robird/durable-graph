# TwoLeg evaluator v1 accounting contract

> Status: metric-accounting slice implemented; full evaluator not yet implemented.

This document fixes the first raw measurement schedule for comparing admitted
TwoLeg policy runs. It does not define a scalar score, a winner, or the protocol
that proves an evaluation horizon closed.

## Admission boundary

`W/P/F/R` exist only for realized, accepted work. A capacity rejection,
`RejectedUnproven`, invalid state, or incomplete workload is a typed inadmissible
outcome, not a run with a numeric penalty. It has no result cursor and contributes
no invented write or read metrics.

The metric layer assumes that a higher evaluator layer has already enforced:

- logical state and current-reconstruction correctness;
- exact layout/address/capacity gates and failure zero-mutation;
- deterministic replay;
- the chosen closed-horizon / terminal-settlement protocol.

Calling `EvaluatorRawMetricAccumulator.Complete` returns the current accounting
snapshot; it does not prove those gates or execute deferred certificate work.
`CancelUnrealizedCommit` accepts no score: it only verifies that a typed
non-realized outcome left the Store and cursor exactly unchanged.

## Raw metrics

### W — `TotalPhysicalWriteBytes`

For each caller-declared outer Commit:

```text
CommitWriteBytes =
    Sum(all file TailOffsetBytes after Commit)
  - Sum(all file TailOffsetBytes before Commit)

W = Sum(CommitWriteBytes)
```

Existing bytes before the evaluation horizon are not charged. A new file did not
exist in the before snapshot, so its initial 4-byte header fence and every Frame
appended by that Commit are charged automatically. The value is modeled physical
append size under the in-memory RBF v0.40 model, not measured filesystem traffic,
flush latency, or write amplification below that model.

### P — `PeakCommitWriteBytes`

```text
P = Max(CommitWriteBytes)
```

The boundary is the outer Commit, not an individual Revision Frame. The accumulator
therefore permits several accepted Revision checkpoints between `BeginCommit` and
`EndCommit`; all of their file growth contributes to the same peak sample. If the
future settlement protocol attributes preparatory/final work to one Commit, that
work must be observed inside the same boundary.

### F — `MaxCurrentFileTailBytes`

```text
F = Max(Current file TailOffsetBytes at the initial state and every realized checkpoint)
```

This is the maximum absolute tail of the file that was Current at that point in the
run. It is not total Store bytes, total historical-file bytes, epoch growth, or
`MaxDurableGraphRelativeFrameStartOffsetBytes - tail` slack. Those may remain useful
diagnostics but are not aliases for F.

### R — `FinalColdHeadReadBytes`

The first read schedule is named `FinalHeadColdLoad`: start with an empty Frame cache
and load the final PublishedRevision once.

```text
RequiredFrames = Unique(
    OVD materialization Revision Frames
  ∪ every live object's current-reconstruction Frames)

R = Sum(RequiredFrames.FrameLengthBytes)
```

An empty live graph can therefore have nonzero R because its OVD chain must still be
materialized. A Frame used by both the OVD and an object chain is counted once in R.
The diagnostic observation retains OVD-only and object-reconstruction sets/bytes
separately, but their individual byte sums may overlap and must not be added.

`FrameLengthBytes` follows the current full-Frame read model; it does not model cache
hits, TailMeta-only routing IO, OS block rounding, compression, or historical lineage
queries beyond current reconstruction.

## Implemented seam and evidence

- `Evaluation/EvaluatorRawMetricAccumulator.cs` records append-only Store snapshots
  inside explicit Commit boundaries and derives W/P/F.
- `Evaluation/FinalColdHeadReadMeasurer.cs` derives R from authoritative OVD
  materialization and object reconstruction.
- `Tests/EvaluatorRawMetricTests.cs` fixes:
  - Stay then Rotate accounting, including the fresh-C 4-byte header;
  - multiple realized Revisions grouped into one Commit peak;
  - OVD/object Frame de-duplication;
  - nonzero cold-head read cost for an empty live graph.

## Still open before evaluator v1 is admissible

- choose and execute a canonical closed-horizon / terminal-settlement protocol;
- define the typed run outcome that separates hard rejection from realized metrics;
- freeze manifest identity, workload families, seeds, and deterministic rerun rules;
- only then extract an experiment-only runner and machine-readable report.
