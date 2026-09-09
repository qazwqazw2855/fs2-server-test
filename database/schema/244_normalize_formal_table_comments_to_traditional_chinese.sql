ALTER TABLE `god2_game`.`character_creation_profiles`
    COMMENT = '正式角色建立出生設定；缺少可信證據時保持空表並安全關閉';

ALTER TABLE `god2_game`.`item_asset_mappings`
    COMMENT = '正式物品圖示與外觀資源對照';

ALTER TABLE `god2_game`.`item_icon_atlases`
    COMMENT = '官方物品圖示圖集範圍與已安裝檔案';

ALTER TABLE `god2_game`.`item_usage_rules`
    COMMENT = '可讀物品使用規則與限制';

ALTER TABLE `god2_game`.`npc_appearance_identities`
    COMMENT = '官方 NPC 外觀資料身份目錄';

ALTER TABLE `god2_game`.`public_beta_skill_effect_v0`
    COMMENT = '公測版技能加持與效果服務端對照';

ALTER TABLE `god2_game`.`status_effects`
    COMMENT = '回合制加持、減益與狀態效果';

ALTER TABLE `god2_player`.`character_lifecycle_idempotency`
    COMMENT = '角色建立、改名與刪除的冪等結果；正式權威位於 god2_player';

ALTER TABLE `god2_player`.`equipment_instances`
    COMMENT = '玩家持有武器、防具與法寶的強化狀態';

ALTER TABLE `god2_player`.`player_inventory_state`
    COMMENT = '正式背包版本與容量狀態';
