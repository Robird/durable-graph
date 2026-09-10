# Package consumer probe

> Active product regression. Run when changing generated output, runtime public APIs,
> history/build integration, or package wiring. Root-solution tests do not exercise this
> delivery boundary. Product progress lives in [src/PROJECT-STATE.md](../../src/PROJECT-STATE.md).

This experiment verifies the reusable delivery boundary rather than project-to-project wiring.
The consumer project has one `PackageReference` to `Atelia.DurableGraph`; it contains no manual
analyzer reference, `AdditionalFiles`, build hook, or `Import`.

Consumers use the runtime's non-generic `ObjectId` for captured/decoded identities, reference DTO
slots and UpgradeContext tracing. Storage rows and probe-owned address sidecars retain numeric
`uint` IDs; those boundaries use `.Value` or `new ObjectId(...)` explicitly. Ordinary numeric
`uint` fields and the independently specified reference-body golden bytes remain unchanged.

The [Dictionary consumer](DictionaryConsumer/README.md) exercises experimental BCL Dictionary support through
`Run-DictionaryProbe.ps1`: supported comparer policies, shared key/value references, unordered key-addressed Delta,
and independent historical enum-key/struct-value upgrades after deleting their old CLR declarations.

The [composite Dictionary consumer](CompositeDictionaryConsumer/README.md) runs through
`Run-CompositeDictionaryProbe.ps1`: generic struct keys whose IEquatable ignores a persistent Timestamp,
typed/resolver Application configuration, preserved restoration modes, and retained nested key/value history.
A separate same-Schema comparison change leaves exact DTO reading available while current TryAdd rejects a collision.

The [record consumer](RecordConsumer/README.md) runs through `Run-RecordProbe.ps1`: readonly positional
generic record keys and nested record values, explicit retained DTO upgrades after deleting old CLR names,
and current Default/Application dictionaries progressing from upgraded Base through NoChange to Delta.
Record-bearing compilations use the existing Family generated API; backing fields use `field:` attributes.

The [Enum consumer](EnumConsumer/README.md) adds explicit durable enum coverage through
`Run-EnumProbe.ps1`: unknown integer values, generic/Nullable/array/List composition,
retained history after deleting the old CLR enum, and explicit upgrades followed by Base/Delta resaves.
The current package writer emits history v9; accepted earlier history remains unchanged. Catalog v2 is retained.
The InlineStruct consumer keeps the ordinary generated API in V1/V2; after deleting the
inline CLR declarations in V3, it uses Family DTO aliases and unified definition registration.
This exercises the retained orphan-inline-history selection rule without changing its saved history.

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

An additional public StateStore package consumer exercises persistent Schema/representation registration and
Base-only representation IDs through actual packages:

```powershell
./experiments/PackageConsumerProbe/Run-StateStoreProbe.ps1
```

This independent script packs the eight local dependency packages into an isolated feed/cache,
including the unmodified sibling `atelia` substrate projects. Its consumer explicitly references
`Atelia.DurableGraph` for generator/build assets and `Atelia.DurableGraph.StateStore` for storage
operations, with no manual analyzer, import or project-reference wiring. Public
`LoadedWorld.PrepareNew` freezes an inherited Character whose base and leaf fields share a string,
registers the complete Schema closure and object representations, and produces its no-Parent Base plan. Registration is
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

DB-036 adds a separate public `GraphRepository` / `GraphSession` exercise using the same generated
graph. Three consecutive commits preserve the original World/Character/Item instances and their
transient state. Reopening needs only the repository path and model directory: the persistent head
selects the World ID and Revision. The loaded session commits again without reloading, and a final
reopen verifies the result. The script requires `GraphSessionContinuousCommit:True` as well as all
earlier markers. The low-level fixed-Parent exercises above remain unchanged.

## Guid, decimal and TimeSpan values

Run `./experiments/PackageConsumerProbe/Run-BclScalarProbe.ps1` for the
[BCL scalar consumer](BclScalarConsumer/README.md). Its two package builds cover direct and generic
scalar state, Nullable and containers, exact decimal scale/signed zero, actual decimal key Remove/Add,
deleted inline CLR history, explicit owner/value Upgrade and Base-to-Delta continuation. New history
uses v9; accepted names, hashes and bytes remain immutable. The runner also accepts a matching feed
through `-PackageSource <feed> -Version <version>`.

## DateOnly, TimeOnly and DateTimeOffset values

