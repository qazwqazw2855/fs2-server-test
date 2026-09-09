CREATE DATABASE IF NOT EXISTS `god2_research`
    CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci;

DROP VIEW IF EXISTS `god2_game`.`vw_equipment_enhancement_rules_readable`;

CREATE TABLE IF NOT EXISTS `god2_research`.`equipment_enhancement_material_evidence` (
    `material_item_id` bigint NOT NULL,
    `client_item_id` int NOT NULL,
    `evidence_status` varchar(30) NOT NULL,
    `source_reference_zh_tw` varchar(500) NOT NULL,
    `archived_at_utc` datetime(6) NOT NULL DEFAULT utc_timestamp(6),
    PRIMARY KEY (`material_item_id`),
    UNIQUE KEY `ux_equipment_enhancement_material_evidence_client` (`client_item_id`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci
  COMMENT='裝備強化材料的證據狀態與來源；正式表只保留強化規則';

CREATE TABLE IF NOT EXISTS `god2_research`.`equipment_enhancement_rate_evidence` (
    `grade` varchar(20) NOT NULL,
    `target_enhancement_level` int NOT NULL,
    `evidence_status` varchar(30) NOT NULL,
    `source_reference_zh_tw` varchar(500) NOT NULL,
    `archived_at_utc` datetime(6) NOT NULL DEFAULT utc_timestamp(6),
    PRIMARY KEY (`grade`,`target_enhancement_level`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci
  COMMENT='裝備強化成功率的證據狀態與來源；正式表只保留成功率規則';

INSERT INTO `god2_research`.`equipment_enhancement_material_evidence`
    (`material_item_id`,`client_item_id`,`evidence_status`,`source_reference_zh_tw`)
SELECT `material_item_id`,`client_item_id`,`evidence_status`,`source_reference_zh_tw`
FROM `god2_game`.`equipment_enhancement_materials`
ON DUPLICATE KEY UPDATE
    `client_item_id`=VALUES(`client_item_id`),
    `evidence_status`=VALUES(`evidence_status`),
    `source_reference_zh_tw`=VALUES(`source_reference_zh_tw`);

INSERT INTO `god2_research`.`equipment_enhancement_rate_evidence`
    (`grade`,`target_enhancement_level`,`evidence_status`,`source_reference_zh_tw`)
SELECT `grade`,`target_enhancement_level`,`evidence_status`,`source_reference_zh_tw`
FROM `god2_game`.`equipment_enhancement_rates`
ON DUPLICATE KEY UPDATE
    `evidence_status`=VALUES(`evidence_status`),
    `source_reference_zh_tw`=VALUES(`source_reference_zh_tw`);

ALTER TABLE `god2_game`.`equipment_enhancement_materials`
    DROP CONSTRAINT IF EXISTS `ck_equipment_enhancement_material_evidence`,
    DROP COLUMN IF EXISTS `evidence_status`,
    DROP COLUMN IF EXISTS `source_reference_zh_tw`;

ALTER TABLE `god2_game`.`equipment_enhancement_rates`
    DROP CONSTRAINT IF EXISTS `ck_equipment_enhancement_rate_evidence`,
    DROP COLUMN IF EXISTS `evidence_status`,
    DROP COLUMN IF EXISTS `source_reference_zh_tw`;

CREATE OR REPLACE VIEW `god2_game`.`vw_equipment_enhancement_rules_readable` AS
SELECT material.`client_item_id` AS `客戶端材料ID`,
       material.`name_zh_tw` AS `強化道具名稱`,
       CASE material.`target_type`
           WHEN 'Weapon' THEN '武器'
           WHEN 'Equipment' THEN '防具'
       END AS `適用類別`,
       material.`equipment_tier` AS `裝備階級`,
       CASE material.`grade`
           WHEN 'General' THEN '一般'
           WHEN 'Advanced' THEN '高級'
           WHEN 'Special' THEN '特級'
       END AS `材料品質`,
       material.`minimum_increment` AS `最小強化增幅`,
       material.`maximum_increment` AS `最大強化增幅`,
       material.`maximum_durability_loss` AS `強化耐久損失`,
       CASE material.`failure_policy`
           WHEN 'DestroyTarget' THEN '失敗消失'
           WHEN 'PreserveTarget' THEN '失敗保留'
       END AS `失敗處置`,
       rate.`target_enhancement_level` AS `目標強化等級`,
       rate.`success_rate_basis_points` / 100 AS `成功率百分比`,
       material.`enabled` AS `材料啟用`,
       rate.`enabled` AS `成功率啟用`
FROM `god2_game`.`equipment_enhancement_materials` material
LEFT JOIN `god2_game`.`equipment_enhancement_rates` rate
  ON rate.`grade` = material.`grade`
ORDER BY material.`client_item_id`, rate.`target_enhancement_level`;

GRANT SELECT, INSERT, UPDATE, DELETE ON `god2_research`.`equipment_enhancement_material_evidence` TO 'god2_server'@'localhost';
GRANT SELECT, INSERT, UPDATE, DELETE ON `god2_research`.`equipment_enhancement_material_evidence` TO 'god2_server'@'127.0.0.1';
GRANT SELECT, INSERT, UPDATE, DELETE ON `god2_research`.`equipment_enhancement_rate_evidence` TO 'god2_server'@'localhost';
GRANT SELECT, INSERT, UPDATE, DELETE ON `god2_research`.`equipment_enhancement_rate_evidence` TO 'god2_server'@'127.0.0.1';
