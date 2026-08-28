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
single-file run under either `AlwaysBase` or `AlwaysDeltaWhenLegal`:

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

The current single-file model preflights the necessary object-payload-only RBF
limit, but has no complete Revision capacity or rotation legality gate. Within
that surviving input set, `AlwaysDeltaWhenLegal` is presently equivalent to
“always Delta.” The enum is intentionally not a general policy interface yet.

## RBF envelope and raw observations

The in-memory file now reproduces the size and placement rules of the locally
inspected RBF draft v0.40 envelope:

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

Simulation observations deliberately use `AccountingScope.ObjectPayloadOnly`.
The supplied RBF Payload is only the selected synthetic
`ObjectVersion.PayloadBytes`, while modeled TailMeta is zero. The accounting
explicitly excludes ObjectVersion headers, ObjectVersionDict, TailMeta indexes,
relative-ticket VarUInt bytes, and self-ticket fixed-point effects. Therefore
the RBF envelope is exact for its supplied lengths, but the resulting numbers
are **not** complete DurableGraph Revision or wire sizes.

Each `RevisionObservation` separates a write event from the post-save
reconstruction snapshot. Writes record Base/Delta object payload and the
object-payload-only frame/append lengths. Reconstruction records required
object versions, unique frames, required payload, all object payload co-read
from those frames, and their full object-payload-only RBF frame lengths.
Post-save read snapshots are not a run total unless an experiment explicitly
chooses to simulate a full reload after every Save.

The fixed generated mixed workload currently demonstrates a tradeoff, not a
winner: under this incomplete accounting, AlwaysBase writes a 436-byte modeled
file and its final reconstruction reads one 148-byte modeled frame;
AlwaysDelta writes 364 modeled bytes but its final reconstruction reads three
modeled frames totaling 348 bytes. Those values must be rerun after a concrete
ObjectVersion/OVD/index codec exists.

This preparatory slice still does not model complete `SizedPtr`/relative-ticket
encoding, publication, relay revisions, two-file closure, full Revision
capacity, rotation planning, adaptive Base-or-Deltify decisions,
`CanPrepareAndRotate`, or policy scoring. Generated Base/Delta sizes remain
synthetic payload observations, not a serializer or wire-format claim.
`RelativeFrameTicket` is interpreted relative to the file containing it:
stepping creates a new file and a new `FileScope`; an old frame must still be
read with the scope of its own origin file. This pair is an in-memory precursor,
not a proposed durable ticket encoding.

Run from the repository root:

```powershell
dotnet test experiments/TwoLegRotationProbe/TwoLegRotationProbe.csproj
```
