# Merchant 3310 Isolated Recovery — 2026-10-13

## Repository
- Branch: codex/inventory-grant-20261003
- Checkpoint HEAD: f838205
- Local branch ahead of remote by 2 commits.
- Preserve unrelated untracked logs.
- Do not stage all files indiscriminately.

## Environment
- Isolated DB: god2-merchant-db-isolated
- Host endpoint: 127.0.0.1:3310
- Shared 3308 / 3309: do not modify.
- Live 6001 / 6002: do not modify.
- Merchant evidence gates: Blocked.

## Verified milestones
- 90Q-23I: empty Fixture Runner and EXIT trap PASS.
- 90Q-23J: Fixture unique indexes verified.
- 90Q-23K: 14/14 cleanup tables use InnoDB.
- 90Q-23L: transaction settings inspected.
- 90Q-23M: lock-preview SQL created.
- 90Q-23N: FOR UPDATE and ROLLBACK PASS.
- 90Q-23O: identity-preview SQL created.
- 90Q-23P: empty Fixture 0:0:0 PASS.
- 90Q-23Q: same-transaction identity check PASS.
- 90Q-23R: 0:0:0 rejected by external gate PASS.
- 90Q-23S: identity-gate SQL created; static validation PASS.

## Unfinished
- Database-side identity abort implemented and verified (90Q-23T–W).
- Transactional cleanup enabled in isolated Runner and verified.
- Real committed 1:1:1 Fixture cleanup verified.
- BUY/SELL isolated 3310 producer/recovery tests passed 4/4.
- No promotion of Merchant evidence gates.

## Next step: 90Q-24L
Audit remaining grouped fixture scripts for shared-database mutations.
Do not run full grouped regression until that review is complete.
Official merchant evidence gates remain Blocked.

## Safety
- Never source production server-v2.env in isolated Runner.
- Do not run legacy merchant scripts on shared databases.
- Preserve existing untracked logs.
- Commit only reviewed paths.
- Do not push without explicit approval.

## 90Q-23T through 90Q-23W verified
- 23T: database rejects missing identity with ERROR 1644 / SQLSTATE 45000;
  subsequent SQL not executed.
- 23U: same-connection verification: identity 0:0:0,
  in_transaction=0 after rejection.
- 23V: execution without transaction rejected with
  MERCHANT_FIXTURE_TRANSACTION_REQUIRED.
- 23W: real inserted identity 1:1:1 accepted; transaction remains active.
- 23W: explicit rollback removes all three inserted identity rows.
- 23W: real inserted identity changed to 1:0:1 rejected;
  gate rollback verified with in_transaction=0 and residual rows=0.
- No fixture committed, no DELETE executed, no BUY/SELL integration run.
- AUTO_INCREMENT counters may advance despite rollback.
- Runner cleanup remains disabled. Evidence gates remain Blocked.

## 90Q-24D integration verified
- 24A: real inventory/audit/sequence cleanup passed.
- 24A: unrelated sequence preserved; SAVEPOINT rollback restored 8 rows.
- 24A: cleanup commit left no test residue; canonical item preserved.
- 24B: empty Runner EXIT cleanup passed.
- 24C: committed 1:1:1 Fixture removed by Runner EXIT cleanup.
- 24D BUY: journal/result-loss producer and separate-process recovery 2/2.
- 24D SELL: TCP/result-loss producer and separate-process recovery 2/2.
- Total 4 executed / 4 passed / 0 failed / 0 skipped.
- Both Runner executions removed temporary DB user and fixture identities.
- Evidence:
  - merchant-3310-buy-20261013-113425-jmqDfc
  - merchant-3310-sell-20261013-113445-94B261
- Merchant official evidence gates remain Blocked.
- Shared databases and live service unchanged by these isolated runs.

## 90Q-24F final verification
- Receipt validator / database guard unit tests: 29/29, no skips.
- Isolated DB: 14 checked fixture tables empty.
- Canonical item 253231541 present in items and item_registry.
- Both 24D temporary test users absent.
- Evidence: merchant-3310-final-20261013-AkrpoX.

## 90Q-24G independent guard cases
- Added rejection tests using approved port 3310 with invalid users,
  disabled integration and invalid hosts.
- Focused validator/guard tests: 36/36, no failures or skips.
- Evidence: merchant-3310-guard-20261013-wfDzUV.

## 90Q-24H reviewed checkpoint
- RecoveryReader validator extraction preserves existing integrity checks.
- Isolated producer variants added for BUY journal and SELL TCP result loss.
- Separate-process Recovery tests now require isolated 3310 target.
- Legacy 3308 process-recovery runners require migration before reuse.
- Verified: integration 4/4; focused unit tests 36/36.
- Transactional cleanup, SAVEPOINT restore and committed cleanup verified.
- Final DB state: 14 fixture tables empty; canonical item intact.
- No push or live deployment performed.

## 90Q-24J wrapper verification
- Grouped runner now calls isolated BUY/SELL recovery wrapper.
- Wrapper verification: 4/4, zero failures/skips; both EXIT cleanups passed.
- These are repeat executions of the same four integration tests.
- Evidence: merchant-3310-wrapper-20261013/wrapper.log.
- Full grouped regression not rerun; no push or live deployment.
