# DB-071 compiled organization inventory

This standalone .NET 10 witness has no product or NuGet references. Supply one
directory containing the four real DurableGraph runtime DLLs and their Atelia
dependencies (for example, a package consumer's output). Each capture is a fresh
process and loads the supplied closure through a dedicated load context. Missing
Atelia dependencies and mixed old/new organization DLLs fail explicitly.

```powershell
dotnet build experiments/PackageConsumerProbe/OrganizationApiInventory/OrganizationApiInventory.csproj -v:q
$inventory = 'experiments/PackageConsumerProbe/OrganizationApiInventory/bin/Debug/net10.0/Atelia.OrganizationApiInventory.dll'
dotnet $inventory capture <old-consumer-output> obj/db071-baseline/api/old
dotnet $inventory capture <new-consumer-output> obj/db071-baseline/api/new
dotnet $inventory compare obj/db071-baseline/api/old/normalized.txt obj/db071-baseline/api/new/normalized.txt
```

`manifest.json` records actual loaded paths, identities and SHA-256 hashes;
`types.tsv` retains original metadata names, mapped names, visibility and
compiler-generated/nested flags. `normalized.txt` includes all types and declared
members, including internal/private types and compiler artifacts, assembly
reference names, base/interfaces, generic arity and constraints, visibility and
implementation flags, parameter names/defaults, constant values, custom modifiers
and custom attributes (including nullable and unmanaged metadata). Bodies, MVIDs,
assembly versions and assembly-level packaging attributes are intentionally not
compared; behavior and real package provenance need their separate witnesses.

Only the legacy capture applies the exact DB-071 assembly/namespace mapping.
The new capture uses actual names, so leaving a type in its old namespace fails.
Core source top-level counts exclude nested types, `<...>` compiler artifacts,
and non-DurableGraph compiler-support namespaces; all those types remain in the
complete comparison. Both captures require the approved 146 = 21 root + 13 Schema
+ 112 Runtime source type count. The full sorted comparison fails on any added or
removed record; its output shows each difference for independent review.
