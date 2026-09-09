-- Schema 359
-- Game function: consumable items temporarily raise one five-element attribute for 3 hours.
-- Evidence source: Traditional Chinese descriptions explicitly say only one element bonus can exist and later use replaces prior effect.
-- Runtime bridge: MariaDbItemUseRepository replaces active element bonuses and writes character_item_element_bonuses atomically.

ALTER TABLE `god2_game`.`item_effects`
    DROP CONSTRAINT `ck_item_effects_type`,
    ADD CONSTRAINT `ck_item_effects_type` CHECK (`effect_type` IN (
        'RestoreHp','RestoreMp','RestoreHpPercent','RestoreMpPercent',
        'CleansePoison','CleanseSleep','CleanseSeal','CleansePetrify','CleanseConfusion',
        'ApplyStrengthBuff','ApplyConstitutionBuff','ApplyIntelligenceBuff','ApplySpeedBuff','ApplyBattleExperienceBuff',
        'ApplyMetalElementBuff','ApplyWoodElementBuff','ApplyWaterElementBuff','ApplyFireElementBuff','ApplyEarthElementBuff',
        '生命恢復','法力恢復','生命百分比恢復','法力百分比恢復',
        '解除中毒','解除睡眠','解除神仙封','解除石化','解除混亂',
        '提升腕力','提升體力','提升智力','提升速度','戰鬥經驗加成',
        '提升金屬性','提升木屬性','提升水屬性','提升火屬性','提升土屬性'
    ));

