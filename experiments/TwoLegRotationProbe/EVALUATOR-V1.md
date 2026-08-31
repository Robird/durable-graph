# TwoLeg evaluator v1 admissibility and accounting contract

> Status: typed outcome, closed-horizon session, cross-assembly matched batch, and
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
source epoch but retains a measurable terminal-liability bias. The named fixed-cadence
witness below measures one such carry into the next epoch; general complete-cycle and
long-run protocols remain outside v1.

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
resolved from an organizer-supplied `StrategyBindingV1` and carries versioned identities for source fixture,
trace definition, generator, one atomic selection profile, evaluator, settlement,
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

The four current strategies live in the independent Baselines assembly: debt-zero
rotation with either no migration or smallest-eligible-debt pacing, plus Adaptive
`(3,5%)` and `(4,4%)`. The organizer explicitly supplies their identity/binding matrix;
Arena does not own a concrete strategy registry. The current implementations consume
only `StrategyStepViewV1` payload facts and return complete Stay/Rotate actions through
one whole-run context. That adapter does not require future candidates to expose a
selector interface or use the same internal code shape. The context hides step index,
future trace, candidate feasibility, metrics, addresses, and observations, and never
falls back after rejection.

`StrategyRunProductV1` is Arena-certified and contains the final in-memory Store,
workload Commit receipts, final checkpoint, typed termination, and terminal-settlement
Revision count. It has no candidate-declared metrics. A naked final Store would be
insufficient because P needs outer-Commit boundaries, F needs Current-scope checkpoints,
and R needs the final PublishedRevision. General validation of an arbitrary hand-built
Store/ledger is not implemented by v1.

`BenchmarkV1Json` writes compact canonical UTF-8 manifest/report documents with one
trailing LF, fixed property/token order, ordinal case ordering, 16-digit hexadecimal
seeds, manifest SHA-256, and resolved trace SHA-256. The report is deliberately the
comparable W/P/F/R plus admissibility/final-scope/settlement projection—not a lossless
dump of `FinalColdHeadReadObservation` or candidate diagnostics. Rejected leaves have
no metrics/cursor/settlement properties. V1 is writer-only: external parsing, file I/O,
and CLI publication remain outside this slice.

Corpus revision 5 runs all four profiles over four matched inputs. Within each workload
group the source fixture, exact expanded trace, evaluator protocols, and accounting
horizon are identical; apart from the case ID, the only experimental input that changes
is the atomic selection profile. The canonical raw outcomes are:

| Trace | Selection profile | W | P | F | R | Final scope |
|---|---|---:|---:|---:|---:|---|
| `debt-zero-then-rotate` | no migration | 856 | 680 | 680 | 832 | 2/3 |
| `debt-zero-then-rotate` | paced one debt | 1536 | 696 | 804 | 756 | 3/4 |
| `debt-zero-then-rotate` | Adaptive `(3,5%)` | 1536 | 696 | 804 | 756 | 3/4 |
| `debt-zero-then-rotate` | Adaptive `(4,4%)` | 1536 | 696 | 804 | 756 | 3/4 |
| `mixed-small` seed 12345 | no migration | 368 | 164 | 344 | 352 | 2/3 |
| `mixed-small` seed 12345 | paced one debt | 368 | 164 | 344 | 352 | 2/3 |
| `mixed-small` seed 12345 | Adaptive `(3,5%)` | 440 | 164 | 184 | 280 | 3/4 |
| `mixed-small` seed 12345 | Adaptive `(4,4%)` | 440 | 164 | 184 | 280 | 3/4 |
| `read-amplification-threshold-band` | no migration | 1460 | 1072 | 1072 | 1064 | 2/3 |
| `read-amplification-threshold-band` | paced one debt | 1512 | 1056 | 1056 | 1472 | 2/3 |
| `read-amplification-threshold-band` | Adaptive `(3,5%)` | 1516 | 1056 | 1056 | 1212 | 2/3 |
| `read-amplification-threshold-band` | Adaptive `(4,4%)` | 1512 | 1056 | 1056 | 1472 | 2/3 |
| `previous-debt-share-dilution-boundary` | no migration | 3160 | 1056 | 2148 | 1048 | 2/3 |
| `previous-debt-share-dilution-boundary` | paced one debt | 4188 | 1056 | 2156 | 1048 | 3/4 |
| `previous-debt-share-dilution-boundary` | Adaptive `(3,5%)` | 2148 | 1004 | 1096 | 1124 | 3/4 |
| `previous-debt-share-dilution-boundary` | Adaptive `(4,4%)` | 2192 | 1012 | 1132 | 1088 | 3/4 |

