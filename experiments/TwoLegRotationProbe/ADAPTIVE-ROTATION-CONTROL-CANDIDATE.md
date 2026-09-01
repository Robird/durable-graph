# Adaptive rotation control candidate

> Status: deferred research candidate; not implemented. TwoLeg was paused on 2026-09-02.
>
> Scope: one TwoLeg segment. Tiered Hot/Cold composition is a separate deferred branch in
> [`DB-013`](../../docs/design-branches/0013-tiered-state-segments.md).

## Question

The current `ReadAmplificationBaseBudgetPolicy` combines two sound local mechanisms:

- Base-versus-Delta representation choice from estimated read amplification;
- a soft per-Save discretionary Base payload envelope
  `Q = Floor(G * BaseBudgetFraction)`.

Its target rule treats one predicate as both “Rotate is cheap enough” and “Rotate is timely”:

```text
Rotate when G == 0 or E < G * BaseBudgetFraction
```

Executable evidence observes one missing bridge: budget-compatible, low-amplification A-debt
remains unchanged through all 64 natural Saves in the current composite trace. If the same strategy
view continues to repeat, the current pure selector deterministically keeps choosing the same Stay;
that indefinite stall is a source-level deduction, not a measured infinite run. A separate derived
trace shows the opposite failure: when `E == 0`, an active B-contained object whose Update naturally
writes Base can make the policy create a new file on every Save. These are different control problems
and should not be repaired with one overloaded score.

## Three independent responsibilities

The candidate model separates:

1. **ReadMotive** — whether Base is preferable to Delta/inheritance for object reconstruction;
2. **ReadinessProgress** — bounded work that makes remaining old-A evacuation fit the Base envelope;
3. **ShouldRotate** — whether the Current leg has accumulated enough use or pressure to justify
   creating the Next file now.

The target shape becomes:

```text
Rotate = ReadyToRotate && ShouldRotate
```

Current v2 behaves as if `ShouldRotate` were always true. The first candidate change below closes
ReadinessProgress; a later discriminator must select the pressure fact and threshold.

## Strict readiness and exact Need

For `G == 0`, readiness is a separate true special case. For `G > 0`, let:

```text
F = BaseBudgetFraction
Q = Floor(G * F)
ReadyMaxDebtBytes = Ceiling(G * F) - 1

ReadyToRotate = E <= ReadyMaxDebtBytes
              = E < G * F
```

`E` is an integer payload byte count, so these forms are equivalent. Readiness implies mandatory
Rotate evacuation payload is at most `Q`; it does not imply the exact candidate's physical bytes or
capacity are acceptable.

For a selected Stay, existing dominant or read-motivated Base decisions may already retire some
A-debt. After counting each selected object once, define:

```text
ResidualE = E - Sum(BasePayloadBytes of already-selected A-dependent Base objects)
Need = Max(0, ResidualE - ReadyMaxDebtBytes)
```

`ReadinessProgress` may add A-dependent Update or NoChange Bases only until their retired payload
reaches or crosses `Need`. Object granularity may overshoot `Need`, but progress-added Base payload
must not overshoot `Q` or the remaining discretionary envelope.

This is threshold-seeking preparation, not “fill every available Base byte”. Once Ready, low-
amplification progress stops. Independently justified ReadMotive and `Base <= Delta` choices retain
their own semantics and are merely credited once toward readiness.

## Budget scheduling and bounded greedy

The candidate retains v0's distinction between unbudgeted and discretionary actions: Insert and
weakly dominant `Base <= Delta` Update remain outside `Q`; read-motivated and progress-added Base
selections share `Q`. The minimum scheduling order is:

1. select mandatory Insert and weakly dominant Updates; credit dominant A-dependent Updates toward
   `Need`, but do not consume `Q`;
2. select A-dependent ReadMotive objects inside `Q` and credit them toward `Need`;
3. give residual readiness progress a non-starvable place in the remaining `Q`, ahead of B-contained
   read optimization;
4. let B-contained ReadMotive use only the remaining `Q`;
5. include both A-dependent Update and NoChange—processing only NoChange can be defeated by a
   continuously updated A-debt object;
6. skip a progress candidate that cannot fit the remaining envelope, so one oversized object does
   not block later small objects;
7. stop adding low-amplification A-debt when `Need` is met; use any remainder for ordinary
   B-contained ReadMotive.

Each ObjectId belongs to its first applicable bucket and is counted once. An existing motivated
indivisible-first overshoot sets the remaining discretionary envelope to zero; a B-contained
overshoot cannot jump ahead of required A-debt progress. These ordering details are candidate
semantics, not a claim that `Q` is an exact physical P cap.

The first experiment may use deterministic ascending `BasePayloadBytes`, then ascending ObjectId,
for otherwise equal progress candidates. It must claim only positive bounded progress, not
single-Save subset optimality. Finding the best subset that crosses
`Need` under `Q` is a knapsack variant; a simple greedy may need one extra Save even when another
subset could become Ready immediately. No current evidence justifies a solver.

The existing motivated indivisible-first overshoot is a separate, explicit read-amplification
trade. Merely being old A-debt does not grant that exception.

## Why Need-only is preferable to draining Q

Suppose `G=1000`, `F=5%`, `Q=50`, and old-A debt is `E=60`. Strict readiness requires only
`Need=11` bytes of retirement.

```text
Fill-Q:
    Stay writes about 50 Base bytes into B
    Rotate writes the remaining about 10 into C
    about 50 bytes immediately become new Previous debt after B/C rotation

Need-only:
    Stay writes about 11 Base bytes into B
    Rotate writes the remaining about 49 into C
    only about 11 bytes become new Previous debt
```

