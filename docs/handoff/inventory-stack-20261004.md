# Inventory stack database verification — 2026-10-04

- Targeted InventoryGrantStackTests: 3 passed, 0 failed, 0 skipped.
- Connection-local temporary items table supplies a fixture maximum_stack of 5.
- Formal item content is unchanged; the fixture does not approve official stack rules.
- Merge: quantity 3 + 1 becomes 4, preserving item identity and incrementing slot version 3 to 4.
- Spill: quantity 3 + 4 becomes 5 in the original slot and 2 in a new slot with a new identity.
- Successful grants increment inventory version 7 to 8 and mutation sequence 11 to 12.
- Per-slot audit verifies before/after quantities, item identities and inventory versions.
- Same-transaction replay adds no audit entries.
- Capacity: quantity 3 + 8 with two slots is rejected without slot, state, identity reservation, receipt or audit writes.
- Caller-owned transactions always roll back; the original character snapshot and reservation/receipt row counts are restored.
- Audit writes use a connection-local temporary ledger.
- Temporary DB user cleanup reported 0; runner exit code 0.
- This verifies GrantInTransactionAsync, not committed cross-connection stack behavior or concurrent stack merging.
- Latest full six-suite result remains 405 passed at source 1f1c98c; full suites were not rerun.
- No production writer changes, TCP integration, deployment or Client acceptance.
