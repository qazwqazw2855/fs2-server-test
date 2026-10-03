# Inventory login restore and peer visibility audit — 2026-10-03

## Reviewed evidence

- Classic OfficialInventoryBootstrapWireCodec reuses the verified merchant purchase response.
- Supported shape: empty inventory, or one canonical merchant item of quantity 1 in authority slot 0, mapped to client sale slot 4.
- Its tests verify frame construction and unsupported-state rejection; they do not establish an official inventory relogin sequence.
- Migration 417 audits merchant sale catalogs, not player inventory login restoration.
- PlayerSpawnS2C128 is derived from a frozen self World bootstrap; dynamic ID/name projection preserves opaque fields.
- WorldProtocolRecovery.Phase3.Batch1.json explicitly records secondClientEvidence as "not captured".

## Source availability

- This search under /home/ubuntu/games found no metadata.jsonl or Stage 5/6 campaign/evidence files.
- Automation/State/packet-capture-active.json was absent.
- live-recovery-monitor.json references the historical Windows attempt-759-trace under host-run-20260812-000800; current existence is unverified.
- Chinese-labeled import manifest covers 16 sessions dated 2026-08-01, imported on 2026-08-06; it is a separate batch from attempt-759.
- Those 16 labels do not explicitly identify inventory relogin or controlled peer entry/update/leave.
- Keyword inspection of runtime-mappings, serializer-candidates, packet-sequences and promotion-gates found no direct relogin/peer evidence record. This is not a complete raw-packet audit.

## Next evidence gate

1. Locate and hash the original attempt-759 trace and Stage 5/6 exports.
2. Inspect whether retained captures include login while carrying a known item, with inventory state and packet sequence correlated.
3. Locate any later controlled two-client capture; otherwise collect peer Enter/Update/Leave evidence with known identities and positions.
4. Verify client build and source provenance before promoting a codec.

V2 inventory and peer wire dispatch remain blocked. No runtime code, DB data or promotion gate was changed by this audit.
