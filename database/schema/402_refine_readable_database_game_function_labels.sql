CREATE TABLE IF NOT EXISTS `god2_research`.`database_traditional_chinese_surface_audit`
(
    `audit_name` varchar(96) NOT NULL,
    `checked_scope_zh_tw` varchar(160) NOT NULL,
    `issue_count` int NOT NULL DEFAULT 0,
    `status_zh_tw` varchar(64) NOT NULL,
    `cleanup_policy_zh_tw` varchar(220) NOT NULL,
    `reviewed_at_utc` datetime(6) NOT NULL DEFAULT utc_timestamp(6),
    PRIMARY KEY (`audit_name`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

REPLACE INTO `god2_research`.`database_traditional_chinese_surface_audit`
    (`audit_name`,`checked_scope_zh_tw`,`issue_count`,`status_zh_tw`,`cleanup_policy_zh_tw`)
SELECT
    'safe_surface_bilingual_field_names',
    '正式遊戲資料與人工可讀候選表欄位名稱',
    COUNT(*),
    CASE WHEN COUNT(*) = 0 THEN '通過' ELSE '需要清理' END,
    CASE
        WHEN COUNT(*) = 0 THEN '目前沒有發現 zh_cn、name_original、simplified、original 等雙語或簡體欄位名稱。'
        ELSE '發現雙語或簡體欄位名稱，需改成單一繁中語意或移出人工可讀面。'
    END
FROM information_schema.`COLUMNS` column_row
JOIN `god2_research`.`database_readability_surface` surface
  ON surface.`schema_name` = column_row.`TABLE_SCHEMA`
 AND surface.`table_name` = column_row.`TABLE_NAME`
WHERE surface.`safe_to_browse` = 1
  AND (
        column_row.`COLUMN_NAME` LIKE '%zh_cn%'
     OR column_row.`COLUMN_NAME` LIKE '%name_original%'
     OR column_row.`COLUMN_NAME` LIKE '%simplified%'
     OR column_row.`COLUMN_NAME` LIKE '%original%'
  );

UPDATE `god2_research`.`database_readability_surface`
SET `game_function_zh_tw` = '裝備打造、配方材料與產出資料',
    `notes_zh_tw` = '鍛造系統使用的配方、材料需求與產出對應。'
WHERE `schema_name` = 'god2_game'
  AND `table_name` IN ('crafting_recipes','crafting_recipe_materials','crafting_recipe_outputs');

UPDATE `god2_research`.`database_readability_surface`
SET `game_function_zh_tw` = '裝備強化、強化材料與成功率資料',
    `notes_zh_tw` = '裝備強化系統使用的材料需求、機率與強化規則。'
WHERE `schema_name` = 'god2_game'
  AND `table_name` IN ('equipment_enhancement_materials','equipment_enhancement_rates');

UPDATE `god2_research`.`database_readability_surface`
SET `game_function_zh_tw` = '裝備穿戴、屬性加成與外觀模型資料',
    `notes_zh_tw` = '角色穿戴裝備後使用的攻防、五行、HP/MP 與模型資料。'
WHERE `schema_name` = 'god2_game'
  AND `table_name` IN ('equipment','weapons','magic_treasures');

UPDATE `god2_research`.`database_readability_surface`
SET `game_function_zh_tw` = '神仙模板、階級與基礎能力資料',
    `notes_zh_tw` = '神仙系統使用的模板、品階、基礎屬性與成長資料。'
WHERE `schema_name` = 'god2_game'
  AND `table_name` IN ('immortal_templates','immortal_ranks','immortal_base_stats');

UPDATE `god2_research`.`database_readability_surface`
SET `game_function_zh_tw` = '戰鬥陣型與隊伍站位資料',
    `notes_zh_tw` = '戰鬥隊伍排列、前後排與陣型效果使用的資料。'
WHERE `schema_name` = 'god2_game'
  AND `table_name` = 'formations';

UPDATE `god2_research`.`database_readability_surface`
SET `game_function_zh_tw` = '技能狀態、增益減益與持續效果資料',
    `notes_zh_tw` = '技能、道具或戰鬥造成的 buff、debuff、持續回合與效果資料。'
WHERE `schema_name` = 'god2_game'
  AND `table_name` = 'status_effects';

UPDATE `god2_research`.`database_readability_surface`
SET `game_function_zh_tw` = '裝備套裝部位與套裝效果候選資料',
    `notes_zh_tw` = '套裝需要哪些部位、幾件觸發效果，以及是否已同步正式套裝表。'
WHERE `schema_name` = 'god2_research'
  AND `table_name` = 'equipment_set_member_candidate_rows';

UPDATE `god2_research`.`database_readability_surface`
SET `cleanup_policy_zh_tw` = '可人工瀏覽；目前未發現工具追蹤欄位，且已歸類到實際遊戲功能。'
WHERE `safe_to_browse` = 1
  AND `game_function_zh_tw` <> '服務端資料整理與證據追蹤';
