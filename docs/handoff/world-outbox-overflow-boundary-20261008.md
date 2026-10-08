# World replication outbox overflow boundary — 2026-10-08

- Added TrySnapshotForDispatch as a guarded snapshot acquisition API.
- Unknown connections and connections with dropped events return false and no events.
- Intact queues return ordered snapshots without consuming events.
- Diagnostic Snapshot retains its existing behavior.
- Overflow rejection remains until the connection outbox is removed.
- Removing and registering an outbox creates a fresh history; this does not prove client resynchronization.
- Other connections remain unaffected by one connection's overflow.
- Outbox tests passed 11/11, 0 failed, 0 skipped.
- Log: /tmp/god2-outbox-overflow-20261008-114644.log.
- The guard applies at snapshot acquisition only; it does not acknowledge delivery or protect later sends.
- The API is not wired to TCP dispatch, which remains blocked.
- Verified peer codecs, delivery handling and resynchronization remain pending.
- No production 6001 deployment was performed.
