-- Publish only deterministic consumable effects recovered from official client data.
-- Existing formal effects are replaced so the runtime never combines old and new values.

CREATE TABLE IF NOT EXISTS `god2_game`.`item_effects` (
    `item_id` bigint NOT NULL COMMENT '共用物品索引ID',
    `effect_index` int NOT NULL COMMENT '同一道具內的效果順序',
    `effect_type` varchar(32) NOT NULL COMMENT '服務端效果類型：RestoreHp或RestoreMp',
    `numeric_value` bigint NOT NULL COMMENT '固定回復數值',
    `usage_scope` varchar(16) NOT NULL COMMENT '可使用場景：World、Battle或Both',
    `target_policy` varchar(32) NOT NULL COMMENT '目標規則：Self或OtherAllowed',
    `effect_text_zh_tw` varchar(300) NOT NULL COMMENT '繁體中文可讀效果',
    `evidence_status` varchar(30) NOT NULL COMMENT '資料證據狀態',
    `source_reference_zh_tw` varchar(300) NOT NULL COMMENT '可讀證據來源',
    `enabled` tinyint(1) NOT NULL DEFAULT 0 COMMENT '是否由服務端啟用',
    `updated_at_utc` datetime(6) NOT NULL DEFAULT UTC_TIMESTAMP(6) ON UPDATE CURRENT_TIMESTAMP(6) COMMENT '更新UTC時間',
    PRIMARY KEY (`item_id`,`effect_index`),
    KEY `ix_item_effects_type` (`effect_type`,`enabled`),
    CONSTRAINT `fk_item_effects_registry` FOREIGN KEY (`item_id`) REFERENCES `god2_game`.`item_registry` (`item_id`) ON DELETE CASCADE,
    CONSTRAINT `ck_item_effects_type` CHECK (`effect_type` IN ('RestoreHp','RestoreMp')),
    CONSTRAINT `ck_item_effects_value` CHECK (`numeric_value` > 0),
    CONSTRAINT `ck_item_effects_scope` CHECK (`usage_scope` IN ('World','Battle','Both')),
    CONSTRAINT `ck_item_effects_target` CHECK (`target_policy` IN ('Self','OtherAllowed')),
    CONSTRAINT `ck_item_effects_evidence` CHECK (`evidence_status` IN ('Verified','Recovered')),
    CONSTRAINT `ck_item_effects_enabled` CHECK (`enabled` IN (0,1))
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci COMMENT='服務端已實裝且有證據的道具固定效果';

CREATE TABLE IF NOT EXISTS `god2_player`.`item_use_idempotency` (
    `idempotency_key_hash` char(64) CHARACTER SET ascii COLLATE ascii_bin NOT NULL COMMENT '使用請求冪等鍵SHA256',
    `payload_hash` char(64) CHARACTER SET ascii COLLATE ascii_bin NOT NULL COMMENT '使用請求內容SHA256',
    `transaction_id` char(36) CHARACTER SET ascii COLLATE ascii_bin NOT NULL COMMENT '道具使用交易GUID',
    `character_id` bigint NOT NULL COMMENT '角色ID',
    `item_id` bigint NOT NULL COMMENT '物品ID',
    `inventory_version_before` bigint NOT NULL COMMENT '使用前背包版本',
    `inventory_version_after` bigint NOT NULL COMMENT '使用後背包版本',
    `restored_hp` bigint NOT NULL COMMENT '實際恢復生命',
    `restored_mp` bigint NOT NULL COMMENT '實際恢復法力',
    `current_hp` bigint NOT NULL COMMENT '使用後目前生命',
    `current_mp` bigint NOT NULL COMMENT '使用後目前法力',
    `created_at_utc` datetime(6) NOT NULL COMMENT '請求建立UTC時間',
    `completed_at_utc` datetime(6) NOT NULL COMMENT '交易完成UTC時間',
    PRIMARY KEY (`idempotency_key_hash`),
    KEY `ix_item_use_idempotency_character` (`character_id`,`completed_at_utc`),
    CONSTRAINT `fk_item_use_idempotency_character` FOREIGN KEY (`character_id`) REFERENCES `god2_player`.`characters` (`character_id`) ON DELETE CASCADE,
    CONSTRAINT `fk_item_use_idempotency_item` FOREIGN KEY (`item_id`) REFERENCES `god2_game`.`item_registry` (`item_id`),
    CONSTRAINT `ck_item_use_idempotency_version` CHECK (`inventory_version_after`=`inventory_version_before`+1),
    CONSTRAINT `ck_item_use_idempotency_recovery` CHECK (`restored_hp`>=0 AND `restored_mp`>=0 AND (`restored_hp`>0 OR `restored_mp`>0))
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci COMMENT='道具使用成功交易的冪等結果';

-- rawFields[14..18] are official client usage flags. Enable these field rules while
-- keeping description-derived class, rebirth, and gender restrictions evidence-gated.
UPDATE `god2_game`.`item_usage_rules`
SET `enabled` = CASE
        WHEN `field_evidence_status` IN ('Verified','Recovered')
         AND (`normal_use` IS NOT NULL OR `battle_use` IS NOT NULL OR `equippable` IS NOT NULL OR `use_on_other` IS NOT NULL)
            THEN 1
        ELSE 0
    END;

DROP TEMPORARY TABLE IF EXISTS `tmp_verified_consumable_effects`;
CREATE TEMPORARY TABLE `tmp_verified_consumable_effects` AS
SELECT registry_row.`item_id`,
       JSON_UNQUOTE(JSON_EXTRACT(source_row.`PayloadJson`, '$.statCandidates[0]')) AS `effect_text`,
       CASE
           WHEN rule_row.`normal_use`=1 AND rule_row.`battle_use`=1 THEN 'Both'
           WHEN rule_row.`battle_use`=1 THEN 'Battle'
           WHEN rule_row.`normal_use`=1 THEN 'World'
           ELSE NULL
       END AS `usage_scope`,
       CASE WHEN rule_row.`use_on_other`=1 THEN 'OtherAllowed' ELSE 'Self' END AS `target_policy`,
       rule_row.`enabled` AS `rule_enabled`
FROM `god2_game`.`item_registry` registry_row
JOIN `god2`.`items` source_row ON source_row.`Id`=registry_row.`item_id`
JOIN `god2_game`.`item_usage_rules` rule_row ON rule_row.`item_id`=registry_row.`item_id`
WHERE registry_row.`enabled`=1
  AND registry_row.`item_category`='Consumable'
  AND JSON_UNQUOTE(JSON_EXTRACT(source_row.`PayloadJson`, '$.statCandidates[0]')) REGEXP
      '^(恢复生命[[:space:]]+[0-9]+|恢复法力[[:space:]]+[0-9]+|恢复生命[[:space:]]+[0-9]+，法力[[:space:]]+[0-9]+)$';

DELETE FROM `god2_game`.`item_effects`;

INSERT INTO `god2_game`.`item_effects`
    (`item_id`,`effect_index`,`effect_type`,`numeric_value`,`usage_scope`,`target_policy`,
     `effect_text_zh_tw`,`evidence_status`,`source_reference_zh_tw`,`enabled`)
SELECT effect_row.`item_id`,1,'RestoreHp',
       CAST(REGEXP_REPLACE(REGEXP_SUBSTR(effect_row.`effect_text`, '恢复生命[[:space:]]+[0-9]+'),'[^0-9]','') AS UNSIGNED),
       effect_row.`usage_scope`,effect_row.`target_policy`,
       CONCAT('恢復生命 ',CAST(REGEXP_REPLACE(REGEXP_SUBSTR(effect_row.`effect_text`, '恢复生命[[:space:]]+[0-9]+'),'[^0-9]','') AS UNSIGNED)),
       'Recovered','官方客戶端 gamedata.csvZ 固定回復欄位',1
FROM `tmp_verified_consumable_effects` effect_row
WHERE effect_row.`effect_text` REGEXP '^恢复生命[[:space:]]+[0-9]+'
  AND effect_row.`usage_scope` IS NOT NULL
  AND effect_row.`rule_enabled`=1;

INSERT INTO `god2_game`.`item_effects`
    (`item_id`,`effect_index`,`effect_type`,`numeric_value`,`usage_scope`,`target_policy`,
     `effect_text_zh_tw`,`evidence_status`,`source_reference_zh_tw`,`enabled`)
SELECT effect_row.`item_id`,
       CASE WHEN effect_row.`effect_text` REGEXP '^恢复生命' THEN 2 ELSE 1 END,
       'RestoreMp',
       CAST(REGEXP_REPLACE(REGEXP_SUBSTR(effect_row.`effect_text`, '法力[[:space:]]+[0-9]+'),'[^0-9]','') AS UNSIGNED),
       effect_row.`usage_scope`,effect_row.`target_policy`,
       CONCAT('恢復法力 ',CAST(REGEXP_REPLACE(REGEXP_SUBSTR(effect_row.`effect_text`, '法力[[:space:]]+[0-9]+'),'[^0-9]','') AS UNSIGNED)),
       'Recovered','官方客戶端 gamedata.csvZ 固定回復欄位',1
FROM `tmp_verified_consumable_effects` effect_row
WHERE effect_row.`effect_text` REGEXP '(^恢复法力|，法力)[[:space:]]+[0-9]+$'
  AND effect_row.`usage_scope` IS NOT NULL
  AND effect_row.`rule_enabled`=1;

DROP TEMPORARY TABLE IF EXISTS `tmp_verified_consumable_effects`;

CREATE OR REPLACE VIEW `god2_game`.`vw_item_effects_readable` AS
SELECT definition_row.`client_item_id` AS `客戶端道具ID`,
       definition_row.`name_zh_tw` AS `繁體名稱`,
       definition_row.`catalog_type_zh_tw` AS `資料表分類`,
       definition_row.`item_category` AS `主要分類`,
       effect_row.`effect_index` AS `效果順序`,
       CASE effect_row.`effect_type` WHEN 'RestoreHp' THEN '恢復生命' WHEN 'RestoreMp' THEN '恢復法力' END AS `效果類型`,
       effect_row.`numeric_value` AS `效果數值`,
       CASE effect_row.`usage_scope` WHEN 'World' THEN '平時' WHEN 'Battle' THEN '戰鬥' WHEN 'Both' THEN '平時與戰鬥' END AS `可使用場景`,
       CASE effect_row.`target_policy` WHEN 'Self' THEN '僅自己' WHEN 'OtherAllowed' THEN '可對他人' END AS `可用目標`,
       effect_row.`effect_text_zh_tw` AS `效果說明`,
       effect_row.`evidence_status` AS `證據狀態`,
       effect_row.`enabled` AS `服務端啟用`
FROM `god2_game`.`item_effects` effect_row
JOIN `god2_game`.`vw_all_item_definitions` definition_row ON definition_row.`item_id`=effect_row.`item_id`;

CREATE OR REPLACE VIEW `god2_game`.`vw_items_classified` AS
SELECT item_row.`client_item_id` AS `客戶端道具ID`,item_row.`code` AS `可讀代碼`,item_row.`name_zh_tw` AS `繁體名稱`,
       item_row.`description_zh_tw` AS `用途說明`,item_row.`catalog_type_zh_tw` AS `資料表分類`,item_row.`item_category` AS `主要分類`,
       item_row.`item_family` AS `細分類`,item_row.`source_item_type` AS `來源類型`,rule_row.`normal_use` AS `平時可用`,
       rule_row.`battle_use` AS `戰鬥可用`,item_row.`equippable` AS `可裝備`,item_row.`tradable` AS `可交易`,
       item_row.`droppable` AS `可丟棄`,item_row.`storable` AS `可存倉`,item_row.`stackable` AS `可堆疊`,
       item_row.`use_on_other` AS `可對他人使用`,rule_row.`class_restriction_zh_tw` AS `職業限制`,
       rule_row.`minimum_rebirth` AS `最低轉生`,rule_row.`gender_restriction_zh_tw` AS `性別限制`,
       rule_row.`equipment_target_restriction_zh_tw` AS `裝備部位限制`,rule_row.`field_evidence_status` AS `使用旗標證據`,
       rule_row.`text_rule_evidence_status` AS `文字限制證據`,
       GROUP_CONCAT(effect_row.`effect_text_zh_tw` ORDER BY effect_row.`effect_index` SEPARATOR '、') AS `已實裝效果`,
       item_row.`evidence_status` AS `資料證據狀態`,item_row.`enabled` AS `服務端啟用`
FROM `god2_game`.`vw_all_item_definitions` item_row
LEFT JOIN `god2_game`.`item_usage_rules` rule_row ON rule_row.`item_id`=item_row.`item_id`
LEFT JOIN `god2_game`.`item_effects` effect_row ON effect_row.`item_id`=item_row.`item_id` AND effect_row.`enabled`=1
GROUP BY item_row.`item_id`,item_row.`client_item_id`,item_row.`code`,item_row.`name_zh_tw`,item_row.`description_zh_tw`,
         item_row.`catalog_type_zh_tw`,item_row.`item_category`,item_row.`item_family`,item_row.`source_item_type`,
         rule_row.`normal_use`,rule_row.`battle_use`,item_row.`equippable`,item_row.`tradable`,item_row.`droppable`,
         item_row.`storable`,item_row.`stackable`,item_row.`use_on_other`,rule_row.`class_restriction_zh_tw`,
         rule_row.`minimum_rebirth`,rule_row.`gender_restriction_zh_tw`,rule_row.`equipment_target_restriction_zh_tw`,
         rule_row.`field_evidence_status`,rule_row.`text_rule_evidence_status`,item_row.`evidence_status`,item_row.`enabled`;
