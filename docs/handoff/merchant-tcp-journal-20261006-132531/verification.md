# TCP merchant purchase journal — 2026-10-06

- Filtered TCP/MariaDB integration: 1/1 passed, zero skipped.
- Login, World, NPC open and BUY use loopback TCP.
- Journaled writer saves the exact request before purchase dispatch.
- Writer entry reads the journal through a separate connection and verifies it.
- Real MariaDB purchase commits before an injected result-loss exception.
- TCP closes without a purchase response; no automatic retry.
- New journal and recovery reader recover the request and committed receipt.
- Before explicit replay, purchase writer call count remains one.
- Explicit identical writer API replay makes no additional mutation.
- One journal row and one V2MerchantBuy receipt.
- Runner reports remaining_shop_fixture=0 and exit code 0.

## Limits

- Synthetic login, authority, catalog and evidence approval.
- Result loss is injected after commit, not a real DB network failure.
- Cross-process recovery is covered separately by the journal process runner.
- Client retry protocol, response redelivery and full Host restart remain pending.
- Formal Host does not inject purchase writers or enable inventory restore.
- Production 6001 unchanged; no full integration-suite rerun.
- Raw local evidence: db.log.
