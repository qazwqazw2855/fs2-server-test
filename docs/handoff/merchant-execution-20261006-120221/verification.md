# Merchant execution verification — 2026-10-06

- Network Release suite: 143/143 passed.
- Added checks for no automatic retry after a writer exception,
  explicit retry preserving the entire request, and distance
  revalidation before held-lease purchase dispatch.
- MerchantPurchaseIntegrationTests: 1/1 passed.
- MerchantCommandIntegrationTests: 1/1 passed.
- Both database runners reported remaining_shop_fixture=0 and exit code 0.

## Database coverage

- Purchase receipt and audit failures roll back inventory and wallet writes.
- Eight identical requests produce one purchase and seven replays.
- Changed transaction identity with the same key is rejected.
- Competing wallet versions permit one purchase.
- Insufficient funds are rejected; old committed requests still replay.
- Held-lease command dispatch validates interaction and performs durable
  BUY and SELL with replay and cleanup.

## Limits

- The exception unit test uses a recording writer; it does not simulate
  a lost commit acknowledgement.
- Database tests use dedicated disabled characters and synthetic merchants.
- This is two filtered integration cases, not a complete integration suite.
- TCP transaction execution and official response serialization remain unwired.
- No production 6001 restart or test001 wallet change was performed.
- Raw local evidence: purchase.log and command.log.
