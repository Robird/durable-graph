# Read-amplification + Base-budget policy v0

> Status: frozen implemented experiment with bounded executable evidence; not an active
> product-policy candidate after the TwoLeg route was paused on 2026-09-02.

This note freezes the smallest executable interpretation of the two-parameter policy.
It is an experiment contract, not a product policy, durable format, score, or winner.

## Parameters

- `ReadAmplificationLimit >= 1`: a target-supported Update or NoChange has a Base motive
  only when its estimated next reconstruction amplification is strictly greater than
  this limit.
- `BaseBudgetFraction`, `0 < value <= 1`: the soft per-Save envelope for
  read-amplification-motivated Base payload, expressed as a fraction of the post-Save
  graph's all-Base payload.

The prototype uses deterministic `decimal` parameter values. Equality at the read
limit gives no motive—a non-dominant Update remains Delta and NoChange remains
inherited—matching “exceeds the limit”.

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

Thus debt at or above the configured share remains on B and can be reduced incrementally;
debt strictly below the share is evacuated by the Rotate-C candidate. Target comparison
uses the unrounded decimal share; `Q` remains the floored byte envelope. `G == E == 0`
directly selects Rotate-C; whether an outer caller submits such an empty-live maintenance
Save is outside this selector.

## Read-amplification motive

For an Update:

```text
nextDeltaCold(i) = h(i) + d(i)
amplification(i) = nextDeltaCold(i) / b(i)

has Base motive when amplification(i) > ReadAmplificationLimit
```

Before that rule, `b(i) <= d(i)` forces Base: Delta would write no less payload while
retaining a longer reconstruction chain. A zero-sized Update Base is therefore handled
without division because Delta payload is positive.

For a NoChange, the alternative to same-state Base is inheritance, not a zero Delta:

```text
amplification(i) = h(i) / b(i)
```

Stay-B supports this motive only for A-dependent NoChange; Rotate-C supports it only
for B-contained NoChange, because old-A evacuation is already mandatory there. `0/0`
is treated as amplification 1; positive history over zero Base is positive infinity.
Equality at the limit gives no motive for either Update or NoChange.

Ratios sort descending with ObjectId ascending as the deterministic tie-break. The
implementation compares deterministic decimal values and does not use binary floating
point.

## Soft Base envelope and indivisible first object

`Q` paces only read-amplification-motivated Base payload. It is a synthetic payload
proxy for smoothing writes, not an exact physical `Pworkload` cap or admission gate:

- Insert is always Base and is excluded from discretionary budget consumption.
- `Base <= Delta` Updates are weakly dominant Base choices and cannot be reverted by
  the budget. This dominance rule is independent of a read-amplification motive.
- Rotate-C must Base every old-A-dependent Update/NoChange. Their aggregate `E` consumes
  the envelope first; only `max(0, Q - E)` remains for optional B-contained
  Update/NoChange objects.

For each target, all supported, non-dominant objects with a strict motive are sorted by
descending amplification and ObjectId ascending. The policy selects the longest prefix
whose complete Base payload fits the available envelope and stops at the first non-fit;
it does not skip that object to pack later smaller objects.

Because an object is indivisible, Stay-B admits the first motivated object even when it
alone exceeds `Q`; later objects receive no budget. Rotate-C may make the same first-object
overshoot only when mandatory evacuation is empty (`E == 0`). When `E > 0`, evacuation
consumes `Q` first and optional selection cannot overshoot the remainder. An empty motive
set performs no optional Base or NoChange migration: there is no unconditional progress
floor and NoChange has no category priority over Update.

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
- B-contained NoChange: External or policy-selected same-state Base+Self;
- Remove: absent from the full OVD.

The selector returns the existing explicit Stay-B and Rotate-C decisions plus the
selected target and diagnostics. It does not append, publish, retry, fall back, or own a
cursor. Exact candidate construction, whole-frame sizing, hard rejection and apply stay
with the existing planners/evaluator.

## Evidence gates

