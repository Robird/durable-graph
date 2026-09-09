# ListDeltaReplayProbe active context

Purpose: replay the same ordinary domain edits into independent DurableGraph repositories and
compare codec-2 List writers under the same policy, with correctness ahead of performance ranking.

Current focus: complementary fallback research has passed correctness and two-seed measurements.
Local-first single rescue fixes both known failures and preserves ordinary-trace bytes, but
successful rescue can replace tiny inline patches with wide literals. It is the preferred research
candidate, not a new public/default writer. Next is byte-cost acceptance of alternative pairings.
Evidence, limits and reproducible commands live in [FALLBACK.md](FALLBACK.md); prior causal examples
and real repository replay remain in [WHITEBOX.md](WHITEBOX.md) and [RESULTS.md](RESULTS.md).
Commands, measurement boundaries and workload inventory live in [README.md](README.md).

Selected invariants: exact same script and initial content per case; repository-local ObjectIds
never address cross-run edits; one Session per trace; all persisted revisions and all prepared
candidate Deltas validate; no writer selector needed by readers; algorithm order interleaved and
warmup/initial binding recorded separately. Whole Commit includes all product work and flushes.

Output is ignored per-run JSON/CSV/Markdown plus independent repositories. Root product context is
[src/PROJECT-STATE.md](../../src/PROJECT-STATE.md); design/acceptance authority is
[DB-049](../../docs/design-branches/0049-list-range-delta-and-matcher-trial-slice.md).

Fallback mode instead uses captured pairs and raw body validation, with no publication or cumulative
policy simulation. It shares kernels/body with the product, and the experimental candidates remain here.

Next: compare candidate pairings by byte cost as well as matching success; retain counterexamples,
both white-box families and ordinary traces when evaluating fallback behavior;
use representative application traces before changing defaults or tuning budgets; compare
whole Commit and isolated Diff rather than assigning all overhead to flush. Larger traces,
allocation profiling and segment-level code competition
remain optional follow-ups triggered by representative results, not prerequisites.
