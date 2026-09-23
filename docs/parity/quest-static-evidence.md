# Supplemental quest static evidence

This offline check covers recovered client presentation data, not Formal DB or verified quest state transitions.

| Source | Rows | SHA-256 |
|---|---:|---|
| `quests/quests.official.json` | 418 | `7064806a04e9b9202b038020ff663b395ec012170f7c7b8dc2c8320e057770d3` |
| `rewards/rewards.official.json` | 0 | `73a51e706f2bc3afbc29b269167ea391491c5fb560de56ec02e0c90ab379b02d` |

All 418 quest IDs are distinct client-presentation entries with objective text. None has a bound start/end NPC, server state-machine ID or reward candidate. Exactly 169 quests have client item target references; item identity and count semantics are a separate deferred item task. No NPC, monster or map target references are populated in these records.

## AWS Formal DB snapshot, 2026-09-23

Read-only export from `god2-runtime-db-test`, schema version `477`, repository commit `07ec82c0b60d22e596ffe95d7c6e89b6dc72f865` at `2026-09-23T12:08:43.837939+00:00`:

| Formal table or quest field | Count |
|---|---:|
| `god2_game.quests` | 418 |
| `quests.enabled=1` | 167 |
| `quests.start_npc_id IS NOT NULL` | 0 |
| `quests.end_npc_id IS NOT NULL` | 0 |
| `quests.description_zh_tw IS NOT NULL` | 0 |
| `quests.completion_text_zh_tw IS NOT NULL` | 170 |
| `god2_game.quest_objectives` | 0 |
| `god2_game.quest_rewards` | 0 |

Migration 381 can copy legacy `ProductionProfileEnabled` into `quests.enabled` and `RewardTextZhTw` into `completion_text_zh_tw`. These counts do not show a runnable quest or a verified reward. The separate supplemental reward inventory also has zero records.

Next evidence work without the client: trace the provenance of the 167 enabled flags and 170 completion texts against the existing migration and source records, while keeping their IDs and authority separate. Item target identity and Bahamut item information remain deferred. Quest runtime activation remains blocked pending NPC/dialog binding, verified objective/state transitions, and reward evidence. No production DB write is implied.
