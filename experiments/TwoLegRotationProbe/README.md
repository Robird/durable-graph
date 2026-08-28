# Two-leg rotation probe

This isolated .NET 10/xUnit project is the in-memory workbench for exploring
two-leg StateStore file rotation and Base-or-Deltify policies.

The first scaffold deliberately models only a few container facts:

- RBF file numbers start at 1 and increase monotonically;
- an `RbfFile` only appends frames and can randomly read an existing frame by
  its provisional zero-based `FrameId`;
- an `RbfFileStore` creates files and retrieves them by file number;
- a `FileScope` has a readonly `CurrentFileNumber`, derives its
  `PreviousFileNumber`, and resolves relative parent IDs against the store;
- a built `Frame` owns a read-only `ObjectId` (`uint`) to `ObjectVersion` map;
- `ObjectVersion.ParentId` is nullable for a root, otherwise it is
  `(bool IsPreviousFile, FrameId)`;
- mutable `FrameBuilder` and `ObjectVersionBuilder` instances are copied into
  read-only built state.

This does not yet model byte offsets, `SizedPtr`, frame layout, publication,
Base/Delta payloads, relay revisions, rotation planning, or policy scoring.
`ParentId` is interpreted relative to the file containing it: stepping creates
a new file and a new `FileScope`; an old frame must still be read with the scope
of its own origin file. This pair is an in-memory precursor, not a proposed
durable ticket encoding.

Run from the repository root:

```powershell
dotnet test experiments/TwoLegRotationProbe/TwoLegRotationProbe.csproj
```
