# Merchant purchase journal recovery — 2026-10-06

## Verified

- Identity and journal dispatch unit tests: 3/3 passed.
- Fixed key/fingerprint values preserve the original lowercase receipt format.
- Filtered MariaDB journal recovery integration: 1/1 passed, zero skipped.
- Exact immutable server request is saved before purchase dispatch.
- Repeated identical saves retain one journal row.
- Changed request, transaction identity or idempotency key conflicts are rejected.
- Journal failure prevents purchase writer dispatch.
- A real purchase commits before a synthetic result-loss exception.
- New journal and recovery reader instances recover the request and receipt.
- Read-only recovery confirms wallet 100 -> 60 and inventory version 0 -> 1.
- Final inventory contains one item 253231541, quantity 1, authority slot 0.
- Inventory, mutation and wallet versions remain 1.
- Exactly one purchase writer call, purchase receipt and journal row.
- Runner reports remaining_shop_fixture=0 and exit code 0.

## Limits

- New services run in the same process; actual process restart is unverified.
- Result loss uses an injected exception, not a real database network failure.
- Missing receipt returns Unknown and does not authorize automatic purchase.
- Committed receipt does not prove client response delivery.
- Journal wrapper is not wired into formal Host execution.
- Schema 481 journal table was created in the isolated runtime test database.
- Synthetic merchant/evidence approval; production 6001 was not restarted.
- No full integration-suite rerun or original-client acceptance.
- Raw local evidence: db.log.
