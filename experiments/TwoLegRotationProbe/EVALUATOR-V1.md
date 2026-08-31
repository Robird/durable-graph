# TwoLeg evaluator v1 admissibility and accounting contract

> Status: typed outcome, closed-horizon session, cross-assembly matched batch, and
> canonical machine-readable manifest/report implemented.

This document fixes the first executable protocol and raw measurement schedule for
comparing admitted TwoLeg policy runs. It does not define a scalar score, a winner,
or a product policy API.

## Admission boundary

`W_workload/P_workload/F/R` exist only for realized, accepted work. A selected capacity rejection,
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
the B preparation peak; the whole burst forms the single internal `W_terminal` sample and
therefore participates only in the internally derived `P_closed`, not canonical
`P_workload`. A
direct Rotate therefore has zero empty Stay Revisions, while a multi-step settlement
deliberately exposes its full terminal burst.

Successful closure means terminal source `A/B` became result `B/C`, logical state was
preserved, and the final OVD materialization plus every live current-reconstruction
path use only B/C. It does **not** mean the result has zero Previous debt: legal C-side
External bindings may make B the new Previous dependency. This v1 protocol closes one
source epoch but retains a measurable terminal-liability bias. The named fixed-cadence
witness below measures one such carry into the next epoch; general complete-cycle and
long-run protocols remain outside v1.

## Raw metrics

### Wworkload — `WorkloadPhysicalWriteBytes`

For each successful caller workload outer Commit:

```text
CommitWriteBytes =
    Sum(all file TailOffsetBytes after Commit)
  - Sum(all file TailOffsetBytes before Commit)

W_workload = WorkloadPhysicalWriteBytes
           = Sum(CommitWriteBytes for successful workload Saves)
```

This is the canonical write comparator. Existing bytes before the evaluation horizon
are not charged. A new file did not exist in the before snapshot, so its initial 4-byte
header fence and every Frame appended by that Commit are charged automatically. The
value is modeled physical append size under the in-memory RBF v0.40 model, not measured
filesystem traffic, flush latency, or write amplification below that model.

The accumulator also retains closed-horizon write accounting internally:

```text
W_terminal = TerminalSettlementPhysicalWriteBytes
W_total = TotalPhysicalWriteBytes
        = W_workload + W_terminal
```

`W_terminal` contains the one
canonical terminal-settlement outer Commit, including all preparatory and final
Revisions in that synthetic Commit. Bootstrap and rejected/unrealized attempts enter
neither part. `W_terminal` and `W_total` verify closure accounting and diagnose terminal
liability, but they are phase-sensitive liquidation values rather than policy-comparison
metrics. Canonical reports omit both while the evaluator continues to enforce the
conservation identity above. Terminal settlement itself remains mandatory for admission.

### Workload payload references

Two more exact workload-only integers provide a stable foreground payload denominator:

```text
DeltaReference = Sum(Insert.ResultBasePayloadBytes)
               + Sum(Update.DeltaPayloadBytes)

BaseReference  = Sum(Insert.ResultBasePayloadBytes)
               + Sum(Update.ResultBasePayloadBytes)
```

Only Inserts and Updates from successful workload Saves contribute. Remove, NoChange,
bootstrap, terminal settlement, and rejected/unrealized Saves contribute zero. The
references depend on normalized workload facts, not on whether the selected candidate
actually wrote an Update as Base or Delta.

These are synthetic foreground payload references. They omit record headers, tags,
tickets, OVD/meta bytes, file headers, alignment/layout effects, cold migration, and
terminal evacuation. Consequently they are neither physical all-Delta/all-Base baseline
runs, nor lower/upper bounds, nor a score. The report emits the raw integers and no
derived floating-point ratio. A fuller `I/UB/UD/NB` decomposition and attribution of
overhead to particular strategy reasons remain deferred until the new diagnostics have
an observed consumer.

### P — `PeakWorkloadCommitWriteBytes`

