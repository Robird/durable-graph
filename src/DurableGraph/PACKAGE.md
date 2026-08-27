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

When a durable type advances beyond version 1, the Generator emits required private partial
adjacent handlers such as `UpgradeV1ToV2(in oldValue, out newValue)`. Generated deserialization
can read a known historical version, validate its exact Schema, run the static adjacent chain, and
return the current object. Loading does not rewrite stored state; only a later explicit Save does.
