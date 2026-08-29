# Two-leg rotation probe

This isolated .NET 10/xUnit project is the in-memory workbench for exploring
two-leg StateStore file rotation and Base-or-Deltify policies.

The compact current goal, near-term roadmap, and unresolved work live in
[`PROJECT-STATE.md`](PROJECT-STATE.md). This README describes the implemented
model and executable evidence; it is not the active backlog.

The first scaffold deliberately models only a few container facts:

- RBF file numbers start at 1 and increase monotonically;
- an `RbfFile` only appends frames and can randomly read an existing frame by
  its `(OffsetBytes, LengthBytes)` `FrameTicket`;
- an `RbfFileStore` creates files and retrieves them by file number;
- a `FileScope` has a readonly `CurrentFileNumber`, derives its
  `PreviousFileNumber`, and resolves relative parent IDs against the store;
- a built `Frame` owns a read-only `ObjectId` (`uint`) to `ObjectVersion` map;
- a Revision-like `Frame` may explicitly carry an immutable runtime OVD; `null`
  means this older/raw probe frame did not model OVD authority and is not an
  authoritative empty map;
- `ObjectVersion` distinguishes Base from Delta and records synthetic payload
  cost, resulting Base size and logical version ordinal;
- only Delta carries `DeltaParentFrameTicket`, an exact
  `(bool IsPreviousFile, FrameTicket)` reconstruction parent; Base has no
  per-record parent;
- the live in-memory StateMap uses `AbsoluteFrameAddress(FileNumber,
  FrameTicket)`, rather than retaining a context-dependent relative ticket;
- mutable `FrameBuilder` and `ObjectVersionBuilder` instances are copied into
  read-only built state.

`LogicalVersionOrdinal` is deliberately not a physical chain ordinal. A Delta
always has positive payload and advances exactly one logical version. A
same-version Base is a relocated full value; it is the only transparent
maintenance record retained by the selected model. Reconstruction follows
exact Delta parents and stops at Base. Base lineage uses the containing
Revision OVD's parent as one shared prior-snapshot anchor, then looks up the
ObjectId there. In this size-only probe, logical equality means exact
`(BasePayloadBytes, LogicalVersionOrdinal)` equality, not future field-value
equality.

The runtime OVD probe models Base/Delta dictionaries and Self/External/Remove
bindings. `LookupLive(revision, objectId)` takes no caller StateMap. Self,
External, and Remove are decisive; only a Delta miss follows its parent, while
a Base miss is definitive absence. Every inherited relative ticket is decoded
in the file scope of the Revision that contains that binding. Missing OVDs,
non-earlier addresses, external aliases, and bindings to a frame without the
same ObjectId fail closed.

## Generated workload traces

The probe can now generate a complete workload trace before any storage policy
runs:

```text
ScenarioDefinition + stable forked random streams
    -> fixed-count lifecycle decisions
    -> Field/List synthetic object behaviors
    -> immutable GeneratedScenario { Definition, WorkloadTrace }
    -> logical replay validation
```

A trace contains canonical `SaveStep` batches of `CreateObject`, `UpdateObject`,
and `RemoveObject` changes. Updates carry the resulting full Base payload size
and the candidate Delta payload size. Logical replay owns the previous size and
rejects impossible uncompressed growth, missing objects, reused IDs, invalid
lifecycles, and non-positive Delta sizes.

Field behavior maintains a fixed vector of component sizes. List behavior
maintains item sizes and generates legal Insert, Remove, or Replace operations.
Both use explicit synthetic accounting rules; these shapes exist only while
generating the trace and are not visible to a future Base-or-Deltify policy.

The stable random root forks independent lifecycle, object-creation, and
object-update streams by `(seed, step, object, lane)`. A local extra draw cannot
advance another keyed random stream, although changing one object's generated
state can naturally affect that same object's later updates. The random
algorithm, generator version, and immutable scenario definition are part of the
reproducibility boundary. A complete mixed-scenario golden test requires an
explicit generator-version bump when generation behavior intentionally changes.

“Immutable trace” currently means one in-memory trace is generated once and the
same instance can be replayed by every candidate policy. It does **not** mean
the trace is serialized to a file. A canonical trace format is deferred until
cross-process replay or persisted failure artifacts have a concrete consumer.

## Physical baseline compilation

`WorkloadSimulator` compiles the same frozen trace into a fresh, private
single-file run under `AlwaysBase`, `AlwaysDeltaWhenLegal`, or
`ObjectPayloadReadAmplification3`:

```text
SaveStep
    -> one candidate Frame
    -> candidate absolute live StateMap
    -> symbolic materialize and exact logical-prefix comparison
    -> next accepted in-memory step
```

Create always writes a Base. Update writes the strategy-selected Base or Delta
from the containing Revision's accepted prior snapshot. Delta points to that
object's exact previous head, which can skip unrelated or remove-only frames;
Base relies on the Revision OVD's shared anchor. Remove only deletes the live
StateMap binding; its SaveStep still appends an empty physical Frame and old
frames remain readable history.

