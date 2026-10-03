# Internal merchant command dispatch — 2026-10-03

- Network suite: 100 passed, 0 failed, 0 skipped.
- Purchase and sale commands omit character and merchant identity.
- Character identity is read from current World presence; merchant identity comes from reviewed server binding.
- Interaction checks run before dispatch to the existing writer interfaces.
- Tests verify request identity and versions, writer rejection propagation, no dispatch on invalid interaction, cancellation before dispatch and exception propagation.
- Dispatch tests use recording writers, not MariaDB.
- The service must be awaited within the connection's serialized command loop.
- TCP packet dispatch is not wired; cross-thread lifecycle coordination remains incomplete.
- No formal merchant binding, distance policy or wire codec was promoted.
- No production deployment or DB privilege changes.

## Full verification

- Tested source: 15b3f02.
- Six suites: 393 passed, 0 failed, 0 skipped; DB integration enabled.
- Core 7, Application 114, Session 20, Protocol 111, Network 100, Persistence Integration 41.
- All fixture runners reported successful cleanup.
- Command dispatch uses recording writers; no command-service-to-MariaDB integration test yet.
- TCP integration, transaction lifecycle coordination and formal content approval remain incomplete.
- Production 6001 remains at source 3344eb3; no Client acceptance performed.

## Database command closed loop

- Tested source: 8b20e8a.
- Six suites: 394 passed, 0 failed, 0 skipped; DB integration enabled.
- Core 7, Application 114, Session 20, Protocol 111, Network 100, Persistence Integration 42.
- Supersedes the earlier command-service-to-MariaDB test gap.
- Dedicated disabled fixture verifies interaction resolver -> command service -> real purchase/sale writers.
- Balance: 100 -> 60 -> 64; final inventory and wallet versions: 2.
- Fresh writer instances replay both transactions.
- Out-of-range purchase creates no receipt; closed interaction sale leaves inventory unchanged.
- Reopening invalidates the old interaction identity.
- All fixture runners reported successful cleanup.
- Full repeat: Automation/test-v2-merchant-command.sh --all with GOD2_TEST_PASSWORD set.
- World/NPC bindings and content approval are fixture-only.
- Mid-transaction lifecycle coordination, TCP dispatch and Client acceptance remain incomplete.
- Production 6001 remains at source 3344eb3.
