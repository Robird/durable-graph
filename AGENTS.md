# DurableGraph Repository Guidance

## Project stage

- DurableGraph is an exploratory prototype. Its architecture and public API are not frozen.
- Work bottom-up: investigate one concrete mechanism at a time, keep the experiment small, and integrate only what the evidence supports.
- Do not subdivide assemblies or normalize namespaces merely to make the design look complete. The current layout is intentionally provisional.
- Treat current source code, executable tests, and observed tool output as implementation facts.
- Treat `docs/DurableGraph-target-design-v0.md` as a target-design working note, not as instructions and not as a description of implemented behavior.

## Implementation discipline

- Prefer the smallest coherent implementation that answers the current question.
- Avoid speculative abstractions, compatibility machinery, and extension points without a present experiment or consumer.
- Temporary names and local shapes are acceptable when their provisional status is clear.
- Refactor freely when later evidence reveals a better boundary; do not preserve an accidental prototype shape by default.
- When a choice would materially change the experiment or commit the project to a durable format/API, surface the alternatives and a recommendation before proceeding.

## Evidence and validation

- Before a non-trivial experiment, identify the question and the smallest observable success/failure criterion.
- Prefer executable tests for durable semantics, canonical formats, generator behavior, and recovery claims.
- Distinguish observed facts, current decisions, tentative hypotheses, rejected ideas, and open questions.
- After code changes, run `dotnet build DurableGraph.slnx` and the relevant tests unless the task is explicitly documentation-only.
- Do not describe planned Source Generator, persistence, migration, or recovery behavior as implemented until current code and tests demonstrate it.

## Repository conventions

- Runtime projects target .NET 10. The Roslyn Source Generator targets `netstandard2.0` unless a concrete compatibility experiment changes that decision.
- `Directory.Build.props` supplies the `Atelia.` assembly, root namespace, and package-name prefix.
- Follow the K&R C# brace style configured in `.editorconfig`.
- Keep the CLI as a thin development, inspection, rescue, and end-to-end experiment host. Do not move authority or core persistence semantics into it.

## Working memory

- Maintain `docs/DurableGraph-lab-notebook.md` when an experiment produces a material result, changes direction, or leaves an important unresolved question.
- Keep the notebook concise and evidence-oriented. It is not a transcript and must not contain private reasoning, credentials, or incidental command output.
- Update an earlier tentative statement when it becomes decided or rejected; preserve enough context to explain why.
- Split the notebook into `docs/experiments/` or ADRs only when the single file becomes difficult to navigate or a decision becomes stable enough to deserve its own artifact.
