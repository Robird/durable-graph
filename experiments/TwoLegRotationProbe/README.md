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

This closes both pure explicit candidate builders. It is not yet an automatic
policy, paired feasibility result, durable head publication path, or continuous
multi-rotation runner.

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
Failure of a bounded explorer to find one must remain `NotFoundWithinBounds`,
not a proof that no completion exists. The next slice should evaluate the
explicit Stay-B and Rotate-C candidates independently for the same normalized
Save, preserving typed exact-feasibility outcomes and raw observations without
choosing a weighted winner. It can then support a conservative completion
certificate and scripted continuous multi-rotation run. A bounded reference
explorer waits for a concrete conservative rejection or suspected heuristic
false-negative; it does not block the first strategy loop.

The rejected forwarding alternatives and their executable comparison are
preserved by annotated tag `research/relay-vs-relay-free-20260829` and DB-009.
DB-010 has selected the shared anchor and removed Base parent tokens. This also
explicitly rejects mixed-snapshot Base import/rescue in the current model;
durable proof of never-reused ObjectIds remains a separate open problem.

Run from the repository root:

```powershell
dotnet test experiments/TwoLegRotationProbe/TwoLegRotationProbe.csproj
```
