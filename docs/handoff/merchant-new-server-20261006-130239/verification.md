# New server inventory restore — 2026-10-06

- Filtered MariaDB TCP case: 1/1 passed, zero skipped.
- First server receives BUY, commits and sends the purchase response.
- First server is cancelled, awaited and disposed before the next round.
- Second server has fresh session, presence, NPC and interaction registries.
- Second Login/World sends no NPC open or BUY.
- Second server restores inventory using the combined MariaDB snapshot.
- Restore frame contains wallet balance 60.
- Final inventory contains item 253231541, quantity 1, authority slot 0.
- Inventory, mutation and wallet versions remain 1.
- Exactly one purchase call and one V2MerchantBuy receipt.
- Runner reports remaining_shop_fixture=0 and exit code 0.

## Limits

- Both server instances run in the same test process.
- The repository adapter is shared between rounds.
- Process restart and durable recovery of an uncertain request remain unverified.
- Synthetic authority/evidence; original-client display remains unverified.
- Formal Host features remain disabled; production 6001 unchanged.
- Raw local evidence: db.log.
