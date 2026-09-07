# Atelia.DurableGraph

DurableGraph is an exploratory .NET 10 prototype for versioned durable object graphs.
Its public API, Schema-history format, and build workflow are not stable yet.

A direct package reference supplies the runtime library, Source Generator, and Schema-history
build integration. Each successful local `CoreCompile` publishes exact Schema records under
`DurableGraphSchemaHistory/`; `ContinuousIntegrationBuild=true` verifies that the current records
already exist without writing them. Configure this with `DurableGraphSchemaHistoryDirectory` and
`DurableGraphSchemaHistoryMode` (`Publish`, `Verify`, or `Off`). Keep `.dgschema` files under source
control. `Off` disables Publish/Verify but does not make legacy history valid. The removed
`DurableGraphSnapshotHistoryDirectory` property, the old default `DurableGraphSnapshots/` directory,
and legacy `.dgsnapshot` files are rejected rather than silently ignored or consumed.

## Generated Schema and state

Annotate each durable partial class with `[DurableType("example.character", 1)]`; the root of a
durable inheritance chain derives from `DurableBase`. This is the only generation mode. The
Generator emits exact Schema/history, readonly versioned state DTOs, raw Base/Delta body operations,
Capture, reader/model registration, Normalize, Allocate, and Hydrate. Unsupported type shapes or
field types are diagnostics rather than a metadata-only fallback.

The generated `Schema` property is the current exact Schema. `GetSchema(int version)` returns cached
metadata for versions 1 through the current version, including each version's exact historical base
chain; other versions throw `ArgumentOutOfRangeException`. Each declaration owns its own FieldId
space. `Schema.Fields` describes that declaration while `Schema.BaseSchema` binds its exact immutable
ancestor layout.

The assembly-internal nested `__DurableState` contains readonly structs `V1` through the current
version. Each DTO's static `Schema` property is the same cached exact definition returned by
`GetSchema(n)`. DTOs are regenerated from accepted `.dgschema` history and the current definition;
there is no separate DTO source history to maintain. Current and historical layouts support bool,
byte/sbyte, short/ushort, int/uint, long/ulong, char, Half, float, double, string, and supported
durable references. Reference fields become UInt32 ObjectId slots while their Schema type retains
String or its nominal durable target.

Each DTO physically flattens the exact ancestor chain into fields such as `Segment0Field1`: base
declarations first, then each declaration's fields in FieldId order. Schema metadata remains
segmented. Historical DTOs do not depend on old CLR base definitions remaining in source.

Inline structs are explicitly marked with their own `[DurableType("example.position", 1)]` on a
top-level non-generic `partial struct` (including `readonly partial struct`). They do not derive
from DurableBase. Each has Schema/history even if no class currently uses it, and every instance
field remains explicitly durable or transient. Supported scalar, string, durable-reference and
nested struct fields are recursively projected into unmanaged readonly DTOs; references become IDs.
The struct has no separate ObjectId, object row, root registration or StateModelBinding.

`SchemaKind.InlineValue` distinguishes these layouts from `ReferenceObject` definitions.
`DurableFieldInfo.InlineSchema` binds the exact nested value layout. A value version change requires
explicit owner version increases along inline/base dependencies; nominal target versions do not
propagate. A Schema family cannot switch kind. Shared generated value DTO/body helpers use exact
Schema identity independently of the current domain struct declaration. Removing an old struct
does not remove a retained owner's historical inline layout or require a value migration shell.
Owner upgrades explicitly construct nested DTOs, including target-typed `new`; the framework does
not run a second automatic struct upgrade chain.

Base and fused Delta bodies compose through static nested calls. A changed composite Delta slot
contains a nonempty child Delta, without another Schema header or length. Struct ref Hydrate starts
with a default temporary and fills private/readonly fields without running constructors or
initializers; Transient fields remain default. Field and array-element slots can use this same
helper, but array-object serialization is not yet supported. Current Capture/Hydrate bridges keep
domain and state types separate for future generic composition. Generic types, record/ref structs,
CLR nested type declarations, boxed identity and additional BCL value types remain unsupported.

Schema history/manifest and runtime Schema batches write format v2 and also read the previous
class-only v1 format. Already accepted history files are not rewritten.

For a scalar-only layout:

