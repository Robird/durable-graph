# Two-leg rotation probe

This isolated .NET 10/xUnit project is the in-memory workbench for exploring
two-leg StateStore file rotation and Base-or-Deltify policies.

The compact current goal, near-term roadmap, and unresolved work live in
[`PROJECT-STATE.md`](PROJECT-STATE.md). This README describes the implemented
model and executable evidence; it is not the active backlog.

Documentation responsibilities are deliberately split:

- exact executable vectors live in [`Tests/`](Tests/);
- chronological results live in
  [`../../docs/DurableGraph-lab-notebook.md`](../../docs/DurableGraph-lab-notebook.md);
- competing or selected designs live in
  [`../../docs/design-branches/`](../../docs/design-branches/);
- current work and next slices live only in `PROJECT-STATE.md`.

New experiments should replace or merge a selected-evidence entry below rather
than append another chronological chapter.

Run the probe from the repository root:

```powershell
dotnet test experiments/TwoLegRotationProbe/TwoLegRotationProbe.csproj
```

Within one file epoch:

```text
A = Previous file
B = Current file containing the latest PublishedRevision
C = fresh Next file created by Rotate-C
```

Stay-B appends to B; Rotate-C changes the scope from A/B to B/C. A live object
has **A-debt** only when its current reconstruction chain terminates at a Base
in A. Historical lineage that can navigate back to A is not reconstruction debt.

The current in-memory model has these container facts:

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

## Frozen workloads and physical baselines

`ScenarioDefinition` generates a complete `WorkloadTrace` before a policy runs.
Each canonical, nonempty `SaveStep` contains `CreateObject`, `UpdateObject`, or
`RemoveObject` changes. Updates freeze both the resulting full-Base size and a
candidate Delta size. Logical replay rejects invalid lifecycles, reused or
missing IDs, non-positive Deltas, and uncompressed growth that the Delta could
not describe.

Field- and List-shaped generators provide different synthetic change patterns.
Their internal shapes are generation inputs only; policies see the same frozen
changes. Random streams are forked by `(seed, step, object, lane)`, so an extra
draw in one lane cannot advance another. The algorithm, generator version, and
scenario definition form the reproducibility boundary.

“Frozen” means every treatment replays the same in-memory trace instance. It is
not a serialized trace format. `WorkloadSimulator` compiles that trace into a
fresh single-file run and validates every accepted logical prefix:

```text
SaveStep -> candidate Frame -> absolute live StateMap
         -> symbolic materialization -> next accepted step
```

Create writes Base; Update uses the selected Base or Delta; Remove changes only
the live binding and still appends its Revision Frame. Delta payload bytes are a
storage-cost observation rather than serialized value-transform bytes. The
size-only oracle checks exact parent state, logical ordinal, reconstruction
payload, and the no-compression growth bound before accepting a Delta.

The three single-file baselines are `AlwaysBase`, `AlwaysDeltaWhenLegal`, and
`ObjectPayloadReadAmplification3`. In the current generated workloads every
legal Delta is selected by `AlwaysDeltaWhenLegal`; the enum is not a general
policy interface. These baseline runs are separate from the A/B rotation
planners described later.

### Object-local read-amplification baseline

The StateJournal-inspired local policy selects Base when:

```text
BasePayloadBytes <= DeltaPayloadBytes
    or
(BasePayloadBytes - DeltaPayloadBytes) * 3
    <= ParentReconstructionObjectPayloadBytes
```

Equality selects Base. `ReconstructionObjectPayloadBytes` is recomputed by the
oracle: Base resets it to its payload, while Delta adds its payload to the
parent chain. This field is simulation metadata, excluded from both accounting
profiles and from any wire commitment. The ratio preserves the shape of the
inspected StateJournal rule, not its one-object-per-Frame overhead model.

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

`AccountingScope.ObjectPayloadOnly` counts only selected synthetic
`ObjectVersion.PayloadBytes`; TailMeta is zero and DurableGraph headers, OVD,
directory, and address tokens are excluded.

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
that ticket. This avoids the rejected literal self-ticket fixed point, which
could admit two stable widths.

