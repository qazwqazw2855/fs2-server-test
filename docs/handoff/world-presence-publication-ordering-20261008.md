# World presence publication ordering — 2026-10-08

- Presence mutation and replication enqueue previously used separate lock scopes.
- Per-connection command lanes did not serialize publication across connections.
- Added a synchronous replication scope using the existing presence monitor.
- TCP entry, movement and disconnect now publish within that scope.
- Map transition updates and leave/enter publication use the same scope.
- DB and network awaits remain outside the scope.
- The scope is thread-affine and must not cross an await.
- Three controlled contention cases verify entry, movement and map-update publication before concurrent leave.
- These tests cover the shared scope and registry/outbox composition, not end-to-end TCP scheduling.
- Existing focused tests passed 41/41 after the implementation change.
- Full Network suite with the new cases passed 208/208, 0 failed, 0 skipped.
- Log: /tmp/god2-presence-ordering-tests-20261008-114327.log.
- Peer wire dispatch remains blocked; client acceptance remains pending.
- Bounded outbox overflow can still discard older events; resynchronization policy remains unresolved.
- No production 6001 deployment was performed.
