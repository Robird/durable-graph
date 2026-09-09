# List Delta domain-history replay

DB-049's current-product experiment compares Position, LocalResync and BoundedMyers under one
List codec. It references product projects and the real source generator, and copies no matcher.
The first completed matrix and current default decision are recorded in [RESULTS.md](RESULTS.md).
Two white-box adverse families and boundary controls are described in [WHITEBOX.md](WHITEBOX.md).
The executable uses Runtime/StateStore test friendship only for obtaining closed element operations
and directly measuring their List body implementation. This is not a package delivery test.

Run from the repository root, with no competing builds or benchmark processes:

```powershell
# Small complete correctness smoke: 5 workloads, all algorithms, 14 edits plus initial commit.
./experiments/ListDeltaReplayProbe/Run-Probe.ps1

# Representative three-scale run, including repeated fresh repositories and interleaved writers.
./experiments/ListDeltaReplayProbe/Run-Probe.ps1 -Counts '32,512,4096' -Repeats 3 -DiffRepeats 5

# A larger targeted repetition; avoid an unrestricted largest-width/count Cartesian product.
./experiments/ListDeltaReplayProbe/Run-Probe.ps1 -Counts '8192' -Repeats 3 -Rounds 2 -DiffRepeats 5

# Two white-box families: local window and global edit-depth boundaries, with controls.
./experiments/ListDeltaReplayProbe/Run-Probe.ps1 -Suite whitebox -Counts '4096,16384' -Repeats 3 -DiffRepeats 31
```

The runner builds Release unless `-NoBuild` is supplied. `-Output` must name a new directory;
existing repositories are never adopted, deleted or reused. Defaults retain artifacts below ignored
`obj/`. The representative run executes 1,890 measured Commit calls and 60 warmup Commit calls;
budget several minutes depending on flush latency and historical-chain verification. The smoke
executes 225 measured plus 60 warmup Commit calls. No timeout changes the algorithms or pass criteria.
Count accepts 8–50,000, Repeats 1–20, Rounds 1–20, DiffRepeats 1–100. `-Seed`,
`-ReadAmplification` (X, default 8) and `-BaseBudgetPercent` (Y, default 5) are recorded unchanged
for every algorithm. The widest inline workload caps count at 512 and deduplicates capped sizes.

`-Suite ordinary` is the default. `-Suite whitebox` defaults to Count 4096, requires Count >= 512
and Rounds = 1, and uses deterministic single-edit histories without Seed. It runs five cases
(two adverse cases and three controls), each in its own repository, plus 15 warmup repositories.
It reuses the same Commit, historical Load, candidate Apply and frozen-input checks. In addition,
`whitebox.json` records instrumented comparison counts, known optimal unchanged-pair counts,
independent relaxed-bound diagnostics and isolated matcher time/allocation; `whitebox.md` summarizes
them. Counters are excluded from timing. Saved data always uses the unmodified product budgets.
See WHITEBOX for the run with tiered compilation disabled to reduce JIT phase interference.

Workloads and script:

- Unique integers and alternating integers.
- Repeated `Node` references and null slots; a pool preserves all nodes, including a reference
  cycle. A child-only edit changes node content while List ObjectId slots remain unchanged.
- Generic `Cell<int>` with a sparse field change.
- Larger nested `Wide<Cell<int>>` with eight stable long fields; a micro-change touches its nested
  value, including the difficult case where every element changes a little before an insertion.

Every round includes head/middle insertion/deletion, tail append/removal, scattered insertion/deletion,
sparse updates, rotation, reverse, micro-change-plus-insert, an undone edit and child-only update.
The script is generated before runs with a fixed seed and domain test IDs/positions. It never uses
repository-assigned ObjectIds to address edits. Reference cases intentionally contain duplicates;
their deletions use deterministic positions rather than pretending references have unique keys.

Each workload/scale/repetition/algorithm gets a fresh independent repository and domain graph.
One GraphSession performs its complete trace; no reopen happens between measured Commit calls.
All first binding/JIT/publication warmups are separate. Algorithm order rotates between workloads,
scales and repetitions. Edits and fingerprints are outside timing; the complete synchronous Commit
includes Capture, PrepareBase, planner chain reads, append and publication flush.

After closing the writer, fresh readers materialize every historical Revision and verify complete
content/order/count, shared List identity, repeated children and cycles. An additional Repository
reopen/load verifies the latest graph without selecting the old writer algorithm. This is cold
handles/bindings, not a cleared OS page cache. Its lower-priority metric includes repository integrity
checks and writable-open flushes; do not describe it as raw decoder throughput.

The exact frozen states read from consecutive revisions also enter direct product PrepareDelta.
Reflection closes the generic test entry outside timing; one untimed call precedes repeated samples.
Every candidate is Apply-checked even when the storage policy selected Base, and both inputs are
checked against their original Base encodings. StateEquals, HasChanges and reconstructed bytes must
agree. This local measurement excludes Capture/Base preparation by design and supplements rather
than replaces the whole-Commit measurements.

Reports:

- `report.json`: environment, baseline and compiled assembly hashes, policy, matcher budgets,
  generated script, warmup/setup/initial publication, individual observations and median/min/max
  across fresh runs. Allocation is current-thread managed allocation, not peak memory or all-thread
  allocation. Matcher limits are read from product constants; the current comparison-budget formula
  is documented alongside the binary fingerprints.
- `steps.csv`: per-step Commit and isolated Diff samples, candidate sizes, actual Base/Delta counts
  and actual decoded ObjectVersion payload bytes, plus List-specific write kind/bytes.
- `summary.md`: concise comparison. Correctness is the only automatic gate; no performance winner
  or maximum cold-read latency is required.

Object payload bytes exclude ObjectId, membership and shared frame overhead. State, Schema and
publication file sizes are separately reported in each RunResult. Candidate Delta sizes include
NoChange bodies as preparation observations. Undo and child-only edits must leave List slot state
unchanged and cannot produce a List Delta; the policy may still choose an ordinary Base rewrite
to reduce read amplification, and that write remains included in the actual-byte report.
Flush variability and full-graph work can obscure matcher differences; preserve that observation.
This is a bounded synthetic study, not a universal workload ranking or a peak-memory profiler.

Current research context lives in [PROJECT-STATE.md](PROJECT-STATE.md); product authority and acceptance
remain in [DB-049](../../docs/design-branches/0049-list-range-delta-and-matcher-trial-slice.md).
