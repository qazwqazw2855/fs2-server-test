# Merchant SELL committed-result loss — 2026-10-07

- Filtered TCP/MariaDB integration: 1/1 passed, zero skipped.
- TCP BUY first commits and returns its purchase response.
- Real MariaDB SELL commits before an injected IOException.
- TCP closes without a SELL response.
- Server logs execution uncertainty and automaticRetry=None.
- Before explicit replay, purchase and sale writers are each called once.
- Wallet remains 64/version 2; inventory is empty.
- Inventory version and mutation sequence remain 2.
- Identical original SELL request returns Replayed.
- Replay changes neither wallet nor inventory.
- One V2MerchantBuy receipt and one V2MerchantSell receipt remain.
- Runner reports remaining_shop_fixture=0 and exit code 0.

## Limits

- Original SELL request is retained in test-process memory.
- Durable SELL journal and cross-process recovery remain pending.
- Result loss is injected after commit, not a real DB network failure.
- Synthetic authority and evidence approval.
- No client retry protocol, response redelivery or original-client acceptance.
- Formal Host SELL remains disabled; production 6001 unchanged.
- Raw local evidence: db.log.
