# TwoLeg evaluator v1 admissibility and accounting contract

> Status: typed outcome, closed-horizon session, registry-backed matched batch, and
> canonical machine-readable manifest/report implemented.

This document fixes the first executable protocol and raw measurement schedule for
comparing admitted TwoLeg policy runs. It does not define a scalar score, a winner,
or a product policy API.

## Admission boundary

`W/P/F/R` exist only for realized, accepted work. A selected capacity rejection,
`RejectedUnproven`, or incomplete workload is a typed inadmissible outcome, not a run
with a numeric penalty. It has no result cursor and contributes no invented write or
read metrics. Stale/corrupt state, invalid decisions, lifecycle misuse, and unexpected
exceptions remain fail-closed errors; v1 does not hide them behind an `InvalidInput`
outcome.

The mutually exclusive outcomes are:

- `AdmittedEvaluatorRun`: complete workload plus realized terminal settlement;
- `EvaluatorRunCapacityRejected`: the caller-selected workload candidate hit one
  exact hard gate; no fallback is implied;
- `EvaluatorRunRejectedUnproven`: the current finite completion/settlement path was
  blocked; this is not a general impossibility claim;
- `EvaluatorRunIncomplete`: the declared workload or terminal phase was explicitly
  left unfinished.

Only `AdmittedEvaluatorRun` carries a final cursor and `EvaluatorRawMetrics`.

The metric layer assumes that a higher evaluator layer has already enforced:

- logical state and current-reconstruction correctness;
- exact layout/address/capacity gates and failure zero-mutation;
- deterministic replay;
- the chosen closed-horizon / terminal-settlement protocol.

Calling `EvaluatorRawMetricAccumulator.Complete` returns the current accounting
snapshot; by itself it still does not prove those gates or execute deferred work.
`CancelUnrealizedCommit` accepts no score: it only verifies that a typed
non-realized outcome left the Store and cursor exactly unchanged.

## Closed horizon: `DirectRotateElseAscendingSingleDebt-v1`

`EvaluatorV1Session` runs on an isolated in-memory Store fork. It must consume the
declared number of workload steps before admission. It then unconditionally closes
the terminal source epoch, even when the last workload Commit already rotated:

1. normalize the final A/B head as maintenance-only facts;
2. try the exact default Rotate-C with no preparatory Stay;
3. after a capacity rejection, migrate the smallest remaining A-debt ObjectId as one
   same-state Base@B and retry Rotate-C;
4. repeat until Rotate-C fits or the finite path returns `RejectedUnproven`.

Planning and proof replay happen on scratch forks and do not count as writes. Once a
certificate exists, every preparatory Stay and the final Rotate are applied to the
session Store inside one synthetic outer Commit. Each Revision is observed so F sees
the B preparation peak; the whole burst forms one P sample. A direct Rotate therefore
has zero empty Stay Revisions, while a multi-step settlement deliberately exposes its
full terminal burst.

Successful closure means terminal source `A/B` became result `B/C`, logical state was
preserved, and the final OVD materialization plus every live current-reconstruction
path use only B/C. It does **not** mean the result has zero Previous debt: legal C-side
External bindings may make B the new Previous dependency. This v1 protocol closes one
source epoch but retains a measurable terminal-liability bias; complete-cycle and
long-run protocols remain candidates for later comparison.

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
`EndCommit`; all of their file growth contributes to the same peak sample. Evaluator
v1 attributes every preparatory/final settlement Revision to one synthetic Commit and
observes all of them inside that boundary.

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
- `Planning/CanonicalTerminalSettlementPlanner.cs` extracts the deterministic
  continuation that is shared by ordinary Stay admission and terminal settlement.
- `Evaluation/EvaluatorV1Session.cs` owns the isolated run fork, maps workload hard
  gates to typed outcomes, actually replays settlement, and alone promotes success to
  comparable metrics.
- `Tests/CanonicalTerminalSettlementPlannerTests.cs` and
  `Tests/EvaluatorV1SessionTests.cs` cover direct and multi-step settlement, exact
  one-Commit burst accounting, scope closure, deterministic rejection, prefix
  rejection, incompleteness, and source-Store isolation.

## Benchmark-v1 batch consumer

`BenchmarkV1Runner` is the first real consumer of the evaluator seam. Every case is
resolved from one closed registry and carries versioned identities for source fixture,
trace definition, generator, target/decision treatments, evaluator, settlement,
accounting, provisional frame layout/grammar, and read schedule. Its manifest also
contains the SHA-256 of the exact expanded trace, so generator or handwritten payload
drift changes the manifest hash before a result is accepted.

