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
- Database-side mandatory identity abort not implemented.
- Transactional DELETE not enabled.
- 1:1:1 cleanup not tested.
- BUY/SELL 3310 integration tests not executed.
- No promotion of Merchant evidence gates.

## Next step: 90Q-23T
Implement a database-side mandatory identity abort.
Verify 0:0:0 rejection before enabling cleanup.
Keep identity checks and cleanup in one transaction.

## Safety
- Never source production server-v2.env in isolated Runner.
- Do not run legacy merchant scripts on shared databases.
- Preserve existing untracked logs.
- Commit only reviewed paths.
- Do not push without explicit approval.
