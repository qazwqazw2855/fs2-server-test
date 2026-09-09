-- Publish official-client enhancement material behavior and a clearly marked
-- compatibility rate curve for the percentages the original server never exposed.

CREATE TABLE IF NOT EXISTS `god2_game`.`equipment_enhancement_materials` (
    `material_item_id` bigint NOT NULL COMMENT '強化材料物品ID',
    `client_item_id` int NOT NULL COMMENT '客戶端道具ID',
    `name_zh_tw` varchar(200) NOT NULL COMMENT '繁體中文名稱',
    `target_type` varchar(20) NOT NULL COMMENT '適用類別：Weapon或Equipment',
    `equipment_tier` int NOT NULL COMMENT '適用裝備階級1至10',
    `grade` varchar(20) NOT NULL COMMENT '品質：General、Advanced或Special',
    `minimum_increment` int NOT NULL COMMENT '成功時最少強化增幅',
    `maximum_increment` int NOT NULL COMMENT '成功時最多強化增幅',
    `maximum_durability_loss` int NOT NULL COMMENT '成功時最大耐久上限損失',
    `failure_policy` varchar(20) NOT NULL COMMENT '失敗處置：DestroyTarget或PreserveTarget',
    `evidence_status` varchar(30) NOT NULL COMMENT '證據狀態',
    `source_reference_zh_tw` varchar(500) NOT NULL COMMENT '可讀證據來源',
    `enabled` tinyint(1) NOT NULL DEFAULT 1 COMMENT '服務端是否啟用',
    `updated_at_utc` datetime(6) NOT NULL DEFAULT UTC_TIMESTAMP(6) ON UPDATE CURRENT_TIMESTAMP(6),
    PRIMARY KEY (`material_item_id`),
    UNIQUE KEY `ux_equipment_enhancement_material_client` (`client_item_id`),
    KEY `ix_equipment_enhancement_material_target` (`target_type`,`equipment_tier`,`grade`,`enabled`),
    CONSTRAINT `fk_equipment_enhancement_material_registry` FOREIGN KEY (`material_item_id`) REFERENCES `god2_game`.`item_registry` (`item_id`) ON DELETE CASCADE,
    CONSTRAINT `ck_equipment_enhancement_material_target` CHECK (`target_type` IN ('Weapon','Equipment')),
    CONSTRAINT `ck_equipment_enhancement_material_tier` CHECK (`equipment_tier` BETWEEN 1 AND 10),
    CONSTRAINT `ck_equipment_enhancement_material_grade` CHECK (`grade` IN ('General','Advanced','Special')),
    CONSTRAINT `ck_equipment_enhancement_material_increment` CHECK (`minimum_increment` >= 1 AND `maximum_increment` >= `minimum_increment`),
    CONSTRAINT `ck_equipment_enhancement_material_durability` CHECK (`maximum_durability_loss` >= 0),
    CONSTRAINT `ck_equipment_enhancement_material_failure` CHECK (`failure_policy` IN ('DestroyTarget','PreserveTarget')),
    CONSTRAINT `ck_equipment_enhancement_material_evidence` CHECK (`evidence_status` IN ('OfficialClient','BahamutVerified')),
    CONSTRAINT `ck_equipment_enhancement_material_enabled` CHECK (`enabled` IN (0,1))
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci COMMENT='武器與防具強化材料正式規則';

CREATE TABLE IF NOT EXISTS `god2_game`.`equipment_enhancement_rates` (
    `grade` varchar(20) NOT NULL COMMENT '強化材料品質',
    `target_enhancement_level` int NOT NULL COMMENT '本次成功後至少到達的強化等級',
    `success_rate_basis_points` int NOT NULL COMMENT '成功率萬分比，10000代表100%',
    `evidence_status` varchar(30) NOT NULL COMMENT 'BahamutVerified或CompatibilityEstimate',
    `source_reference_zh_tw` varchar(500) NOT NULL COMMENT '成功率來源與適用說明',
    `enabled` tinyint(1) NOT NULL DEFAULT 1 COMMENT '服務端是否啟用',
    `updated_at_utc` datetime(6) NOT NULL DEFAULT UTC_TIMESTAMP(6) ON UPDATE CURRENT_TIMESTAMP(6),
    PRIMARY KEY (`grade`,`target_enhancement_level`),
    CONSTRAINT `ck_equipment_enhancement_rate_grade` CHECK (`grade` IN ('General','Advanced','Special')),
    CONSTRAINT `ck_equipment_enhancement_rate_level` CHECK (`target_enhancement_level` BETWEEN 1 AND 10),
    CONSTRAINT `ck_equipment_enhancement_rate_value` CHECK (`success_rate_basis_points` BETWEEN 0 AND 10000),
    CONSTRAINT `ck_equipment_enhancement_rate_evidence` CHECK (`evidence_status` IN ('OfficialClient','BahamutVerified','CompatibilityEstimate')),
    CONSTRAINT `ck_equipment_enhancement_rate_enabled` CHECK (`enabled` IN (0,1))
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci COMMENT='裝備強化成功率服務端規則';

DELETE FROM `god2_game`.`equipment_enhancement_materials`;

INSERT INTO `god2_game`.`equipment_enhancement_materials`
    (`material_item_id`,`client_item_id`,`name_zh_tw`,`target_type`,`equipment_tier`,`grade`,
     `minimum_increment`,`maximum_increment`,`maximum_durability_loss`,`failure_policy`,
     `evidence_status`,`source_reference_zh_tw`,`enabled`)
SELECT registry_row.`item_id`,registry_row.`client_item_id`,registry_row.`name_zh_tw`,
       CASE WHEN registry_row.`client_item_id` BETWEEN 9101 AND 9130 THEN 'Weapon' ELSE 'Equipment' END,
       MOD(registry_row.`client_item_id`-9101,10)+1,
       CASE
           WHEN registry_row.`client_item_id` BETWEEN 9101 AND 9110 OR registry_row.`client_item_id` BETWEEN 9131 AND 9140 THEN 'General'
           WHEN registry_row.`client_item_id` BETWEEN 9111 AND 9120 OR registry_row.`client_item_id` BETWEEN 9141 AND 9150 THEN 'Advanced'
           ELSE 'Special'
       END,
       1,
       CASE
           WHEN registry_row.`client_item_id` BETWEEN 9101 AND 9110 OR registry_row.`client_item_id` BETWEEN 9131 AND 9140 THEN 1
           ELSE 2
       END,
       CASE
           WHEN registry_row.`client_item_id` BETWEEN 9101 AND 9110 OR registry_row.`client_item_id` BETWEEN 9131 AND 9140 THEN 3
           WHEN registry_row.`client_item_id` BETWEEN 9111 AND 9120 OR registry_row.`client_item_id` BETWEEN 9141 AND 9150 THEN 2
           ELSE 0
       END,
       CASE
           WHEN registry_row.`client_item_id` BETWEEN 9101 AND 9110 OR registry_row.`client_item_id` BETWEEN 9131 AND 9140 THEN 'DestroyTarget'
           ELSE 'PreserveTarget'
       END,
       'OfficialClient',
       '官方客戶端 gamedata.csvZ 道具說明；一般失敗毀裝、高級失敗保裝、特級100%成功。',
       1
FROM `god2_game`.`item_registry` registry_row
WHERE registry_row.`client_item_id` BETWEEN 9101 AND 9160
  AND registry_row.`enabled`=1;

DELETE FROM `god2_game`.`equipment_enhancement_rates`;

INSERT INTO `god2_game`.`equipment_enhancement_rates`
    (`grade`,`target_enhancement_level`,`success_rate_basis_points`,`evidence_status`,`source_reference_zh_tw`,`enabled`)
VALUES
    ('General',1,10000,'BahamutVerified','巴哈姆特封神2精華區：武器與防具安定值為+3。',1),
    ('General',2,10000,'BahamutVerified','巴哈姆特封神2精華區：武器與防具安定值為+3。',1),
    ('General',3,10000,'BahamutVerified','巴哈姆特封神2精華區：武器與防具安定值為+3。',1),
    ('General',4,8000,'CompatibilityEstimate','官方未公開+4以上百分比；本機服務端相近曲線，可由資料表調整。',1),
    ('General',5,7000,'CompatibilityEstimate','官方未公開+4以上百分比；本機服務端相近曲線，可由資料表調整。',1),
    ('General',6,6000,'CompatibilityEstimate','官方未公開+4以上百分比；本機服務端相近曲線，可由資料表調整。',1),
    ('General',7,5000,'CompatibilityEstimate','官方未公開+4以上百分比；本機服務端相近曲線，可由資料表調整。',1),
    ('General',8,4000,'CompatibilityEstimate','官方未公開+4以上百分比；本機服務端相近曲線，可由資料表調整。',1),
    ('General',9,3000,'CompatibilityEstimate','官方未公開+4以上百分比；本機服務端相近曲線，可由資料表調整。',1),
    ('General',10,2000,'CompatibilityEstimate','官方未公開+4以上百分比；本機服務端相近曲線，可由資料表調整。',1),
    ('Advanced',1,10000,'BahamutVerified','巴哈姆特封神2精華區：武器與防具安定值為+3。',1),
    ('Advanced',2,10000,'BahamutVerified','巴哈姆特封神2精華區：武器與防具安定值為+3。',1),
    ('Advanced',3,10000,'BahamutVerified','巴哈姆特封神2精華區：武器與防具安定值為+3。',1),
    ('Advanced',4,9000,'CompatibilityEstimate','維持高於一般強化道具的官方客戶端語意。',1),
    ('Advanced',5,8500,'CompatibilityEstimate','維持高於一般強化道具的官方客戶端語意。',1),
    ('Advanced',6,8000,'CompatibilityEstimate','維持高於一般強化道具的官方客戶端語意。',1),
    ('Advanced',7,7500,'CompatibilityEstimate','維持高於一般強化道具的官方客戶端語意。',1),
    ('Advanced',8,7000,'CompatibilityEstimate','維持高於一般強化道具的官方客戶端語意。',1),
    ('Advanced',9,6500,'CompatibilityEstimate','維持高於一般強化道具的官方客戶端語意。',1),
    ('Advanced',10,6000,'CompatibilityEstimate','維持高於一般強化道具的官方客戶端語意。',1),
    ('Special',1,10000,'OfficialClient','官方客戶端道具說明：強化機率100%成功。',1),
    ('Special',2,10000,'OfficialClient','官方客戶端道具說明：強化機率100%成功。',1),
    ('Special',3,10000,'OfficialClient','官方客戶端道具說明：強化機率100%成功。',1),
    ('Special',4,10000,'OfficialClient','官方客戶端道具說明：強化機率100%成功。',1),
    ('Special',5,10000,'OfficialClient','官方客戶端道具說明：強化機率100%成功。',1),
    ('Special',6,10000,'OfficialClient','官方客戶端道具說明：強化機率100%成功。',1),
    ('Special',7,10000,'OfficialClient','官方客戶端道具說明：強化機率100%成功。',1),
    ('Special',8,10000,'OfficialClient','官方客戶端道具說明：強化機率100%成功。',1),
    ('Special',9,10000,'OfficialClient','官方客戶端道具說明：強化機率100%成功。',1),
    ('Special',10,10000,'OfficialClient','官方客戶端道具說明：強化機率100%成功。',1);

ALTER TABLE `god2_player`.`equipment_instances`
    ADD COLUMN IF NOT EXISTS `maximum_durability_penalty` int NOT NULL DEFAULT 0 COMMENT '歷次強化造成的最大耐久上限總損失' AFTER `maximum_durability`;

CREATE TABLE IF NOT EXISTS `god2_player`.`equipment_enhancement_idempotency` (
    `idempotency_key_hash` char(64) CHARACTER SET ascii COLLATE ascii_bin NOT NULL COMMENT '請求冪等鍵SHA256',
    `payload_hash` char(64) CHARACTER SET ascii COLLATE ascii_bin NOT NULL COMMENT '請求內容SHA256',
    `transaction_id` char(36) CHARACTER SET ascii COLLATE ascii_bin NOT NULL COMMENT '強化交易GUID',
    `character_id` bigint NOT NULL COMMENT '角色ID',
    `material_item_id` bigint NOT NULL COMMENT '強化材料ID',
    `target_inventory_id` bigint NOT NULL COMMENT '目標裝備實例ID',
    `inventory_version_before` bigint NOT NULL COMMENT '交易前背包版本',
    `inventory_version_after` bigint NOT NULL COMMENT '交易後背包版本',
    `enhancement_succeeded` tinyint(1) NOT NULL COMMENT '本次強化是否成功',
    `target_destroyed` tinyint(1) NOT NULL COMMENT '目標是否因失敗損毀',
    `enhancement_level_before` int NOT NULL COMMENT '強化前等級',
    `enhancement_level_after` int NOT NULL COMMENT '強化後等級',
    `applied_increment` int NOT NULL COMMENT '本次實際增加等級',
    `success_rate_basis_points` int NOT NULL COMMENT '本次採用成功率萬分比',
    `current_durability_after` int NULL COMMENT '交易後目前耐久',
    `maximum_durability_after` int NULL COMMENT '交易後最大耐久',
    `created_at_utc` datetime(6) NOT NULL COMMENT '請求建立UTC時間',
    `completed_at_utc` datetime(6) NOT NULL COMMENT '交易完成UTC時間',
    PRIMARY KEY (`idempotency_key_hash`),
    KEY `ix_equipment_enhancement_idempotency_character` (`character_id`,`completed_at_utc`),
    CONSTRAINT `fk_equipment_enhancement_idempotency_character` FOREIGN KEY (`character_id`) REFERENCES `god2_player`.`characters` (`character_id`) ON DELETE CASCADE,
    CONSTRAINT `fk_equipment_enhancement_idempotency_material` FOREIGN KEY (`material_item_id`) REFERENCES `god2_game`.`item_registry` (`item_id`),
    CONSTRAINT `ck_equipment_enhancement_idempotency_version` CHECK (`inventory_version_after`=`inventory_version_before`+1),
    CONSTRAINT `ck_equipment_enhancement_idempotency_flags` CHECK (`enhancement_succeeded` IN (0,1) AND `target_destroyed` IN (0,1))
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci COMMENT='裝備強化成功交易冪等結果';

CREATE OR REPLACE VIEW `god2_game`.`vw_equipment_enhancement_rules_readable` AS
SELECT material_row.`client_item_id` AS `客戶端道具ID`,material_row.`name_zh_tw` AS `強化道具名稱`,
       CASE material_row.`target_type` WHEN 'Weapon' THEN '武器' ELSE '防具' END AS `適用類別`,
       material_row.`equipment_tier` AS `適用裝備階級`,
       CASE material_row.`grade` WHEN 'General' THEN '一般' WHEN 'Advanced' THEN '高級' ELSE '特級' END AS `道具品質`,
       CONCAT(material_row.`minimum_increment`,'至',material_row.`maximum_increment`) AS `成功強化增幅`,
       material_row.`maximum_durability_loss` AS `最大耐久上限損失`,
       CASE material_row.`failure_policy` WHEN 'DestroyTarget' THEN '失敗毀損裝備' ELSE '失敗保留裝備' END AS `失敗處置`,
       rate_row.`target_enhancement_level` AS `目標強化等級`,
       rate_row.`success_rate_basis_points`/100.0 AS `成功率百分比`,
       CASE rate_row.`evidence_status`
           WHEN 'OfficialClient' THEN '官方客戶端'
           WHEN 'BahamutVerified' THEN '巴哈精華區實證'
           ELSE '服務端相容估算'
       END AS `成功率依據`,
       rate_row.`source_reference_zh_tw` AS `規則說明`
FROM `god2_game`.`equipment_enhancement_materials` material_row
JOIN `god2_game`.`equipment_enhancement_rates` rate_row ON rate_row.`grade`=material_row.`grade` AND rate_row.`enabled`=1
WHERE material_row.`enabled`=1;

CREATE OR REPLACE VIEW `god2_player`.`vw_character_equipment_enhancement` AS
SELECT instance_row.`character_id` AS `角色ID`,instance_row.`inventory_id` AS `物品實例ID`,
       definition_row.`client_item_id` AS `客戶端道具ID`,definition_row.`name_zh_tw` AS `裝備名稱`,
       CASE instance_row.`catalog_type` WHEN 'Weapon' THEN '武器' WHEN 'Equipment' THEN '防具' ELSE '法寶' END AS `裝備分類`,
       instance_row.`enhancement_level` AS `目前強化`,instance_row.`refinement_level` AS `目前精煉`,
       instance_row.`current_durability` AS `目前耐久`,instance_row.`maximum_durability` AS `最大耐久`,
       instance_row.`maximum_durability_penalty` AS `強化耐久損失`,instance_row.`socket_count` AS `鑲嵌槽數`,
       instance_row.`metal_bonus` AS `金加成`,instance_row.`wood_bonus` AS `木加成`,
       instance_row.`water_bonus` AS `水加成`,instance_row.`fire_bonus` AS `火加成`,instance_row.`earth_bonus` AS `土加成`
FROM `god2_player`.`equipment_instances` instance_row
JOIN `god2_game`.`item_registry` definition_row ON definition_row.`item_id`=instance_row.`item_id`
WHERE instance_row.`enabled`=1;