Delta payload bytes remain a storage-cost observation, not a value transform.
For an executable size-only oracle, each Delta separately records its expected
parent Base size and resulting Base size. Materialization must reconstruct the
parent, check that precondition, validate the logical ordinal and no-compression
growth bound, and only then produce the result. This is symbolic size-state
apply, not serialization or content-level delta replay. Current-state
materialization stops at the newest Base; the separate lineage diagnostic can
walk older parents without turning those reads into reconstruction cost.

The current single-file model can run either the historical
`ObjectPayloadOnly` profile or the explicitly provisional full-frame
`ProvisionalRevisionV0` size grammar described below. Separate pure planners
and explicit appenders now close caller-selected preparatory B Base migrations
and one immediate A/B to B/C runtime step, but those steps are not wired into
the single-file policy runs. Within those runs,
`AlwaysDeltaWhenLegal` is presently equivalent to “always Delta.” The enum is
intentionally not a general policy interface yet.

## Object-local payload policy

`ObjectPayloadReadAmplification3` preserves the shape and ratio of the locally
inspected StateJournal `VersionChainStatus.ShouldRebase` rule, but deliberately
does not port its estimated 38-byte per-object Frame/metadata overhead. That
overhead assumes one object per Frame and would conflict with this probe's
multi-object Frames and `ObjectPayloadOnly` accounting.

For an Update, the policy writes Base when:

```text
BasePayloadBytes <= DeltaPayloadBytes
    or
(BasePayloadBytes - DeltaPayloadBytes) * 3
    <= ParentReconstructionObjectPayloadBytes
```

Otherwise it writes Delta. Equality selects Base. Every `ObjectVersion`
records `ReconstructionObjectPayloadBytes`: Base resets it to its own payload;
Delta sets it to parent cumulative payload plus its own payload. The
materialization oracle independently recomputes and validates this value, so it
does not become a trusted shortcut or runner-private second authority.

This cumulative field is provisional simulation metadata. It is excluded from
both accounting grammars and is not a wire-format commitment. Accordingly the
policy is StateJournal-inspired, not an exact StateJournal cost-model port. The
V0 run can observe shared-frame and metadata cost after each decision, but the
local policy itself still does not use those facts.

## RBF envelope and raw observations

The in-memory file reproduces the size and placement rules of the locally
inspected RBF draft v0.40 envelope at Atelia commit
`fec021295828fcfe638434d69d04ff078c87c8ce`:

```text
file = 4-byte HeaderFence + frames
frame length = 24-byte fixed fields + Payload + TailMeta + 0..3 byte padding
append length = frame length + 4-byte trailing Fence
```

`FrameTicket` is the frame's offset/length range and excludes its trailing
Fence. The estimator validates 4-byte alignment, native RBF frame limits and
TailMeta limits; a separate gate reports the DurableGraph 512 GiB
relative-start boundary.
A last frame may begin at a legal maximum start and end beyond it; only the next
start then becomes impossible.

Actual `IRbfFile` reads a complete frame for L3 Payload CRC validation and does
not expose arbitrary payload slices. TailMeta can be previewed separately, but
that path has only L2 Trailer CRC trust; a TailMeta directory cannot install an
authoritative StateMap until the containing frame passes full L3 validation.
Accordingly reconstruction cost continues to count each required unique full
frame once.

## Accounting profiles

`AccountingScope.ObjectPayloadOnly` remains the default and preserves the
original golden baseline. Its RBF Payload is only selected synthetic
`ObjectVersion.PayloadBytes`; TailMeta is zero. It excludes every DurableGraph
header, OVD, directory and address token.

`AccountingScope.ProvisionalRevisionV0` gives the same frozen workload a fresh,
self-consistent physical address space and accounts for one experimental
grammar:

```text
Base record   = U(body length) + kind + opaque synthetic body
Delta record  = U(body length) + kind + U(exact parent) + opaque synthetic body
OVD record    = U(body length) + kind + U(parent) + sorted mutations
TailMeta      = U(OVD offset) + sorted ObjectId -> record-offset directory
```

Here `U` is canonical unsigned Base128 width. The projection of
`SizedPtr.Serialize()` and the same/previous LSB selector match the inspected
Atelia source. A field-contextual one-byte `BindSelf` represents OVD bindings
to the containing Revision Frame; the RBF append/read context already supplies
that ticket. This removes the earlier literal self-ticket fixed point. An
executable counterexample demonstrates that the rejected literal design could
have two stable widths, so merely iterating until stable would not define a
unique canonical encoding.

V0 sorts records by ObjectId, checks component arithmetic, TailMeta,
Payload+TailMeta, native RBF bounds and the 512 GiB relative-start gate before
Append. It returns component sizes and validates the resulting RBF layout, but
does not write or parse bytes. It is exact for this named provisional grammar,
not a production wire-format or compatibility promise. RBF Tag values,
opcodes, final record fields and Extent behavior remain unchosen.

