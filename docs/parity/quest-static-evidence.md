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

Migration 381 groups `god2.quest_content_profiles` by `QuestId`, takes `MAX(ProductionProfileEnabled)` and `MAX(NULLIF(RewardTextZhTw, ''))`, then copies those values into Formal `quests.enabled` and `completion_text_zh_tw`. Its upsert keeps any existing `enabled=1` via `existing OR incoming`; therefore even a matching count would not prove every enabled row came from a currently enabled profile. The profile source hash was moved to research archival by Migration 217, and the profile evidence statuses were split by Migration 185.

The read-only AWS provenance audit at schema version 477 found 170 quests with a legacy profile, 167 with an enabled profile, and all 167 Formal enabled rows paired with an enabled profile. No enabled Formal quest lacked an enabled profile. Both the Formal completion-text field and grouped legacy profile reward-text field are non-null on 170 rows, with zero one-sided null mismatches. This establishes row-level coverage of the flags and text presence, not text equality, independent gameplay evidence, or a verified reward. An optional follow-up query against `god2_research.quest_catalog_evidence` failed because that table does not exist in this AWS instance; the audit script was corrected in commit `ebe63ff` to skip the unavailable table. These counts do not show a runnable quest or a verified reward. The separate supplemental reward inventory also has zero records.

Next evidence work without the client: trace the provenance of the 167 enabled flags and 170 completion texts against the existing migration and source records, while keeping their IDs and authority separate. Item target identity and Bahamut item information remain deferred. Quest runtime activation remains blocked pending NPC/dialog binding, verified objective/state transitions, and reward evidence. No production DB write is implied.

## AWS Formal profile provenance refresh, 2026-09-24

The operator reran the existing read-only `Automation/audit-quest-profile-provenance.sh` against the AWS runtime database. The result matches the 2026-09-23 profile coverage snapshot: **418** Formal quests, **167** enabled, **170** with legacy profiles, **167** with enabled profiles, **167** enabled Formal quests with enabled profiles, **0** enabled Formal quests without enabled profiles and **0** disabled Formal quests with enabled profiles. Formal completion text and grouped legacy profile reward text are each present on **170** quests, with **0** one-sided null mismatches.

This refresh covers aggregate row pairing and text presence only. It does not verify text equality, NPC bindings, objectives, rewards, client flow or executable quest state transitions. The earlier counts for `quest_objectives` and `quest_rewards` were **not** independently refreshed in this run. No database write or quest runtime promotion was performed.

### Exact text and runtime table follow-up

The operator executed the updated, read-only audit script on the AWS quest audit worktree. All **170** Formal non-null `completion_text_zh_tw` values exactly match the aggregated legacy profile `RewardTextZhTw` values by byte representation; exact mismatches: **0**. Formal `quest_objectives`: **0 rows**; Formal `quest_rewards`: **0 rows**. This validates the text copied from the legacy profile and confirms the runtime table counts at this point in time. The field name `RewardTextZhTw` and matching completion text do **not** establish actual reward identity, quantity, payout, objective or state transition. The 167 enabled quests remain catalog/presentation candidates, not runnable quest content.
