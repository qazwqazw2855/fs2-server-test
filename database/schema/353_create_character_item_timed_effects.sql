-- Schema 353
-- Game function: character-level timed effects created by consumable items.
-- Scope note: this table is the formal landing point for long-duration consumable buffs.
-- Runtime item-use transactions remain fail-closed until application writes to this table atomically.

CREATE TABLE IF NOT EXISTS `god2_player`.`character_item_timed_effects` (
    `character_item_effect_id` bigint NOT NULL AUTO_INCREMENT COMMENT '角色道具限時效果 ID',
    `character_id` bigint NOT NULL COMMENT '角色 ID',
    `source_item_id` bigint NOT NULL COMMENT '來源道具 ID',
    `effect_type` varchar(32) NOT NULL COMMENT '效果類型：提升腕力、提升智力、提升速度',
    `stat_type` varchar(32) NOT NULL COMMENT '加成能力：腕力、智力、速度',
    `bonus_value` int NOT NULL COMMENT '加成數值',
    `duration_seconds` int NOT NULL COMMENT '持續時間秒數',
    `started_at_utc` datetime(6) NOT NULL COMMENT '開始 UTC 時間',
    `expires_at_utc` datetime(6) NOT NULL COMMENT '到期 UTC 時間',
    `source_inventory_id` bigint NULL COMMENT '來源背包列 ID；歷史資料可為 NULL',
    `source_transaction_id` char(36) CHARACTER SET ascii COLLATE ascii_bin NULL COMMENT '來源道具使用交易 GUID',
    `runtime_state` varchar(32) NOT NULL DEFAULT 'PendingBridge' COMMENT '執行狀態',
    `enabled` tinyint(1) NOT NULL DEFAULT 0 COMMENT '是否由正式服務端啟用',
    `created_at_utc` datetime(6) NOT NULL DEFAULT UTC_TIMESTAMP(6) COMMENT '建立 UTC 時間',
    `updated_at_utc` datetime(6) NOT NULL DEFAULT UTC_TIMESTAMP(6) ON UPDATE CURRENT_TIMESTAMP(6) COMMENT '更新 UTC 時間',
    PRIMARY KEY (`character_item_effect_id`),
    KEY `ix_character_item_timed_effects_character_active` (`character_id`,`enabled`,`expires_at_utc`),
    KEY `ix_character_item_timed_effects_source_item` (`source_item_id`),
    CONSTRAINT `fk_character_item_timed_effects_character` FOREIGN KEY (`character_id`) REFERENCES `god2_player`.`characters` (`character_id`) ON DELETE CASCADE,
    CONSTRAINT `fk_character_item_timed_effects_item` FOREIGN KEY (`source_item_id`) REFERENCES `god2_game`.`item_registry` (`item_id`),
    CONSTRAINT `ck_character_item_timed_effects_type` CHECK (`effect_type` IN ('提升腕力','提升智力','提升速度')),
    CONSTRAINT `ck_character_item_timed_effects_stat` CHECK (`stat_type` IN ('腕力','智力','速度')),
    CONSTRAINT `ck_character_item_timed_effects_bonus` CHECK (`bonus_value` > 0),
    CONSTRAINT `ck_character_item_timed_effects_duration` CHECK (`duration_seconds` > 0),
    CONSTRAINT `ck_character_item_timed_effects_enabled` CHECK (`enabled` IN (0,1)),
    CONSTRAINT `ck_character_item_timed_effects_time` CHECK (`expires_at_utc` > `started_at_utc`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci COMMENT='角色道具限時效果';

CREATE OR REPLACE VIEW `god2_player`.`vw_character_item_timed_effects_readable` AS
SELECT effect_row.`character_item_effect_id` AS `角色道具效果ID`,
       character_row.`character_id` AS `角色ID`,
       character_row.`name` AS `角色名稱`,
       item_row.`client_item_id` AS `來源客戶端道具ID`,
       item_row.`name_zh_tw` AS `來源道具名稱`,
       effect_row.`effect_type` AS `效果類型`,
       effect_row.`stat_type` AS `能力`,
       effect_row.`bonus_value` AS `加成數值`,
       effect_row.`duration_seconds` AS `持續秒數`,
       effect_row.`started_at_utc` AS `開始UTC`,
       effect_row.`expires_at_utc` AS `到期UTC`,
       effect_row.`runtime_state` AS `執行狀態`,
       effect_row.`enabled` AS `是否啟用`
FROM `god2_player`.`character_item_timed_effects` effect_row
JOIN `god2_player`.`characters` character_row ON character_row.`character_id`=effect_row.`character_id`
JOIN `god2_game`.`item_registry` item_row ON item_row.`item_id`=effect_row.`source_item_id`;

CREATE OR REPLACE VIEW `god2_player`.`vw_character_item_timed_effects_active_readable` AS
SELECT *
FROM `god2_player`.`vw_character_item_timed_effects_readable`
WHERE `是否啟用`=1
  AND `到期UTC` > UTC_TIMESTAMP(6);
