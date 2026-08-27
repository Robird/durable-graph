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
CI-style read-only verification.