All sixteen cases are admitted. On `debt-zero-then-rotate`, both Adaptive profiles equal
paced exactly; on `mixed-small`, no-migration equals paced while both Adaptive profiles
equal each other. The threshold-band input ends that universal masking: paced equals
Adaptive `(4,4%)`, while Adaptive `(3,5%)` writes 4 more bytes for 260 fewer final
cold-read bytes at equal P/F. No-migration writes and reads less than either group but
has P/F 16 bytes higher. Its three unique vectors are therefore pairwise incomparable.
The debt-share workload separately makes the Adaptive pair cross the strict 4%/5% target
boundary. These are matched bounded results, not a default profile or steady-state winner.

Manifest schema version 2 replaces the old target/decision pair with one
`selectionProfile`; corpus revision 5 records the sixteen-case expansion. Report schema
and W/P/F/R leaves are unchanged and remain bound through the manifest SHA-256. There
is no compatibility layer, mandatory strategy interface, arbitrary parameter input,
or score. The report also rejects an outcome whose declared workload horizon differs from
its manifest, or whose admitted/capacity phase cannot be emitted by evaluator v1.
Executable authority is split between
[`BenchmarkV1RunnerTests.cs`](Tests/BenchmarkV1RunnerTests.cs) for case inventory,
the original eight outcomes, and canonical hashes,
[`BenchmarkV1ThresholdBandWorkloadTests.cs`](Tests/BenchmarkV1ThresholdBandWorkloadTests.cs)
for the third workload's exact ties and local Pareto relations,
[`BenchmarkV1DebtShareDilutionWorkloadTests.cs`](Tests/BenchmarkV1DebtShareDilutionWorkloadTests.cs)
for the fourth workload's ordinary-trace target boundary,
[`BenchmarkAdaptiveSelectionProfileTests.cs`](Tests/BenchmarkAdaptiveSelectionProfileTests.cs)
for divergent exact parameter binding,
[`StrategyArenaContractTests.cs`](Tests/StrategyArenaContractTests.cs) for the
cross-assembly/product and parent-debt-vs-E seam, and
[`BenchmarkV1JsonTests.cs`](Tests/BenchmarkV1JsonTests.cs) for the schema-2 JSON leaf.

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

### Named source-layout/provenance 2x2 diagnostic

A second test-local diagnostic uses the existing six-object role-disjoint source:
cold objects `{1,2,3}` and later-changed objects `{10,20,30}` have equal aggregate
Base payloads. The Shared source puts all six payload heads in one A Revision; the
Split source uses one cold and one changed payload Revision in one legal accepted
chain. Both end in a metadata-only full-OVD A anchor and the same-shaped metadata-only
B anchor. Revision count, OVD path, tickets, offsets, and encoded source bytes also
change, so this is a bundled source-layout/provenance treatment rather than pure
Frame packing.

Each cell runs the same four workload Stays, one terminal settlement, and one explicit
zero-workload terminal settlement. All therefore have six outer Commits, exactly two
scope advances, and final scope `3/4`:

| Source layout | Decision treatment | W | P | F | R | Final Previous debt |
|---|---|---:|---:|---:|---:|---|
| Shared | no migration | 1556 | 1288 | 1288 | 1352 | 1, 2, 3, 10, 20, 30 |
| Shared | paced one debt | 2284 | 788 | 956 | 1352 | 20, 30 |
| Split | no migration | 1556 | 1288 | 1288 | 1352 | 1, 2, 3, 10, 20, 30 |
| Split | paced one debt | 2284 | 788 | 956 | 1352 | 20, 30 |

