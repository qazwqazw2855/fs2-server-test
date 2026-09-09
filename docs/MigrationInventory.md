# God2 Official Client Recovery Migration Inventory

本文件列出目前匯入 `db/imports/official/` 的 Official Client Recovery inventory。所有資料只保留官方來源或待交叉掃描狀態，不補假資料。

永久原則：不得因找不到單一檔案就宣稱 `Missing`。必須持續執行 DAT、Lua、Script、Resource、Localization、Model、Packet、Runtime、NPC、Quest、Dialog、Item、Skill 的交叉比對。

| 類別 | 來源 | 格式 | 筆數 | 驗證狀態 | Recovery Progress |
| --- | --- | --- | ---: | --- | --- |
| maps | God2 Classic Unified Server/Data/Runtime/Maps/map-portals.runtime.json | Decoded Runtime JSON | 69 | Recovered; NumericMapIdUnverified | Pending DAT + resource + packet cross-reference for numeric map id, spawn position, exit regions, portal triggers, and server destination ids. |
| portals | God2 Classic Unified Server/Data/Runtime/Maps/map-portals.runtime.json | Decoded Runtime JSON | 68 | Recovered; TransferTriggerUnverified | Pending DAT + resource + packet + runtime cross-reference for unresolved destination candidates. |
| npcs | God2 Classic Unified Server/Data/Runtime/Npcs/npc.runtime.json | Decoded Runtime JSON | 319 | Recovered; SpawnDialogBindingUnverified | Pending dialog, quest, merchant, map, script, and packet cross-reference. |
| monsters | God2 Classic Unified Server/Data/Runtime/Monsters/monster.runtime.json | Decoded Runtime JSON | 208 | Recovered; CombatStatsUnverified | Pending drop table, skill, animation, model, and packet cross-reference. |
| spawns | God2 Classic Unified Server/Data/Runtime/Spawns/spawn.runtime.json | Decoded Runtime JSON | 69 | Unverified; Needs Recovery | Pending map, monster/NPC, packet, and runtime cross-reference. |
| items | God2 Classic Unified Server/Data/Runtime/Items/item.runtime.json | Decoded Runtime JSON | 17407 | Recovered; GameplayFieldsUnverified | Pending merchant, quest, drop table, skill, icon, model, localization, and packet cross-reference. |
| equipment | God2 Classic Unified Server/Data/Runtime/EquipmentRules/equipment-rule.runtime.json | Decoded Runtime JSON | 1 | Unverified; EquipmentRulesOnly | Pending item/equipment stat, model, icon, skill, and packet cross-reference. |
| skills | God2 Classic Unified Server/Data/Runtime/Skills/skill.runtime.json | Decoded Runtime JSON | 1108 | Recovered; RuntimeSemanticsUnverified | Pending animation, effect, item, monster, packet, and script cross-reference. |
| quests | God2 Classic Unified Server/Data/Runtime/Quests/quest.runtime.json | Decoded Runtime JSON | 418 | Recovered; RewardNpcStateUnverified | Pending NPC, dialog, reward, item, monster, localization, and script cross-reference. |
| merchants | God2 Classic Unified Server/Data/Runtime/Merchants/merchant.runtime.json | Decoded Runtime JSON | 16 | Recovered; InventoryUnverified | Pending NPC, item, price, dialog, and packet cross-reference. |
| dialogs | God2 Classic Unified Server/Data/Runtime/Npcs/npc.runtime.json | Decoded Runtime JSON | 18 | Recovered; DialogRowsUnexpanded | Pending string table, NPC, quest, script, and packet cross-reference. |
| drop_tables | God2 Classic Unified Server/Data/Runtime/DropTables/drop-table.runtime.json | Decoded Runtime JSON | 208 | Unverified; EvidenceOnly | Pending monster, item, quest, packet, and runtime cross-reference. |
| localization | God2 Classic Unified Server/Data/Runtime/Localization/localization.runtime.json | Decoded Runtime JSON | 34 | Recovered; TableManifestOnly | Pending string table extraction and row-level key/value cross-reference. |
| immortals | Data2/Patch/Comm/gamedata.csvZ | Decoded Official Client GOD Section | 34 | VerifiedExactClientStaticIdentity; RuntimeSemanticsEvidenceBlocked | Exact-client identity/resource catalog recovered; cross-version package corroborates rows 2801-2830. Login wire identity, HP/MP, attributes, ownership, skills, and growth remain evidence-gated. |
| battle_pets | Data2/Patch/Comm/gamedata.csvZ | Decoded Official Client PET Section | 178 | VerifiedExactClientStaticIdentity; RuntimeSemanticsEvidenceBlocked | Exact-client identity/resource catalog recovered; cross-version package exposes 94 PET and 198 FightPetSkill rows. Stats, growth, feeding, ownership, skills, and battle wire semantics remain evidence-gated. |
| rewards | God2 Classic Unified Server/Data/Runtime/Quests/quest.runtime.json + God2 Classic Unified Server/Data/Runtime/Npcs/npc.runtime.json | Pending Derived Recovery Inventory | 0 | Needs Recovery; CrossReferencePending | Must be derived from quest, NPC, dialog, script, item, and localization evidence. |
| animations | Official Client Recovery cross-reference queue | Pending Cross Reference Inventory | 0 | Needs Recovery; CrossReferencePending | Queued for DAT + resource + model + skill + effect cross-reference. |
| models | Official Client Recovery cross-reference queue | Pending Cross Reference Inventory | 0 | Needs Recovery; CrossReferencePending | Queued for container + resource + NPC + monster + item + equipment cross-reference. |
| textures | Official Client Recovery cross-reference queue | Pending Cross Reference Inventory | 0 | Needs Recovery; CrossReferencePending | Queued for container + resource + model + UI asset cross-reference. |
| icons | Official Client Recovery cross-reference queue | Pending Cross Reference Inventory | 0 | Needs Recovery; CrossReferencePending | Queued for item + skill + UI resource + texture cross-reference. |
| sounds | Official Client Recovery cross-reference queue | Pending Cross Reference Inventory | 0 | Needs Recovery; CrossReferencePending | Queued for resource + effect + skill + UI action cross-reference. |
| music | Official Client Recovery cross-reference queue | Pending Cross Reference Inventory | 0 | Needs Recovery; CrossReferencePending | Queued for map + resource + container cross-reference. |
| effects | Official Client Recovery cross-reference queue | Pending Cross Reference Inventory | 0 | Needs Recovery; CrossReferencePending | Queued for skill + animation + resource + packet cross-reference. |
| scripts | Official Client Recovery cross-reference queue | Pending Cross Reference Inventory | 0 | Needs Recovery; CrossReferencePending | Queued for DAT + Lua + script + quest + NPC + dialog cross-reference. |
| lua | Official Client Recovery cross-reference queue | Pending Cross Reference Inventory | 0 | Needs Recovery; CrossReferencePending | Queued for Lua/source/resource/string/quest/NPC cross-reference. |
| containers | Official Client Recovery cross-reference queue | Pending Cross Reference Inventory | 0 | Needs Recovery; CrossReferencePending | Queued for archive/container format detection and extraction inventory. |
| resource_tables | Official Client Recovery cross-reference queue | Pending Cross Reference Inventory | 0 | Needs Recovery; CrossReferencePending | Queued for resource manifest, DAT, container, model, texture, icon, sound, music, and effect cross-reference. |
| string_tables | Official Client Recovery cross-reference queue | Pending Cross Reference Inventory | 0 | Needs Recovery; CrossReferencePending | Queued for localization, DAT, Lua, script, dialog, NPC, quest, item, and skill cross-reference. |

## 匯入策略

- 直接匯入 gameplay tables 的欄位若未被官方 recovery 驗證，一律不填假預設。
- 所有已恢復或 evidence-only 資料先匯入 MariaDB official staging：`official_import_categories`、`official_import_records`、`official_import_reference_issues`。
- 空類別保留 inventory 與 importer 介面，狀態使用 `NeedsRecovery`，不得放假 record。
- 後續只有在 Primary Key、Foreign Key、Reference Validation 全部通過後，才可升級到正式 gameplay tables。
