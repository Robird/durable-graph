# Snapshot upgrade shape probe

This experiment fixes the C# facts behind the provisional Snapshot and upgrade
handler design. It verifies that:

- ordinary structs can flow through direct strongly typed `in`/`out` calls;
- assigning every visible target field satisfies definite assignment, while
  omitting one field reports `CS0177`;
- assigning the whole output to `default` bypasses that field-by-field tripwire;
- a `bool` false path must still assign the `out` value;
- an explicitly accessible partial method without an implementation reports
  `CS8795`;
- return types alone cannot distinguish overloads (`CS0111`);
- overloads distinguished by the `out` target type work, but `out var` is
  ambiguous (`CS0121`);
- a managed Snapshot containing `string` cannot be used as `stackalloc` element
  storage (`CS0208`).

Run the probe from the repository root:

```powershell
pwsh -File experiments/SnapshotUpgradeShapeProbe/Run-Probe.ps1
```

This is a language-semantics experiment, not a performance measurement. It does
not claim that ordinary struct locals are always placed on the stack or that the
current boxed StateStore becomes allocation-free.
