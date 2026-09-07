# Package consumer probe

> Active product regression. Run when changing generated output, runtime public APIs,
> history/build integration, or package wiring. Root-solution tests do not exercise this
> delivery boundary. Product progress lives in [src/PROJECT-STATE.md](../../src/PROJECT-STATE.md).

This experiment verifies the reusable delivery boundary rather than project-to-project wiring.
The consumer project has one `PackageReference` to `Atelia.DurableGraph`; it contains no manual
analyzer reference, `AdditionalFiles`, build hook, or `Import`.

Run from the repository root:

```powershell
./experiments/PackageConsumerProbe/Run-Probe.ps1
```

The probe packs a unique local package, uses an isolated package cache and Schema-history
directory under this experiment's ignored `obj` directory, and exercises local publish plus
CI-style read-only verification. It also proves that the packaged build target rejects legacy
`.dgsnapshot` files instead of silently ignoring them. Its V1/V2 lane proves that bare `[DurableType]` emits exact
Schema history and versioned State DTOs, including a compile-checked adjacent upgrade shape.

The feed also contains the runtime's Serialization dependency. A final consumer captures private
base/derived fields into a readonly versioned DTO through generated `__DurableState`,
then mutates the domain instance. Static DTO body calls verify the original golden bytes,
DTO/Schema pairing and domain isolation. It still has just one PackageReference;
Serialization is supplied transitively, with no friend access or manual analyzer wiring.

The same consumer also exercises every added scalar kind through generated Capture/DTO bodies,
including an isolated surrogate, negative zero and NaN payloads against fixed golden bytes.
This validates the public primitive API and publication of the extended Schema tags.

The same consumer prepares a Delta between frozen base/derived DTOs after mutating
their source objects. It checks independent bitmap/value golden bytes, the actual Delta length,
and a no-change result whose zero bitmap is nonempty. The public `PreparedDeltaBody` comes from the
transitive Serialization package; its owned `Body` is reused directly for repeated generated
Apply calls, with complete body consumption and golden Base reconstruction. An invalid padding
bit must fail. The script requires the `PreparedDeltaBody:True` marker in addition to the existing
markers. This covers the same-Schema body API, not persisted Delta records or prior-chain validation.

The consumer also calls generated `PrepareBaseBody(in V1)` on the frozen inherited DTO and the prebuilt
`StringPayloadCodec.PrepareBase(string)` for nonempty and empty string content. The public
`PreparedBaseBody` and string helper arrive through the same transitive Serialization package.
Golden bytes, later domain mutation and changes to external body copies verify reusable owned
body content. The script additionally requires `PreparedBaseBody:True`; this is body preparation,
not Storage envelope sizing or a complete Save operation.

Generated AddRoot also supplies a stable preparation binding. The same single-package consumer
uses `CaptureSession.Prepare(candidate)` for heterogeneous roots, private inheritance segments and
string objects without selecting DTO types or calling their body helpers. Independent Base and Delta
goldens verify frozen content, repeated preparation, a nonempty no-change bitmap, equal-but-distinct
string replacement, retirement, and content surviving Discard. The script requires the additional
`CapturePreparation:True` marker. This prepares in-memory candidates; it does not establish a
persistent Parent baseline, append a StateRevision, or publish a branch head.

Generated `RegisterReaders` also accepts a small consumer-owned `IStateReaderRegistration` sink
from Runtime alone. Two calls supply the same stable reader binding and its exact generated Schema;
the script requires `GeneratedReaders:True`. This verifies the generator's registration seam
without adding a StateStore package dependency or assigning registry policy to the sink.

The Runtime-only consumer also registers the stable generated `Model` through a consumer-owned
`IStateModelRegistration` sink, normalizes a captured row to the current-version DTO, then calls generated `Allocate`
and `Hydrate`. Its private readonly base fields survive; neither the leaf/base constructors nor
the transient field initializer executes. The script additionally requires `GeneratedModel:True`
and `ReadonlyRestore:True`. This verifies generated restoration helpers without adding StateStore
to the Runtime package or implementing a consumer-owned persistence coordinator.

The final consumer also captures two concrete roots with shared strings, distinct equal strings,
null/empty/surrogate content, and private base fields through generated AddRoot adapters. It checks
the ID-closed candidate DTO directory, static ID-body golden bytes, mutation isolation, accept/discard, stable IDs for
instances retained across accepted candidates, discarded-number consumption, and fresh IDs after retirement. These use public runtime seams
from the single package reference; generated helpers and DTOs stay internal to the consumer.
This remains an in-memory candidate witness, not StateStore Save.

The string decoding witness then encodes the frozen DTOs and string objects into independent
owned bytes. Its typed loader accepts only bytes, roots and explicit metadata, preflights the
complete directory for unique nonzero IDs and exact Schema/body bindings, decodes all bodies with
full consumption, and validates every owner's generated string reference slots before returning.
It checks reference sharing across owners/base segments, equal but distinct strings, null/empty/
surrogate content, mutation after Seal but before encoding, and reversed object ordering. Missing
or wrong-kind references, cross-kind duplicate IDs, mismatched Schema, invalid roots and malformed
bodies must fail. The generated helpers call the public string APIs through the same single
PackageReference, with no friend access. This typed byte bundle is not a persistent format,
StateRevision, generic loader, or restored domain object graph.

