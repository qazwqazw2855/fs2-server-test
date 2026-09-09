-- Publish field-level exact-current-client item permission/combination flags.
-- Values are static catalog evidence only and never authorize gameplay execution.

CREATE TABLE IF NOT EXISTS `god2_game`.`item_static_permission_evidence` (
    `item_id` bigint NOT NULL,
    `client_item_id` int NOT NULL,
    `source_section` varchar(16) NOT NULL,
    `normal_use_flag` tinyint(1) NULL,
    `battle_use_flag` tinyint(1) NULL,
    `equippable_flag` tinyint(1) NULL,
    `use_on_other_flag` tinyint(1) NULL,
    `hotkey_allowed_flag` tinyint(1) NULL,
    `tradable_flag` tinyint(1) NULL,
    `droppable_flag` tinyint(1) NULL,
    `storable_flag` tinyint(1) NULL,
    `stackable_flag` tinyint(1) NULL,
    `combine_up_flag` tinyint(1) NULL,
    `combine_down_flag` tinyint(1) NULL,
    `source_path` varchar(260) NOT NULL,
    `source_row` int NOT NULL,
    `source_sha256` char(64) NOT NULL,
    `normalized_catalog_sha256` char(64) NOT NULL,
    `evidence_status` varchar(64) NOT NULL,
    `semantic_status` varchar(80) NOT NULL,
    `activation_evidence_status` varchar(80) NOT NULL,
    `catalog_enabled` tinyint(1) NOT NULL DEFAULT 1,
    `runtime_eligible` tinyint(1) NOT NULL DEFAULT 0,
    `created_at_utc` datetime(6) NOT NULL DEFAULT UTC_TIMESTAMP(6),
    `updated_at_utc` datetime(6) NOT NULL DEFAULT UTC_TIMESTAMP(6) ON UPDATE CURRENT_TIMESTAMP(6),
    PRIMARY KEY (`item_id`),
    UNIQUE KEY `ux_item_static_permission_client` (`source_section`,`client_item_id`),
    CONSTRAINT `fk_item_static_permission_registry` FOREIGN KEY (`item_id`) REFERENCES `god2_game`.`item_registry` (`item_id`) ON DELETE CASCADE,
    CONSTRAINT `ck_item_static_permission_catalog` CHECK (`catalog_enabled` IN (0,1)),
    CONSTRAINT `ck_item_static_permission_runtime` CHECK (`runtime_eligible`=0),
    CONSTRAINT `ck_item_static_permission_evidence` CHECK (`evidence_status`='VerifiedExactCurrentClientStaticFlags'),
    CONSTRAINT `ck_item_static_permission_activation` CHECK (`activation_evidence_status`='EvidenceBlockedMissingOfficialTransition')
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

DROP PROCEDURE IF EXISTS `god2_game`.`assert_exact_item_static_permission_catalog`;
DELIMITER $$
CREATE PROCEDURE `god2_game`.`assert_exact_item_static_permission_catalog`()
BEGIN
    DECLARE actual_count bigint DEFAULT 0;
    DECLARE actual_hash char(64) DEFAULT NULL;
    SET SESSION group_concat_max_len=16777216;
    SELECT COUNT(*), LOWER(SHA2(GROUP_CONCAT(CONCAT_WS('|',
               registry_row.`source_item_type`,registry_row.`client_item_id`,
               COALESCE(CAST(rule_row.`normal_use` AS CHAR),'N'),COALESCE(CAST(rule_row.`battle_use` AS CHAR),'N'),
               COALESCE(CAST(rule_row.`equippable` AS CHAR),'N'),COALESCE(CAST(rule_row.`use_on_other` AS CHAR),'N'),
               COALESCE(CAST(rule_row.`hotkey_allowed` AS CHAR),'N'),COALESCE(CAST(registry_row.`tradable` AS CHAR),'N'),
               COALESCE(CAST(registry_row.`droppable` AS CHAR),'N'),COALESCE(CAST(registry_row.`storable` AS CHAR),'N'),
               COALESCE(CAST(registry_row.`stackable` AS CHAR),'N'),
               COALESCE(JSON_UNQUOTE(JSON_EXTRACT(source_row.`PayloadJson`,'$.rawFields[23]')),'N'),
               COALESCE(JSON_UNQUOTE(JSON_EXTRACT(source_row.`PayloadJson`,'$.rawFields[24]')),'N'))
               ORDER BY registry_row.`source_item_type`,registry_row.`client_item_id` SEPARATOR '\n'),256))
      INTO actual_count,actual_hash
      FROM `god2_game`.`item_usage_rules` rule_row
      JOIN `god2_game`.`item_registry` registry_row ON registry_row.`item_id`=rule_row.`item_id`
      JOIN `god2`.`items` source_row ON source_row.`Id`=registry_row.`item_id`;
    IF actual_count<>17407 OR actual_hash<>'41ce1d3cc9914e1f31289374bd530bcdf843eb3c8a27996dbd4313b0d25b95fb' THEN
        SIGNAL SQLSTATE '45000' SET MESSAGE_TEXT='Formal item static flags do not match the exact-current catalog';
    END IF;
END$$
DELIMITER ;
CALL `god2_game`.`assert_exact_item_static_permission_catalog`();
DROP PROCEDURE `god2_game`.`assert_exact_item_static_permission_catalog`;

INSERT INTO `god2_game`.`item_static_permission_evidence`
    (`item_id`,`client_item_id`,`source_section`,`normal_use_flag`,`battle_use_flag`,`equippable_flag`,`use_on_other_flag`,
     `hotkey_allowed_flag`,`tradable_flag`,`droppable_flag`,`storable_flag`,`stackable_flag`,`combine_up_flag`,`combine_down_flag`,
     `source_path`,`source_row`,`source_sha256`,`normalized_catalog_sha256`,`evidence_status`,`semantic_status`,
     `activation_evidence_status`,`catalog_enabled`,`runtime_eligible`)
SELECT registry_row.`item_id`,registry_row.`client_item_id`,registry_row.`source_item_type`,
       rule_row.`normal_use`,rule_row.`battle_use`,rule_row.`equippable`,rule_row.`use_on_other`,rule_row.`hotkey_allowed`,
       registry_row.`tradable`,registry_row.`droppable`,registry_row.`storable`,registry_row.`stackable`,
       CASE JSON_UNQUOTE(JSON_EXTRACT(source_row.`PayloadJson`,'$.rawFields[23]')) WHEN '1' THEN 1 WHEN '0' THEN 0 ELSE NULL END,
       CASE JSON_UNQUOTE(JSON_EXTRACT(source_row.`PayloadJson`,'$.rawFields[24]')) WHEN '1' THEN 1 WHEN '0' THEN 0 ELSE NULL END,
       'Data2/Patch/Comm/gamedata.csvZ',
       CAST(JSON_UNQUOTE(JSON_EXTRACT(source_row.`PayloadJson`,'$.sourceRecordIndex')) AS UNSIGNED),
       'c3bc7ebe8b2e7ea346c376dee25b3b07f90e82208d14f8e26f5feded18bc1e1f',
       '41ce1d3cc9914e1f31289374bd530bcdf843eb3c8a27996dbd4313b0d25b95fb',
       'VerifiedExactCurrentClientStaticFlags','ExactCurrentValues_CrossVersionConsumerCorroborated',
       'EvidenceBlockedMissingOfficialTransition',1,0
FROM `god2_game`.`item_registry` registry_row
JOIN `god2_game`.`item_usage_rules` rule_row ON rule_row.`item_id`=registry_row.`item_id`
JOIN `god2`.`items` source_row ON source_row.`Id`=registry_row.`item_id`
ON DUPLICATE KEY UPDATE
    `client_item_id`=VALUES(`client_item_id`),`source_section`=VALUES(`source_section`),
    `normal_use_flag`=VALUES(`normal_use_flag`),`battle_use_flag`=VALUES(`battle_use_flag`),
    `equippable_flag`=VALUES(`equippable_flag`),`use_on_other_flag`=VALUES(`use_on_other_flag`),
    `hotkey_allowed_flag`=VALUES(`hotkey_allowed_flag`),`tradable_flag`=VALUES(`tradable_flag`),
    `droppable_flag`=VALUES(`droppable_flag`),`storable_flag`=VALUES(`storable_flag`),
    `stackable_flag`=VALUES(`stackable_flag`),`combine_up_flag`=VALUES(`combine_up_flag`),
    `combine_down_flag`=VALUES(`combine_down_flag`),`source_path`=VALUES(`source_path`),
    `source_row`=VALUES(`source_row`),`source_sha256`=VALUES(`source_sha256`),
    `normalized_catalog_sha256`=VALUES(`normalized_catalog_sha256`),`evidence_status`=VALUES(`evidence_status`),
    `semantic_status`=VALUES(`semantic_status`),`activation_evidence_status`=VALUES(`activation_evidence_status`),
    `catalog_enabled`=1,`runtime_eligible`=0;

CREATE OR REPLACE VIEW `god2_game`.`vw_item_static_permission_evidence_readable` AS
SELECT evidence_row.`client_item_id` AS `客戶端道具ID`,definition_row.`name_zh_tw` AS `名稱`,
       evidence_row.`source_section` AS `來源分類`,evidence_row.`normal_use_flag` AS `平時使用旗標`,
       evidence_row.`battle_use_flag` AS `戰鬥使用旗標`,evidence_row.`equippable_flag` AS `裝備旗標`,
       evidence_row.`use_on_other_flag` AS `對他使用旗標`,evidence_row.`hotkey_allowed_flag` AS `快捷鍵旗標`,
       evidence_row.`tradable_flag` AS `交易旗標`,evidence_row.`droppable_flag` AS `丟棄旗標`,
       evidence_row.`storable_flag` AS `存倉旗標`,evidence_row.`stackable_flag` AS `堆疊旗標`,
       evidence_row.`combine_up_flag` AS `向上合成旗標`,evidence_row.`combine_down_flag` AS `向下合成旗標`,
       evidence_row.`evidence_status` AS `靜態證據`,evidence_row.`activation_evidence_status` AS `執行證據`,
       evidence_row.`runtime_eligible` AS `可執行`
FROM `god2_game`.`item_static_permission_evidence` evidence_row
JOIN `god2_game`.`vw_all_item_definitions` definition_row ON definition_row.`item_id`=evidence_row.`item_id`
WHERE evidence_row.`catalog_enabled`=1;