The source treatment is nevertheless observable before the horizon closes. Required
Previous-file live-object reconstruction payload-Frame bytes after the four workload
steps are `1276,1276,1276,1276` for both Shared cells, `1308,1308,1308,1308` for
Split/no migration, and `1308,1308,656,656` for Split/paced. The paced Split treatment
releases the cold payload Frame on the third workload step; object-debt membership
alone does not express that closure.

Cross-layout W/P/F/R equality is therefore a useful negative result, not evidence that
source layout is generally irrelevant. Bootstrap writes are outside W, R reads only
the final head, and two scope advances remove the original A from final current
reconstruction. The intermediate Frame observations are diagnostics, not actual or
cumulative IO and not a fifth score. Exact provenance and vector assertions live in
[`SourceLayoutFixedHorizonTests.cs`](Tests/SourceLayoutFixedHorizonTests.cs).

### Named fixed-cadence two-epoch terminal-liability witness

A third test-local diagnostic isolates migration scheduling from rotation-trigger
selection. Both treatments start with the same three cold objects in one A Frame and
the same metadata-only B head. Each epoch supplies three isomorphic, nonempty
Create/Remove foreground Saves; every workload target is externally fixed to Stay-B,
then one direct terminal settlement advances the scope. The foreground ObjectIds differ
between epochs because this workload model forbids ObjectId reuse, but retain the same
encoded widths and leave the live logical state unchanged at each epoch boundary.

The no-migration and paced-one-debt treatments therefore both execute eight outer
Commits and exactly two scope advances, ending at scope `3/4` with the same live state
and Previous debt `{10,20,30}`:

| Treatment | Segment | Commits | W | P | F | R |
|---|---|---:|---:|---:|---:|---:|
| no migration | epoch 1 | 4 | 796 | 664 | 664 | 656 |
| no migration | epoch 2 | 4 | 188 | 52 | 800 | 700 |
| no migration | combined | 8 | 984 | 664 | 800 | 700 |
| paced one debt | epoch 1 | 4 | 812 | 348 | 800 | 792 |
| paced one debt | epoch 2 | 4 | 812 | 348 | 812 | 792 |
| paced one debt | combined | 8 | 1624 | 348 | 812 | 792 |

After the first settlement, no-migration has no Previous debt while paced has
`{10,20,30}`: its B-side migrations became the next scope's Previous dependency. The
second epoch's three natural paced Saves then migrate exactly `10`, `20`, and `30`
again. Thus the first epoch's terminal liability incurs repeated work in later natural
Saves rather than merely being carried through another zero-workload settlement.

Within this fixed cadence, pacing trades `+640 W` for `-316 P`, while horizon `F` and
final-only `R` are respectively 12 and 92 bytes higher. This is a raw Pareto
observation, not a winner: the cadence is an experimental control rather than
`DebtZeroThenRotate` or a product rotation trigger, R remains final-head-only, and
equal final debt membership does not imply equal retained physical layout/provenance
state or a regenerative steady-state cycle. The exact debt/migration trajectory and
vectors live in
[`FixedCadenceTerminalLiabilityTests.cs`](Tests/FixedCadenceTerminalLiabilityTests.cs).

### Common third-epoch continuation discriminator

The two fixed-cadence endpoints above agree on live logical state, scope `3/4`,
Previous debt `{10,20,30}`, each object's `H/B=1`, and a 52-byte Current-file tail.
They deliberately retain different physical provenance: no-migration's three cold
Bases share one file-3 Frame, while paced's Bases occupy three Frames. Their source
cold-head reads are therefore 700 and 792 bytes even though the coarse continuation
inputs agree.

Both endpoints then receive the same third epoch: the same three isomorphic nonempty
Saves, fixed Stay-B targets, and the same paced-one-debt selector. Both migrate
`10`, `20`, and `30`; their exact workload-Commit writes are `152,260,348` bytes and
their direct terminal settlements each write 52 bytes. The closed third-epoch vector
is identical on both sides:

| Source history | Commits | W | P | F | R |
|---|---:|---:|---:|---:|---:|
| no-migration shaped | 4 | 812 | 348 | 812 | 792 |
| paced shaped | 4 | 812 | 348 | 812 | 792 |

