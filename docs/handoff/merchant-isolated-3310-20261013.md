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

## Next step: 90Q-25B
Inspect remaining merchant purchase/sale fixtures and fault-account requirements.
5 legacy grouped scripts remain; full grouped regression pending.
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

## 90Q-24L grouped target audit
- 13 remaining fixture scripts load server-v2.env and target shared 3308.
- Only merchant recovery wrapper uses isolated 3310.
- Some legacy scripts apply schema/runtime SQL.
- Audit: grouped-fixture-target-audit-20261013.md.
- Full grouped runner not executed.

## 90Q-24M command integration verified
- Added isolated 3310 variant; transaction assertions retained.
- Command integration passed 1/1, no failure or skip.
- BUY/SELL replay, interaction rejection and final balance/version verified.
- Temporary test user and committed Fixture removed successfully.
- Evidence: merchant-3310-command-20261013-115037-mcO4P6.
- Grouped command runner now uses isolated wrapper.
- Verified isolated integration cases: 5; validator/guard cases: 36.
- 12 legacy grouped fixture scripts remain to be isolated.

## 90Q-24P TCP BUY/SELL verified
- Added isolated 3310 variant; original transaction/wire assertions retained.
- TCP BUY/SELL test passed 1/1, zero failures/skips.
- BUY/SELL responses, balance 100 -> 60 -> 64 and SELL replay verified.
- Temporary user and committed Fixture cleanup passed.
- Evidence: merchant-3310-tcp-20261013-115319-LYR6Am.
- Grouped BUY/SELL TCP runner now uses isolated wrapper.
- Isolated integration cases verified: 6; focused unit cases: 36.
- 11 legacy grouped fixture scripts remain; no full grouped run or push.

## 90Q-24S/T TCP migration verified
- Basic TCP, new-server restore and reconnect restore: each 1/1.
- Existing transaction, replay and restore assertions retained.
- All three executions removed temporary users and committed Fixtures.
- Isolated integration cases verified: 9; focused unit cases: 36.
- Grouped fixture count remains 14; 8 legacy scripts remain.
- Evidence:
  - merchant-3310-basic-20261013-115629-38aHxe
  - merchant-3310-newserver-20261013-115644-lWBk9a
  - merchant-3310-reconnect-20261013-115805-BDvjvn
- Full grouped regression not run; no push or live deployment.

## 90Q-24W BUY TCP lost-result verified
- Isolated 3310 variant passed 1/1, zero failures/skips.
- Real commit followed by synthetic caller result loss verified.
- Persisted journal recovery and replay caused no second mutation.
- Temporary DB user and committed Fixture cleanup passed.
- Evidence: merchant-3310-lostresult-20261013-120113-mb80S0.
- Isolated integration cases verified: 10; focused unit cases: 36.
- 7 legacy grouped scripts remain; no full grouped run or push.

## 90Q-24Z economy/restore verification
- Economy snapshot concurrency: 1/1; inventory/wallet snapshot consistency verified.
- Unsupported inventory restore: 1/1; closed without transaction mutation.
- Both executions removed temporary users and committed Fixtures.
- Evidence:
  - merchant-3310-economy-20261013-120440-xTs5Sc
  - merchant-3310-restoreblocked-20261013-120453-8ThIah
- Isolated integration cases verified: 12; focused unit cases: 36.
- Remaining grouped legacy scripts:
  - test-v2-position-committed.sh
  - test-v2-merchant-purchase.sh
  - test-v2-quest-process-recovery.sh
  - test-v2-merchant-sale.sh
  - test-v2-inventory-stack-committed.sh
