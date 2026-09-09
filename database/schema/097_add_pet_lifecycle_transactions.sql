CREATE TABLE IF NOT EXISTS `god2_player`.`pet_instance_identity_sequence` (
    `sequence_id` bigint NOT NULL AUTO_INCREMENT COMMENT '戰寵實例流水號',
    `reserved_at_utc` datetime(6) NOT NULL DEFAULT UTC_TIMESTAMP(6) COMMENT '保留時間',
    PRIMARY KEY (`sequence_id`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci COMMENT='戰寵實例ID配置器';

CREATE TABLE IF NOT EXISTS `god2_player`.`pet_operation_idempotency` (
    `idempotency_key_hash` char(64) CHARACTER SET ascii COLLATE ascii_bin NOT NULL COMMENT '冪等鍵SHA256',
    `payload_hash` char(64) CHARACTER SET ascii COLLATE ascii_bin NOT NULL COMMENT '請求內容SHA256',
    `transaction_id` char(36) CHARACTER SET ascii COLLATE ascii_bin NOT NULL COMMENT '交易ID',
    `character_id` bigint NOT NULL COMMENT '角色ID',
    `operation_type` varchar(30) CHARACTER SET ascii COLLATE ascii_bin NOT NULL COMMENT '操作類型',
    `pet_instance_id` bigint NULL COMMENT '戰寵實例ID',
    `result_json` longtext CHARACTER SET utf8mb4 COLLATE utf8mb4_bin NOT NULL COMMENT '可重播結果JSON',
    `completed_at_utc` datetime(6) NOT NULL DEFAULT UTC_TIMESTAMP(6) COMMENT '完成時間',
    PRIMARY KEY (`idempotency_key_hash`),
    KEY `ix_pet_operation_idempotency_character` (`character_id`,`completed_at_utc`),
    CONSTRAINT `fk_pet_operation_idempotency_character` FOREIGN KEY (`character_id`) REFERENCES `god2_player`.`characters` (`character_id`) ON DELETE CASCADE,
    CONSTRAINT `ck_pet_operation_idempotency_type` CHECK (`operation_type` IN ('Acquire','LevelUp','FourthSkill','Death'))
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci COMMENT='戰寵正式交易冪等紀錄';

CREATE TABLE IF NOT EXISTS `god2_player`.`pet_operation_audit` (
    `audit_id` bigint NOT NULL AUTO_INCREMENT COMMENT '稽核ID',
    `transaction_id` char(36) CHARACTER SET ascii COLLATE ascii_bin NOT NULL COMMENT '交易ID',
    `character_id` bigint NOT NULL COMMENT '角色ID',
    `operation_type` varchar(30) CHARACTER SET ascii COLLATE ascii_bin NOT NULL COMMENT '操作類型',
    `pet_instance_id` bigint NULL COMMENT '戰寵實例ID',
    `detail_json` longtext CHARACTER SET utf8mb4 COLLATE utf8mb4_bin NOT NULL COMMENT '操作結果JSON',
    `completed_at_utc` datetime(6) NOT NULL DEFAULT UTC_TIMESTAMP(6) COMMENT '完成時間',
    PRIMARY KEY (`audit_id`),
    KEY `ix_pet_operation_audit_character` (`character_id`,`completed_at_utc`),
    KEY `ix_pet_operation_audit_transaction` (`transaction_id`),
    CONSTRAINT `fk_pet_operation_audit_character` FOREIGN KEY (`character_id`) REFERENCES `god2_player`.`characters` (`character_id`) ON DELETE CASCADE
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci COMMENT='戰寵正式交易稽核';

CREATE TABLE IF NOT EXISTS `god2_game`.`pet_skill_learning_items` (
    `item_id` bigint NOT NULL COMMENT '戰寵技能學習道具ID',
    `skill_id` bigint NOT NULL COMMENT '可學習技能ID',
    `evidence_status` varchar(30) NOT NULL DEFAULT 'EvidenceBlocked' COMMENT '證據狀態',
    `enabled` tinyint(1) NOT NULL DEFAULT 0 COMMENT '是否啟用',
    `admin_note` varchar(500) NULL COMMENT '服主管理備註',
    PRIMARY KEY (`item_id`),
    CONSTRAINT `fk_pet_skill_learning_item` FOREIGN KEY (`item_id`) REFERENCES `god2_game`.`item_registry` (`item_id`),
    CONSTRAINT `fk_pet_skill_learning_skill` FOREIGN KEY (`skill_id`) REFERENCES `god2_game`.`skills` (`skill_id`),
    CONSTRAINT `ck_pet_skill_learning_enabled` CHECK (`enabled` IN (0,1))
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci COMMENT='第四技能道具與技能的正式對應';

INSERT IGNORE INTO `god2_game`.`pet_template_skills`
    (`pet_template_id`,`slot_index`,`skill_id`,`skill_name_cache`,`skill_level`,`enabled`,`admin_note`)
SELECT source_row.`pet_template_id`,source_row.`slot_index`,MIN(skill_row.`skill_id`),source_row.`skill_name_zh_tw`,source_row.`required_level`,1,
       '依繁體中文技能名稱精確對應；未對應者不發布'
FROM (
    SELECT `pet_template_id`,1 AS `slot_index`,`level_20_skill_name_zh_tw` AS `skill_name_zh_tw`,20 AS `required_level`
    FROM `god2_game`.`pet_templates` WHERE `enabled`=1 AND `level_20_skill_name_zh_tw` IS NOT NULL
    UNION ALL
    SELECT `pet_template_id`,2,`level_40_skill_name_zh_tw`,40
    FROM `god2_game`.`pet_templates` WHERE `enabled`=1 AND `level_40_skill_name_zh_tw` IS NOT NULL
    UNION ALL
    SELECT `pet_template_id`,3,`level_60_skill_name_zh_tw`,60
    FROM `god2_game`.`pet_templates` WHERE `enabled`=1 AND `level_60_skill_name_zh_tw` IS NOT NULL
) source_row
JOIN `god2_game`.`skills` skill_row
  ON skill_row.`name_zh_tw`=source_row.`skill_name_zh_tw` AND skill_row.`enabled`=1
GROUP BY source_row.`pet_template_id`,source_row.`slot_index`,source_row.`skill_name_zh_tw`,source_row.`required_level`;

CREATE OR REPLACE VIEW `god2_game`.`vw_pet_skill_mapping_queue` AS
SELECT template_row.`pet_template_id` AS `戰寵模板ID`,template_row.`name_zh_tw` AS `戰寵名稱`,
       source_row.`技能格`,source_row.`需求等級`,source_row.`技能名稱`,
       CASE WHEN mapped_row.`skill_id` IS NULL THEN '尚未對應正式技能ID' ELSE '已對應' END AS `對應狀態`
FROM `god2_game`.`pet_templates` template_row
JOIN (
    SELECT `pet_template_id`,1 AS `技能格`,20 AS `需求等級`,`level_20_skill_name_zh_tw` AS `技能名稱` FROM `god2_game`.`pet_templates`
    UNION ALL
    SELECT `pet_template_id`,2,40,`level_40_skill_name_zh_tw` FROM `god2_game`.`pet_templates`
    UNION ALL
    SELECT `pet_template_id`,3,60,`level_60_skill_name_zh_tw` FROM `god2_game`.`pet_templates`
) source_row ON source_row.`pet_template_id`=template_row.`pet_template_id`
LEFT JOIN `god2_game`.`pet_template_skills` mapped_row
  ON mapped_row.`pet_template_id`=source_row.`pet_template_id` AND mapped_row.`slot_index`=source_row.`技能格`
WHERE template_row.`enabled`=1 AND source_row.`技能名稱` IS NOT NULL;