Empty strings are the explicit exception to reference preservation: Capture normalizes all empty
instances to `string.Empty` and one ID per capture view; loading different IDs with empty bodies
returns that same singleton, while duplicate IDs remain invalid and ID zero remains null. The
consumer proves this through generated Capture with independently allocated empty test inputs and
then byte-only loading. Public `Replace` calls supply those test inputs with explicit identity
assertions; product behavior has no dependency on that allocation behavior or private runtime hooks.

An additional public StateStore package consumer exercises persistent Schema registration and
Base-only type references through actual packages:

```powershell
./experiments/PackageConsumerProbe/Run-StateStoreProbe.ps1
```

This independent script packs the eight local dependency packages into an isolated feed/cache,
including the unmodified sibling `atelia` substrate projects. Its consumer explicitly references
`Atelia.DurableGraph` for generator/build assets and `Atelia.DurableGraph.StateStore` for storage
operations, with no manual analyzer, import or project-reference wiring. Public
`LoadedWorld.PrepareNew` freezes an inherited Character whose base and leaf fields share a string,
registers the complete Schema closure, and produces its no-Parent Base plan. Registration is
idempotent and rejects a conflicting batch before append. Loading that Revision and editing the
domain object produces an ordinary raw Delta through `LoadedWorld.Prepare`; read-only reopening recovers exact Schema definitions and
uses generated `RegisterReaders` with the public `StateReaderRegistry` and `RevisionDecoder.Read`
to reconstruct complete stored-exact DTO/string directories for both revisions. No per-object
reader selection, Base-envelope codec, or manual string-table construction coordinates decoding.
The consumer checks all live rows, selected Revision addresses, the frozen pre-mutation Base, the
raw Delta bytes, and shared string identity across inherited fields. Repeating generated registration
is idempotent. The script requires `PersistedSchema`, `PreparedWorld`, `RawDelta`, `ColdTypedRead`,
`SharedString`, `ConflictBeforeAppend`, and `DecodedRevision` markers and retains artifacts under its
unique ignored `obj` directory. This first phase retains its inherited stored-exact witness and two
history files.

The script then builds and runs a separate `World` model at V1, then builds and runs V2 against the
same actual packages and V1-created database. It publishes and consumes real generated history
(three history files after V1; eight after V2,
including the four reference-graph models described below). The V2
consumer receives the V1 process's exact Revision address and World ID through a probe-owned sidecar.
The V1 process writes its Base plus two Deltas through `PrepareNew`, `Load`, and `Prepare`, then closes
the files. After writable reopening,
public `LoadedWorld.Load<World>` upgrades the complete old DTO, allocates without a constructor,
and hydrates private readonly scalar/string fields. `Prepare` forces an unchanged upgraded object
to a Base for the current-version DTO; a later domain mutation cannot change that prepared content. The host explicitly
appends and loads a new owner, whose unchanged plan contains no object writes and whose next edit
uses ordinary Delta. A second cold reopening restores that Base/Delta pair and still reads the
original historical revision. No fixture-owned DTO migration or planning coordinator substitutes
for the public loading/preparation APIs.

Additional required markers are `HistoricalUpgrade`, `ConstructorFree`, `ReadonlyHydrate`,
`ForcedBase`, `UnchangedResave`, `NormalDelta`, and `ReopenedWorld`, each followed by `True`.
The historical witness supplies the explicit World ID and exact Revision address; it does not publish
a head, discover roots/CLR types, hand-build old DTO bodies, or invoke transient hooks. Mixed historical model families
and failure boundaries remain covered by product integration tests.

The V2 consumer additionally constructs an ordinary `GraphWorld` with a shared derived
`GraphCharacter` in nominal base fields. The Character and Item retain readonly mutual references;
the Item also refers to itself, and its label shares the Character's inherited readonly name.
Public `LoadedWorld.PrepareNew` produces the initial no-Parent plan, which the host appends without
hand-built DTOs or bootstrap records. Mutating and disconnecting the original graph after preparation
cannot change those saved bytes. Closing and reopening restores the exact derived type, sharing,
cycles and readonly fields without executing constructors or transient initializers.

A child-only edit produces one Character Delta and retains the World object's prior object head. Repeated
preparation is equivalent, reloading an unchanged graph produces no writes, and disconnecting both
World references removes the entire cyclic island and its string from the new view. A final cold
reopening checks the removed view and both earlier graphs. Required markers are `PrepareNewGraph`,
`SharedDerived`, `ReadonlyCycles`, `ChildOnlyDelta`, `UnreachableCycleRemoved`, and
`HistoricalGraphPreserved`, each followed by `True`. The host still retains the explicit WorldId
and Revision address; preparation and Append do not publish a head or advance the loaded baseline.
