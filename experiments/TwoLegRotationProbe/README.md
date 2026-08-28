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
- a built `Frame` owns a read-only `ObjectId` (`uint`) to `ObjectVersion` map;
- `ObjectVersion.ParentId` is the nullable, provisional zero-based number of
  the parent frame in the same simulated RBF file;
- mutable `FrameBuilder` and `ObjectVersionBuilder` instances are copied into
  read-only built state.

This does not yet model byte offsets, `SizedPtr`, frame layout, publication,
cross-file parent addresses, Base/Delta payloads, relay revisions, rotation
planning, or policy scoring. The zero-based frame number and `ParentId` are only
in-memory collection keys and are not proposed durable addresses.

Run from the repository root:

```powershell
dotnet test experiments/TwoLegRotationProbe/TwoLegRotationProbe.csproj
```