Physical history remains visible before the third epoch closes. After the three
workload Saves, exact cold-head bytes are `848,1104,792` versus `792,792,792`, and
required Previous-file object Frames are `1,1,0` versus `2,1,0`. The shared Frame on
the first side remains required until its last resident debt object moves; the second
side releases singleton Frames one by one. Once all three objects have been rewritten,
the two physical states converge for this continuation and the direct settlement keeps
their final-only R equal.

Across all three epochs the accumulated vectors are `12/1796/664/812/792` and
`12/2436/348/812/792`. Those cumulative W/P differences were incurred while shaping
the two source histories; they are not a cost difference in the common third epoch.
The observation therefore says only that the coarse endpoint summary is sufficient
for this fixed continuation's closed W/P/F/R, while retained Frame layout/provenance
still determines intermediate checkpoint cold-read pressure. It does not prove general
state sufficiency, pure packing causality, actual cumulative IO, or a fifth metric.

### Named adaptive payload-policy matched-cadence diagnostic

The first `ReadAmplificationLimit=3`, `BaseBudgetFraction=5%` policy witness starts with
four 100-byte old-A objects, updates one hot object four times with a 50-byte Delta, then
inserts a sentinel. Adaptive naturally selects four Stays followed by one Rotate; the
no-migration and paced controls are externally held to that same workload target cadence.
All sides then execute one direct terminal settlement, so each has six outer Commits,
two scope advances, final scope `3/4`, and the same logical state:

| Decision treatment | W | P | F | R | Final Previous debt |
|---|---:|---:|---:|---:|---|
| Delta, no migration | 924 | 476 | 476 | 524 | 10,20,30,40,1001 |
| Delta, paced one debt | 1248 | 372 | 748 | 524 | 10,1001 |
| Adaptive `(3,5%)` | 1296 | 472 | 796 | 524 | 1001 |
| Adaptive `(4,4%)` | 1296 | 472 | 796 | 524 | 1001 |

Both adaptive parameter sets migrate `20,30,40`, then write hot object `10` as Base.
Across the initial head and four Update result heads, their realized reconstruction
payload is `100,150,200,250,100`, versus `100,150,200,250,300` for both controls. They
are exactly equal because the
20B/16B soft budgets cannot fit a 100B optional Base and the fourth Save's progress
floor—not either strict read threshold—forces the remaining A-dependent hot object to
Base. This is a parameter-insensitive negative control. Paced nevertheless strictly
dominates both adaptive rows in W/P/F with equal final-only R in this fixture. The two
subsequent rotations make R blind to the intermediate reset; that is a
measurement/horizon boundary, not evidence that read amplification has no value and not
a general policy ranking.

The test-local realized diagnostic records raw per-object `H/B` at initial, accepted
workload, and admitted final-settlement heads, with `0/0` and positive-over-zero
represented explicitly. It is distinct from the
prospective policy operand `(H+D)/B`, and it is neither physical Frame IO nor a fifth
canonical metric.

A threshold-band fixture, now also encoded as the third canonical benchmark workload,
makes the hot object B-contained, leaves one-byte
NoChange objects to satisfy progress, and gives both parameter sets enough discretionary
budget for its 10-byte Base. Both sides therefore Stay eight times and migrate `1..8`;
only prospective amplification `3.5` distinguishes the strict limits:

| Parameters | W | P | F | R | Final hot H/B | Hot reconstruction Frames |
|---|---:|---:|---:|---:|---:|---:|
| Adaptive `(3,5%)` | 1516 | 1056 | 1056 | 1212 | 15/10 | 2 |
| Adaptive `(4,4%)` | 1512 | 1056 | 1056 | 1472 | 40/10 | 7 |

Here `(4,4%)` writes 4 fewer physical bytes and reads 260 more bytes at final cold load;
P/F remain equal because the shared 1000-byte terminal evacuation dominates them. This
is a bounded hot-chain-reset discriminator, not a pure `E/G` rotation band, Peak result,
tuned default, or general winner. The final Remove also expires small maintenance
co-residents and releases their Frame pins; the official result is therefore integrated
hot-chain plus lifecycle/layout evidence, not a pure per-object microbenchmark. Detailed
`H/B`, budget, migration, and Frame-count diagnostics remain test-local.