Run `./experiments/PackageConsumerProbe/Run-TemporalScalarProbe.ps1` for the
[temporal scalar consumer](TemporalScalarConsumer/README.md). Two package builds verify scalar
composition, exact offset changes at the same instant, Dictionary key Remove/Add, deleted inline
CLR history and explicit owner/shared List value upgrades. Upgraded objects rewrite Base, then
resume NoChange and ordinary Delta with no repeated business calls after cold reopen. New history
uses v9 while accepted filenames, hashes and bytes remain unchanged. A matching eight-package
feed can be reused with `-PackageSource <feed> -Version <version>`.

## Historical capability retention

```powershell
./experiments/PackageConsumerProbe/Run-HistoryCapabilityProbe.ps1
```

This independent script builds four real package consumers: V1 writes a World and a cyclic Legacy
object with Base/Delta history; V2 without Upgrade methods proves exact DTO decoding works while
editable Load fails; V2 with explicit upgrades removes the World edge and retains an abstract
migration shell, validates even the orphan's references, then writes a required World Base and
Legacy Remove. V3 removes the Legacy CLR class entirely: the migrated Revision loads, the old
Revision fails for lack of an exact reader despite its persisted Schema and retained history files.
Existing history hashes must remain unchanged. The witness freezes no automatic retired-type API.

An application that still promises to load older Revisions must retain their reader and normalization
capabilities. Removing a family from a newer Revision does not make its older versions independently
recoverable from metadata. `-PackageSource <feed> -Version <version>` can reuse an existing eight-package
feed; the default invocation packs its own isolated dependency closure.


## Inline struct history and retained value layouts

```powershell
./experiments/PackageConsumerProbe/Run-InlineStructProbe.ps1
```

This DB-037 regression builds four actual package consumers using the same feed and immutable
history directory. V1 saves an inherited World with two levels of readonly inline values, shared
and distinct-equal string references, a shared child and a World self-reference. A nested leaf edit
produces one owner Delta; the inline values never acquire object rows. Reopening restores private
readonly fields and defaults transient state without running value constructors.

V2 changes the inner value's numeric layout and explicitly advances the outer value, base owner
and derived World Schemas. The owner Upgrade constructs nested historical DTOs through target-typed
constructors. GraphSession rewrites upgraded objects as Base, then compares unchanged while retaining
the same domain instances. A separate rebuild advances only the nominal child's version: owner and
inline Schema versions remain unchanged, and the next save writes only a required child Base.

V3 removes both inline domain struct declarations and their containing persistent field. Its retained
V1-to-V2-to-V3 owner Upgrade chain still compiles, exact-reads the original Base/Delta revision, and
loads that old revision into the current model. The published current head removes the now-unreachable
child and strings and reopens normally. Shared historical value DTO/body helpers survive independently
of the deleted domain CLR names; this does not provide readers for deleted reference-object families.

The script requires all stage markers and history counts `5 -> 10 -> 11 -> 13`, rejects deletion or
modification of previously published `.dgschema` files, and checks that new history uses text format v9.
The unchanged score uses a five-byte integer value so the nested leaf change remains a real Delta
candidate after Base v4 shortened the type header; the ordinary B/D policy is unchanged.
It packs the same eight-package dependency closure by default; `-PackageSource <feed> -Version <version>`
reuses an existing feed. Generated artifacts and probe-owned address sidecars remain under the unique
ignored `obj` run directory.

## Generic definitions and owner upgrades

```powershell
./experiments/PackageConsumerProbe/Run-GenericProbe.ps1
```

The [generic consumer](GenericConsumer/README.md) exercises the DB-038 product path through real
runtime and StateStore packages. It builds successive application versions in separate processes,
registers `Generated.DurableDefinitions`, and lets operation snapshots close the encountered
`Box<int>`, `Box<Point>`, and inline `Pair<LegacyPoint>` bindings. Historical state DTOs live in
generated Family hosts, independently of current domain CLR declarations.

The stages cover actual Base/Delta persistence, stored-exact decoding, generic pass-through and
closed business upgrades, forced Base after Upgrade, stable resave, and deletion of an old inline
domain struct. A missing closed conversion leaves exact decoding available while editable loading
fails before that object's first business callback. The consumer also checks that UpgradeContext
identifies the actual object and each adjacent owner edge; it does not exercise DB-039 value tools.

