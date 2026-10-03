# Merchant wire source audit — 2026-10-03

## Existing bounded Classic implementation

- Client build: god2-opt-6b127086e0c0.
- Evidence reference: LiveRecovery/Stages4-7-attempt-759-trace.
- Supported profile: NPC handle 3954, client item 6901, canonical item 253231541, quantity 1.
- Purchase catalog index: 7. Sale client slot: 4, mapped to authority slot 0.
- Transaction request: opcode 0x38, 12 bytes.
- Purchase result: opcode 0x3B, 57 bytes.
- Sale result: opcode 0x41, 25 bytes.
- Classic tests contain fixed frame examples. These do not independently establish original capture provenance or V2 promotion approval.

## Source availability

- live-recovery-monitor.json records a historical Windows trace under host-run-20260812-000800/attempt-759-trace.
- Monitor status is STOPPED, updated 2026-08-12.
- The monitor is a path/status record, not a merchant protocol approval record.
- A name-based search under /home/ubuntu/games found no attempt-759 directory or Stage 4–6 evidence JSON.
- Search excluded .git, bin, obj, node_modules and vendor.
- This result does not establish absence from other locations, renamed archives or the original Windows machine.
- Original trace existence, integrity, build metadata and Stage exports remain unverified.

## Separate evidence batch

- Current-build ShopSellCandidate describes an 8-byte candidate and remains decoder/serializer blocked.
- Its Chinese-labeled capture batch is separate from attempt-759.
- Do not combine the two batches or treat the Classic bounded implementation as a general merchant protocol.

## Remaining work

- Locate and hash the original trace and Stage 4–6 exports.
- Verify capture build, source authority, field correlations and item/slot mappings.
- Review content and protocol promotion independently before V2 TCP dispatch.
- No protocol code, evidence gates, DB data or privileges changed in this audit.
- No production deployment or Client acceptance performed.
- Latest full verification remains 405 passed at source 1f1c98c; this documentation change adds no test coverage.
