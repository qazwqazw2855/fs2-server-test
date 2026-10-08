# Map transition presence check — 2026-10-08

- Async map transitions retain the original presence reference.
- After DB persistence returns, the service checks that reference inside the existing replication synchronization scope before changing world state.
- Presence replacement is rejected even when its runtime version is unchanged.
- A successful DB write followed by a rejected world update raises the existing inconsistent-state exception.
- Tests cover presence changes with both changed and unchanged runtime versions.
- Both cases preserve the competing map/coordinates, NPC interaction and empty replication outbox.
- Full Network regression: 212 passed, 0 failed, 0 skipped.
- Operator test log: /tmp/god2-map-presence-check-20261008-121600.log.
- This prevents stale world-state replacement; it does not roll back an already committed DB transition.
- No client protocol acceptance or production deployment was performed.