An exact terminal-C grammar discriminator uses the largest legal Previous-file
ticket, whose relative address needs a 10-byte VarUInt. With 268,435,390 bytes
of mandatory payload, retaining a zero-payload B-local object as External makes
Payload+TailMeta exactly 268,435,428 bytes; one more mandatory byte overflows.
For that same one-byte-larger candidate, encoding the object as a same-state
Base plus Self reduces Payload+TailMeta to 268,435,427 bytes and address-token
bytes from 21 to 12. Padding still makes the resulting frame exactly
`MaxFrameLength`.

This proves only that External does not dominate optional same-state relocation
under the current provisional v0 grammar. It does not implement that runtime
action, enlarge the proven `CanPrepareAndRotate` reachable set, establish a
complete planner, or constrain a future wire format. The whole-candidate
estimator remains the sole sizing authority; the address-token difference is
not a composable per-object savings rule.

Each `RevisionObservation` separates a write event from the post-save
reconstruction snapshot. Writes record Base/Delta object payload and the
selected profile's frame/append layout. Reconstruction records required object
versions, unique frames, required payload, all object payload co-read from those
frames, and the selected profile's full RBF frame lengths.
Post-save read snapshots are not a run total unless an experiment explicitly
chooses to simulate a full reload after every Save.

The current named matrix uses exact handwritten threshold/direction/hot-cold
cases plus one fixed-seed Field/List workload. Reports keep additive write
events separate from the final post-save read snapshot; no `TotalReadBytes` is
defined. Two examples demonstrate tradeoffs rather than a winner:

Object-payload-only baseline:

| Workload | Policy | Modeled file bytes | Final modeled frame bytes read |
|---|---|---:|---:|
| fixed-seed mixed | AlwaysBase | 436 | 148 |
| fixed-seed mixed | AlwaysDelta | 364 | 348 |
| fixed-seed mixed | ObjectPayloadReadAmplification3 | 372 | 232 |
| hot-one/cold-eight | AlwaysBase | 1700 | 1048 |
| hot-one/cold-eight | AlwaysDelta | 1268 | 1236 |
| hot-one/cold-eight | ObjectPayloadReadAmplification3 | 1340 | 1048 |

The same traces under `ProvisionalRevisionV0`; `metadata write` is domain
headers + OVD record + TailMeta directory and does not double-count its
address-token diagnostic subset:

| Workload | Policy | Body write | Metadata write | Modeled file bytes | Final modeled frame bytes read |
|---|---|---:|---:|---:|---:|
| fixed-seed mixed | AlwaysBase | 342 | 81 | 516 | 176 |
| fixed-seed mixed | AlwaysDelta | 274 | 92 | 460 | 444 |
| fixed-seed mixed | ObjectPayloadReadAmplification3 | 279 | 89 | 460 | 296 |
| hot-one/cold-eight | AlwaysBase | 1500 | 152 | 1864 | 1132 |
| hot-one/cold-eight | AlwaysDelta | 1050 | 168 | 1428 | 1396 |
| hot-one/cold-eight | ObjectPayloadReadAmplification3 | 1125 | 165 | 1500 | 1132 |

Both tables demonstrate tradeoffs rather than a winner. Reports sum write
events and show only the final post-save read snapshot; no `TotalReadBytes` is
defined. The matrix lives in executable tests; a ScenarioCatalog or report
format waits for a CLI, persisted artifact, or batch-run consumer.

## Normalized Save facts and explicit Stay-B / Rotate-C candidates

`SaveStepNormalizer` now turns one `parent PublishedRevision@B + SaveStep` into
one immutable, ObjectId-ordered fact sequence. `Insert`, `Update`, `Remove`, and
`NoChange` are typed views of that sequence rather than separate caller inputs.
Every parent-live address comes from runtime OVD materialization. Both the
source OVD replay chain and every object reconstruction are validated to remain
inside the A/B `FileScope` before the facts are exposed. The facts freeze the
exact source head and terminating Base, logical state, head reconstruction
payload cost, parent state, and post-Save state.

The normalizer can prove that an Insert is absent from the parent snapshot; it
cannot prove from that snapshot alone that an ObjectId was never used in older
history. Globally fresh Insert IDs therefore remain an upstream session/workload
precondition rather than a caller-supplied second authority.

`NormalizeMaintenanceOnly` reuses the same source inspection but supplies no
foreground changes, producing all-`NoChange` facts without weakening the
generated workload rule that a `SaveStep` is nonempty. An empty live graph is
also legal through this separate entry point.

`StayBSaveDecision` is deliberately caller-explicit. It must choose exactly one
Base/Delta mode for every Update and may select only `NoChange` objects whose
terminating Base is still in A for same-state migration. Missing, extra,
duplicate, conflicting, B-local, or unknown decisions fail before any Store
mutation.