```csharp
var state = Character.__DurableState.Capture(character);
var prepared = Character.__DurableState.PrepareBaseBody(in state);

var reader = new BinaryPayloadReader(prepared.Body);
var restoredState = Character.__DurableState.ReadBaseBodyV1(ref reader);
reader.EnsureFullyConsumed();
```

`WriteBaseBody(ref writer, in Vn)` writes a raw Base body. `PrepareBaseBody(in Vn)` owns those bytes
for reuse. `PrepareDeltaBody(in prior, in current)` owns its change decision and raw Delta body;
`ApplyDeltaBodyVn` applies it to the exact same-version prior DTO. The public containers are
`PreparedBaseBody` and `PreparedDeltaBody`, and expose immutable bytes as `Body`. They contain
neither the StateStore Base type header nor the Storage ObjectVersion envelope.

The Serialization library is a transitive dependency. Its Reader/Writer constructors, scalar
operations, non-null string content codec, and boundary checks are public for generated-code
consumers. Char preserves a UTF-16 code unit; Half/float/double preserve their fixed-width bit
patterns. Failed reads return no partial DTO but may leave the Reader advanced; failed writes do
not roll back earlier output.

When a model advances, user code supplies any required adjacent single-object conversion:

```csharp
private static void UpgradeStateV1ToV2(
    in __DurableState.V1 oldValue,
    out __DurableState.V2 newValue) {
    newValue = new(oldValue.Segment0Field1, default);
}
```

Historical readers do not require an upgrade path, but loading an old DTO into the current editable
model does. Loading itself does not rewrite State; a surviving upgraded object is emitted as a new
Base by a later explicit Prepare.

## Capture and object-state rows

Layouts containing references capture through a `CaptureContext`. Generated `AddRoot` supplies the
exact model binding:

```csharp
var session = new CaptureSession();
using var capture = session.BeginCapture();
uint rootId = Character.__DurableState.AddRoot(capture, character);
var candidate = capture.Seal();
session.Accept(candidate); // Or session.Discard(candidate).
```

Keep the domain graph stable from registration through Seal. Capture uses CLR reference identity,
allocates nonzero UInt32 ObjectIds, freezes DTO values, and lists the complete candidate rows in
ascending ID order. `ObjectStateRecord` is the common immutable carrier for a durable exact DTO or
string content; its enclosing `CapturedGraph`, `DecodedRevision`, or normalized view supplies the
stage meaning. Empty strings normalize to `string.Empty`; distinct nonempty string instances retain
distinct identity.

`CaptureSession.Accept` installs only an in-memory candidate and its bindings. It is not State
append, Commit, or publication. Discard, failure, and disposing an unresolved context preserve the
accepted graph, although allocated numeric IDs can remain consumed.

## Typed StateStore path

Product loading registers generated readers and models with `StateReaderRegistry` and
`StateModelRegistry`. `RevisionDecoder` reconstructs a complete stored-exact DTO/string directory.
`LoadedWorld.Load` then validates, upgrades, allocates all reachable objects, and hydrates references
before exposing the single World root. It uses `RuntimeHelpers.GetUninitializedObject`; constructors,
field initializers, and Transient rebuild hooks do not run. User code rebuilds Transient state after
delivery.

For a new graph, use `LoadedWorld.PrepareNew`. For an explicitly selected Revision and WorldId, use
`LoadedWorld.Load`, mutate `World`, and call `Prepare`. Prepare may persist Schema registrations but
does not append State, publish a head, or advance the loaded Parent. The host appends the returned
`StateRevision` and reloads its exact address to establish the next baseline on this low-level path.
For continuous saves retaining the same domain instances, use `GraphRepository.Create/Load` and
`GraphSession.Commit`; it owns the published Parent, frozen baseline and instance identity bindings.

The Base type-header codec is internal to StateStore. Public generated bodies are raw;
`EncodedBaseObjectBody` brands the internal `[type header | raw Base body]` result so the typed
planner cannot omit or apply that header twice. Storage continues to accept opaque body bytes and
does not interpret this brand. Neither raw body access nor `StateRevisionStore.Append` constitutes
Commit or publication.

Changing an exact base binding requires an explicit version increase in the derived class and every
affected descendant. Accepted `.dgschema` history retains the old binding. Generator and publisher
validate complete, conflict-free ancestry. The Schema-history syntax is a build-time metadata format,
not the object-graph payload or runtime SchemaStore wire format.
