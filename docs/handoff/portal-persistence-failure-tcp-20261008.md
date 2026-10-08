# Portal persistence failure TCP verification — 2026-10-08

- A loopback TCP fixture completes login and world entry, opens an NPC interaction, and sends the existing verified portal activation request.
- A synthetic route reaches a map-transition writer that throws a controlled exception.
- The writer is called exactly once; the client receives EOF without a portal response.
- SessionRegistry.Count is zero after fixture shutdown.
- Logs confirm world presence release and Closed stage.
- Private presence, interaction and outbox registries are not directly asserted.
- This tests a writer exception, not an actual DB commit followed by presence rejection.
- Focused test: 1 passed, 0 failed, 0 skipped.
- Full Network regression: 213 passed, 0 failed, 0 skipped.
- Logs: /tmp/god2-portal-failure-tcp-20261008-122156.log and /tmp/god2-portal-failure-regression-20261008-122234.log.
- No production deployment or official client acceptance was performed.

## Conflict branch verification

- The TCP fixture now covers both a writer exception and an explicit Conflict result.
- Each case calls the writer once and receives EOF without a portal response.
- Conflict logs contain DB_TRANSITION_CONFLICT and WorldTransitionRejected, without the controlled writer exception.
- SessionRegistry.Count is zero after fixture shutdown in both cases.
- Full Network regression: 214 passed, 0 failed, 0 skipped.
- Operator log: /tmp/god2-portal-conflict-regression-20261008-122450.log.
- No actual DB transaction, private-registry inspection or official client acceptance is claimed.

## Relogin before server shutdown

- Both exception and Conflict cases wait for SessionRegistry.Count to reach zero while the server remains running.
- Cleanup observation has a five-second deadline; EOF alone is not treated as cleanup completion.
- The same account then completes login, world handshake, bootstrap and NPC spawn reception on the same server.
- The second world connection logs out normally and its session is released before server shutdown.
- Map-transition writer call count remains one, with no automatic retry.
- Full Network regression: 214 passed, 0 failed, 0 skipped.
- Operator log: /tmp/god2-portal-relogin-regression-20261008-123001.log.
- Synthetic TCP verification does not establish official client acceptance or actual DB recovery.
