# God2 Gameplay Table Reference

所有下列表均為 InnoDB、`utf8mb4_unicode_ci`，正式欄位具有繁體中文 COMMENT。`*_cache` 僅供管理員閱讀，權威仍是對應 ID。

## `god2_game`

- `items`：物品共同欄位；`equipment`：裝備能力與穿戴限制；`consumables`：消耗品使用效果。
- `item_sets`：套裝；`item_set_members`：套裝成員；`item_set_bonuses`：件數效果。
- `combine_recipes`：合成定義；`combine_recipe_materials`：合成材料。
- `containers`：容器／禮盒；`container_rewards`：容器產出與未知機率。
- `maps`：地圖與客戶端 identity；`map_rules`：地圖規則；`portals`：傳送點。
- `monsters`：怪物模板與完整能力；`monster_skills`：怪物可用技能；`monster_spawns`：出生位置、數量、重生；`monster_drops`：掉落；`monster_ai_profiles`：AI 設定；`monster_ai_rules`：逐回合條件與動作。
- `npcs`：NPC 模板；`npc_spawns`：NPC 出生與 wire authority；`npc_dialogs`：對話節點；`npc_dialog_options`：選項。
- `merchants`：商人；`merchant_inventory`：商品、售價、回收價、庫存與排序。
- `skills`：戰鬥技能；`skill_levels`：各級耗 MP／數值；`skill_effects`：技能效果；`status_effects`：Buff／Debuff 疊加與 phase。
- `quests`：任務；`quest_prerequisites`：前置；`quest_objectives`：目標；`quest_rewards`：獎勵。
- `pet_templates`：戰寵基礎能力與壽命；`pet_growth_profiles`：成長；`pet_template_skills`：模板技能槽。
- `immortal_templates`：神仙模板；`immortal_ranks`：獨立階位；`immortal_template_skills`：固有／可使用技能。
- `mount_templates`：坐騎模板；`mount_template_skills`：坐騎技能。
- `formations`：陣型；`formation_slots`：資料驅動站位；`formation_bonuses`：站位／陣型加成。
- `life_skills`：四項生活技能定義；`crafting_recipes`：鍛造／煉製配方；`crafting_recipe_materials`：材料；`crafting_recipe_outputs`：產出與機率。
- `character_classes`：職業；`class_level_stats`：職業等級值；`class_stat_growth`：成長規則；`level_experience`：升級經驗。
- `character_creation_profiles`：有可信 Evidence 才能啟用的出生設定。
- `server_rates`：倍率；`battle_rule_parameters`：回合制規則。未知規則維持 `NULL`、`enabled=0`。

## `god2_player`

- `accounts`：安全帳號狀態；`characters`：角色、職業、稱號、能力、五行、HP/MP、地圖座標。
- `character_inventory`：背包實例；`character_equipment`：穿戴槽；`character_skills`：角色戰鬥技能；`character_quests`：任務進度。
- `character_pets`：玩家戰寵、壽命、合體到期與能力；`character_pet_skills`：可擴充技能槽。
- `character_immortals`：玩家神仙、階位、等級與 conversation EXP；`character_immortal_skills`：固有／使用技能分離。
- `character_mounts`：坐騎；`character_formations`：已學陣型；`character_formation_members`：成員站位。
- `character_life_skills`：每角色四筆生活技能進度；`character_learned_recipes`：配方解鎖。
- `character_lifecycle_idempotency`：建立／改名／刪除冪等結果。
- `player_inventory_state`、`player_currency_balances`：背包版本與貨幣。
- `inventory_item_identity_sequence`、`inventory_transaction_idempotency`、`inventory_audit_ledger`：物品配號、交易冪等與稽核。
- `world_interaction_idempotency`、`world_interaction_audit`：移動／傳送的冪等與稽核。

## `god2_game_meta`

- `field_mappings`：Evidence 到正式欄位的白名單；`field_provenance`：已同步值來源。
- `admin_field_locks`：管理員逐欄鎖；`admin_change_audit`：old/new 稽核。
- `sync_runs`：同步批次；`sync_conflicts`：同級衝突；`unmapped_fields`：尚未映射 Evidence；`synchronization_watermarks`：增量水位。
- `catalog_validation`：驗證問題；`runtime_catalog_releases`：immutable release；`runtime_authority`：目前權威指標。
- `combatant_stat_observations`：畫面／封包觀測；不得直接覆寫正式 base stat。

## 可讀 View

`god2_game` 提供 `vw_items_full`、`vw_equipment_full`、`vw_monsters_full`、`vw_monster_skills_full`、`vw_monster_spawns_full`、`vw_monster_drops_full`、`vw_npcs_full`、`vw_npc_spawns_full`、`vw_merchants_full`、`vw_merchant_inventory_full`、`vw_skills_full`、`vw_status_effects_full`、`vw_quests_full`、`vw_quest_objectives_full`、`vw_quest_rewards_full`、`vw_pet_templates_full`、`vw_immortal_templates_full`、`vw_character_creation_profiles_full`。

怪物資料日常查閱請使用 `vw_monsters_readable`、`vw_monster_skills_readable`、`vw_monster_spawns_readable`、`vw_monster_drops_readable`。這四個檢視只呈現繁體名稱與可理解的遊戲欄位，不顯示代碼、雜湊、來源鍵、原始封包或管理用技術欄位；未知數值維持 `NULL`。

`god2_player` 提供 `vw_characters_full`、`vw_character_pets_full`、`vw_character_immortals_full`、`vw_character_life_skills_full`。View 供閱讀，不是唯一交付；Runtime 讀 base tables 的具名欄位。
