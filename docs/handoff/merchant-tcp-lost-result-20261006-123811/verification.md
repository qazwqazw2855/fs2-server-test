# Merchant committed-result loss verification — 2026-10-06

- Filtered integration case: 1/1 passed, zero failed or skipped.
- Real MariaDB purchase commits before the injected wrapper throws IOException.
- TCP closes without a purchase response.
- Server reports execution uncertainty and automaticRetry=None.
- Writer is called once before explicit reconciliation.
- Identical original server request returns Replayed.
- After replay: wallet remains 60, wallet version 1.
- Inventory remains one canonical item 253231541, quantity 1, slot 0.
- Inventory version and mutation sequence remain 1.
- One V2MerchantBuy receipt remains.
- Runner reports remaining_shop_fixture=0 and exit code 0.

## Limits

- Wrapper exception models caller result loss, not a real DB network failure.
- Original request is retained by the test fixture in memory.
- Durable recovery across process restart is not implemented by this test.
- Replay uses the writer API, not a client retry protocol.
- Reconnect inventory projection and original-client acceptance remain pending.
- Synthetic authority and evidence gate; production 6001 unchanged.
- Raw local evidence: tcp-db.log.
