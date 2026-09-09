SET @god2_sync_mode = 1;

ALTER TABLE `god2_game`.`skills`
    ADD COLUMN IF NOT EXISTS `official_client_item_id` int NULL COMMENT '官方客戶端技能書物品編號' AFTER `name_original`,
    ADD COLUMN IF NOT EXISTS `official_display_id` int NULL COMMENT '官方客戶端技能顯示編號' AFTER `official_client_item_id`,
    ADD COLUMN IF NOT EXISTS `attack_range` int NULL COMMENT '官方客戶端標示攻擊距離' AFTER `mp_cost`,
    ADD COLUMN IF NOT EXISTS `target_scope_zh_tw` varchar(256) NULL COMMENT '官方客戶端繁中作用範圍原文' AFTER `target_type`,
    ADD COLUMN IF NOT EXISTS `client_metadata_evidence_status` varchar(40) NULL COMMENT 'MP、距離與範圍的客戶端證據狀態' AFTER `evidence_status`;

CREATE TABLE IF NOT EXISTS `god2_game`.`skill_client_metadata` (
    `official_client_item_id` int NOT NULL COMMENT '官方客戶端技能書物品編號',
    `official_display_id` int NULL COMMENT '官方客戶端顯示編號',
    `name_zh_tw` varchar(256) NOT NULL COMMENT '技能繁體中文名稱',
    `description_zh_tw` varchar(500) NULL COMMENT '技能書用途說明',
    `skill_mode_zh_tw` varchar(32) NULL COMMENT '主動、被動或特殊技能',
    `skill_category_zh_tw` varchar(128) NULL COMMENT '技能分類',
    `skill_tier` int NULL COMMENT '技能階級',
    `mp_cost` int NULL COMMENT '官方客戶端標示 MP 消耗',
    `attack_range` int NULL COMMENT '官方客戶端標示攻擊距離',
    `target_scope_zh_tw` varchar(256) NULL COMMENT '官方客戶端繁中作用範圍',
    `official_effect_text_zh_tw` varchar(1500) NULL COMMENT '官方客戶端效果文字',
    `evidence_status` varchar(40) NOT NULL COMMENT '中繼資料證據狀態',
    `live_verified` tinyint(1) NOT NULL DEFAULT 0 COMMENT '是否經實機驗證',
    `observed_at_utc` datetime(6) NULL COMMENT '實機驗證時間',
    `created_at_utc` datetime(6) NOT NULL DEFAULT UTC_TIMESTAMP(6),
    `updated_at_utc` datetime(6) NOT NULL DEFAULT UTC_TIMESTAMP(6) ON UPDATE CURRENT_TIMESTAMP(6),
    PRIMARY KEY (`official_client_item_id`),
    KEY `ix_skill_client_metadata_display` (`official_display_id`),
    KEY `ix_skill_client_metadata_name` (`name_zh_tw`),
    CONSTRAINT `ck_skill_client_metadata_mp` CHECK (`mp_cost` IS NULL OR `mp_cost` >= 0),
    CONSTRAINT `ck_skill_client_metadata_range` CHECK (`attack_range` IS NULL OR `attack_range` >= 0),
    CONSTRAINT `ck_skill_client_metadata_verified` CHECK (`live_verified` IN (0,1))
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci COMMENT='官方客戶端技能書 MP、攻擊距離與作用範圍完整目錄';

CREATE TABLE IF NOT EXISTS `god2_game`.`skill_client_metadata_mappings` (
    `official_client_item_id` int NOT NULL COMMENT '官方客戶端技能書物品編號',
    `skill_id` bigint NOT NULL COMMENT '正式技能編號',
    `mapping_method` varchar(40) NOT NULL COMMENT '對應方法',
    `evidence_status` varchar(40) NOT NULL COMMENT '對應證據狀態',
    `created_at_utc` datetime(6) NOT NULL DEFAULT UTC_TIMESTAMP(6),
    PRIMARY KEY (`official_client_item_id`,`skill_id`),
    KEY `ix_skill_client_metadata_mapping_skill` (`skill_id`),
    CONSTRAINT `fk_skill_client_metadata_mapping_metadata`
        FOREIGN KEY (`official_client_item_id`)
        REFERENCES `god2_game`.`skill_client_metadata` (`official_client_item_id`),
    CONSTRAINT `fk_skill_client_metadata_mapping_skill`
        FOREIGN KEY (`skill_id`)
        REFERENCES `god2_game`.`skills` (`skill_id`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci COMMENT='官方客戶端技能書與正式技能的證據對應';

INSERT INTO `god2_game`.`skill_client_metadata`
    (`official_client_item_id`,`official_display_id`,`name_zh_tw`,`description_zh_tw`,
     `skill_mode_zh_tw`,`skill_category_zh_tw`,`skill_tier`,`mp_cost`,`attack_range`,
     `target_scope_zh_tw`,`official_effect_text_zh_tw`,`evidence_status`,`live_verified`,`observed_at_utc`)
SELECT source_row.`OfficialClientItemId`,source_row.`OfficialDisplayId`,source_row.`SkillNameZhTw`,
       source_row.`DescriptionZhTw`,source_row.`SkillModeZhTw`,source_row.`SkillCategoryZhTw`,
       source_row.`SkillLevel`,source_row.`MpCost`,source_row.`AttackRange`,source_row.`TargetScopeZhTw`,
       source_row.`OfficialEffectTextZhTw`,'OfficialClientStatic',source_row.`LiveVerified`,source_row.`ObservedAtUtc`
FROM `god2`.`official_skill_book_catalog` source_row
ON DUPLICATE KEY UPDATE
    `official_display_id`=VALUES(`official_display_id`),
    `name_zh_tw`=VALUES(`name_zh_tw`),
    `description_zh_tw`=VALUES(`description_zh_tw`),
    `skill_mode_zh_tw`=VALUES(`skill_mode_zh_tw`),
    `skill_category_zh_tw`=VALUES(`skill_category_zh_tw`),
    `skill_tier`=VALUES(`skill_tier`),
    `mp_cost`=VALUES(`mp_cost`),
    `attack_range`=VALUES(`attack_range`),
    `target_scope_zh_tw`=VALUES(`target_scope_zh_tw`),
    `official_effect_text_zh_tw`=VALUES(`official_effect_text_zh_tw`),
    `evidence_status`=VALUES(`evidence_status`),
    `live_verified`=VALUES(`live_verified`),
    `observed_at_utc`=VALUES(`observed_at_utc`);

INSERT IGNORE INTO `god2_game`.`skill_client_metadata_mappings`
    (`official_client_item_id`,`skill_id`,`mapping_method`,`evidence_status`)
SELECT metadata.`official_client_item_id`,skill_row.`skill_id`,
       'ExactNameIgnoringSpacing','OfficialClientStatic'
FROM `god2_game`.`skill_client_metadata` metadata
JOIN `god2_game`.`skills` skill_row
  ON REPLACE(REPLACE(skill_row.`name_zh_tw`,' ',''),'　','') =
     REPLACE(REPLACE(metadata.`name_zh_tw`,' ',''),'　','');

UPDATE `god2_game`.`skills` skill_row
JOIN (
    SELECT mapping.`skill_id`,MIN(mapping.`official_client_item_id`) AS `official_client_item_id`
    FROM `god2_game`.`skill_client_metadata_mappings` mapping
    GROUP BY mapping.`skill_id`
) canonical_mapping ON canonical_mapping.`skill_id`=skill_row.`skill_id`
JOIN `god2_game`.`skill_client_metadata` metadata
  ON metadata.`official_client_item_id`=canonical_mapping.`official_client_item_id`
SET skill_row.`official_client_item_id`=metadata.`official_client_item_id`,
    skill_row.`official_display_id`=metadata.`official_display_id`,
    skill_row.`mp_cost`=COALESCE(metadata.`mp_cost`,skill_row.`mp_cost`),
    skill_row.`attack_range`=COALESCE(metadata.`attack_range`,skill_row.`attack_range`),
    skill_row.`target_scope_zh_tw`=COALESCE(NULLIF(metadata.`target_scope_zh_tw`,''),skill_row.`target_scope_zh_tw`),
    skill_row.`client_metadata_evidence_status`='OfficialClientStatic';

CREATE OR REPLACE VIEW `god2_game`.`vw_skill_client_metadata_readable` AS
SELECT metadata.`official_client_item_id` AS `技能書物品編號`,
       metadata.`official_display_id` AS `官方顯示編號`,
       metadata.`name_zh_tw` AS `技能名稱`,
       metadata.`skill_mode_zh_tw` AS `技能型態`,
       metadata.`skill_category_zh_tw` AS `技能分類`,
       metadata.`skill_tier` AS `技能階級`,
       metadata.`mp_cost` AS `MP消耗`,
       metadata.`attack_range` AS `攻擊距離`,
       metadata.`target_scope_zh_tw` AS `作用範圍`,
       metadata.`official_effect_text_zh_tw` AS `官方效果文字`,
       COUNT(mapping.`skill_id`) AS `正式技能對應數`,
       GROUP_CONCAT(DISTINCT skill_row.`name_zh_tw` ORDER BY skill_row.`name_zh_tw` SEPARATOR '、') AS `對應正式技能名稱`,
       CASE WHEN COUNT(mapping.`skill_id`)>0 THEN '已按名稱精確對應' ELSE '等待正式技能身分對應' END AS `對應狀態`,
       CASE WHEN metadata.`live_verified`=1 THEN '已實機驗證' ELSE '官方客戶端靜態資料' END AS `資料證據`
FROM `god2_game`.`skill_client_metadata` metadata
LEFT JOIN `god2_game`.`skill_client_metadata_mappings` mapping
  ON mapping.`official_client_item_id`=metadata.`official_client_item_id`
LEFT JOIN `god2_game`.`skills` skill_row ON skill_row.`skill_id`=mapping.`skill_id`
GROUP BY metadata.`official_client_item_id`,metadata.`official_display_id`,metadata.`name_zh_tw`,
         metadata.`skill_mode_zh_tw`,metadata.`skill_category_zh_tw`,metadata.`skill_tier`,metadata.`mp_cost`,
         metadata.`attack_range`,metadata.`target_scope_zh_tw`,metadata.`official_effect_text_zh_tw`,metadata.`live_verified`;

CREATE OR REPLACE VIEW `god2_game`.`vw_skills_full` AS
SELECT skill_row.`skill_id` AS `技能編號`,
       skill_row.`name_zh_tw` AS `技能名稱`,
       skill_row.`name_original` AS `原文名稱`,
       skill_row.`official_client_item_id` AS `技能書物品編號`,
       skill_row.`official_display_id` AS `官方顯示編號`,
       skill_row.`skill_family` AS `技能家族`,
       skill_row.`skill_category` AS `技能分類`,
       skill_row.`damage_type` AS `傷害類型`,
       skill_row.`element` AS `五行`,
       skill_row.`required_level` AS `需求等級`,
       skill_row.`maximum_level` AS `最高等級`,
       skill_row.`mp_cost` AS `MP消耗`,
       skill_row.`hp_cost` AS `HP消耗`,
       skill_row.`attack_range` AS `攻擊距離`,
       skill_row.`target_scope_zh_tw` AS `作用範圍`,
       skill_row.`target_side` AS `目標陣營`,
       skill_row.`target_type` AS `目標類型`,
       skill_row.`cooldown_rounds` AS `冷卻回合`,
       skill_row.`consumes_turn` AS `消耗回合`,
       skill_row.`client_metadata_evidence_status` AS `客戶端資料證據`,
       skill_row.`evidence_status` AS `技能效果證據`,
       skill_row.`enabled` AS `正式啟用`,
       skill_row.`admin_note` AS `管理備註`
FROM `god2_game`.`skills` skill_row;

SET @god2_sync_mode = NULL;