1. Projection tests fix `G/A/E/Q`, Update result sizing, Remove exclusion and zero cases.
2. Pure decision tests fix strict Update/NoChange threshold equality, `Base <= Delta`
   dominance, unified ratio ordering/ties, budget rounding, longest-prefix stopping and
   target-specific first-object overshoot.
3. Target-shape tests cover every Insert/Update/Remove/NoChange mapping for Stay and
   Rotate.
4. Multi-Save target witnesses fix the first `G == 0` or
   `E < G * BaseBudgetFraction` Save selecting Rotate; a Stay with no motivated debt is
   allowed to retain its A dependency.
5. A policy-selected exact capacity failure must remain typed and cause no fallback or
   Store mutation. The unchanged harness retains its existing generic
   `RejectedUnproven` contract; v0 does not duplicate that fixture.
6. Evaluator comparison retains raw Wworkload/Pworkload/F/R and may report no winner. Benchmark-v1
   binds only the two named parameter tuples as atomic selection profiles; it does not
   accept arbitrary decimals or expose a policy plugin surface.

Known blind spots are intentional: equal payload projections may hide different shared
Frame pressure; soft payload fit does not imply exact candidate feasibility; stable
A debt with amplification at or below the limit can stall indefinitely; and the simple
trigger equates “Rotate is cheap enough” with “Rotate should happen now”. A future
candidate may add Rotate hysteresis from leg age/write/file pressure, but v0 does not.

## First executable evidence

Projection and pure-selection tests lock the payload formula, strict target/read
boundaries for Update and NoChange, weak dominance, deterministic unified ordering,
zero-size behavior, prefix selection, soft-budget rounding/overshoot, empty-motive
behavior, and target-specific decision shapes. An
exact B-contained hot-chain witness proves `(251 + 50) / 100 = 3.01` selects Base
through ordinary planning/apply without an unconditional progress rule. A selected
oversized Rotate-C returns typed `PayloadAndTailMetaLength` rejection without fallback
or Store mutation.

The threshold-band witness keeps target and motive selection aligned and gives both
profiles enough optional budget. At prospective ratio `3.5`, only `(3,5%)` writes Base; `(4,4%)`
retains Delta, and equality at `4.0` remains Delta. Its bounded result demonstrates the
strict read threshold, not a tuned default or general winner.

The Base-fraction boundary reaches a shared `G=1000,E=40` view with representation
choice inert. Strict target selection gives
`(3,5%)=[Stay,Rotate,Stay]` and `(4,4%)=[Stay,Stay,Rotate]`, proving the 5%/4%
boundary. This does not turn payload `E/G` into exact physical sizing authority.

Benchmark corpus revision 18 registers eighteen workloads and only these two Adaptive
bindings, producing 36 cases. Their existing component IDs now use version 2 because
this motive/budget correction changes selection behavior. The adversarial
`oversized-cold-nochange-tiny-clock` workload preserves a 10,000-byte unmotivated cold
NoChange beside a one-byte active clock. Before the fix its workload peak was `10052`;
the regression now requires `Pworkload < 10000`, proving that the cold Base did not
escape the envelope without freezing an incidental exact result. The older
no-migration and paced-one-debt profiles, controls, and 64-case report are archived at
Git tag `research/no-migration-paced-baselines-20260901`; `DeltaReference` and
`BaseReference` now provide strategy-independent write comparators. Current corpus
counts and raw metrics belong to [`EVALUATOR-V1.md`](EVALUATOR-V1.md). Exact policy
authority remains in the `ReadAmplificationBaseBudgetPolicy*` tests.

The aligned-channel `active-hundred-mixed-cold-debt` workload separately exposes the
remaining liveness blind spot: 20 low-amplification cold A-debt objects survive all 64
natural Saves and both profiles select only Stay. This is evidence for a future bounded
progress repair, not part of the v0 selector contract.

The candidate successor model separates ReadMotive, budget-constrained ReadinessProgress,
and ShouldRotate pressure; it is documented in
[`ADAPTIVE-ROTATION-CONTROL-CANDIDATE.md`](ADAPTIVE-ROTATION-CONTROL-CANDIDATE.md) and is not
implemented by this v0 contract.
