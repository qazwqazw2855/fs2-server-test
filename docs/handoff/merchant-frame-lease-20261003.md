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
