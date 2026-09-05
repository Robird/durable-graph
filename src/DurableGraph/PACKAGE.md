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
or upgrade handlers. The supported field kinds remain bool, int, long, and string.

For a current-layout binary body, additionally set `GenerateBinaryBody = true` on every class
in a SchemaOnly domain chain. This provisional slice accepts only bool, int, and long durable
fields; string is rejected until reference-identity encoding is available. It generates the
assembly-internal nested `__DurableBinaryBody` with static `Write(ref BinaryPayloadWriter, T)`
and `Read(ref BinaryPayloadReader, T)` methods. These call the byte primitives directly, handling
base declarations first and each declaration's fields in FieldId order. Transient fields are untouched.

The Serialization library is a transitive package dependency. Its Reader/Writer constructors,
bool/int/long operations, and Reader boundary checks are public for generated-code consumers.
The body reads into a caller-provided instance and covers the current declared layout and ancestors;
it does not allocate objects, dispatch on runtime types or stored schemas, or encode a type/version header.
`GetSchema(oldVersion)` still returns metadata only: it does not select a historical binary body.
Callers must supply the matching layout, a stable source or unpublished target, and check the final
payload boundary with `EnsureFullyConsumed()`. Read/write failure may leave earlier fields/bytes changed;
null targets/sources are rejected before I/O. This is not yet the graph Save/Load API.

Changing an exact base binding requires an explicit version increase in its derived class and then
in each affected descendant. Accepted `.dgsnapshot` history retains the old base binding. Generator
and publisher both validate that history has a complete, conflict-free ancestor chain. History v1
records may include a canonical `// base:<base64-schema-id>|<version>` line after the version line;
records without a base retain their existing representation. This is a provisional metadata format,
not the object graph payload format.
