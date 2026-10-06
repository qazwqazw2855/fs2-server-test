# Unsupported inventory restore layout — 2026-10-06

- Filtered TCP/MariaDB integration: 1/1 passed, zero skipped.
- Synthetic combined-read projection contains two inventory items.
- Actual database inventory remains empty; no fixture inventory write.
- Client receives bootstrap and NPC spawn, then EOF without restore frame.
- Server logs InventoryLayoutEvidenceBlocked.
- No NPC open or BUY sent; purchase and sale writers are never called.
- Wallet remains 100/version 0.
- Inventory version and mutation sequence remain 0.
- No V2MerchantBuy receipt exists.
- Runner reports remaining_shop_fixture=0 and exit code 0.

## Limits

- This tests unsupported projection handling, not a real multi-item DB layout.
- Original-client acceptance and process restart remain unverified.
- Formal Host features and production 6001 unchanged.
- Raw local evidence: db.log.
