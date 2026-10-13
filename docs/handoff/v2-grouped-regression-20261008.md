# V2 grouped regression — 2026-10-08

- Tested source: 3590115 plus the test-only runner changes committed with this record.
- GOD2_V2_TEST_ONLY=1 routes the existing fixture chain to test-v2-all-no-progress.sh without updating progress.json.
- Six base project groups passed: 554/554, 0 failed, 0 skipped.
- Sixteen fixture-dependent cases are excluded from the base Persistence group and exercised through their dedicated runners.
- Fourteen fixture scripts completed 19 test executions, all passed with 0 failed and 0 skipped.
- Fixture executions include repeated setup tests; 554+19 is not asserted as a unique test count.
- BUY, SELL and quest separate-process recovery retain their ordered write/read phases.
- Fixture cleanup completed successfully; reported remaining fixture counts were zero.
- progress.json SHA-256 was unchanged.
- Operator log: /tmp/god2-v2-grouped-20261008-124014.log.
- TRX and per-script logs remain in the temporary result directory printed by the runner.
- Reproduce: export GOD2_TEST_PASSWORD and GOD2_V2_TEST_ONLY=1, then run bash Automation/test-v2-quest-reward-committed.sh --all.
- Official client acceptance, merchant production evidence promotion, monster source recovery and production deployment remain pending.

## Committed-position runner integration

- Tested source: 4faf215 plus the grouped runner update.
- CharacterPositionCommittedTests is excluded from the base Persistence group and executed through its dedicated fixture runner.
- Six base groups passed: 554/554, zero failed or skipped.
- Fifteen fixture scripts completed twenty passing test-case executions, including prerequisite reruns.
- Fixture execution counts are not added to the base count as a unique-test total.
- The committed-position fixture and outer fixture accounts/users were cleaned successfully.
- The outer committed-quest runner exited with code 0.
- progress.json remained unchanged, verified by the invoking shell.
- Log: /tmp/god2-v2-grouped-position-20261008-133014.log
- Production deployment and original-client acceptance were not performed.

## Isolated merchant recovery wiring — 2026-10-13
- Replaced both legacy merchant process-recovery runner calls with
  test-v2-merchant-recovery-isolated.sh.
- Wrapper executes BUY and SELL independently on isolated 3310.
- Fixture script count updated from 15 to 14; base suite exclusions retained.
- Other fixture scripts remain unchanged and require separate safety review.
- Full grouped regression has not been rerun after this wiring change.
- Isolated wrapper verified: 4/4; both fixture/user cleanups passed.
- Operator log: docs/handoff/merchant-3310-wrapper-20261013/wrapper.log.

## Isolated command wiring — 2026-10-13
- Replaced test-v2-merchant-command.sh with isolated command wrapper.
- Command test passed 1/1; Fixture and temporary user cleanup passed.
- Fixture script count remains 14; 12 legacy scripts still target 3308.
- Full grouped regression has not been rerun.

## Isolated BUY/SELL TCP wiring — 2026-10-13
- Replaced legacy BUY/SELL TCP runner with isolated 3310 wrapper.
- Verified 1/1; temporary user and Fixture cleanup passed.
- Fixture script count remains 14; 11 legacy scripts still target 3308.
- Full grouped regression has not been rerun.

## Isolated TCP restore wiring — 2026-10-13
- Basic TCP, new-server and reconnect runners replaced with isolated wrappers.
- Each test passed 1/1; all temporary-user/Fixture cleanups passed.
- 8 legacy scripts remain; full grouped regression has not been rerun.

## Isolated BUY lost-result wiring — 2026-10-13
- Replaced legacy BUY TCP lost-result runner with isolated 3310 wrapper.
- Verified 1/1; temporary user and Fixture cleanup passed.
- 7 legacy scripts remain; full grouped regression has not been rerun.
