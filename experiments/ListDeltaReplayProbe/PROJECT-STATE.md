# ListDeltaReplayProbe active context

Purpose: replay the same ordinary domain edits into independent DurableGraph repositories and
compare codec-2 List writers under the same policy, with correctness ahead of performance ranking.

Current focus: [DB-051](../../docs/design-branches/0051-bounded-list-delta-competition.md) is the reviewed
next-turn construction plan, not implemented: full Local incumbent plus byte-limited Myers competition,
with independent search budgets, then Adaptive as product default. The existing fallback candidates
remain research evidence and must not be promoted unchanged. Current product default remains LocalResync.
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

Next: implement DB-051 with product writer vs standalone matcher enumeration kept distinct, add
Adaptive body/persistence measurements and retain the historical three/five-strategy evidence.
Its limited encoder must preserve incumbent bytes and discard incomplete alternatives. Pooling,
element-level interruption and region-byte reuse remain later profile-driven work.
