# Grouped fixture target audit — 2026-10-13

Read-only static inspection; scripts were not executed.

| Script | Target |
| --- | --- |
| test-v2-position-committed.sh | Shared 3308; mutating fixture script |
| test-v2-merchant-purchase.sh | Shared 3308; mutating fixture script |
| test-v2-economy-snapshot.sh | Shared 3308; mutating fixture script |
| test-v2-merchant-tcp-reconnect.sh | Shared 3308; mutating fixture script |
| test-v2-merchant-tcp-lost-result.sh | Shared 3308; mutating fixture script |
| test-v2-merchant-command.sh | Shared 3308; mutating fixture script |
| test-v2-inventory-restore-blocked.sh | Shared 3308; mutating fixture script |
| test-v2-quest-process-recovery.sh | Shared 3308; mutating fixture script |
| test-v2-merchant-buy-sell-tcp.sh | Shared 3308; mutating fixture script |
| test-v2-merchant-sale.sh | Shared 3308; mutating fixture script |
| test-v2-merchant-tcp.sh | Shared 3308; mutating fixture script |
| test-v2-inventory-stack-committed.sh | Shared 3308; mutating fixture script |
| test-v2-merchant-tcp-new-server.sh | Shared 3308; mutating fixture script |
| test-v2-merchant-recovery-isolated.sh | Isolated 3310 recovery wrapper |

Full grouped regression has not been rerun.
Legacy scripts require isolation work before reuse in this workflow.
No test coverage removed and no official evidence gates promoted.