Ignoring indivisibility and metadata, both paths write the same old-A Base payload before A retires.
Fill-Q changes the phase but not the worst maintenance envelope, speculates more work before a
possible Remove/Update, grows B more, and carries more debt into the next epoch. Therefore the
candidate freezes Need-only preparation; Fill-Q remains a possible future burst-shaping challenger,
not the default.

## Conditional liveness boundary

ReadinessProgress can guarantee eventual readiness only when all of the following eventually hold:

- accepted Save opportunities continue;
- live graph and debt sizes become bounded enough for progress;
- every non-Ready Stay can select at least one needed A-debt object that fully fits the allowed
  atomic progress envelope;
- exact candidate admission continues to succeed;
- B-contained read optimization cannot starve readiness progress.

Within one epoch, the A-debt ObjectId set is monotone decreasing. Under these assumptions, each
accepted non-Ready Stay decision retires at least one member through an already-selected Base or
added progress, so readiness is reached after finitely many Saves. Actual Rotate additionally
requires `ShouldRotate` eventually to become true; readiness alone is not full target liveness.

No policy can simultaneously promise eventual rotation, a per-Save progress limit below `B(i)`, and
an indivisible live object of size `B(i)`. If `Q == 0` or every remaining low-amplification debt object
is larger than `Q`, this candidate neither overshoots nor claims liveness. Object splitting,
Artifact/offload, explicit maintenance/SLO, or DB-013 tiering owns that boundary.

## ShouldRotate remains a separate candidate

The current strategy facts cannot distinguish two states with identical live `G/E/H/D/B` but very
different Current-file histories. One may have just opened B; another may have appended and later
removed enough transient state to put B near its capacity. A pure current-view function must choose
the same target for both, so general Rotate timing needs at least one pressure fact or accepted-only
epoch state.

The leading product-oriented amortization signal is recoverable physical growth after the opening
Revision:

```text
CurrentLegGrowthBytes =
    CurrentFileTailBytes - OpeningRotateRevisionEndBytes

Illustrative candidate shape only:
    K > 0
    EpochReferenceBytes = Max(1, frozen reference chosen when the leg opens)
    Pressured = CurrentLegGrowthBytes >= K * EpochReferenceBytes
    Rotate = ReadyToRotate && Pressured
```

Excluding the opening Rotate Revision prevents its unavoidable checkpoint cost from immediately
triggering another Rotate. Physical growth is monotone, survives reopen, and includes foreground,
migration, OVD, TailMeta and padding bytes. Freezing the reference at leg creation avoids a changing
graph denominator making pressure move backwards. This signal measures amortized use of the leg; it
is not sufficient capacity pressure because a large opening Revision can already put the file near a
hard limit while post-opening growth remains small. Absolute tail/remaining capacity stays a separate
soft/hard pressure and admission concern. The threshold `K`, frozen reference accounting scope,
empty-graph behavior and exact public fact/API are not selected; no current workload or product SLO
justifies freezing them.

Two weaker experimental controls remain useful:

- accepted foreground payload since Rotate: easy to keep in a fresh whole-run executor and useful
  for a first challenger, but volatile across reopen and blind to metadata/maintenance;
- epoch Save count: deterministic and cheap, but gives an empty Save and a huge Save equal weight;
  use only as a possible maximum-lag backstop, not the primary pressure signal.

Accumulated unused Base credit is not preferred. If future spending remains capped by current `Q`,
credit adds no ability beyond reserving this Save's debt-progress budget. If it permits a later burst
above `Q`, it trades average write rate for a larger P and needs a new explicit Peak/SLO parameter.
Per-object debt age is also unnecessary in the first model: all old-A debt in an epoch shares the
same generation, while “time since domain modification” is a different read/heat fact.

When `Ready && !Pressured`, low-amplification readiness progress stops. If later Insert/Remove/size
changes make readiness false again, progress resumes. ReadMotive decisions remain independent.

## Evidence required if the candidate is reactivated

Do not resume from an implementation task list. After an explicit TwoLeg reactivation,
use small test-local traces first:

1. **Persistent versus naturally retired cold debt** — identical prefix with many small, budget-fit
   A-debt objects; one arm remains cold, the other removes them before rotation. This measures the
   unavoidable online cost of early progress.
2. **Near-ready placement sawtooth** — `E` exceeds strict readiness by only a few bytes. Compare
   Need-only with Fill-Q, then observe post-Rotate debt and whether ready-only targeting degenerates
   into one-Revision-per-file churn.
3. **Same age, different leg pressure** — equal `G/E/Q`, object sizes and Save count, but small versus
   large foreground Delta/write accumulation. This distinguishes Save-age/credit from leg-write or
   physical-tail pressure.
4. Retain `active-hundred-mixed-cold-debt` as the complex integration regression and
   `oversized-cold-nochange-tiny-clock` as the no-progress-overshoot guard.

The first reactivation slice would build the two narrow discriminators and one isolated candidate
rather than mutate the v2 baseline in place. Only after raw target/migration sequences and
`Wworkload/Pworkload/F/R/L` select a useful control could the frozen Adaptive profile version change
or a trace enter a successor corpus.

## Explicitly not decided

- a production `ShouldRotate` threshold or new hyperparameter;
- Arena V2 physical-pressure facts or TailMeta additions;
- persisted policy state and reopen semantics;
- optimal debt subset selection or frame-release-aware grouping;
- automatic cold detection, cross-segment migration or tiered implementation;
- any scalar score, winner, default parameter tuple, or numeric P/F guarantee.
