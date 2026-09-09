CREATE TABLE IF NOT EXISTS `god2_research`.`skill_catalog_evidence` (
    `skill_id` bigint NOT NULL,
    `evidence_status` varchar(30) NOT NULL,
    `client_metadata_evidence_status` varchar(40) NULL,
    `admin_note` varchar(500) NULL,
    `moved_at_utc` datetime(6) NOT NULL DEFAULT CURRENT_TIMESTAMP(6),
    PRIMARY KEY (`skill_id`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

INSERT INTO `god2_research`.`skill_catalog_evidence`
    (`skill_id`,`evidence_status`,`client_metadata_evidence_status`,`admin_note`)
SELECT `skill_id`,`evidence_status`,`client_metadata_evidence_status`,`admin_note`
FROM `god2_game`.`skills`
ON DUPLICATE KEY UPDATE
    `evidence_status`=VALUES(`evidence_status`),
    `client_metadata_evidence_status`=VALUES(`client_metadata_evidence_status`),
    `admin_note`=VALUES(`admin_note`);

DROP VIEW IF EXISTS `god2_game`.`vw_skills_full`;
DROP VIEW IF EXISTS `god2_game`.`vw_skill_client_metadata_readable`;

ALTER TABLE `god2_game`.`skills`
    DROP COLUMN IF EXISTS `evidence_status`,
    DROP COLUMN IF EXISTS `client_metadata_evidence_status`,
    DROP COLUMN IF EXISTS `admin_note`;

CREATE OR REPLACE VIEW `god2_game`.`vw_skills_full` AS
SELECT
    skill_row.`skill_id` AS `技能編號`,
    skill_row.`code` AS `服務端代碼`,
    skill_row.`name_zh_tw` AS `技能名稱`,
    skill_row.`name_original` AS `原始名稱`,
    skill_row.`official_client_item_id` AS `官方技能書物品編號`,
    skill_row.`official_display_id` AS `官方顯示編號`,
    skill_row.`skill_family` AS `技能系列`,
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
    skill_row.`cast_rounds` AS `施放回合`,
    skill_row.`duration_rounds` AS `持續回合`,
    skill_row.`consumes_turn` AS `消耗回合`,
    skill_row.`status_effect_id` AS `狀態效果編號`,
    skill_row.`animation_id` AS `動畫編號`,
    skill_row.`effect_id` AS `特效編號`,
    CASE skill_row.`enabled` WHEN 1 THEN '已啟用' ELSE '未啟用' END AS `服務端狀態`
FROM `god2_game`.`skills` skill_row;

CREATE OR REPLACE VIEW `god2_game`.`vw_skill_client_metadata_readable` AS
SELECT
    metadata.`official_client_item_id` AS `技能書物品編號`,
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
    CASE WHEN COUNT(mapping.`skill_id`)>0 THEN '已對應正式技能' ELSE '等待正式技能身份對應' END AS `對應狀態`,
    CASE WHEN metadata.`live_verified`=1 THEN '已實機驗證' ELSE '官方客戶端靜態資料' END AS `資料狀態`
FROM `god2_game`.`skill_client_metadata` metadata
LEFT JOIN `god2_game`.`skill_client_metadata_mappings` mapping
  ON mapping.`official_client_item_id`=metadata.`official_client_item_id`
LEFT JOIN `god2_game`.`skills` skill_row ON skill_row.`skill_id`=mapping.`skill_id`
GROUP BY metadata.`official_client_item_id`,metadata.`official_display_id`,metadata.`name_zh_tw`,
         metadata.`skill_mode_zh_tw`,metadata.`skill_category_zh_tw`,metadata.`skill_tier`,metadata.`mp_cost`,
         metadata.`attack_range`,metadata.`target_scope_zh_tw`,metadata.`official_effect_text_zh_tw`,metadata.`live_verified`;