`StayBRevisionPlanner` traverses the canonical facts once and constructs one
pure B-local runtime candidate:

```text
Insert             -> Base + OVD Self
Update(Base)        -> new Base + OVD Self
Update(Delta)       -> Delta to the exact source object head + OVD Self
Remove              -> OVD Remove, no domain record
selected NoChange   -> same-state Base + OVD Self
other NoChange      -> inherit through the OVD Delta
```

The candidate OVD Delta points to the exact source PublishedRevision. Its final
immutable `Frame` is sized only through `PlannedRevisionV0`; planning does not
append or publish. An executable mixed fixture appends the candidate through a
test-only mutation boundary, replays its OVD, reconstructs the logical state,
and proves an A-debt set reduction from five exact ObjectIds to two. It also
checks canonical input-order independence, decision conflicts, capacity failure,
and success/failure planning purity.

`RotateCSaveDecision` exposes only real C-side choices: every B-contained Update
must choose Base or Delta, and selected B-contained `NoChange` objects may be
written as same-state Bases instead of remaining External. A-dependent Updates
and `NoChange` objects are not caller choices: the planner forces them to Base@C
so current reconstruction cannot retain A.

`RotateCRevisionPlanner` consumes the same frozen facts without rematerializing
the source OVD or rerunning the reconstruction oracle. It writes one fresh-C
candidate with a full OVD Base anchored at the source PublishedRevision@B:

```text
Insert                     -> Base + Self
A-dependent Update         -> new Base + Self
B-contained Update         -> explicit Base or Delta to exact B head + Self
Remove                     -> omitted from full OVD and domain records
A-dependent NoChange       -> same-state Base + Self
B-contained NoChange       -> exact-head External or explicit Base + Self
```

The full OVD entries equal `PostLive`; its parent is a shared historical-lineage
anchor, not an OVD replay parent. A mixed executable fixture uses B-local heads
that predate PublishedRevision, proving Delta and External encode the exact old
head rather than the shared anchor. It also proves logical PostLive equality,
B/C reconstruction closure, canonical decisions, exact whole-frame sizing,
capacity-failure purity, and maintenance-candidate equivalence with the older
ImmediateRotation planner.

This closes both pure explicit candidate builders. They are not yet an automatic
policy, durable head publication path, or continuous multi-rotation runner.

## Explicit paired evaluation

`ExplicitCandidatePairEvaluator` now attempts the caller-supplied Stay-B and
Rotate-C actions independently over the same `NormalizedSaveFacts`. A feasible
attempt retains the exact plan, runtime candidate and the candidate's existing
`ProvisionalRevisionV0Estimate`; it does not rebuild the candidate or invoke a
second sizing authority. The pair deliberately has no winner, aggregate score,
fallback, append, or publication behavior.

The provisional estimator now reports five narrowly typed physical limits:
native target-frame start, DurableGraph-relative target-frame start, referenced
relative ticket, TailMeta length, and combined Payload+TailMeta length. The pair
turns only this marker into a bounded capacity rejection and continues with the
other target. Caller-decision errors, stale or corrupt source state, invalid
runtime grammar, and other model errors still escape and fail closed. Rejecting
one explicit candidate is not a proof that no alternate action can fit.

For each feasible candidate, raw observations join the estimator's already
sized domain records with the normalized fact kinds to separate foreground and
same-state maintenance record bytes. Post-live reconstruction observations use
only the candidate Frame and the frozen source reconstruction paths: they
report required versions and payload, canonical unique full Frames and bytes,
and the live objects whose reconstruction still reaches the Previous file in
the candidate's result `FileScope`. The candidate estimate is reused by
identity; each stored source Frame is checked against the same sole estimator
before its layout is admitted as `ProvisionalRevisionV0` provenance. A mismatch
is source/accounting corruption, not a capacity outcome. Historical lineage is
excluded.

Executable pair fixtures prove both-success, either one-sided capacity
rejection while the other side is still evaluated, two independent capacity
rejections, non-capacity errors escaping, input-order determinism, exact
candidate/estimate identity, and no Store mutation.

## Explicit probe apply and completion certificate

`ProbeRevisionCursor` is a caller-owned, volatile tuple of the current
`FileScope`, PublishedRevision address, and Current-file tail. It is deliberately
not stored in `RbfFileStore` and does not claim durable publication, concurrency,
or crash safety. `ExplicitProbeRevisionApplier` accepts only an exact feasible
Stay-B or Rotate-C attempt. Before its sole mutation it revalidates the cursor,
source OVD and reconstruction facts, candidate result state, shared anchor,
retained layout, target file, and target tail. Stay-B appends to B; Rotate-C
stages the first C Frame and installs the new file only after the exact layout
matches. Any preflight failure leaves the Store unchanged.

