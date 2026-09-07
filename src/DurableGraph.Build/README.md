# DurableGraph snapshot history tool

This private .NET 10 console tool is intended for the DurableGraph package's
MSBuild integration. It validates the comment-only manifest emitted by the
Source Generator and either publishes exact history files or verifies that the
current snapshots are already present.

```text
DurableGraph.Build publish --manifest <generated.g.cs> --history <directory>
DurableGraph.Build verify  --manifest <generated.g.cs> --history <directory>
```

The manifest format is strict:

```csharp
// durable-graph-snapshot-manifest:1
// snapshot-begin
// schema-id-base64:cHJvYmUuY2hhcmFjdGVy
// version:1
// field:1|4
// snapshot-end
```

It may contain zero or more snapshot blocks. Field IDs must be positive,
unique, and sorted. Type tags `1` through `14` retain their existing meanings
and two-column field records. Tag `15` is a durable reference and requires a
third column containing its nominal target SchemaId in canonical UTF-8 Base64:
`// field:2|15|cHJvYmUuaXRlbQ==`. It binds a family, without a target version
or an exact target Schema dependency. Other tags prohibit a third column.
This extends the vocabulary under the existing headers; older strict parsers
reject reference records. Existing scalar/string history bytes and hashes stay unchanged.

Each checked-in `*.dgsnapshot` uses the same single block with the header
`// durable-graph-snapshot:1`. History is canonical UTF-8 without a byte-order
mark, uses LF line endings, and ends in one LF. Its filename contains full
SHA-256 hashes of the decoded SchemaId and canonical history content, plus the
version; a SchemaId never becomes a path component.

Publishing validates the entire manifest and all existing history before it
writes. Exact existing snapshots are left untouched. New snapshots are written
to a temporary file in the history directory and moved into place without
overwrite. The prototype assumes one writer; publishing several files is not a
transaction.
