# Two-leg rotation probe

This isolated .NET 10/xUnit project is the in-memory workbench for exploring
two-leg StateStore file rotation and Base-or-Deltify policies.

The first scaffold deliberately models only a few container facts:

- RBF file numbers start at 1 and increase monotonically;
- an `RbfFile` only appends frames and can randomly read an existing frame by
  its provisional zero-based frame number;
- an `RbfFileStore` creates files and retrieves them by file number;
- a `FileScope` exposes mutable `CurrentFileNumber` state and derives its
  read-only `PreviousFileNumber`;
- `Frame` and `ObjectVersion` are empty placeholders.

This does not yet model byte offsets, `SizedPtr`, frame layout, publication,
ObjectVersion chains, Base/Delta payloads, relay revisions, rotation planning,
or policy scoring. The zero-based frame number is only an in-memory collection
key and is not a proposed durable address.

Run from the repository root:

```powershell
dotnet test experiments/TwoLegRotationProbe/TwoLegRotationProbe.csproj
```
