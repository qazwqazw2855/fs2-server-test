# Monster promotion gaps — 2026-10-08

## Current Formal DB observation

Operator-executed read-only queries against god2-runtime-db-test:
- Nine monsters, all disabled.
- None has level, max_hp, max_mp or resource_id populated.
- monster_spawns and monster_drops each contain zero rows.
- No V2 monster runtime implementation was found in the reviewed Application, Persistence or Network sources.

## Evidence boundaries

- Supplemental FightEny inventory contains 208 client presentation identities.
- Its serverAuthoritativeId values require separate mapping; resource references do not establish battle statistics.
- Live file: db/imports/live/monsters/monsters.verified.latest.json.
- SHA256: 074d2bd204b4bf7d1c05d997f7f38c6c6e767c1108293c6ba9ff178f790d0999.
- One row reports clientMonsterId 27, historical serverMonsterId 30, name 仙狐.
- That row classifies level 6 and experience 73 as verified, but underlying capture provenance has not been independently reviewed in this audit.
- Its maximumHp is null, marked WithdrawnSourcePositionMisread, and runtimeEligible is false.
- Migration 067 records withdrawal of HP 34 for 褐蝸螺 and leaves 仙狐 HP unknown.
- Damage totals and source-position fields must not be used to reconstruct maximum HP.
- Supplemental clientMonsterId 30 identifies 野兔; it is not a mapping to historical serverMonsterId 30.
- The nine current Formal IDs form another identity set; numeric or name similarity alone does not approve a join.
- Migrations 390/391 classify reconciliation sources and explicitly do not establish official live measurements.

## Pending evidence work

- Locate the original live session and HP withdrawal artifact; verify hashes, client build and regional authority.
- Establish explicit capture/client/historical/current Formal identity mappings.
- Verify HP/MP from direct actor state with correct target correlation.
- Verify encounter placement, rewards and drop semantics independently.
- Keep Taiwan primary evidence separate from supplemental or unidentified sources.
- Client-dependent capture work is deferred until the client is available.

No DB rows, runtime gates or production 6001 deployment were changed.

## Reproducible import identity audit

- Tool: Automation/audit-monster-import-identity.py.
- Supplemental source SHA256: e222fa38e8ec2e1f2dd5f8fa61ab2b93ad9cef5b24cef5a58b5e09363101d882.
- Recovery run: 88f531a0-4d5c-469e-9fb5-5c9059dcda8d.
- Current recovery table: 208 rows; all lack level, HP, MP and experience.
- Importer uses the normalized RecordKey SHA256 first four bytes, interpreted little-endian on this Ubuntu host, masked to a positive 31-bit integer.
- Zero becomes one; collisions are resolved by incrementing in source order.
- All 208 recovery IDs and codes matched; collision adjustments were zero.
- All nine Formal IDs and codes matched the source-derived recovery identities; all remain disabled.
- Formal import migrations 053/387/388 preserve the legacy monster Id rather than assigning a new monster ID.
- Current god2.monsters is a base table using RecoveryStatus.
- Current DB queries returned no legacy IDs 30/32 and no rows named 仙狐/褐蝸螺.
- Historical live server IDs therefore remain unmapped to this recovery dataset.
- This verifies import identity/code consistency, not original-client build equivalence, name semantics or gameplay approval.

## Current drop relationship verification

- Operator-provided read-only DB results on 2026-10-08 found 47 relationships covering exactly the nine current Formal monsters.
- All 47 monster and item IDs resolve to current Formal rows.
- Relationship statuses: Candidate 17; Derived 30.
- All 47 have NULL minimum/maximum quantities, declared chance and weight.
- Effective chance is zero; IsDropEnabled and ProductionDropEnabled are zero for all rows.
- Recovery reporting explicitly describes NULL declared chance plus effective zero as disabled unknown probability, not official 0%.
- All relationships belong to run cd28082b-3804-4b1e-b628-daf59ca769ee, created on 2026-08-11.
- Current Phase2MariaDbPromoter code synchronizes monsters referenced by drop candidates and inserts disabled Formal drops with NULL rates when declared chance is unknown.
- This code path is consistent with the nine-monster subset, but does not prove which historical code version populated it.
- Formal monster_drops currently contains zero rows.
- No columns were found for research tables monster_drop_gap_evidence_audit, legacy_monster_drop_relationship_evidence or relationship_source_archive; these tables are absent from the inspected DB.
- Migration 418's missing-monster statement does not describe current identity coverage; the migration file is not proof of execution.
- Derived relationship status alone does not establish quantity or probability evidence.
- Remaining work: recover original relationship sources and execution history, verify quantities/probability units/conditions, and establish the V2 combat and reward baseline.
- No migrations were rerun, no runtime content was enabled, and no production deployment was performed.
