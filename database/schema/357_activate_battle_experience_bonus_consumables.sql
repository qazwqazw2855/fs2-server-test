-- Schema 357
-- Game function: consumable items temporarily raise battle experience gain efficiency.
-- Evidence source: Traditional Chinese client item descriptions explicitly say battle experience percent and duration.
-- Runtime bridge: MariaDbItemUseRepository writes character_item_experience_bonuses atomically before consuming the item.

ALTER TABLE `god2_game`.`item_effects`
    DROP CONSTRAINT `ck_item_effects_type`,
    ADD CONSTRAINT `ck_item_effects_type` CHECK (`effect_type` IN (
        'RestoreHp','RestoreMp','RestoreHpPercent','RestoreMpPercent',
        'CleansePoison','CleanseSleep','CleanseSeal','CleansePetrify','CleanseConfusion',
        'ApplyStrengthBuff','ApplyConstitutionBuff','ApplyIntelligenceBuff','ApplySpeedBuff','ApplyBattleExperienceBuff',
        '生命恢復','法力恢復','生命百分比恢復','法力百分比恢復',
        '解除中毒','解除睡眠','解除神仙封','解除石化','解除混亂',
        '提升腕力','提升體力','提升智力','提升速度','戰鬥經驗加成'
    ));

CREATE TABLE IF NOT EXISTS `god2_player`.`character_item_experience_bonuses` (
    `character_experience_bonus_id` bigint NOT NULL AUTO_INCREMENT COMMENT '角色道具經驗加成ID',
    `character_id` bigint NOT NULL COMMENT '角色ID',
    `source_item_id` bigint NOT NULL COMMENT '來源道具ID',
    `bonus_percent` int NOT NULL COMMENT '戰鬥經驗效率提升百分比',
    `duration_seconds` int NOT NULL COMMENT '持續秒數',
    `started_at_utc` datetime(6) NOT NULL COMMENT '開始UTC時間',
    `expires_at_utc` datetime(6) NOT NULL COMMENT '到期UTC時間',
    `source_inventory_id` bigint NULL COMMENT '來源背包物品ID',
    `source_transaction_id` char(36) CHARACTER SET ascii COLLATE ascii_bin NULL COMMENT '來源道具使用交易GUID',
    `runtime_state` varchar(32) NOT NULL DEFAULT 'PendingBridge' COMMENT '執行狀態',
    `enabled` tinyint(1) NOT NULL DEFAULT 0 COMMENT '是否由正式服務端啟用',
    `created_at_utc` datetime(6) NOT NULL DEFAULT UTC_TIMESTAMP(6) COMMENT '建立UTC時間',
    `updated_at_utc` datetime(6) NOT NULL DEFAULT UTC_TIMESTAMP(6) ON UPDATE CURRENT_TIMESTAMP(6) COMMENT '更新UTC時間',
    PRIMARY KEY (`character_experience_bonus_id`),
    KEY `ix_character_item_experience_bonuses_character_active` (`character_id`,`enabled`,`expires_at_utc`),
    KEY `ix_character_item_experience_bonuses_source_item` (`source_item_id`),
    CONSTRAINT `fk_character_item_experience_bonuses_character` FOREIGN KEY (`character_id`) REFERENCES `god2_player`.`characters` (`character_id`) ON DELETE CASCADE,
    CONSTRAINT `fk_character_item_experience_bonuses_item` FOREIGN KEY (`source_item_id`) REFERENCES `god2_game`.`item_registry` (`item_id`),
    CONSTRAINT `ck_character_item_experience_bonuses_percent` CHECK (`bonus_percent` IN (20,30,40,50,60,70)),
    CONSTRAINT `ck_character_item_experience_bonuses_duration` CHECK (`duration_seconds` IN (3600,7200,14400)),
    CONSTRAINT `ck_character_item_experience_bonuses_enabled` CHECK (`enabled` IN (0,1)),
    CONSTRAINT `ck_character_item_experience_bonuses_time` CHECK (`expires_at_utc` > `started_at_utc`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci COMMENT='角色道具戰鬥經驗加成';

INSERT INTO `god2_game`.`item_effects`
    (`item_id`,`effect_index`,`effect_type`,`numeric_value`,`usage_scope`,`target_policy`,`effect_text_zh_tw`,
     `runtime_eligible`,`enabled`,`duration_seconds`)
SELECT item_row.`item_id`,
       965,
       '戰鬥經驗加成',
       CASE item_row.`client_item_id`
           WHEN 5561 THEN 50
           WHEN 5570 THEN 20
           WHEN 5661 THEN 20 WHEN 5662 THEN 20 WHEN 5663 THEN 30 WHEN 5664 THEN 30 WHEN 5665 THEN 40
           WHEN 5666 THEN 40 WHEN 5667 THEN 50 WHEN 5668 THEN 50 WHEN 5669 THEN 60 WHEN 5670 THEN 60
           WHEN 6227 THEN 20 WHEN 6228 THEN 20 WHEN 6229 THEN 30 WHEN 6230 THEN 30 WHEN 6231 THEN 40
           WHEN 6232 THEN 40 WHEN 6233 THEN 50 WHEN 6234 THEN 50 WHEN 6235 THEN 60 WHEN 6236 THEN 60
           WHEN 6247 THEN 20 WHEN 6248 THEN 30 WHEN 6249 THEN 40 WHEN 6250 THEN 50 WHEN 6251 THEN 60
           WHEN 6399 THEN 70
       END,
       '世界與戰鬥',
       '自身',
       CONCAT('戰鬥經驗效率提升 ', CASE item_row.`client_item_id`
           WHEN 5561 THEN 50
           WHEN 5570 THEN 20
           WHEN 5661 THEN 20 WHEN 5662 THEN 20 WHEN 5663 THEN 30 WHEN 5664 THEN 30 WHEN 5665 THEN 40
           WHEN 5666 THEN 40 WHEN 5667 THEN 50 WHEN 5668 THEN 50 WHEN 5669 THEN 60 WHEN 5670 THEN 60
           WHEN 6227 THEN 20 WHEN 6228 THEN 20 WHEN 6229 THEN 30 WHEN 6230 THEN 30 WHEN 6231 THEN 40
           WHEN 6232 THEN 40 WHEN 6233 THEN 50 WHEN 6234 THEN 50 WHEN 6235 THEN 60 WHEN 6236 THEN 60
           WHEN 6247 THEN 20 WHEN 6248 THEN 30 WHEN 6249 THEN 40 WHEN 6250 THEN 50 WHEN 6251 THEN 60
           WHEN 6399 THEN 70
       END, '%，持續 ', CASE
           WHEN item_row.`client_item_id` IN (5661,6227,6247,6248,6249,6250,6251) THEN 60
           WHEN item_row.`client_item_id` IN (5662,5664,5666,5668,5670,6228,6230,6232,6234,6236,6399) THEN 120
           ELSE 240
       END, ' 分鐘'),
       1,
       1,
       CASE
           WHEN item_row.`client_item_id` IN (5661,6227,6247,6248,6249,6250,6251) THEN 3600
           WHEN item_row.`client_item_id` IN (5662,5664,5666,5668,5670,6228,6230,6232,6234,6236,6399) THEN 7200
           ELSE 14400
       END
FROM `god2_game`.`item_registry` item_row
LEFT JOIN `god2_game`.`item_usage_rules` rule_row ON rule_row.`item_id`=item_row.`item_id`
WHERE item_row.`client_item_id` IN (
    5561,5570,
    5661,5662,5663,5664,5665,5666,5667,5668,5669,5670,
    6227,6228,6229,6230,6231,6232,6233,6234,6235,6236,
    6247,6248,6249,6250,6251,
    6399
)
  AND item_row.`usable`=1
  AND item_row.`item_category`='消耗品'
  AND item_row.`item_family`='消耗品'
  AND rule_row.`normal_use`=1
  AND rule_row.`battle_use`=1
  AND item_row.`description_zh_tw` REGEXP '戰鬥經驗加成道具'
  AND item_row.`description_zh_tw` REGEXP '[124]小時內提升玩家戰鬥經驗值效率[0-9]+％'
ON DUPLICATE KEY UPDATE
    `effect_type`=VALUES(`effect_type`),
    `numeric_value`=VALUES(`numeric_value`),
    `usage_scope`=VALUES(`usage_scope`),
    `target_policy`=VALUES(`target_policy`),
    `effect_text_zh_tw`=VALUES(`effect_text_zh_tw`),
    `runtime_eligible`=VALUES(`runtime_eligible`),
    `enabled`=VALUES(`enabled`),
    `duration_seconds`=VALUES(`duration_seconds`);

CREATE OR REPLACE VIEW `god2_game`.`vw_battle_experience_bonus_consumables_runtime_readable` AS
SELECT item_row.`client_item_id` AS `客戶端道具ID`,
       item_row.`code` AS `服務端代碼`,
       item_row.`name_zh_tw` AS `道具名稱`,
       effect_row.`effect_type` AS `效果類型`,
       effect_row.`numeric_value` AS `經驗加成百分比`,
       effect_row.`duration_seconds` AS `持續秒數`,
       rule_row.`normal_use` AS `平時可用`,
       rule_row.`battle_use` AS `戰鬥可用`,
       '文字證據：戰鬥經驗效率提升' AS `證據狀態`
FROM `god2_game`.`item_effects` effect_row
JOIN `god2_game`.`item_registry` item_row ON item_row.`item_id`=effect_row.`item_id`
LEFT JOIN `god2_game`.`item_usage_rules` rule_row ON rule_row.`item_id`=item_row.`item_id`
WHERE effect_row.`effect_type`='戰鬥經驗加成'
  AND effect_row.`runtime_eligible`=1
  AND effect_row.`enabled`=1;

CREATE OR REPLACE VIEW `god2_player`.`vw_character_item_experience_bonuses_readable` AS
SELECT bonus_row.`character_experience_bonus_id` AS `角色經驗加成ID`,
       character_row.`character_id` AS `角色ID`,
       character_row.`name` AS `角色名稱`,
       item_row.`client_item_id` AS `來源客戶端道具ID`,
       item_row.`name_zh_tw` AS `來源道具名稱`,
       bonus_row.`bonus_percent` AS `經驗加成百分比`,
       bonus_row.`duration_seconds` AS `持續秒數`,
       bonus_row.`started_at_utc` AS `開始UTC`,
       bonus_row.`expires_at_utc` AS `到期UTC`,
       bonus_row.`runtime_state` AS `執行狀態`,
       bonus_row.`enabled` AS `是否啟用`
FROM `god2_player`.`character_item_experience_bonuses` bonus_row
JOIN `god2_player`.`characters` character_row ON character_row.`character_id`=bonus_row.`character_id`
JOIN `god2_game`.`item_registry` item_row ON item_row.`item_id`=bonus_row.`source_item_id`;

CREATE OR REPLACE VIEW `god2_player`.`vw_character_item_experience_bonuses_active_readable` AS
SELECT *
FROM `god2_player`.`vw_character_item_experience_bonuses_readable`
WHERE `是否啟用`=1
  AND `到期UTC` > UTC_TIMESTAMP(6);
