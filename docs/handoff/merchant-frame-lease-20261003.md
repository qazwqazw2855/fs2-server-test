# Merchant dispatch within a frame lease — 2026-10-03

- Network suite: 111 passed, 0 failed, 0 skipped.
- Existing real MariaDB command integration test: 1 passed; fixture cleanup succeeded.
- PurchaseInLeaseAsync and SellInLeaseAsync validate lease ownership and disposal state.
- Both dispatch paths retain interaction checks and cancellation handling.
- Dispatch does not reacquire or dispose the caller's lease.
- Caller must await each operation before disposing; concurrent sharing is unsupported.
- Tests use recording writers for the new lease entry points.
- Existing DB test still exercises the ordinary command entry points.
- Full six-suite verification was not rerun; progress.json retains 401 from the previous run.
- Merchant TCP packet dispatch and formal content approval remain incomplete.
- Production 6001 was not deployed; no Client acceptance performed.

## Durable dispatch and full verification

- Tested source: 1f1c98c706e5f0708da7e00ade57d0901db0408a.
- Six suites: 405 passed, 0 failed, 0 skipped; DB integration enabled.
- Core 7, Application 114, Session 20, Protocol 111, Network 111, Persistence Integration 42.
- All fixture runners reported successful cleanup.
- Supersedes the earlier gap: the DB command test now dispatches through held frame leases.
- Durable purchase, sale and fresh-writer replay passed.
- Out-of-range and closed/reopened interaction checks retain rejection behavior.
- Wallet balance: 100 -> 60 -> 64; inventory and wallet versions: 2.
- Two transaction receipts remain; lane cleanup completes after lease release.
- World identity, merchant listing and approval gates remain test fixtures.
- TCP merchant dispatch and shared service construction remain incomplete.
- Lease callers must await dispatch before disposal and avoid concurrent sharing.
- Earlier isolated World Probe passed at b17cdbb; no Probe rerun for this source.
- Production 6001 remains at 3344eb3; no Client acceptance performed.
