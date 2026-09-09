# ListDeltaReplayProbe active context

Purpose: replay the same ordinary domain edits into independent DurableGraph repositories and
compare codec-2 List writers under the same policy, with correctness ahead of performance ranking.

Current focus: ordinary replay and the two white-box adverse families have passed. The window-33
and edit-distance-130 cases expose different failures; positional fallback then causes large
element Delta preparation allocations that a later Base choice does not reclaim. Boundary
controls distinguish the causes. LocalResync remains the default and Myers remains selectable.
Evidence and limits live in [WHITEBOX.md](WHITEBOX.md) and the ordinary matrix [RESULTS.md](RESULTS.md).
Commands, measurement boundaries and workload inventory live in [README.md](README.md).

Selected invariants: exact same script and initial content per case; repository-local ObjectIds
never address cross-run edits; one Session per trace; all persisted revisions and all prepared
candidate Deltas validate; no writer selector needed by readers; algorithm order interleaved and
warmup/initial binding recorded separately. Whole Commit includes all product work and flushes.

Output is ignored per-run JSON/CSV/Markdown plus independent repositories. Root product context is
[src/PROJECT-STATE.md](../../src/PROJECT-STATE.md); design/acceptance authority is
[DB-049](../../docs/design-branches/0049-list-range-delta-and-matcher-trial-slice.md).

Next: retain both white-box families and ordinary traces when evaluating fallback behavior;
use representative application traces before changing defaults or tuning budgets; compare
whole Commit and isolated Diff rather than assigning all overhead to flush. Larger traces,
allocation profiling and segment-level code competition
remain optional follow-ups triggered by representative results, not prerequisites.