The sole v1 bootstrap is named
`trace-step0-single-a-full-base-then-b-anchor/1`: trace step 0 must contain only
Creates; all resulting Bases share one full-OVD A Frame, followed by a metadata-only
B anchor. Step 0 is outside W/P, while the evaluator consumes `steps[1..]`. This
single-A-Frame topology is an explicit experimental treatment, not a general source
layout claim; exceeding its one-Frame envelope fails the batch instead of becoming an
evaluator capacity outcome.

The closed policy registry currently exposes one historical-facts-only target,
`debt-zero-then-rotate/1`, and two Delta decision treatments: no migration or migrate
the smallest eligible A-debt NoChange ObjectId. Selectors receive only current
`NormalizedSaveFacts`; they cannot inspect step index, future trace, candidate
feasibility, or observations, and never fall back after rejection.

`BenchmarkV1Json` writes compact canonical UTF-8 manifest/report documents with one
trailing LF, fixed property/token order, ordinal case ordering, 16-digit hexadecimal
seeds, manifest SHA-256, and resolved trace SHA-256. The report is deliberately the
comparable W/P/F/R plus admissibility/final-scope/settlement projection—not a lossless
dump of `FinalColdHeadReadObservation` or candidate diagnostics. Rejected leaves have
no metrics/cursor/settlement properties. V1 is writer-only: external parsing, file I/O,
and CLI publication remain outside this slice.

Corpus revision 2 runs both decision treatments over two matched inputs. Within each
pair the source fixture, exact expanded trace, target treatment, evaluator protocols,
and accounting horizon are identical; apart from the case ID, the only experimental
input that changes is the decision treatment. The canonical raw outcomes are:

| Trace | Decision treatment | W | P | F | R | Final scope |
|---|---|---:|---:|---:|---:|---|
| `debt-zero-then-rotate` | no migration | 856 | 680 | 680 | 832 | 2/3 |
| `debt-zero-then-rotate` | paced one debt | 1536 | 696 | 804 | 756 | 3/4 |
| `mixed-small` seed 12345 | no migration | 368 | 164 | 344 | 352 | 2/3 |
| `mixed-small` seed 12345 | paced one debt | 368 | 164 | 344 | 352 | 2/3 |

All four cases are admitted. `mixed-small` is a negative control: neither evaluated
step contains an eligible A-debt `NoChange`, so the two treatment labels execute the
same decisions and produce the same result. The handwritten pair is active, but it
also exposes the current horizon coupling: pacing rotates once during the workload,
then the unconditional terminal settlement rotates again. Its higher W/P/F and lower
R therefore cannot be attributed to a general pacing effect or used to name a winner.

The manifest revision changed without changing the v1 JSON schemas. Pair identity is
currently enforced by corpus construction and executable tests; no duplicate
`ComparisonGroupId`, generic policy interface, or score was introduced. The report
also rejects an outcome whose declared workload horizon differs from its manifest, or
whose admitted/capacity phase cannot be emitted by evaluator v1.

### Named fixed-two-scope-advances diagnostic

One test-local diagnostic continues the handwritten control from its admitted 2/3
head through one additional zero-workload `EvaluatorV1Session`, while the paced case
already ends at 3/4. Each session remains an independently accounted closed segment;
the diagnostic concatenates them with Commit count/W as sums, P/F as maxima, and R
from the final cold head only. It does not change the canonical v1 report schema.

| Treatment | Commits | W | P | F | R | Final scope | Final Previous debt |
|---|---:|---:|---:|---:|---:|---|---|
| no migration | 6 | 944 | 680 | 680 | 752 | 3/4 | 10, 20, 30 |
| paced one debt | 5 | 1536 | 696 | 804 | 756 | 3/4 | 1004 |

The control's extra segment is one direct settlement Commit with
`W/P/F/R=88/88/680/752`; full Store tail growth independently equals the concatenated
W. Equal scope removes the different-final-file-generation confounder, but not Commit
placement, debt membership, Frame layout, terminal liability, or the single-A-Frame
fixture bias. The vector is therefore a named discriminator, not a general winner.

## Still open before strategy selection

- repeat the matched/equal-scope question over an anchor-normalized shared/split source
  layout before promoting the diagnostic into the canonical corpus;
- compare complete-cycle or long-run schedules before treating terminal economics as
  neutral;
- retain Pareto/raw outcomes until workload/SLO evidence justifies guardrails or a
  ranking rule.