CREATE TABLE IF NOT EXISTS `god2_player`.`character_item_element_bonuses` (
    `character_element_bonus_id` bigint NOT NULL AUTO_INCREMENT COMMENT '角色道具五行屬性加成ID',
    `character_id` bigint NOT NULL COMMENT '角色ID',
    `source_item_id` bigint NOT NULL COMMENT '來源道具ID',
    `element_type` varchar(8) NOT NULL COMMENT '五行屬性：金、木、水、火、土',
    `bonus_value` int NOT NULL COMMENT '五行屬性提升數值',
    `duration_seconds` int NOT NULL COMMENT '持續秒數',
    `started_at_utc` datetime(6) NOT NULL COMMENT '開始UTC時間',
    `expires_at_utc` datetime(6) NOT NULL COMMENT '到期UTC時間',
    `source_inventory_id` bigint NULL COMMENT '來源背包物品ID',
    `source_transaction_id` char(36) CHARACTER SET ascii COLLATE ascii_bin NULL COMMENT '來源道具使用交易GUID',
    `replaced_by_transaction_id` char(36) CHARACTER SET ascii COLLATE ascii_bin NULL COMMENT '取代此效果的交易GUID',
    `runtime_state` varchar(32) NOT NULL DEFAULT 'PendingBridge' COMMENT '執行狀態',
    `enabled` tinyint(1) NOT NULL DEFAULT 0 COMMENT '是否由正式服務端啟用',
    `created_at_utc` datetime(6) NOT NULL DEFAULT UTC_TIMESTAMP(6) COMMENT '建立UTC時間',
    `updated_at_utc` datetime(6) NOT NULL DEFAULT UTC_TIMESTAMP(6) ON UPDATE CURRENT_TIMESTAMP(6) COMMENT '更新UTC時間',
    PRIMARY KEY (`character_element_bonus_id`),
    KEY `ix_character_item_element_bonuses_character_active` (`character_id`,`enabled`,`expires_at_utc`),
    KEY `ix_character_item_element_bonuses_source_item` (`source_item_id`),
    CONSTRAINT `fk_character_item_element_bonuses_character` FOREIGN KEY (`character_id`) REFERENCES `god2_player`.`characters` (`character_id`) ON DELETE CASCADE,
    CONSTRAINT `fk_character_item_element_bonuses_item` FOREIGN KEY (`source_item_id`) REFERENCES `god2_game`.`item_registry` (`item_id`),
    CONSTRAINT `ck_character_item_element_bonuses_element` CHECK (`element_type` IN ('金','木','水','火','土')),
    CONSTRAINT `ck_character_item_element_bonuses_bonus` CHECK (`bonus_value` IN (300,400,500,600)),
    CONSTRAINT `ck_character_item_element_bonuses_duration` CHECK (`duration_seconds`=10800),
    CONSTRAINT `ck_character_item_element_bonuses_state` CHECK (`runtime_state` IN ('PendingBridge','Active','Replaced','Expired','Removed')),
    CONSTRAINT `ck_character_item_element_bonuses_enabled` CHECK (`enabled` IN (0,1)),
    CONSTRAINT `ck_character_item_element_bonuses_time` CHECK (`expires_at_utc` > `started_at_utc`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci COMMENT='角色道具五行屬性加成';

INSERT INTO `god2_game`.`item_effects`
    (`item_id`,`effect_index`,`effect_type`,`numeric_value`,`usage_scope`,`target_policy`,`effect_text_zh_tw`,
     `runtime_eligible`,`enabled`,`duration_seconds`)
SELECT item_row.`item_id`,
       966,
       CASE
           WHEN item_row.`client_item_id` BETWEEN 6368 AND 6371 THEN '提升金屬性'
           WHEN item_row.`client_item_id` BETWEEN 6372 AND 6375 THEN '提升木屬性'
           WHEN item_row.`client_item_id` BETWEEN 6376 AND 6379 THEN '提升水屬性'
           WHEN item_row.`client_item_id` BETWEEN 6380 AND 6383 THEN '提升火屬性'
           WHEN item_row.`client_item_id` BETWEEN 6384 AND 6387 THEN '提升土屬性'
       END,
       CASE ((item_row.`client_item_id` - 6368) MOD 4)
           WHEN 0 THEN 300
           WHEN 1 THEN 400
           WHEN 2 THEN 500
           WHEN 3 THEN 600
       END,
       '世界與戰鬥',
       '自身',
       CONCAT(CASE
           WHEN item_row.`client_item_id` BETWEEN 6368 AND 6371 THEN '金'
           WHEN item_row.`client_item_id` BETWEEN 6372 AND 6375 THEN '木'
           WHEN item_row.`client_item_id` BETWEEN 6376 AND 6379 THEN '水'
           WHEN item_row.`client_item_id` BETWEEN 6380 AND 6383 THEN '火'
           WHEN item_row.`client_item_id` BETWEEN 6384 AND 6387 THEN '土'
       END, '屬性提升 ', CASE ((item_row.`client_item_id` - 6368) MOD 4)
           WHEN 0 THEN 300
           WHEN 1 THEN 400
           WHEN 2 THEN 500
           WHEN 3 THEN 600
       END, '，持續 180 分鐘；後使用覆蓋前使用'),
       1,
       1,
       10800
FROM `god2_game`.`item_registry` item_row
LEFT JOIN `god2_game`.`item_usage_rules` rule_row ON rule_row.`item_id`=item_row.`item_id`
WHERE item_row.`client_item_id` BETWEEN 6368 AND 6387
  AND item_row.`usable`=1
  AND rule_row.`normal_use`=1
  AND rule_row.`battle_use`=1
  AND item_row.`description_zh_tw` REGEXP '(金|木|水|火|土)屬性提升(300|400|500|600)'
  AND item_row.`description_zh_tw` REGEXP '效果有效時間為3小時'
  AND item_row.`description_zh_tw` REGEXP '後使用的會覆蓋前使用的效果'
ON DUPLICATE KEY UPDATE
    `effect_type`=VALUES(`effect_type`),
    `numeric_value`=VALUES(`numeric_value`),
    `usage_scope`=VALUES(`usage_scope`),
    `target_policy`=VALUES(`target_policy`),
    `effect_text_zh_tw`=VALUES(`effect_text_zh_tw`),
    `runtime_eligible`=VALUES(`runtime_eligible`),
    `enabled`=VALUES(`enabled`),
    `duration_seconds`=VALUES(`duration_seconds`);

CREATE OR REPLACE VIEW `god2_game`.`vw_element_attribute_bonus_consumables_runtime_readable` AS
SELECT item_row.`client_item_id` AS `客戶端道具ID`,
       item_row.`code` AS `服務端代碼`,
       item_row.`name_zh_tw` AS `道具名稱`,
       effect_row.`effect_type` AS `效果類型`,
       effect_row.`numeric_value` AS `屬性提升數值`,
       effect_row.`duration_seconds` AS `持續秒數`,
       rule_row.`normal_use` AS `平時可用`,
       rule_row.`battle_use` AS `戰鬥可用`,
       '文字證據：五行屬性限時提升且後用覆蓋前用' AS `證據狀態`
FROM `god2_game`.`item_effects` effect_row
JOIN `god2_game`.`item_registry` item_row ON item_row.`item_id`=effect_row.`item_id`
LEFT JOIN `god2_game`.`item_usage_rules` rule_row ON rule_row.`item_id`=item_row.`item_id`
WHERE effect_row.`effect_type` IN ('提升金屬性','提升木屬性','提升水屬性','提升火屬性','提升土屬性')
  AND effect_row.`runtime_eligible`=1
  AND effect_row.`enabled`=1;

CREATE OR REPLACE VIEW `god2_player`.`vw_character_item_element_bonuses_readable` AS
SELECT bonus_row.`character_element_bonus_id` AS `角色五行加成ID`,
       character_row.`character_id` AS `角色ID`,
       character_row.`name` AS `角色名稱`,
       item_row.`client_item_id` AS `來源客戶端道具ID`,
       item_row.`name_zh_tw` AS `來源道具名稱`,
       bonus_row.`element_type` AS `五行屬性`,
       bonus_row.`bonus_value` AS `屬性提升數值`,
       bonus_row.`duration_seconds` AS `持續秒數`,
       bonus_row.`started_at_utc` AS `開始UTC`,
       bonus_row.`expires_at_utc` AS `到期UTC`,
       bonus_row.`runtime_state` AS `執行狀態`,
       bonus_row.`enabled` AS `是否啟用`
FROM `god2_player`.`character_item_element_bonuses` bonus_row
JOIN `god2_player`.`characters` character_row ON character_row.`character_id`=bonus_row.`character_id`
JOIN `god2_game`.`item_registry` item_row ON item_row.`item_id`=bonus_row.`source_item_id`;

CREATE OR REPLACE VIEW `god2_player`.`vw_character_item_element_bonuses_active_readable` AS
SELECT *
FROM `god2_player`.`vw_character_item_element_bonuses_readable`
WHERE `是否啟用`=1
  AND `執行狀態`='Active'
  AND `到期UTC` > UTC_TIMESTAMP(6);