`CanPrepareAndRotateCertificatePlanner` proves one conservative continuation on
an exact volatile `RbfFileStore.ForkForProbe()`. It first applies the selected
Stay-B candidate on the fork, tries a default maintenance Rotate-C, then—only
after a capacity rejection—migrates the smallest remaining A-dependent ObjectId
as one unified maintenance Stay-B candidate and retries. The path is finite,
ObjectId-canonical, has no branching, score, backtracking, or optional repair,
and every step is generated by the existing planners and replayed through the
same apply seam.

A successful certificate freezes the exact initial Stay-B attempt, zero or more
exact maintenance Stay-B attempts, and one exact final Rotate-C attempt. A
capacity-blocked canonical path returns typed `RejectedUnproven`; it does not
claim that another order, batch, action, or future solver cannot succeed. The
3 x 140,000,000-byte fixture produces two one-object B migrations before the
final C candidate fits, while an exhausted B start boundary produces a stable
`TargetFrameStartRelative` rejection. Certificate construction never mutates
the caller Store, and the successful script is executable against it.

A caller-scripted continuous witness now composes these existing seams without
adding a second runner authority. It performs Stay-B, Rotate-C, Stay-C, then
Rotate-D, requiring an exact certificate before each accepted Stay while only
applying that certificate's initial caller-selected Save. The executable path
proves `A/B -> B/C -> C/D`, exact logical state and reconstruction closure after
every Save, and a Previous-debt sawtooth of `{10} -> {20} -> {} -> {20}`.
The script retains the selected candidates' existing raw observations locally;
it does not define a policy, score, transcript format, or durable head.

## Explicit policy step harness and controlled migration pacing

`ExplicitRotationPolicyStepHarness` is the first deliberately reusable policy
scaffold. It remains a stateless, single-step seam: the caller supplies an exact
pair and an explicit target, and receives one of four typed outcomes—applied
Stay-B, applied Rotate-C, selected-target capacity rejection, or Stay-B
completion `RejectedUnproven`. It never chooses a target, falls back to the
other candidate, executes certificate maintenance/final steps, caches a
StateMap, or owns a publication cursor.

The first comparison keeps target timing outside the policies and fixed at
`[Stay-B, Stay-B, Rotate-C]`. Both runs replay the same three-Insert
`WorkloadTrace` on independent Store forks. The control performs no cold
migration; `PacedOneDebtByObjectId` migrates the smallest eligible NoChange
A-debt object on each Stay. ObjectId ordering is only a deterministic treatment
assignment—it is not evidence that an object is cold.

| Save | Target | Control Previous debt | Paced Previous debt |
|---|---|---|---|
| 1 | Stay-B | `{10,20,30}` / 600 B | `{20,30}` / 500 B |
| 2 | Stay-B | `{10,20,30}` / 600 B | `{30}` / 300 B |
| 3 | Rotate-C | `{1001,1002}` / 2 B | `{10,20,1001,1002}` / 302 B |

The paced run spreads the same three maintenance Base domain records across
the three Saves, producing a smaller realized peak append and a smaller final
Rotate-C append. It then pays the opposite side of the tradeoff: the Bases
moved into B become Previous debt after rotation, and the required Previous
frames contain more bytes. Before rotation, reducing debt bytes does not reduce
Previous-frame reads because the remaining debt still shares the same A Frame.

For a selected Stay, its pair's alternate Rotate-C is a same-Save alternative,
not a post-Stay terminal estimate. The comparison uses the certificate's final
Rotate-C observation only after proving that the certificate has zero extra
maintenance steps. Those terminal observations are counterfactual facts, not
realized writes. Deterministic replay is currently claimed only for the test's
selected raw projection, not a canonical transcript. No aggregate score or
winner is defined, and the fixed rotation schedule is an experimental control,
not a proposed product trigger.

## Debt-zero trigger progress baseline

A second comparison keeps the migration treatments but replaces the fixed target
schedule with a deliberately conservative test-local rule:

```text
source snapshot has Previous-file debt -> Stay-B
source snapshot has no Previous-file debt -> Rotate-C
```

The trigger reads only `NormalizedSaveFacts.ParentLive` before either candidate
is evaluated. It does not use the current Save's prospective post-debt, candidate
capacity, a score, or the completion certificate. Both runs replay the same four
pure-Insert Saves on independent Store forks, so foreground Update/Remove cannot
silently discharge the initial cold debt.

| Save | No migration: source debt / target / post debt | Paced one: source debt / target / migration / post debt |
|---|---|---|
| 1 | `{10,20,30}` / Stay / `{10,20,30}` | `{10,20,30}` / Stay / `10` / `{20,30}` |
| 2 | `{10,20,30}` / Stay / `{10,20,30}` | `{20,30}` / Stay / `20` / `{30}` |
| 3 | `{10,20,30}` / Stay / `{10,20,30}` | `{30}` / Stay / `30` / `{}` |
| 4 | `{10,20,30}` / Stay / `{10,20,30}` | `{}` / Rotate / none / `{10,20,30,1001,1002,1003}` |

