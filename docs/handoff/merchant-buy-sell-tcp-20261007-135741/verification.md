# Merchant BUY / SELL TCP verification — 2026-10-07

## Verified

- Protocol suite: 147/147 passed.
- Network suite: 202/202 passed.
- Host Release build passed.
- Filtered TCP/MariaDB integration: 1/1 passed, zero skipped.
- Login, World, NPC open, BUY and SELL use loopback TCP.
- BUY returns the supported 57-byte response.
- Inventory readback confirms item 253231541, quantity 1, authority slot 0.
- SELL uses the server-owned persistent item identity and slot version.
- SELL returns the supported 25-byte response.
- Wallet changes 100 -> 60 -> 64.
- Final inventory is empty.
- Inventory version, mutation sequence and wallet version are 2.
- Before explicit replay, purchase and sale writers are each called once.
- Identical SELL writer API replay returns Replayed without another mutation.
- Exactly one V2MerchantBuy receipt and one V2MerchantSell receipt remain.
- Runner reports remaining_shop_fixture=0 and exit code 0.

## Limits

- Synthetic login, NPC authority, catalog and transaction evidence approval.
- Restricted capture-pinned profile: quantity one, client slot 4, authority slot 0.
- Original-client display and acceptance remain unverified.
- SELL result-loss recovery and durable SELL journal remain pending.
- Replay uses the writer API, not a client retry protocol.
- TCP SELL execution is opt-in and defaults to disabled.
- Formal Host does not enable merchant execution.
- Production 6001 unchanged; no full persistence integration-suite rerun.
- Raw local evidence: db.log.
