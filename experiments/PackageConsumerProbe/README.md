# Package consumer probe

This experiment verifies the reusable delivery boundary rather than project-to-project wiring.
The consumer project has one `PackageReference` to `Atelia.DurableGraph`; it contains no manual
analyzer reference, `AdditionalFiles`, build hook, or `Import`.

Run from the repository root:

```powershell
./experiments/PackageConsumerProbe/Run-Probe.ps1
```

The probe packs a unique local package, uses an isolated package cache and snapshot-history
directory under this experiment's ignored `obj` directory, and exercises local publish plus
CI-style read-only verification. Its V2 executable also saves a manual V1 boxed record, loads it
twice through the packaged generated serializer without writeback, explicitly saves V2, and then
confirms the current-version path no longer invokes the upgrade handler.

The feed also contains the runtime's Serialization dependency. A final SchemaOnly consumer opts
into `GenerateBinaryBody`, exercises private base/derived fields with direct static byte calls,
and checks golden bytes and transient preservation. It still has just one PackageReference;
Serialization is supplied transitively, with no friend access or manual analyzer wiring.
