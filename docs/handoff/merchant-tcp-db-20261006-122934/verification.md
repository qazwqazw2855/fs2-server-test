# Merchant TCP + MariaDB verification — 2026-10-06

- Filtered MerchantPurchaseTcpIntegrationTests: 1/1 passed, zero skipped.
- Loopback TCP uses an OS-assigned port.
- Login, World handshake, 1772-byte bootstrap, NPC open and BUY completed.
- Real MariaDB inventory/wallet repositories and purchase writer used.
- Client received the encoded 57-byte 0x3B response before logout and EOF.
- Wallet committed 100 -> 60, version 0 -> 1.
- Inventory contains one item 253231541, quantity 1, authority slot 0.
- Inventory version and mutation sequence are both 1.
- One durable V2MerchantBuy receipt exists.
- Explicit replay of the identical server request returns Replayed.
- Runner reported remaining_shop_fixture=0 and exit code 0.

## Limits

- Login adapter, world/NPC authority, catalog projection and evidence gate
  are synthetic and restricted to a dedicated disabled fixture character.
- Spawn hash is synthetic, not an official captured spawn.
- Replay is verified through the writer API, not a client retry protocol.
- Lost commit acknowledgement, reconnect inventory projection, repeated BUY,
  general layouts, stacking, SELL TCP and original-client acceptance remain.
- Host still injects no writers; formal BUY execution remains disabled.
- Formal evidence/promotion gates unchanged; production 6001 untouched.
- Raw local evidence: tcp-db.log.