V0 sorts records by ObjectId, checks component arithmetic, TailMeta,
Payload+TailMeta, native RBF bounds and the 512 GiB relative-start gate before
Append. It returns component sizes and validates the resulting RBF layout, but
does not write or parse bytes. It is exact for this named provisional grammar,
not a production wire-format or compatibility promise. RBF Tag values,
opcodes, final record fields and Extent behavior remain unchosen.

The terminal-C sizing discriminator shows why the whole candidate must remain
the sole sizing authority: at a VarUInt boundary, a high-ticket External binding
can overflow while a same-state Base plus Self still fits. This does not create a
composable per-object savings rule or commit a future wire format.

Each `RevisionObservation` separates a write event from the post-save
reconstruction snapshot. Writes record Base/Delta object payload and the
selected profile's frame/append layout. Reconstruction records required object
versions, unique frames, required payload, all object payload co-read from those
frames, and the selected profile's full RBF frame lengths.
Post-save read snapshots are not a run total unless an experiment explicitly
chooses to simulate a full reload after every Save.

The handwritten and fixed-seed matrices expose different write/final-snapshot
tradeoffs under both profiles; none selects a winner. Exact goldens live in
[`PolicyMatrixTests.cs`](Tests/PolicyMatrixTests.cs). Reports sum write events
but show only a named post-save reconstruction snapshot; no `TotalReadBytes` is
defined.

## Normalized Save facts and explicit Stay-B / Rotate-C candidates

`SaveStepNormalizer` turns one `parent PublishedRevision@B + SaveStep` into
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
append or publish. Canonical decisions, conflicts, purity, and exact state are
covered by [`StayBRevisionCandidateTests.cs`](Tests/StayBRevisionCandidateTests.cs).

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
anchor, not an OVD replay parent. Delta and External encode exact B heads, which
may predate PublishedRevision, rather than the shared anchor. Current candidate
semantics are covered by
[`RotateCRevisionCandidateTests.cs`](Tests/RotateCRevisionCandidateTests.cs).

## Explicit paired evaluation

`ExplicitCandidatePairEvaluator` attempts the caller-supplied Stay-B and
Rotate-C actions independently over the same `NormalizedSaveFacts`. A feasible
attempt retains the exact plan, runtime candidate and the candidate's existing
`ProvisionalRevisionV0Estimate`; it does not rebuild the candidate or invoke a
second sizing authority. The pair deliberately has no winner, aggregate score,
fallback, append, or publication behavior.

The provisional estimator reports five narrowly typed physical limits:
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

Exact pair identity, determinism, one- and two-sided capacity rejection, and
non-capacity failure behavior are covered by
[`PairedCandidateEvaluationTests.cs`](Tests/PairedCandidateEvaluationTests.cs).

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
claim that another order, batch, action, or future solver cannot succeed.
Certificate construction never mutates the caller Store; executable capacity
and replay witnesses live in
[`CompletionCertificateTests.cs`](Tests/CompletionCertificateTests.cs).

## Explicit policy step harness

`ExplicitRotationPolicyStepHarness` is a stateless, single-step policy seam. The
caller supplies one exact pair and an explicit target and receives one of four
typed outcomes: applied Stay-B, applied Rotate-C, selected-target capacity
rejection, or Stay-B completion `RejectedUnproven`.

The harness never chooses a target, falls back to the other candidate, executes
the certificate's maintenance/final continuation, caches a StateMap, or owns a
publication cursor. Test-local treatments may select no migration or select the
smallest eligible A-debt `NoChange` ObjectId; that ordering is deterministic
assignment, not evidence of object temperature. Target schedules and source-debt
triggers remain caller-owned experimental controls.

## Scoped rotation observation reductions

The comparison fixture reduces each successfully applied step into a
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
observed Save count rather than a complete leg length.

A successful Stay's completion certificate contributes a separately named
counterfactual terminal-C projection. It records the number of additional
preparatory Stay steps and the exact final-C append/result observation. It does
not include preparatory writes or terminal-source pressure, and no
counterfactual candidate contributes to realized totals, peaks, epochs, or
rotation count. The pair's unselected Rotate-C remains a same-source, same-Save
alternative; it is not this post-Stay terminal candidate.