```text
P_workload = Max(CommitWriteBytes for successful workload Saves)
P_closed = Max(P_workload, W_terminal)
```

The boundary is the outer Commit, not an individual Revision Frame. The accumulator
therefore permits several accepted Revision checkpoints between `BeginCommit` and
`EndCommit`; all of their file growth contributes to the same workload peak sample.
Evaluator v1 attributes every preparatory/final settlement Revision to one synthetic
Commit, so its burst is already exactly `W_terminal`. Canonical reports expose only
`P_workload`; the terminal burst and old closed-horizon peak remain internally available
through the identity above instead of hiding the natural-Save peak.

### F — `MaxCurrentFileTailBytes`

```text
F = Max(Current file TailOffsetBytes at the initial state and every realized checkpoint)
```

This is the maximum absolute tail of the file that was Current at that point in the
run. It is not total Store bytes, total historical-file bytes, epoch growth, or
`MaxDurableGraphRelativeFrameStartOffsetBytes - tail` slack. Those may remain useful
diagnostics but are not aliases for F. F deliberately includes terminal-settlement
checkpoints: it is a closed-horizon file-capacity guardrail and can therefore retain
terminal-phase sensitivity even though canonical write comparison uses only
`W_workload`.

### R — `TotalWorkloadColdReadBytes`

The read schedule is named `AfterEveryWorkloadSaveColdLoad`: after every successful
caller workload Save, start with an empty Frame cache and load that accepted
PublishedRevision once. If there are `N` workload Saves, sample `t` is:

```text
RequiredFrames(t) = Unique(
    OVD materialization Revision Frames
  ∪ every live object's current-reconstruction Frames)

C(t) = Sum(RequiredFrames(t).FrameLengthBytes)
G(t) = Sum(every post-Save live object's BasePayloadBytes)

R = Sum(C(t), t = 1..N)
L = Sum(G(t), t = 1..N)
```

The report keeps the exact integer pair `(R, L)` rather than serializing a rounded ratio.
`R / N` is the average cold-load bytes per workload Save; `R / L` is aggregate physical
read amplification over the workload schedule. The latter is a ratio of sums, not an
unweighted mean of per-Save ratios. When `L == 0`, amplification is undefined even though
R may be positive because the OVD chain still has to be materialized.

A Frame used by both the OVD and an object chain is counted once within one sample. The
cache resets between Saves, so a Frame required by several samples is charged once in
each. Bootstrap, rejected/unrealized attempts, settlement preparation Revisions, and the
synthetic terminal settlement Commit do not produce workload read samples. `W_terminal`
remains an internal terminal-liability diagnostic, while canonical F still observes
closed-horizon capacity pressure and canonical P is workload-only. This asymmetry is
deliberate and explicit.

`TerminalColdHeadReadBytes` separately measures one empty-cache load after terminal
settlement. It diagnoses the evaluator's artificial closed-horizon placement and is not R;
schema 5 keeps it internal rather than publishing it as a comparable metric.
The full observation retains OVD-only and object-reconstruction sets/bytes separately,
but their individual byte sums may overlap and must not be added.

`FrameLengthBytes` follows the current full-Frame read model; it does not model cache
hits, TailMeta-only routing IO, OS block rounding, compression, or historical lineage
queries beyond current reconstruction.

## Implemented seam and evidence

- `Evaluation/EvaluatorRawMetricAccumulator.cs` records append-only Store snapshots
  inside explicit Commit boundaries and derives canonical `W_workload/P_workload/F`
  plus internal terminal/total write conservation.
- `Evaluation/FinalColdHeadReadMeasurer.cs` derives each exact cold load from authoritative
  OVD materialization and object reconstruction; the accumulator samples it once per
  successful workload outer Commit and once separately after terminal settlement.
