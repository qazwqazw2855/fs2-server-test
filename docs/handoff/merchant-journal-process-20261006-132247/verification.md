# Merchant journal cross-process recovery — 2026-10-06

## Verified

- Two separate dotnet test invocations; each passed 1/1, zero skipped.
- First process saves the exact request and commits a real MariaDB purchase.
- An injected exception models result loss after commit.
- First test process exits before the recovery test process starts.
- Second process discovers the transaction identity from the journal table.
- Original request and committed receipt are recovered solely from MariaDB.
- Second process calls neither purchase nor sale writer.
- Wallet remains 60/version 1.
- Inventory contains one item 253231541, quantity 1, authority slot 0.
- Inventory version and mutation sequence remain 1.
- Exactly one journal row and one V2MerchantBuy receipt.
- Runner reports remaining_shop_fixture=0 and exit code 0.

## Limits

- Separate test processes, not a full Host restart or crash recovery campaign.
- No TCP recovery dispatch or client response redelivery.
- Result loss uses an injected exception, not an actual DB connection failure.
- Synthetic disabled character, merchant authority and evidence gate.
- Formal Host features remain disabled; production 6001 unchanged.
- No full integration-suite rerun or original-client acceptance.
- Raw local evidence: db.log.