The same frozen traces and fresh Store forks reproduce the complete primitive
projection. This is deterministic value-projection evidence, not a canonical
transcript, persisted report, Store identity, or cross-process format. The
records remain test-local, and successful-run reductions do not represent typed
capacity or completion rejection outcomes.

## Selected executable evidence

The README keeps only results that still shape the current strategy work. Exact
vectors and all edge cases remain authoritative in the linked tests.

| Probe | Established result | Claim boundary and executable authority |
|---|---|---|
| Baseline policy matrix | Base, Delta, and the local ratio trade modeled writes against a named final reconstruction snapshot; there is no universal winner. | No cumulative read total. [`PolicyMatrixTests.cs`](Tests/PolicyMatrixTests.cs) |
| Terminal-C sizing | A high-ticket External binding can overflow when same-state Base+Self fits, so per-object token savings cannot replace whole-candidate sizing. | Provisional grammar only. [`ProvisionalRevisionV0GrammarTests.cs`](Tests/ProvisionalRevisionV0GrammarTests.cs) |
| Controlled migration pacing | Moving one A-debt object on each fixed-target Stay can smooth the final Rotate write, but the B records become new Previous debt after rotation. | ObjectId is deterministic assignment, not coldness. [`RotationPolicyComparisonTests.cs`](Tests/RotationPolicyComparisonTests.cs) |
| Source-debt trigger | A Save that clears source debt still Stays; only the next Save sees debt-free input and may Rotate. Without migration, a finite trace can finish with deferred debt despite valid certificates. | Certificate feasibility is not realized progress or a product trigger. [`RotationPolicyComparisonTests.cs`](Tests/RotationPolicyComparisonTests.cs) |
| Changed A-debt Base vs Delta | Base@B can distribute evacuation work across natural Updates, while Delta defers it to C; after the scope shift those B Bases become new Previous debt. | A shared A Frame masks partial object-debt retirement. [`RotationPolicyComparisonTests.cs`](Tests/RotationPolicyComparisonTests.cs) |
| Role-disjoint 2x2 | Changed-write and cold-migration retire disjoint ObjectId sets additively in the fixture; only their combination releases the shared A Frame. | Fixture-level set additivity is not general independence, synergy, or total-IO improvement. [`RotationPolicyComparisonTests.cs`](Tests/RotationPolicyComparisonTests.cs) |
| Source payload-Frame partition | The same object-debt trajectory can require one shared A Frame or different role-local A Frames. | Object debt is not a sufficient statistic for exact Frame pressure; this is not pure packing or actual IO. [`RotationPolicyComparisonTests.cs`](Tests/RotationPolicyComparisonTests.cs) |
| Migration membership and future opportunity | Equal-cost membership can change immediate Previous-Frame closure; payload skew exposes write-vs-released-byte pressure; a known-future equal-size trace shows that migrating the object about to be updated with a forced Base can leave old-A debt that remains unchanged in that trace. | Handwritten topology/size/oracle witnesses only—not physical reclamation, actual or total IO, online temperature inference, a weighted winner, or a default policy; rotation can reverse the apparent advantage by creating new-scope Previous debt. [`RotationPolicyComparisonTests.cs`](Tests/RotationPolicyComparisonTests.cs) |
| Capacity coupling | Two one-object B migration batches can make a large C evacuation fit; conversely, one optional same-state B migration can push an otherwise feasible grouped-foreground Stay beyond the one-Frame envelope. | Handcrafted provisional-grammar witnesses only; they prove neither complete repair/search nor file-size or rotation-trigger policy. [`PreparatoryBaseMigrationTests.cs`](Tests/PreparatoryBaseMigrationTests.cs), [`CompletionCertificateTests.cs`](Tests/CompletionCertificateTests.cs), [`GroupedForegroundBurstCapacityCouplingTests.cs`](Tests/GroupedForegroundBurstCapacityCouplingTests.cs) |
| Evaluator v1 admissibility | An isolated run fork exposes metrics only after all workload steps and one actually replayed canonical terminal settlement; direct Rotate has no empty Stay, multi-step preparation is one charged Commit, and hard rejections remain typed/non-scoring. | Closes the terminal source epoch `A/B -> B/C`, not all future Previous debt; no scalar score, batch manifest/report, or product evaluator. [`EVALUATOR-V1.md`](EVALUATOR-V1.md), [`EvaluatorV1SessionTests.cs`](Tests/EvaluatorV1SessionTests.cs) |
| Continuous rotation | A caller script crosses `A/B -> B/C -> C/D` while preserving exact state, reconstruction closure, and Stay certificates. | It is not a stateful runner or durable publication path. [`ContinuousMultiRotationTests.cs`](Tests/ContinuousMultiRotationTests.cs) |

