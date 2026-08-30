# Read-amplification + Base-budget policy v0

> Status: implemented experimental policy with bounded executable evidence.

This note freezes the smallest executable interpretation of the two-parameter policy.
It is an experiment contract, not a product policy, durable format, score, or winner.

## Parameters

- `ReadAmplificationLimit >= 1`: an Update prefers Base only when its estimated next
  Delta reconstruction amplification is strictly greater than this limit.
- `BaseBudgetFraction`, `0 < value <= 1`: the soft per-Save envelope for optional Base
  payload, expressed as a fraction of the post-Save graph's all-Base payload.

The prototype uses deterministic `decimal` parameter values. Equality at the read
limit selects Delta, matching “exceeds the limit”.

## Advisory payload projection

V0 deliberately keeps all heuristic operands in synthetic object payload bytes:

```text
b(i) = post-Save BasePayloadBytes of live object i
h(i) = source HeadReconstructionObjectPayloadBytes of existing object i
d(i) = frozen DeltaPayloadBytes of Update i

G = Sum(b(i), i in PostLive)
A = post-live Update/NoChange objects whose source terminating Base is in old A
E = Sum(b(i), i in A)
Q = Floor(G * BaseBudgetFraction)
```

`PostLive = Insert + Update + NoChange`; Remove contributes to neither `G` nor `A`.
An A-dependent Update contributes its result Base size, because that is what Rotate-C
must write. `E` is an evacuation payload proxy, not exact incremental physical bytes.

This proxy excludes record headers, shared-Frame co-read, OVD, TailMeta, address widths,
padding and fences. It must not become a second sizing or admission authority.

## Target selection

```text
RotateC when G == 0, or E < G * BaseBudgetFraction
StayB otherwise
```

Thus debt at or above the configured share remains on B and is reduced incrementally;
debt strictly below the share is evacuated by the Rotate-C candidate. Target comparison
uses the unrounded decimal share; `Q` remains the floored byte envelope. `G == E == 0`
directly selects Rotate-C; whether an outer caller submits such an empty-live maintenance
Save is outside this selector.

## Read-amplification preference

For an Update:

```text
nextDeltaCold(i) = h(i) + d(i)
amplification(i) = nextDeltaCold(i) / b(i)

prefer Base when amplification(i) > ReadAmplificationLimit
```

Before that rule, `b(i) <= d(i)` forces Base: Delta would write no less payload while
retaining a longer reconstruction chain. A zero-sized Update Base is therefore handled
without division because Delta payload is positive.

For an A-dependent NoChange, the alternative to same-state Base is inheritance, not a
zero Delta. Its ordering proxy is `h(i) / b(i)`. `0/0` is treated as amplification 1;
positive history over zero Base is positive infinity.

Ratios sort descending with ObjectId ascending as the deterministic tie-break. The
implementation compares deterministic decimal values and does not use binary floating
point.

## Soft Base envelope and progress floor

`Q` limits only discretionary Base payload. It is not an exact or hard write cap:

- Insert is always Base and is excluded from discretionary budget consumption.
- `Base <= Delta` Updates are weakly dominant Base choices and cannot be reverted by
  the budget.
- Rotate-C must Base every old-A-dependent Update/NoChange. Their aggregate `E` consumes
  the envelope first; only `max(0, Q - E)` remains for optional B-contained Updates.
- Stay-B must retire at least one post-live A-debt object. If dominant Base choices have
  not already done so, the highest-amplification A-debt NoChange is reserved and forced
  first, preserving Delta for a naturally changing object when maintenance can advance
  the leg instead. Only when no A-debt NoChange exists is the highest-amplification
  A-debt Update forced to Base. The indivisible choice consumes the envelope and may
  exceed `Q`; only its nonnegative remainder is available to other preferences.

After mandatory/dominant/progress choices, remaining above-limit candidates are visited
by descending amplification and ObjectId. A candidate is selected only when its complete
Base payload fits the remaining envelope; an oversized candidate is skipped so a later
smaller candidate may fit.

The progress guarantee means “one admitted Stay retires at least one old-A-dependent
object”, not merely “one Base record exists”. An Insert or B-contained Base does not
satisfy it.

## V0 action boundary

Stay-B:

- Insert: mandatory Base;
- Update: policy-selected Base or Delta;
- A-dependent NoChange: inherit or same-state Base migration;
- B-contained NoChange: inherit; proactive B-local same-state rebase is deferred;
- Remove: OVD Remove, no domain record.

Rotate-C:

- Insert and all A-dependent Update/NoChange: mandatory Base;
- B-contained Update: policy-selected Base or Delta;
- B-contained NoChange: External; optional B-local same-state rebase is deferred;
- Remove: absent from the full OVD.

The selector returns the existing explicit Stay-B and Rotate-C decisions plus the
selected target and diagnostics. It does not append, publish, retry, fall back, or own a
cursor. Exact candidate construction, whole-frame sizing, hard rejection and apply stay
with the existing planners/evaluator.

## Evidence gates

1. Projection tests fix `G/A/E/Q`, Update result sizing, Remove exclusion and zero cases.
2. Pure decision tests fix strict threshold equality, `Base <= Delta` dominance,
   ratio ordering/ties, budget rounding, skip-oversized and one-debt overshoot.
3. Target-shape tests cover every Insert/Update/Remove/NoChange mapping for Stay and
   Rotate.
4. A multi-Save witness must show each admitted Stay retiring old-A debt and the first
   `G == 0` or `E < G * BaseBudgetFraction` Save selecting Rotate.
5. A policy-selected exact capacity failure must remain typed and cause no fallback or
   Store mutation. The unchanged harness retains its existing generic
   `RejectedUnproven` contract; v0 does not duplicate that fixture.
6. Evaluator comparison retains raw W/P/F/R and may report no winner. V0 does not enter
   the canonical benchmark registry/report until another consumer justifies that change.

Known blind spots are intentional: equal payload projections may hide different shared
Frame pressure; the simple trigger may rotate frequently on some traces; soft payload
fit does not imply exact candidate feasibility.

## First executable evidence

Projection and pure-selection tests lock the payload formula, strict target/read
boundaries, weak dominance, deterministic ordering, zero-size behavior, soft-budget
rounding/overshoot, NoChange-first progress and target-specific decision shapes. A
separate exact witness constructs a B-contained hot chain with payload history 251 and
proves `(251 + 50) / 100 = 3.01` selects Base through the ordinary planner/apply seam,
independently of the progress override. A policy-selected oversized Rotate-C also
returns typed `PayloadAndTailMetaLength` rejection without fallback or Store mutation.

The first matched-cadence workload uses parameters `(3, 5%)`, four 100-byte old-A
objects, four repeated 50-byte Updates of object 10, then one Insert. Adaptive naturally
selects `Stay, Stay, Stay, Stay, Rotate`; the controls are held to that same target
cadence. Control/paced/adaptive produce W/P/F/R `924/476/476/524`,
`1248/372/748/524`, and `1296/472/796/524`; exact final debt and the comparison table
remain in [`EVALUATOR-V1.md`](EVALUATOR-V1.md).

Adaptive resets object 10's source reconstruction payload sequence from the controls'
`100,150,200,250,300` to `100,150,200,250,100`. Nevertheless paced strictly dominates
adaptive in W/P/F with equal final-only R in this fixture. Subsequent rotation and
terminal settlement hide the intermediate chain reset from R; this is both a policy
counterexample and evidence that a strategy optimizing intermediate amplification needs
a separately named diagnostic before parameter search. It is not a general paced-policy
winner or a reason to alter the canonical evaluator schema.
