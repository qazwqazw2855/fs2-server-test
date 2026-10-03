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
