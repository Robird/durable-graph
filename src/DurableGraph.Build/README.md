# DurableGraph schema history tool

This private .NET 10 console tool is intended for the DurableGraph package's
MSBuild integration. It validates the comment-only manifest emitted by the
Source Generator and either publishes exact schema-history files or verifies
that the current manifest candidates are already present.

```text
DurableGraph.Build publish --manifest <generated.g.cs> --schema-history <directory>
DurableGraph.Build verify  --manifest <generated.g.cs> --schema-history <directory>
```

The manifest format is strict:

```csharp
// durable-graph-schema-history-manifest:6
// schema-begin
// schema-id-base64:cHJvYmUuY2hhcmFjdGVy
// version:1
// kind:1
// arity:0
// field:1|4
// field:2|18|q(b2)
// schema-end
```

It may contain zero or more schema-history record blocks. Field IDs must be positive,
unique, and sorted. Type tags `1` through `14` retain their existing meanings
and two-column field records. Current format v6 uses canonical type patterns:

- Tag `15`: a third column with the nominal named/array/List reference pattern; no target version.
- Tag `16`: an inline named pattern followed by its fixed historical version.
- Tag `17`: a declaration-scoped type parameter, such as `p0`.
- Tag `18`: a Nullable pattern `q(child)`. A named child requires its fixed inline version
  as the fourth column; scalar and parameter children have no version column.

For example, `// field:2|18|q(b2)` is `int?`; `// field:3|18|q(nUG9pbnQ=())|1`
retains the exact Point V1 dependency. Nullable does not have its own definition version.
Arrays use `a1(...)` through `a4(...)`, Lists use `l(...)`, and named definitions use
`n<canonical UTF-8 Base64 ID>(arguments)`. These constructors compose recursively within
the supported CLR shape constraints. Nullable cannot directly wrap references or another Nullable.

Accepted formats v1–v5 remain readable with their original grammar and immutable filenames/hashes.
Nullable patterns are valid only in v6, including when nested inside generic or collection arguments.
Legacy `.dgsnapshot` headers and record markers are rejected.

The Source Generator validates the Schema shapes needed to compile generated code. The Build tool is
the canonical file authority: after `CoreCompile` it additionally rejects duplicate keys,
noncanonical filenames, BOM/CRLF bytes, and other representations that cannot be published as exact
history. A project build succeeds only when both layers accept the inputs.

Each checked-in `*.dgschema` uses the same single block with the header
`// durable-graph-schema-history:6` for newly published records. History is canonical UTF-8 without a byte-order
mark, uses LF line endings, and ends in one LF. Its filename contains full
SHA-256 hashes of the decoded SchemaId and canonical history content, plus the
version; a SchemaId never becomes a path component.

Publishing validates the entire manifest and all existing history before it
writes. Exact existing schema-history records are left untouched. New records are written
to a temporary file in the history directory and moved into place without
overwrite. The prototype assumes one writer; publishing several files is not a
transaction.
