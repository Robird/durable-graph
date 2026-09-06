# Atelia.DurableGraph

DurableGraph is an exploratory .NET 10 prototype for versioned durable object graphs.
Its public API, snapshot-history format, and build workflow are not stable yet.

A direct package reference supplies the runtime library, Source Generator, and the current
snapshot-history build integration. By default, each successful local `CoreCompile` appends exact
snapshot metadata under `DurableGraphSnapshots/`; a later unrelated build phase can still fail.
Builds with `ContinuousIntegrationBuild=true` verify that the current metadata is already present
without writing it.

The history directory and mode can be configured with `DurableGraphSnapshotHistoryDirectory`
and `DurableGraphHistoryMode` (`Publish`, `Verify`, or `Off`). Keep generated `.dgsnapshot` files
under source control. `Off` is intended only for diagnostics and isolated experiments because it
removes the automatic history gate.

In the default serializer mode, when a durable type advances beyond version 1, the Generator emits required private partial
adjacent handlers such as `UpgradeV1ToV2(in oldValue, out newValue)`. Generated deserialization
can read a known historical version, validate its exact Schema, run the static adjacent chain, and
return the current object. Loading does not rewrite stored state; only a later explicit Save does.

For schema metadata without a serializer, opt in with `[DurableType("example.base", 1, SchemaOnly = true)]`.
This mode supports a same-compilation inheritance chain of top-level, non-generic partial classes,
including abstract classes. Every domain class in the chain must opt in, with the root deriving
directly from `DurableBase`. Each declaration owns its own FieldId space. `Schema.Fields` describes
that declaration; `Schema.BaseSchema` binds its exact immutable ancestor layout.

The Generator supplies `Schema` and `GetSchema(int version)` in this mode. The latter returns cached
metadata for versions 1 through the current version, including the historical base chain; other
versions throw `ArgumentOutOfRangeException`. It does not generate a `Serializer`, payload snapshots,
or upgrade handlers. Supported field kinds are bool, byte/sbyte, short/ushort, int/uint,
long/ulong, char, Half, float, double, and string.

For versioned state DTOs and binary bodies, additionally set `GenerateBinaryBody = true` on every
class in a SchemaOnly domain chain. This provisional slice accepts the 13 scalar kinds listed
above and string in current and historical layouts, including ancestor fields. String fields
become uint reference slots in DTOs; their Schema type remains String. The assembly-internal nested `__DurableBinaryBody`
contains readonly structs `V1` through the current version, each paired with `GetSchema(n)` by
its static `Schema` property. DTOs are regenerated from accepted `.dgsnapshot` history and the
current definition; there is no separate DTO source history to maintain.

Each DTO physically flattens the exact ancestor chain into fields such as `Segment0Field1`:
base declarations first, then each declaration's fields in FieldId order. Schema metadata remains
segmented. Historical DTOs do not depend on old CLR base definitions remaining in source.
For scalar-only current layouts, `Capture(T value)` copies current domain fields into the current DTO,
using the base class's Capture for its private fields. Layouts containing string instead require
`Capture(T value, CaptureContext context)`, called during a session's Seal operation. Transient fields
are omitted. The caller supplies a stable view during capture; later domain mutations cannot change
the captured values.

`Write(ref BinaryPayloadWriter writer, in Vn state)` overloads encode DTO values directly;
`ReadVn(ref BinaryPayloadReader reader)` returns a completed DTO. For example, in the consumer assembly:

```csharp
// For a scalar-only Character layout:
var state = Character.__DurableBinaryBody.Capture(character);
Character.__DurableBinaryBody.Write(ref writer, in state);
var restoredState = Character.__DurableBinaryBody.ReadV1(ref reader);
reader.EnsureFullyConsumed();
```

Select the ReadVn matching the stored layout. There are no domain-object Read/Write overloads.

The Serialization library is a transitive package dependency. Its Reader/Writer constructors,
all 13 scalar operations, and Reader boundary checks are public for generated-code consumers.
Char uses canonical UInt16 encoding of a UTF-16 code unit, including isolated surrogates.
Half/float/double use fixed-width little-endian bytes preserving negative zero and NaN payload bits.
Bodies do not restore domain instances, dispatch on runtime types/stored schemas, upgrade DTOs,
or encode a type/version header. GetSchema remains a metadata query; callers explicitly choose
the typed body. Failed ReadVn may leave the Reader advanced, but returns no partial DTO; a failed
Write does not roll back prior output. Capture rejects null before reading fields. The final payload
boundary is caller-owned. This is not yet the graph Save/Load API or StateStore baseline cache.

For a complete in-memory capture of scalar/string roots, concrete classes also receive an internal
`AddRoot(CaptureContext, T?)` adapter. It pairs the exact concrete domain type, current Schema,
DTO and Capture function. In the same consumer assembly:

```csharp
var session = new CaptureSession();
using var capture = session.BeginCapture();
uint rootId = Character.__DurableBinaryBody.AddRoot(capture, character);
var candidate = capture.Seal();
session.Accept(candidate); // Or session.Discard(candidate).
```

AddRoot registers identity; Seal reads the fields. Keep the domain view stable from registration
through Seal. Multiple concrete roots share one context. Root IDs are allocated before strings are
discovered, in root order and then base-first/FieldId order. Repeated/null roots remain in RootIds,
with zero representing null; Objects is the complete immutable live list ordered by ID. Equal-content
strings with different CLR identities remain separate string entries. A string entry exposes its
immutable StringContent; a durable entry exposes its exact Schema and `GetState<TState>()` by value.
The stored DTO box is private, and `TState : unmanaged` prevents managed references in DTO slots.

The session admits one pending capture, including a sealed candidate awaiting accept/discard.
Accept installs that exact candidate without rereading the domain. Discard, Dispose, and capture
failure preserve the accepted parent; allocated nonzero uint IDs remain consumed. Continuously live
instances keep their IDs, while instances retired by Accept lose their bindings and receive fresh
IDs if reintroduced. Exhaustion throws rather than wrapping. Monotonic allocation is local to this
in-memory session, with no reopen/import or ID recycling. Retaining an old CapturedGraph does not
retain its mutable source objects. Disposing an unresolved context discards its candidate.

The public generic AddRoot method is a generated-code seam: hand-written callers must pair Schema,
DTO and callback correctly. Passing a derived instance to a base root binding is rejected; base
segment Capture remains valid on derived instances. No global registry or automatic polymorphic
root dispatch is provided. Reference bodies write/read UInt32 IDs only; ReadVn does not resolve or
validate their targets. This slice does not decode string objects, restore domain objects, handle
Durable reference fields/cycles, or perform StateStore Save.

Changing an exact base binding requires an explicit version increase in its derived class and then
in each affected descendant. Accepted `.dgsnapshot` history retains the old base binding. Generator
and publisher both validate that history has a complete, conflict-free ancestor chain. History v1
records may include a canonical `// base:<base64-schema-id>|<version>` line after the version line;
records without a base retain their existing representation. This is a provisional metadata format,
not the object graph payload format.

Schema TypeTags 1–4 retain their meanings; the new scalar tags occupy 5–14. History text syntax
is unchanged, but older tools reject new tags: update the runtime, Generator and bundled history
tool together by updating the package. These tags do not define the future graph TypeCodec.