### Accepted source-partition provenance

The partition probe requires two different physical sources to represent one
legal logical snapshot. Both end at a metadata-only full-OVD A anchor whose six
entries are External, followed by the same-shaped empty B PublishedRevision
Delta:

```text
shared payload -> full-OVD anchor -> B published
cold payload -> changed OVD Delta -> full-OVD anchor -> B published
```

The split changed Revision inherits the cold snapshot before the anchor is
created, so the fixture does not splice unrelated snapshots. The anchor is an
OVD lookup authority and is excluded from the current object-reconstruction
Frame metric.

Both layouts replay the same trace and treatment assignments and produce the
same old-A debt IDs and logical Base bytes, while their exact required Previous
Frames differ. Under one-Revision/one-Frame, splitting payload heads also changes
Revision count, OVD shape, tickets, and offsets; this is not pure record packing,
actual IO, a product-field requirement, or a winner. Exact addresses and byte
goldens live in `Source_payload_frame_partition_changes_required_previous_frames_not_object_debt`
in [`RotationPolicyComparisonTests.cs`](Tests/RotationPolicyComparisonTests.cs).

## Supporting rotation witnesses and boundaries

### Preparatory B migration

`PreparatoryBaseMigrationPlanner` accepts an explicit, nonempty set of live
A-debt objects and writes same-state, same-ordinal Bases at B. Its OVD Delta
points to the supplied B PublishedRevision, binds relocated objects to Self, and
inherits all other live bindings. The appender rechecks the B tail, source OVD,
and every live reconstruction before mutation. It publishes no durable head and
does not search migration sets; the caller may use the appended Revision as a
later source.

### Relay-free immediate rotation

`ImmediateRotationPlanner` is the narrower maintenance-only predecessor of the
general Rotate-C builder. Its sole live-binding authority is
`MaterializeLive(B PublishedRevision)`. It writes every A-dependent object as a
same-state Base@C, retains B-contained heads as External, and uses B's
PublishedRevision as the shared prior-snapshot anchor. Current reconstruction
then closes over B/C even when diagnostic lineage can resolve through B to A.

The appender revalidates the exact candidate before installing the first C
Frame; capacity, stale-scope, or repeated-apply failures add no file. Exhausting
B's relative start does not itself block a fresh-C plan. These boundaries remain
covered by [`ImmediateRotationPlannerTests.cs`](Tests/ImmediateRotationPlannerTests.cs)
and [`ImmediateRotationAppenderTests.cs`](Tests/ImmediateRotationAppenderTests.cs).

### Current global boundaries

- generated Base/Delta sizes and the V0 grammar are provisional; there is no
  byte writer/parser or durable-format commitment;
- required full reconstruction Frames are not actual or cumulative IO;
- there is no durable head publication, reopen/crash protocol, concurrency,
  atomic multi-store commit, store identity, or file GC;
- there is no general automatic migration search, weighted score, policy winner, or
  completeness proof;
- `CanPrepareAndRotate` is a liveness admission backed by one finite witness,
  not a law of format readability; `RejectedUnproven` remains conservative;
- DB-010's shared anchor rejects mixed-snapshot import/rescue, while durable
  proof that ObjectIds are never reused remains open.

The rejected relay alternatives are preserved by annotated tag
`research/relay-vs-relay-free-20260829` and
[`DB-009`](../../docs/design-branches/0009-base-lineage-parent-locator.md);
[`DB-010`](../../docs/design-branches/0010-base-lineage-anchor-scope.md) records
the selected shared-anchor lineage model. See
[`PROJECT-STATE.md`](PROJECT-STATE.md) for active work rather than extending
this README with a backlog.
