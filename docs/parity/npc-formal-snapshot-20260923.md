# Formal NPC snapshot (2026-09-23)

Source: user-executed read-only SQL against the AWS `god2-runtime-db-test` container. This is a point-in-time operator-provided result; refresh it before using the counts for a later deployment decision.

| Formal table | Rows |
|---|---:|
| `god2_game.npcs` | 339 |
| `god2_game.npc_spawns` | 20 |
| `god2_game.npc_dialogs` | 0 |

The 20 NPC spawns are enabled and have X/Y coordinates. They cover 10 maps:

| Map ID | Map | Spawn rows |
|---:|---|---:|
| 170015007 | 九天冰屋 | 3 |
| 1200010012 | 葛城 | 4 |
| 1200010081 | 南原部落 | 1 |
| 1200060003 | 雲峰部落 | 1 |
| 1200110000 | 太極境 | 1 |
| 1200110022 | 焰霜鎮 | 1 |
| 1200130003 | 茄城 | 1 |
| 1200130012 | 果力村 | 1 |
| 1200160009 | 陵寢花園 | 5 |
| 1675308248 | 崑崙仙界 | 2 |

Two spawn IDs, `170153793` (handle 3793) and `170153954` (handle 3954), originate in Migration 103's observed Map 7 slice. Migration 470 pins handle 3793's exact spawn evidence; Server V2 has bounded NPC 3793 dialog acceptance. Handle 3954's shop inventory and V2 merchant interaction are not established by this snapshot.

The other 18 spawn IDs, including handle 5004, originate in Migration 116's official `DayMissionDesc.csvZ` name/map cross-match. Each of their Migration 116 spawn rows sets service evidence to `EvidenceBlocked`. An enabled row with coordinates is not proof that an NPC dialog, quest or merchant action is accepted by the live client.

The recovered 319 NPC templates and 18 dialog catalogs in `db/imports/official` are supplemental client data, not the same ID namespace or row set as the Formal DB counts above. Merchant inventories remain a later item-identity task.

The Server V2 network path uses `OfficialNpcDialogCodec` for an evidence-gated NPC open response. Therefore the known handle-3793 open response can exist while `god2_game.npc_dialogs` has zero rows; it does not establish general dialog catalog or branching support.
