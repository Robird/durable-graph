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

The probe packs a unique local package, uses an isolated package cache and snapshot-history
directory under this experiment's ignored `obj` directory, and exercises local publish plus
CI-style read-only verification. Its V2 executable also saves a manual V1 boxed record, loads it
twice through the packaged generated serializer without writeback, explicitly saves V2, and then
confirms the current-version path no longer invokes the upgrade handler.

The feed also contains the runtime's Serialization dependency. A final SchemaOnly consumer opts
into `GenerateBinaryBody`, captures private base/derived fields into a readonly versioned DTO,
then mutates the domain instance. Static DTO byte calls verify the original golden bytes,
DTO/Schema pairing and domain isolation. It still has just one PackageReference;
Serialization is supplied transitively, with no friend access or manual analyzer wiring.

The same consumer also exercises every added scalar kind through generated Capture/DTO bodies,
including an isolated surrogate, negative zero and NaN payloads against fixed golden bytes.
This validates the public primitive API and publication of the extended Schema tags.

The final consumer also captures two concrete roots with shared strings, distinct equal strings,
null/empty/surrogate content, and private base fields through generated AddRoot adapters. It checks
the closed ID DTO list, static ID-body golden bytes, mutation isolation, accept/discard, stable live
IDs, discarded-number consumption, and fresh IDs after retirement. These use public runtime seams
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