The third paced Save is the discriminator: it starts with debt and therefore
must Stay even though that applied Save clears the old A/B debt. Only the fourth
Save starts debt-free and creates C. After the `A/B -> B/C` scope change, the
objects retained in B correctly become 603 bytes of new Previous debt across
three Frames; this sawtooth is not a failed rotation.

The no-migration run consumes this finite admitted trace with deferred debt and
no realized rotation. Every one of its Stay steps nevertheless carries a proven
counterfactual terminal Rotate candidate. The test-local progress projection
therefore classifies actual applied outcomes, not certificate contents or final
debt alone. This baseline demonstrates a progress dependency between the trigger
and migration treatment; it does not establish a product trigger, cost winner,
capacity policy, or the physical ability to append forever.

## Scoped rotation observation reductions

The comparison fixture now reduces each successfully applied step into a
test-local, unweighted value projection. Source and result observations each
carry their own `(Previous, Current)` scope, Previous-debt ObjectIds and logical
Base payload bytes, Previous-file unique object-reconstruction Frames and
`FrameLengthBytes`, Current tail, and signed next-relative-frame-start slack.
The slack is only
`MaxDurableGraphRelativeFrameStartOffsetBytes - CurrentTailOffsetBytes`: zero
still permits a Frame to start at the boundary, and a negative value after that
append means no later relative Frame start is representable. It is not general
file or payload capacity.

Previous Frame observations cover only unique full Frames on live objects'
current reconstruction paths in that observation's Previous file. They exclude
OVD lookup/replay, historical lineage, Current/C Frames, trailing fences, cache
behavior, and cumulative IO; no `TotalReadBytes` is defined.

Each realized step also separates foreground domain-record bytes, maintenance
NoChange domain-record bytes, the remaining non-domain append bytes, and the
whole RBF append. Contiguous steps with the same source scope form one observed
epoch. A realized Rotate belongs to and closes its old source epoch; the run may
begin partway through a physical file leg, so the count is deliberately an
observed Save count rather than a complete leg length. Executable evidence now
covers one open four-Stay epoch, closed three- and four-Save epochs, and two
consecutive closed epochs:

```text
source scopes 1/2, 1/2, 2/3, 2/3
targets       Stay, Rotate, Stay, Rotate
result scopes 1/2, 2/3, 2/3, 3/4
```

A successful Stay's completion certificate contributes a separately named
counterfactual terminal-C projection. It records the number of additional
preparatory Stay steps and the exact final-C append/result observation. It does
not include preparatory writes or terminal-source pressure, and no
counterfactual candidate contributes to realized totals, peaks, epochs, or
rotation count. The current comparison reductions exercise the zero-preparation
case; the certificate subsystem separately has a two-preparation capacity
witness.

The same frozen traces and fresh Store forks reproduce the complete primitive
projection. This is deterministic value-projection evidence, not a canonical
transcript, persisted report, Store identity, or cross-process format. The
records remain private to the test fixture until a report, CLI, or batch runner
becomes a real consumer. Successful-run reductions also do not yet represent
typed capacity or completion rejection outcomes.

## Changed A-debt Base-versus-Delta discriminator

A second fixed-schedule comparison now isolates whether a natural foreground
Update may also retire old A debt. Both treatments replay the same handwritten
trace on independent Store forks:

```text
Update 10, Update 20, Update 30, Create 1001
Stay,      Stay,      Stay,      Rotate
```

Every Update has the same logical result size as its source and a legal
one-byte Delta. Both treatments select no optional same-state migration. The
control writes each changed A-debt object as Delta@B; the treatment writes it
as Base@B. A private decision selector supplies the complete existing Stay-B
and Rotate-C decision values from canonical facts, while target selection stays
independent. This is fixture plumbing, not a policy interface or runner.

Under the current provisional v0 grammar, the realized observations are:

| Treatment | Old-A debt after three Stays | Realized append bytes | Final Rotate maintenance records | Result B/C Previous debt |
|---|---|---|---:|---|
| changed A-debt Delta | `600, 600, 600` | `48, 48, 48, 668` | 608 B | none |
| changed A-debt Base | `500, 300, 0` | `144, 244, 344, 60` | 0 B | 600 B / 3 Frames / 720 frame bytes |

Objects 10, 20, and 30 initially share one 652-byte A Frame. Consequently the
Base treatment reduces debt ObjectIds and logical Base payload bytes after its
first two Stays, but Previous-file object-reconstruction Frame bytes do not fall
until the last A dependency disappears. Debt amount is therefore not a direct
proxy for coarse RBF read pressure.

The lower final Rotate is also not free or permanent debt removal. The Base
treatment moves full-value writes into earlier foreground Saves. After
`A/B -> B/C`, its three Base@B records become new-scope Previous debt; the Delta
control instead pays mandatory C evacuation for all three objects and ends that
rotation with no object-reconstruction dependency on B. Zero-preparation
counterfactual terminal-C append bytes similarly change from the control's
`660, 660, 660` to the treatment's `556, 352, 48`, while the treatment terminal
results accumulate new Previous debt `{10} -> {10,20} -> {10,20,30}`. Those
counterfactual candidates remain outside realized totals and peaks.

