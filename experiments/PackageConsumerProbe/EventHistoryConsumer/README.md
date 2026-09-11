# EventHistory package consumer

Run from the repository root:

```powershell
./experiments/PackageConsumerProbe/Run-EventHistoryProbe.ps1
# Or reuse an isolated feed from this source revision:
./experiments/PackageConsumerProbe/Run-EventHistoryProbe.ps1 -PackageSource <feed> -Version <version>
```

This DB-063 delivery witness has only the two public PackageReferences, no project/analyzer/import
wiring. The runner packs nine dependencies, publishes V1 Schema history, runs that process to
completion, then compiles and runs V2 with the same immutable history and repository. Both builds
also run the packaged history Verify target.

V1 writes `S0 -> E1 -> S1 -> E2`, leaving E2 pending. World contains Alice and Bob; Observed events
contain Alice twice, Alice has a self-cycle and shared string labels. E1 records score 2, S1 score 3,
and E2 score 4. Hot saves preserve the caller's World and transient cache.

V2 changes Alice's scalar representation and adds World.Generation through explicit adjacent
upgrades, preserving Alice's stable creation timestamp. That meaningful stable field also makes
the later one-field edit economically worth writing as Delta. From a fresh readonly open, a catalog containing only Observed and Alice reads E2;
World and Bob are not registered and World's Upgrade remains uncalled. ReadPair independently
returns E2 and S1 with their correct values and graph-local aliases. File content and modification
times remain unchanged by readonly operations; the API does not promise cross-view instance sharing.

Cold Resume(E2) returns preceding State and pending Event independently, without mutable Alice
aliases between them. Completing State forces World/Alice Base, then subsequent E/S steps verify
NoChange and ordinary Delta. Another pair validates old E2 after later State changes, and a final
State commit replaces the root while retaining its child instances.

`metrics.txt` records total RBF bytes and the selected State revisions' actual local Base/Delta
write counts. These observations are not physical read-I/O measurements or performance thresholds.
Fault injection, cross-store durability and branch movement are product-suite responsibilities.
