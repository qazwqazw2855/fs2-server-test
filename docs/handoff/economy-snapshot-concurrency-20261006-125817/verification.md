# Economy snapshot consistency — 2026-10-06

- Filtered MariaDB concurrency integration: 1/1 passed, zero skipped.
- Inventory and Gold wallet use one connection and Repeatable Read transaction.
- Deterministic internal test seam commits a real purchase on another connection
  after inventory SELECT and before wallet SELECT.
- Captured snapshot remains empty inventory and wallet 100, versions 0.
- New snapshot contains item 253231541, quantity 1, slot 0 and wallet 60.
- New inventory, mutation and wallet versions are 1.
- Exactly one purchase and one V2MerchantBuy receipt.
- TCP reconnect using the combined repository also passed 1/1.
- Network suite passed 175/175.
- Both filtered runners reported fixture cleanup and exit code 0.

## Limits

- Consistency relies on ordinary consistent SELECTs against InnoDB tables.
- Internal callback is test-only; public reads supply no callback.
- Synthetic disabled character, merchant authority and evidence approval.
- No full integration-suite rerun, process restart or original-client acceptance.
- Host restore and purchase execution remain disabled; production unchanged.
- Raw evidence: db.log.
- TCP evidence: ../merchant-economy-snapshot-20261006-125451/tcp-db.log.
