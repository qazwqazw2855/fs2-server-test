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
