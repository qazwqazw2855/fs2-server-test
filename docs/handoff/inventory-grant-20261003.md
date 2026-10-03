# Inventory grant — 2026-10-03

- Tested source: 029f3513075db2564f3ade0f47074de887859c82.
- Six suites: 317 passed, 0 failed.
- Suites: Core 7, Application 69, Session 20, Protocol 111, Network 80, Persistence Integration 30.
- DB integration enabled on 127.0.0.1:3308.
- Inventory planning preserves input state and rejects insufficient capacity without partial changes.
- DB writer locks character/inventory, checks identity/version/sequence, reads formal item rules, and commits slots, versions, replay records and per-slot audits together.
- Eight identical concurrent requests: one Granted, seven Replayed.
- Two different requests with the same expected version: one Granted, one VersionConflict.
- A new writer replays an earlier committed transaction without another grant.
- Replay requires the original request, including transaction ID and expected versions.
- Rejected requests are not stored as completed transactions.
- Rollback tests preserve original inventory, wallet and transaction records.
- Committed tests use a dedicated disabled fixture account/character; cleanup verified zero remaining fixture accounts.
- AUTO_INCREMENT gaps may remain after testing.
- Table-scoped permissions were applied to god2_v2@172.17.0.1 on the runtime DB; SQL is recorded in Automation/grant-v2-inventory-runtime.sql.
- All 17,407 current formal items have NULL maximum_stack; DB grant tests use the existing effective limit of 1 for item 253231541.
- Stack merge planning has unit coverage; DB stack merge remains unverified.
- Runtime code is not connected to Network and has not been deployed to production 6001.
- Production runtime source remains 3344eb3.
- Inventory wire dispatch and Windows Client acceptance remain evidence-blocked.
- Engineering/playability percentages and promotion gates were not advanced.

## Repeat verification

Run Automation/test-v2-inventory-grant-concurrency.sh for committed concurrency tests.
Run it with --all and GOD2_TEST_PASSWORD set for all six suites.
The runner creates and removes the dedicated committed-test fixture.

## Next work

Add DB stack-merge and mid-transaction failure coverage using isolated test data.
Before gameplay integration, bind grant requests to authoritative reward/drop events.
SourceReference records provenance; it does not itself authorize a reward.

## Audit failure rollback verification

- Tested source: 7f1c1fbd896be04020c1dc9d67221b6760fdc7d7.
- Full verification: 317 passed; DB integration enabled.
- Temporary audit table rejects INSERT after the first slot and identity reservation are written.
- Caller-owned transaction rollback restores inventory and removes reserved identity rows; replay records remain unchanged.
- This verifies GrantInTransactionAsync with caller rollback; it does not inject failure into GrantAsync's owned transaction.
- Both committed fixture and temporary DB account were cleaned.
- Full repeat command: Automation/test-v2-inventory-grant-failure.sh --all with GOD2_TEST_PASSWORD set.
