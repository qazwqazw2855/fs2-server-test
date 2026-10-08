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
