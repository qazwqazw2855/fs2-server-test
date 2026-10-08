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