The runner preserves accepted history hashes and requires v9 for new history. It packs an isolated
dependency feed by default, or reuses one through `-PackageSource <feed> -Version <version>`.
Run instructions, stage details and limitations live in the consumer README; the implementation
record is [DB-038 §12](../../docs/design-branches/0038-generic-schema-state-and-binding-design.md#12-产品施工跟踪).

## Composable value upgrades

```powershell
./experiments/PackageConsumerProbe/Run-ValueUpgradeProbe.ps1
```

The separate [value-upgrade consumer](ValueUpgradeConsumer/README.md) exercises the DB-039
Context tools through actual runtime and StateStore packages. One open Box owner requests an
explicitly declared value dependency: Box<int> uses the opted-in KeepExact rule, Box<Point> invokes
the Point conversion, and Box<Pair<Point>> composes the open Pair provider with that same leaf rule.
Box and Pair deliberately reuse the local key `value` to check their independent Context scopes.
Nested callbacks check the current ObjectId and complete adjacent owner Schema endpoints.

The runner builds three application versions plus a V2 missing-rule variant. That negative build
retains exact decoding but rejects the affected owner's editable Load before its first business
callback, and the runner checks that repository files and hashes remain unchanged. V3 removes an
old inline domain struct while retaining its generated history DTO and value provider. The fixture
checks multi-step loading from V1, forced Base, unchanged resave, ordinary Delta, and cold reopening.
It preserves accepted history hashes across builds; it does not change any persistent format.

The runner packs an isolated eight-package feed by default; use `-PackageSource <feed> -Version <version>`
to reuse a matching feed. Exact stage markers and boundaries live in the consumer README;
construction and acceptance evidence are recorded in
[DB-039 §8](../../docs/design-branches/0039-composable-value-upgrade-design.md#8-产品施工合同与验收映射).

## Composable arrays and array-owned upgrades

```powershell
./experiments/PackageConsumerProbe/Run-ArrayProbe.ps1
```

The [array consumer](ArrayConsumer/README.md) uses two real package builds to exercise all four
supported ranks, jagged sharing, generic array operands and generic struct elements, and a cycle
through a World array. A historical inline element Upgrade runs once per shared array, forces one
Base rewrite, then resumes ordinary Delta saving on the same domain instances. The old Revision
still decodes its exact old element DTO. The runner publishes and verifies immutable history v9.

The array consumer also covers DB-045's repository-local representation IDs: new Base v4 headers
contain only the ID, reopening preserves its layout, and array element Upgrade changes the array's
representation while the referencing owner's representation remains stable. The descriptor and its
exact Schema dependencies live in `schemas.rbf`; generated DTO and body contracts are unchanged.

## BCL List content and list-owned upgrades

```powershell
./experiments/PackageConsumerProbe/Run-ListProbe.ps1
```

The [list consumer](ListConsumer/README.md) exercises the DB-047 content object through two real
package builds. Nested lists, arrays in lists and lists in arrays, generic class/struct operands,
shared identity and World/list cycles survive continuous saves and cold reopening. Element edits,
append and middle/tail deletion preserve the List ObjectId; Capacity-only changes write no objects.
Separate prepared Base/Delta plans survive clearing their source lists before append.

The next build removes the old inline domain CLR declaration while retaining its versioned state
history. Explicit list-owned value Upgrade runs once for the shared list, forces Base and then
resumes ordinary Delta. Representation IDs remain persistent, with a new ID only for the upgraded
list layout. The runner requires stage markers, immutable history hashes and new history v9;
it accepts `-PackageSource <feed> -Version <version>` to reuse the existing eight-package feed.
The consumer uses DB-049's List range codec 2 and DB-051's default Adaptive writer. Algorithm comparisons
and same-history measurements live in [ListDeltaReplayProbe](../ListDeltaReplayProbe/README.md); this lane verifies package delivery.

## Nullable values and lifted value upgrades

Run `./experiments/PackageConsumerProbe/Run-NullableProbe.ps1` for the
[Nullable consumer](NullableConsumer/README.md). The two-stage witness uses nullable struct fields,
a shared List, vector and rank-four array, retaining reference sharing and cycles inside present values.
The second build deletes the old struct CLR declaration while retaining exact history, explicitly lifts
the child conversion, rewrites upgraded owners as Base and resumes Delta. Clearing nullable values
removes the unreachable cyclic island. History v9 hashes and counts are checked in packaged Publish
and Verify modes; the runner also accepts an existing matching eight-package feed.
