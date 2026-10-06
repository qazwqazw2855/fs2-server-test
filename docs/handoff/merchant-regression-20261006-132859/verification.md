# Merchant regression — 2026-10-06

- Source commit: `b74817a81e26f3be296120d861987945d9a257bd`.
- Core 7/7; Application 114/114; Session 20/20.
- Protocol 136/136; Network 175/175.
- Five complete unit suites: 452/452 passed.
- Filtered journal unit tests: 3/3 passed.
- Nine isolated DB runners: 10/10 filtered cases passed.
- Combined executed tests: 465/465, zero failed or skipped.
- Every DB runner confirmed remaining_shop_fixture=0 and exit code 0.
- Host Release build and git diff --check passed.

## Limits

- This is not a complete Persistence integration-suite run.
- Historical six-suite 409/409 baseline remains separate.
- Synthetic merchant authority and evidence gates remain test fixtures.
- Formal Host purchase/restore features remain disabled.
- Original-client acceptance and formal evidence promotion remain pending.
- Production 6001 was not restarted.
- Raw logs and TRX files remain local in this directory.