A lower-bound target-band fixture is now the fourth canonical workload. Ordinary step 0
creates object 1 at 40 bytes and object 100 at 960 bytes; measured full-rewrite Updates
then visit `100/1/100` with `Base == Delta`. For the Adaptive pair, the first Update
produces the same physical pre-boundary source with `G=1000,E=40`; weak dominance makes
the read limit and representation choice inert. Five percent strictly selects Rotate
while four-percent equality selects Stay, and the last Update crosses the targets again:

| Selection profile | Workload targets | W | P | F | R | Final scope |
|---|---|---:|---:|---:|---:|---|
| no migration | Stay, Stay, Stay | 3160 | 1056 | 2148 | 1048 | 2/3 |
| paced one debt | Stay, Stay, Rotate | 4188 | 1056 | 2156 | 1048 | 3/4 |
| Adaptive `(3,5%)` | Stay, Rotate, Stay | 2148 | 1004 | 1096 | 1124 | 3/4 |
| Adaptive `(4,4%)` | Stay, Stay, Rotate | 2192 | 1012 | 1132 | 1088 | 3/4 |

All cases are admitted with three workload Commits plus direct terminal settlement and
no settlement migrations. Only the Adaptive pair shares the intended physical source at
the boundary; the controls remain corpus baselines, not additional 4%/5% crossover
treatments. The result is target timing/terminal-liability evidence, not a static
candidate-cost comparison, steady-state result, default, or winner.

The first test-local parameter matrix is intentionally a causal index rather than a
new executable aggregate or score:

| Witness | Isolated axis | Observed result | Boundary |
|---|---|---|---|
| Matched negative control | masked parameter change | identical trajectory/vector | progress and indivisible Bases can hide both parameters |
| Read-threshold band | strict read limit | less W for more final R; P/F equal | local W/R trade only |
| Base-fraction lower bound | strict rotation share | ordinary-trace target crossover and a different raw trade | only the Adaptive pair shares the intended boundary source; finite horizon |
| Selected capacity rejection | exact hard gate | typed rejection, no fallback or mutation | no numeric penalty or averaging |

Exact vectors, target/debt trajectories, and rejection details remain asserted only by
their owning tests; the table does not create a fifth metric or duplicate a sizing
authority.

The selector uses payload proxies only. A separate B-contained hot-chain witness proves
strictly above-limit Base selection through exact planning/apply, while a policy-selected
oversized Rotate proves typed capacity rejection, no fallback, and zero Store mutation.
None of these test-local diagnostics changes the v1 report schema. Executable authority
lives in [`ReadAmplificationBaseBudgetPolicyIntegrationTests.cs`](Tests/ReadAmplificationBaseBudgetPolicyIntegrationTests.cs),
[`ReadAmplificationBaseBudgetPolicyThresholdBandTests.cs`](Tests/ReadAmplificationBaseBudgetPolicyThresholdBandTests.cs),
[`ReadAmplificationBaseBudgetPolicyTargetBandTests.cs`](Tests/ReadAmplificationBaseBudgetPolicyTargetBandTests.cs),
[`BenchmarkV1DebtShareDilutionWorkloadTests.cs`](Tests/BenchmarkV1DebtShareDilutionWorkloadTests.cs),
[`RealizedReconstructionPayloadAmplificationDiagnosticTests.cs`](Tests/RealizedReconstructionPayloadAmplificationDiagnosticTests.cs),
and [`ReadAmplificationBaseBudgetPolicyCapacityTests.cs`](Tests/ReadAmplificationBaseBudgetPolicyCapacityTests.cs).

## Still open before strategy selection

- test whether the known-future hot/cold witness can become an ordinary matched
  locality/ObjectId-permutation workload without exposing a future oracle; preserve a
  negative result if the natural trace does not reproduce the hand-shaped divergence;
- then add the smallest size-skew, lifecycle/churn, pressure/capacity, and horizon-phase
  families needed before freezing the first competition packet;
- keep checkpoint cold reads outside the canonical frontier; reconsider an intermediate
  read guardrail only when a named restart/read schedule or cold-start SLO exists;
- retain Pareto/raw outcomes until workload/SLO evidence justifies guardrails or a
  ranking rule.
