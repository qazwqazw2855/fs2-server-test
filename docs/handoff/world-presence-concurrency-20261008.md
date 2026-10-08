# World presence concurrency verification — 2026-10-08

- Baseline focused tests: 38 passed, 0 failed, 0 skipped.
- Added three concurrency cases; focused tests now pass 41/41.
- Eight competing connections admit exactly one account/character owner.
- Rejected connections cannot remove the winning presence.
- Movement or map change racing with leave cannot restore departed ownership.
- After replacement login, the old connection cannot move, change map or remove the new owner.
- Scope: WorldPresenceRegistry, WorldReplicationOutboxRegistry and WorldMapTransitionService tests.
- Log: /tmp/god2-presence-concurrency-20261008-113818.log.
- Tests verify registry consistency, not TCP cross-connection event ordering.
- Peer visibility currently uses same-map membership; distance-based AOI is unverified.
- Peer spawn, movement and despawn wire dispatch remains blocked.
- Original-client multiplayer evidence and acceptance remain pending.
- No production 6001 deployment was performed.
