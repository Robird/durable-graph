# Two-leg rotation probe

This isolated .NET 10/xUnit project is the in-memory workbench for exploring
two-leg StateStore file rotation and Base-or-Deltify policies.

The first scaffold deliberately models only a few container facts:

- RBF file numbers start at 1 and increase monotonically;
- an `RbfFile` only appends frames and can randomly read an existing frame by
  its `(OffsetBytes, LengthBytes)` `FrameTicket`;
- an `RbfFileStore` creates files and retrieves them by file number;
- a `FileScope` has a readonly `CurrentFileNumber`, derives its
  `PreviousFileNumber`, and resolves relative parent IDs against the store;
- a built `Frame` owns a read-only `ObjectId` (`uint`) to `ObjectVersion` map;
- `ObjectVersion` distinguishes Base from Delta and records synthetic payload
  cost, resulting Base size, logical version ordinal, and lineage parent;
- `ObjectVersion.ParentFrameTicket` is nullable for a first version, otherwise
  it is `(bool IsPreviousFile, FrameTicket)` interpreted in the containing
  frame's file scope;
- the live in-memory StateMap uses `AbsoluteFrameAddress(FileNumber,
  FrameTicket)`, rather than retaining a context-dependent relative ticket;
- mutable `FrameBuilder` and `ObjectVersionBuilder` instances are copied into
  read-only built state.

`LogicalVersionOrdinal` is deliberately not a physical chain ordinal. A parent
edge with `child = parent + 1` represents a domain change; a same-version edge
represents transparent physical maintenance. With no additional runtime kind,
a zero-payload same-version Delta is a relay and a same-version Base is a
relocated full value. Reconstruction follows Delta records and stops at Base;
the separate lineage inspection continues through Base parents and validates
the full physical chain. In this size-only probe, logical equality means exact
`(BasePayloadBytes, LogicalVersionOrdinal)` equality, not future field-value
equality.

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
and points to that object's previous head, which can skip unrelated or
remove-only frames. Remove only deletes the live StateMap binding; its SaveStep
still appends an empty physical Frame and old frames remain readable history.
Every non-create Base also retains its lineage parent, although reconstruction
stops at the newest Base.

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
`ProvisionalRevisionV0` size grammar described below. A separate pure planner
now projects one immediate A/B to B/C step, but it is not wired into the
single-file policy runs. Within those runs, `AlwaysDeltaWhenLegal` is presently
equivalent to “always Delta.” The enum is intentionally not a general policy
interface yet.

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
domain record = U(body length) + kind + U(parent) + opaque synthetic body
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
| fixed-seed mixed | AlwaysBase | 342 | 99 | 536 | 184 |
| fixed-seed mixed | AlwaysDelta | 274 | 97 | 464 | 448 |
| fixed-seed mixed | ObjectPayloadReadAmplification3 | 279 | 97 | 468 | 300 |
| hot-one/cold-eight | AlwaysBase | 1500 | 179 | 1900 | 1148 |
| hot-one/cold-eight | AlwaysDelta | 1050 | 177 | 1440 | 1408 |
| hot-one/cold-eight | ObjectPayloadReadAmplification3 | 1125 | 177 | 1516 | 1148 |

Both tables demonstrate tradeoffs rather than a winner. Reports sum write
events and show only the final post-save read snapshot; no `TotalReadBytes` is
defined. The matrix lives in executable tests; a ScenarioCatalog or report
format waits for a CLI, persisted artifact, or batch-run consumer.

## Immediate rotation witness

`ImmediateRotationPlanner` is a pure grammar-level planner for the narrow case
that fits in at most one B relay Frame and one C evacuation Frame. It derives
source facts through the existing reconstruction oracle, then computes:

```text
EvacuationSet = live objects whose terminating Base is in A
RelaySet      = EvacuationSet objects whose latest head is still in A
```

The optional B relay Revision contains one zero-synthetic-payload helper per
RelaySet object, an empty OVD Delta whose parent is the old B head, and a
TailMeta directory for those helpers. The C Revision writes a full Base for
every evacuated object and a full OVD Base: evacuated bindings use contextual
Self, while retained objects bind Previous to their unchanged B heads. The
projected StateMap is decoded from those OVD bindings rather than supplied as a
second result authority.

For the canonical `AA / BA / BB` three-object fixture, the provisional exact
layout is:

| Planned Frame | Start/length | Synthetic payload | Domain headers | OVD | TailMeta |
|---|---:|---:|---:|---:|---:|
| B relay | `32 / 40` | 0 | 4 | 5 | 4 |
| C evacuation | `4 / 88` | 35 | 8 | 12 | 6 |

Executable tests also prove canonical ordering, empty/no-relay plans, B relay
TailMeta overflow, C combined-capacity overflow, retryability, and zero input
mutation. Success is a constructive immediate-rotation witness and therefore a
sufficient example of preparability. Failure only rejects this single-relay,
single-C shape; it does not prove that multiple B maintenance/relay Frames or a
general `CanPrepareAndRotate` plan are impossible.

This preparatory slice still does not implement a byte writer/parser, append,
publication, maintenance-record materialization, general two-file completion
search, rotation-aware policy scoring, or the `CanPrepareAndRotate` safety gate.
Generated Base/Delta sizes remain synthetic payload observations, not serializer
output. The caller-supplied StateMap is the current in-memory authority; there is
not yet a persisted OVD reader that can derive it from the supplied head.
`RelativeFrameTicket` is interpreted relative to the file containing it:
stepping creates a new file and a new `FileScope`; an old frame must still be
read with the scope of its own origin file. The V0 projection is a versioned
research input, not a durable-format commitment.

The runtime model now has executable transparent Relay/RelocatedBase semantics,
but the planned rotation records are still not materialized or appended. A
separate open branch, DB-009, asks whether an earlier Revision's authoritative
OVD can serve as a Base-only lineage locator and remove dedicated relay records.
That alternative is not implemented because the probe does not yet have a
replayable persisted OVD reader; the caller StateMap must not impersonate one.

Run from the repository root:

```powershell
dotnet test experiments/TwoLegRotationProbe/TwoLegRotationProbe.csproj
```
