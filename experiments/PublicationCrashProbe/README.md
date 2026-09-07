# Publication process-stop witness

DB-036 G0 substrate experiment. Run `./Run-Probe.ps1`; use `-NoBuild` only after
building this project. It is intentionally outside the product solution.

The controller starts a separate .NET writer and kills it after a checkpoint
without running its disposal code. A fresh process checks the entire State and
publication files, including frame CRCs and scanner termination. Checkpoints cover
an empty repository, State flushed before publication, publication appended before
flush, and publication flushed. The State writer rolls over to a new segment before
the second publication and flushes the exact append lease. A separate injected
truncated publication tail must fail to open; it must never select an earlier head.

The two-byte ordinal payload is only a substrate witness, not a product wire format.
After-append observation proves process-local OS visibility on the tested platform;
it does not establish a durability barrier. This experiment covers process kill and
normal operating-system behavior, not OS crash, power loss, storage-device failure,
or durable directory metadata. Artifacts remain beneath ignored `obj/run-*`.