- `Tests/EvaluatorRawMetricTests.cs` fixes:
  - Stay then Rotate accounting, including the fresh-C 4-byte header;
  - multiple realized Revisions grouped into one Commit peak;
  - exact workload/terminal write conservation and workload-only Delta/Base references;
  - one cold-load sample per outer workload Commit and checked cumulative `(R, L)`;
  - OVD/object Frame de-duplication;
  - positive cold-head bytes over a zero-live-payload denominator.
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

The experiment-only runner combines one step-0 A/B bootstrap, one frozen workload,
an organizer-supplied strategy binding, and one evaluator session. Candidate code sees
only the current `StrategyStepViewV1`; the Arena retains normalization, exact planning,
capacity admission, apply, terminal settlement, logical validation, and all metrics.

Manifest schema 2 uses one atomic `selectionProfile` per case. Metrics identity
`raw-wpfr/5` and report schema 5 emit typed position plus the seven canonical integers
defined above. Corpus revision 17 keeps seventeen adjustable traces and the two active
Adaptive profiles, for 34 admitted cases. Their existing component IDs now carry version
2 because the corrected motive/budget behavior changes the selection contract.
The retired no-migration and paced-one-debt profiles are not benchmark references:
`DeltaReference` and `BaseReference` provide the strategy-independent write
comparators. Their last complete implementation and 64-case report are archived at
Git tag `research/no-migration-paced-baselines-20260901`.

The workload corpus remains feedback-driven rather than hash-locked as a public
benchmark. Tests protect the runner/report contract, strategy parameter binding, hard
rejection behavior, and selected policy semantics; they do not freeze every exploratory
workload vector.

### Current active-hundred observation

The workload creates 100 persistent mixed Field/List objects and performs 64 Saves,
updating 60 distinct objects per Save. Both active profiles share:

- `DeltaReference = 67206`;
- `BaseReference = 165606`;
- `L = 273804`;
- 64 workload cold-load samples.

| Profile | Wworkload | Pworkload | F | R | R/L |
|---|---:|---:|---:|---:|---:|
| Adaptive `(3,5%)` | 116424 | 2128 | 58460 | 3501092 | 12.7869 |
| Adaptive `(4,4%)` | 114756 | 2104 | 91380 | 3754140 | 13.7110 |

Adaptive `(4,4%)` writes 1668 fewer bytes during natural Saves and keeps the natural
workload peak 24 bytes lower. Adaptive `(3,5%)` lowers F by 32920 bytes and R by 253048
bytes. This is a write/peak versus closed-horizon file-tail/read Pareto trade, not a
winner, tuned default, or steady-state claim.

The seventeenth workload, `oversized-cold-nochange-tiny-clock`, is a white-box adversarial
trace: a 10,000-byte unmotivated NoChange remains beside a one-byte active clock. The
pre-fix policy produced `Pworkload=10052` by forcing that migration. The corrected
regression requires `Pworkload < 10000` for both profiles rather than freezing an
incidental exact result.

Exact executable authority is
[`BenchmarkV1RunnerTests.cs`](Tests/BenchmarkV1RunnerTests.cs),
[`BenchmarkV1JsonTests.cs`](Tests/BenchmarkV1JsonTests.cs), and the
`ReadAmplificationBaseBudgetPolicy*` tests. Test-local no-migration or one-object
migration actions may still isolate a mechanism, but they are not registered profiles,
manifest cases, or score-table rows.

## Next strategy work

- isolate stable low-amplification A-debt stalling and Ready-to-Rotate versus
  Should-Rotate hysteresis before selecting one minimal independent candidate;
- preserve the corrected strict motive and soft budget behavior; do not restore an
  unconditional progress floor or a NoChange-first category rule;
- add or change a workload only when white-box candidate review exposes one concrete
  blind spot;
- keep raw typed outcomes and Pareto comparisons; do not introduce a scalar score,
  leaderboard, or automatic search yet;
- close determinism/order/artifact qualification only after the candidate shape is ready
  for a frozen round packet.
