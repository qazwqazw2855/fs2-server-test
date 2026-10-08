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
