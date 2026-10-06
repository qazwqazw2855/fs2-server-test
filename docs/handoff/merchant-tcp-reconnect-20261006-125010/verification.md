# Merchant TCP reconnect verification — 2026-10-06

- Filtered MariaDB reconnect integration: 1/1 passed, zero skipped.
- Two complete Login/World sessions on the same server instance.
- First session purchases once and receives a 57-byte response.
- Second session sends no NPC open or BUY.
- Second World entry receives the restricted inventory restore frame.
- Restore contains the current wallet balance 60.
- Final inventory: item 253231541, quantity 1, authority slot 0.
- Inventory version, mutation sequence and wallet version remain 1.
- Exactly one purchase writer call and one V2MerchantBuy receipt.
- Network suite: 175/175 passed; Host Release build passed.
- Runner reports remaining_shop_fixture=0 and exit code 0.

## Limits

- Synthetic login, NPC authority, catalog and purchase evidence gate.
- Projection mirrors the existing restricted Classic profile.
- Same server instance; process restart recovery is not verified.
- Inventory and wallet reads are separate, not an atomic combined snapshot.
- Standalone wallet frame and original-client display remain unverified.
- Other items/layouts, stacking and SELL TCP remain unsupported.
- Host does not enable restricted inventory bootstrap or purchase writers.
- Formal evidence gates and production 6001 unchanged.
- Raw local evidence: tcp-db.log.
