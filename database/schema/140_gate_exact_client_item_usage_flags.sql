-- Exact-current-client item usage flags are static catalog evidence only.
-- This migration separates known client flags from server-authoritative activation.
CREATE TABLE IF NOT EXISTS `god2_game`.`client_item_usage_flag_additions` (
    `source_section` varchar(16) NOT NULL, `client_item_id` int NOT NULL,
    `resource_key` varchar(120) NOT NULL, `display_name_original` varchar(300) NOT NULL, `display_name_zh_tw` varchar(300) NOT NULL,
    `normal_use` tinyint(1) NULL, `battle_use` tinyint(1) NULL, `equippable` tinyint(1) NULL,
    `use_on_other` tinyint(1) NULL, `hotkey_allowed` tinyint(1) NULL, `tradable` tinyint(1) NULL,
    `droppable` tinyint(1) NULL, `storable` tinyint(1) NULL, `stackable` tinyint(1) NULL,
    `combine_up` tinyint(1) NULL, `combine_down` tinyint(1) NULL,
    `source_row` int NOT NULL, `source_sha256` char(64) NOT NULL,
    `evidence_status` varchar(64) NOT NULL, `binding_status` varchar(64) NOT NULL,
    `runtime_eligible` tinyint(1) NOT NULL DEFAULT 0,
    `created_at_utc` datetime(6) NOT NULL DEFAULT UTC_TIMESTAMP(6),
    PRIMARY KEY (`source_section`,`client_item_id`),
    CONSTRAINT `ck_client_item_usage_addition_runtime` CHECK (`runtime_eligible`=0),
    CONSTRAINT `ck_client_item_usage_addition_binding` CHECK (`binding_status`='EvidenceBlockedMissingCanonicalItemBinding')
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

INSERT INTO `god2_game`.`client_item_usage_flag_additions`
    (`source_section`,`client_item_id`,`resource_key`,`display_name_original`,`display_name_zh_tw`,`normal_use`,`battle_use`,`equippable`,`use_on_other`,`hotkey_allowed`,
     `tradable`,`droppable`,`storable`,`stackable`,`combine_up`,`combine_down`,`source_row`,`source_sha256`,
     `evidence_status`,`binding_status`,`runtime_eligible`)
VALUES
    ('CBK',9530,'Cbk00030','知遇集卡册','知遇集卡冊',0,0,1,0,0,1,1,1,0,1,0,11052,'c3bc7ebe8b2e7ea346c376dee25b3b07f90e82208d14f8e26f5feded18bc1e1f','VerifiedExactCurrentClientStaticFlags','EvidenceBlockedMissingCanonicalItemBinding',0),
    ('COM',26275,'COM02275','樱花小团君','櫻花小團君',1,1,1,0,1,1,1,1,0,1,1,21022,'c3bc7ebe8b2e7ea346c376dee25b3b07f90e82208d14f8e26f5feded18bc1e1f','VerifiedExactCurrentClientStaticFlags','EvidenceBlockedMissingCanonicalItemBinding',0),
    ('MIS03',31408,'Mis31308','刀七技能书融合卷','刀七技能書融合卷',1,0,0,0,0,1,1,1,1,0,1,22663,'c3bc7ebe8b2e7ea346c376dee25b3b07f90e82208d14f8e26f5feded18bc1e1f','VerifiedExactCurrentClientStaticFlags','EvidenceBlockedMissingCanonicalItemBinding',0),
    ('MIS03',31409,'Mis31309','禁传自选包','禁傳自選包',1,0,0,0,0,1,1,1,1,0,0,22664,'c3bc7ebe8b2e7ea346c376dee25b3b07f90e82208d14f8e26f5feded18bc1e1f','VerifiedExactCurrentClientStaticFlags','EvidenceBlockedMissingCanonicalItemBinding',0),
    ('SCD',9701,'Scd99166','知遇卡(壹)','知遇卡(壹)',0,0,0,0,0,0,1,1,0,0,1,11219,'c3bc7ebe8b2e7ea346c376dee25b3b07f90e82208d14f8e26f5feded18bc1e1f','VerifiedExactCurrentClientStaticFlags','EvidenceBlockedMissingCanonicalItemBinding',0),
    ('SCD',9702,'Scd99167','知遇卡(贰)','知遇卡(貳)',0,0,0,0,0,0,1,1,0,0,1,11220,'c3bc7ebe8b2e7ea346c376dee25b3b07f90e82208d14f8e26f5feded18bc1e1f','VerifiedExactCurrentClientStaticFlags','EvidenceBlockedMissingCanonicalItemBinding',0),
    ('SCD',9703,'Scd99168','知遇卡(叁)','知遇卡(叄)',0,0,0,0,0,0,1,1,0,0,1,11221,'c3bc7ebe8b2e7ea346c376dee25b3b07f90e82208d14f8e26f5feded18bc1e1f','VerifiedExactCurrentClientStaticFlags','EvidenceBlockedMissingCanonicalItemBinding',0),
    ('SCD',9704,'Scd99169','知遇卡(肆)','知遇卡(肆)',0,0,0,0,0,0,1,1,0,0,1,11222,'c3bc7ebe8b2e7ea346c376dee25b3b07f90e82208d14f8e26f5feded18bc1e1f','VerifiedExactCurrentClientStaticFlags','EvidenceBlockedMissingCanonicalItemBinding',0),
    ('SCD',9705,'Scd99170','知遇卡(伍)','知遇卡(伍)',0,0,0,0,0,0,1,1,0,0,1,11223,'c3bc7ebe8b2e7ea346c376dee25b3b07f90e82208d14f8e26f5feded18bc1e1f','VerifiedExactCurrentClientStaticFlags','EvidenceBlockedMissingCanonicalItemBinding',0),
    ('SCD',9706,'Scd99171','知遇卡(陆)','知遇卡(陸)',0,0,0,0,0,0,1,1,0,0,1,11224,'c3bc7ebe8b2e7ea346c376dee25b3b07f90e82208d14f8e26f5feded18bc1e1f','VerifiedExactCurrentClientStaticFlags','EvidenceBlockedMissingCanonicalItemBinding',0),
    ('SCD',9707,'Scd99172','知遇卡(柒)','知遇卡(柒)',0,0,0,0,0,0,1,1,0,0,1,11225,'c3bc7ebe8b2e7ea346c376dee25b3b07f90e82208d14f8e26f5feded18bc1e1f','VerifiedExactCurrentClientStaticFlags','EvidenceBlockedMissingCanonicalItemBinding',0)
ON DUPLICATE KEY UPDATE
    `resource_key`=VALUES(`resource_key`),`display_name_original`=VALUES(`display_name_original`),`display_name_zh_tw`=VALUES(`display_name_zh_tw`),
    `normal_use`=VALUES(`normal_use`),`battle_use`=VALUES(`battle_use`),`equippable`=VALUES(`equippable`),
    `use_on_other`=VALUES(`use_on_other`),`hotkey_allowed`=VALUES(`hotkey_allowed`),`tradable`=VALUES(`tradable`),
    `droppable`=VALUES(`droppable`),`storable`=VALUES(`storable`),`stackable`=VALUES(`stackable`),
    `combine_up`=VALUES(`combine_up`),`combine_down`=VALUES(`combine_down`),`source_row`=VALUES(`source_row`),
    `source_sha256`=VALUES(`source_sha256`),`evidence_status`=VALUES(`evidence_status`),
    `binding_status`='EvidenceBlockedMissingCanonicalItemBinding',`runtime_eligible`=0;

ALTER TABLE `god2_game`.`item_usage_rules`
    ADD COLUMN IF NOT EXISTS `exact_client_source_sha256` char(64) NULL AFTER `source_reference_zh_tw`,
    ADD COLUMN IF NOT EXISTS `activation_evidence_status` varchar(64) NOT NULL DEFAULT 'EvidenceBlockedMissingOfficialItemUseTransition' AFTER `exact_client_source_sha256`,
    ADD COLUMN IF NOT EXISTS `runtime_eligible` tinyint(1) NOT NULL DEFAULT 0 AFTER `activation_evidence_status`;

DROP PROCEDURE IF EXISTS `god2_game`.`assert_exact_item_usage_flag_catalog`;
DELIMITER $$
CREATE PROCEDURE `god2_game`.`assert_exact_item_usage_flag_catalog`()
BEGIN
    DECLARE actual_count bigint DEFAULT 0;
    DECLARE actual_hash char(64) DEFAULT NULL;
    SET SESSION group_concat_max_len=16777216;
    SELECT COUNT(*), LOWER(SHA2(GROUP_CONCAT(CONCAT_WS('|',
               registry_row.`source_item_type`,registry_row.`client_item_id`,
               COALESCE(CAST(rule_row.`normal_use` AS CHAR),'N'),COALESCE(CAST(rule_row.`battle_use` AS CHAR),'N'),
               COALESCE(CAST(rule_row.`equippable` AS CHAR),'N'),COALESCE(CAST(rule_row.`use_on_other` AS CHAR),'N'),
               COALESCE(CAST(rule_row.`hotkey_allowed` AS CHAR),'N'))
               ORDER BY registry_row.`source_item_type`,registry_row.`client_item_id` SEPARATOR '\n'),256))
      INTO actual_count,actual_hash
      FROM `god2_game`.`item_usage_rules` rule_row
      JOIN `god2_game`.`item_registry` registry_row ON registry_row.`item_id`=rule_row.`item_id`;
    IF actual_count<>17407 OR actual_hash<>'52cf0d96a89e9451f625fd5e70beefdf9c84539e0c93bfe2f654c3206cc77b07' THEN
        SIGNAL SQLSTATE '45000' SET MESSAGE_TEXT='Exact-client item usage flag catalog does not match the formal rows';
    END IF;
END$$
DELIMITER ;
CALL `god2_game`.`assert_exact_item_usage_flag_catalog`();
DROP PROCEDURE `god2_game`.`assert_exact_item_usage_flag_catalog`;

UPDATE `god2_game`.`item_usage_rules`
SET `field_evidence_status`='Verified',
    `source_reference_zh_tw`='Exact current client Data2/Patch/Comm/gamedata.csvZ fields 14..18; static flags only',
    `exact_client_source_sha256`='c3bc7ebe8b2e7ea346c376dee25b3b07f90e82208d14f8e26f5feded18bc1e1f',
    `activation_evidence_status`='EvidenceBlockedMissingOfficialItemUseTransition',
    `runtime_eligible`=0,
    `enabled`=0;

ALTER TABLE `god2_game`.`item_usage_rules`
    ADD CONSTRAINT `ck_item_usage_rule_exact_source` CHECK (`exact_client_source_sha256`='c3bc7ebe8b2e7ea346c376dee25b3b07f90e82208d14f8e26f5feded18bc1e1f'),
    ADD CONSTRAINT `ck_item_usage_rule_runtime_gate` CHECK (`runtime_eligible`=0 AND `enabled`=0 AND `activation_evidence_status`='EvidenceBlockedMissingOfficialItemUseTransition');

ALTER TABLE `god2_game`.`item_effects`
    ADD COLUMN IF NOT EXISTS `activation_evidence_status` varchar(64) NOT NULL DEFAULT 'EvidenceBlockedMissingOfficialItemUseTransition' AFTER `evidence_status`,
    ADD COLUMN IF NOT EXISTS `runtime_eligible` tinyint(1) NOT NULL DEFAULT 0 AFTER `activation_evidence_status`;

UPDATE `god2_game`.`item_effects`
SET `activation_evidence_status`='EvidenceBlockedMissingOfficialItemUseTransition',
    `runtime_eligible`=0,
    `enabled`=0;

ALTER TABLE `god2_game`.`item_effects`
    ADD CONSTRAINT `ck_item_effect_runtime_gate` CHECK (`runtime_eligible`=0 AND `enabled`=0 AND `activation_evidence_status`='EvidenceBlockedMissingOfficialItemUseTransition');