This witness demonstrates causal write placement and scope-relative debt under
one synthetic trace. It does not choose a winner, define total read IO, predict
another wire grammar, or supply an automatic Base/Delta or rotation trigger.

## Changed-write and cold-migration interaction witness

A role-disjoint 2x2 comparison now composes the two preceding mechanisms. One
six-object A Frame contains two equal-payload groups:

```text
migration-only  1/2/3      payload 100/200/300
changed         10/20/30  payload 100/200/300
```

The frozen trace updates 10, 20 and 30 once each, then creates 1001; every run
uses `[Stay, Stay, Stay, Rotate]`. The paced arms select 1, 2 and 3 respectively,
so no optional migration object is updated by the trace. Four named test-local
decision selectors vary only changed A-debt `Delta/Base` and optional unchanged
migration `none/paced-one`.

Under the current provisional grammar, the realized evidence is:

| Treatment | Old-A debt after the three Stays | Append bytes | Result B/C Previous debt |
|---|---|---|---|
| Delta + none | `1200, 1200, 1200` | `48, 48, 48, 1292` | none |
| Delta + paced | `1100, 900, 600` | `152, 256, 356, 680` | `{1,2,3}` / 600 B / 3 Frames |
| Base + none | `1100, 900, 600` | `144, 244, 344, 680` | `{10,20,30}` / 600 B / 3 Frames |
| Base + paced | `1000, 600, 0` | `248, 452, 652, 72` | all six / 1200 B / 3 Frames |

At every pre-rotation boundary, the combined arm's retired old-A ObjectIds are
exactly the disjoint union of the two single-axis arms. This is a fixture-level
set-additive result, not a general claim that the mechanisms are independent.

All six source Bases share one exact 1276-byte A Frame. The two single-axis arms
each retire half of the old-A payload by the third Stay but still require that
whole Frame; only the combined arm retires the last dependency and releases it.
This is joint shared-frame release, or coarse-frame masking, rather than evidence
of general synergy or total read-IO reduction.

After rotation, the exact Previous Frames are the realized B candidates that
contained earlier Bases. In the combined arm, each Stay co-locates one changed
Base and one same-state migration Base, so six Previous-debt objects occupy three
Frames rather than six. The zero-preparation counterfactual terminal-C append
vectors are `1280/1280/1280`, `1180/976/672`, `1180/976/672`, and
`1076/668/60` in the table's order; their corresponding new-scope debt is kept
separate from realized totals.

The comparison does not define an interaction score, winner, automatic target
trigger, persisted report, or product policy. The follow-up below keeps the
logical fixture and treatments fixed while changing the source payload-Frame
partition, closing the previously proposed layout discriminator.

## Source payload-Frame partition discriminator

A follow-up keeps the same six logical objects, frozen trace, four treatments,
and `[Stay, Stay, Stay, Rotate]` targets, but changes the source's physical
provenance layout. Both source variants end at a metadata-only full-OVD A
anchor whose six entries are External, followed by the same-shaped empty B
PublishedRevision Delta. The accepted histories are:

```text
shared payload -> full-OVD anchor -> B published
cold payload -> changed OVD Delta -> full-OVD anchor -> B published
```

The split changed Revision inherits the cold snapshot before the anchor is
created. This preserves the selected unique-prior-snapshot lineage rule; the
fixture does not assemble a source from unrelated snapshots. The anchor is an
OVD authority/lookup Frame and is intentionally absent from the current object
reconstruction Frame metric.

The old-A debt ObjectIds and logical Base payload bytes are identical between
the layouts at every step. Their required unique Previous Frames after the
third Stay are not:

| Treatment | Old-A debt | Shared payload Frame | Split payload Frames |
|---|---|---:|---:|
| Delta + none | all six / 1200 B | 1276 B | cold 652 B + changed 656 B |
| Delta + paced | changed / 600 B | 1276 B | changed 656 B |
| Base + none | cold / 600 B | 1276 B | cold 652 B |
| Base + paced | empty / 0 B | none | none |

The executable assertion uses exact Frame addresses as well as count and the
stored `FrameLengthBytes` sum. The whole Frame sizes include the provisional
domain records, local OVD, TailMeta directory, envelope and padding; they are
not pure object payload or measured IO.

This is an object-debt-equivalent, Frame-pressure-distinct counterexample:
object debt alone is not a sufficient statistic for exact coarse full-Frame
reconstruction pressure. It does not imply total or actual read IO, prove that
packing is the only physical difference, require a product policy to consume
two independent fields, or select a winner. Under the current one-Revision /
one-Frame model, splitting payload heads also changes Revision count, OVD shape,
tickets and offsets; this is a current-reconstruction source-layout probe, not
a production multi-frame Revision design.

