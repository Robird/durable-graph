# Source Generator history feedback probe

> Completed mechanism witness; rerun when investigating compiler output/history feedback
> or changing the assumptions behind the post-compile hook. Product integration now lives in
> [build targets](../../src/DurableGraph/build/Atelia.DurableGraph.targets) and
> [SchemaHistoryTool](../../src/DurableGraph.Build/SchemaHistoryTool.cs), with delivery
> regression in [PackageConsumerProbe](../PackageConsumerProbe/README.md).
> This isolated negative control remains useful; its local protocol is not a product backlog.

This isolated experiment distinguishes two mechanisms:

1. `Generator.AddSource` adds source only to the current compilation. Even when
   compiler-generated files are written under `obj`, they do not become a later
   build's `AdditionalFiles` automatically.
2. An explicit, opt-in MSBuild target can copy a comment-only generated snapshot
   candidate after a successful `CoreCompile`. A later build can then consume
   that external file through `AdditionalFiles` and regenerate historical,
   strongly typed Snapshot classes.

Run the complete negative-control, feedback, clean-rebuild, and idempotence
matrix from the repository root:

```powershell
pwsh -File experiments/SourceGeneratorHistoryProbe/Run-Probe.ps1
```

The runner keeps evidence under the ignored `artifacts/` directory. The probe
protocol and build hook are deliberately local to this experiment. They are not
the DurableGraph history format, persistence authority, or production build
integration.

The probe clears compiler-generated staging before each controlled compilation
so a failed build cannot be published by a later successful build. Consequently,
publishing must be enabled for the build that actually runs compilation; turning
it on only after an up-to-date build requires an explicit rebuild.