## Preparatory B migration witness

`PreparatoryBaseMigrationPlanner` accepts an explicit, nonempty set of live
objects whose terminating Base is still in A. It reconstructs every live object
to validate the complete A/B closure, then writes the selected objects as
same-state, same-logical-ordinal Bases at B's current tail. The migration OVD is
a Delta over the supplied B PublishedRevision and binds the relocated objects
to contextual Self; all other live bindings are inherited.

`PreparatoryBaseMigrationAppender` rechecks B's tail, source OVD, every live
object's reconstruction closure, and each relocated Base before Append. A
successful append does not publish a StateStore head; the caller may use its
returned address as the source PublishedRevision for a later migration or
rotation. Historical lineage is diagnostic rather than a current-state
admission gate.

An executable 3 x 140,000,000-byte fixture demonstrates the capacity shape:
immediate C evacuation fails, one combined two-object B migration also fails,
one single-object B migration leaves C unable to fit, and a second single-object
B migration makes final C evacuation fit. A separate `head@B / Base@A` case
closes the same migration path through a Delta head.

This is a caller-selected witness, not a search procedure or Base/Delta policy.
It does not establish that an untried batch ordering cannot complete.

## Relay-free immediate rotation witness

`ImmediateRotationPlanner` remains the older pure planner/appender witness for
the narrow maintenance-only case that fits in one C evacuation Frame. Its only
source of live bindings is
`MaterializeLive(B PublishedRevision)`; it does not accept a caller StateMap.

```text
EvacuationSet = live objects whose terminating Base is in A
```

The plan owns one immutable runtime candidate `Frame`; the provisional grammar
is only its derived size projection. The C Revision writes a full Base for every
evacuated object and preserves its logical ordinal. C's full OVD Base uses the B
PublishedRevision as the single shared prior-snapshot anchor:
evacuated bindings use contextual Self, while retained objects bind Previous to
unchanged B heads. `ImmediateRotationAppender` revalidates the candidate against
the source, then constructs and appends the first C Frame before installing the
new file in the in-memory store. It does not publish a StateStore head.

After append, `MaterializeLive(C)` is the sole source of the B/C StateMap; the
plan no longer stores a parallel `ProjectedStateMap`. The immediate path itself
does not append B maintenance or forwarding records; preparatory B migration is
an explicit preceding operation.

The canonical `AA / BA / BB` fixture proves:

- AA current reconstruction reads only C; lineage resolves `C -> A` through
  OVD reads `B -> A`;
- BA current reconstruction reads only C; lineage resolves `C -> B -> A`;
- BB remains at B and C binds it External;
- a Remove in B's Published OVD is authoritative and cannot be revived by a
  separately supplied map;
- OVD insertion order does not affect the plan;
- C capacity failures are repeatable and do not mutate the source store;
- an exhausted B tail does not block a relay-free immediate plan;
- C append produces exactly the planned ticket/layout, and repeated or stale-
  scope apply fails without adding another file;
- historical lineage corruption remains visible to diagnostics but does not
  block current-state reconstruction or immediate rotation.

These slices still do not implement a byte writer/parser, publication/head or
reopen semantics, an automatic migration search, rotation-aware policy scoring,
or durable atomicity. The in-memory store also has no store identity or
publication frontier, so it cannot detect an isomorphic wrong store or
publication staleness that the model does not represent.
Generated Base/Delta sizes remain synthetic payload observations. The V0
projection is a versioned research input, not a durable-format commitment.

`CanPrepareAndRotate` is the currently selected liveness admission invariant,
not a law of whether an already-published format is readable. A concrete finite
B-migration-plus-C-rotation witness proves it true for that source state.
The current canonical prefix proof reports `RejectedUnproven` when it cannot
construct that witness; this is not a proof that no completion exists. The
continuous caller script, fixed-schedule migration comparison, and
`DebtZeroThenRotate` progress baseline are now closed without choosing a weighted
winner. Their scope-safe realized/counterfactual observation reductions are also
closed, as are the changed A-debt Base-versus-Delta discriminator and its
role-disjoint 2x2 composition with paced cold migration. The source payload-Frame
partition discriminator is closed as well. The next slice can keep one source,
one pure-Insert Stay, equal-payload one-object migration choices and a fixed target
while comparing ObjectId-first with frame-release-aware selection. A bounded
reference explorer still waits for a concrete conservative rejection or suspected
heuristic false-negative.

The rejected forwarding alternatives and their executable comparison are
preserved by annotated tag `research/relay-vs-relay-free-20260829` and DB-009.
DB-010 has selected the shared anchor and removed Base parent tokens. This also
explicitly rejects mixed-snapshot Base import/rescue in the current model;
durable proof of never-reused ObjectIds remains a separate open problem.

Run from the repository root:

```powershell
dotnet test experiments/TwoLegRotationProbe/TwoLegRotationProbe.csproj
```
