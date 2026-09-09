-- Publish client item identity, categories, use flags and readable restrictions.

ALTER TABLE `god2_game`.`items`
    ADD COLUMN IF NOT EXISTS `client_item_id` int NULL COMMENT 'Official client item display ID' AFTER `item_id`,
    ADD COLUMN IF NOT EXISTS `source_item_type` varchar(32) NULL COMMENT 'Official client gamedata section' AFTER `item_family`,
    ADD UNIQUE KEY IF NOT EXISTS `ux_items_client_item_id` (`client_item_id`),
    ADD KEY IF NOT EXISTS `ix_items_source_item_type` (`source_item_type`);

UPDATE `god2_game`.`items` formal_row
JOIN `god2`.`items` source_row ON source_row.`Id` = formal_row.`item_id`
LEFT JOIN `god2`.`item_content_profiles` profile_row ON profile_row.`ItemId` = source_row.`Id`
SET formal_row.`client_item_id` = CAST(JSON_UNQUOTE(JSON_EXTRACT(source_row.`PayloadJson`, '$.clientItemId')) AS UNSIGNED),
    formal_row.`code` = CONCAT('item_', CAST(JSON_UNQUOTE(JSON_EXTRACT(source_row.`PayloadJson`, '$.clientItemId')) AS UNSIGNED)),
    formal_row.`source_item_type` = JSON_UNQUOTE(JSON_EXTRACT(source_row.`PayloadJson`, '$.itemType')),
    formal_row.`item_family` = COALESCE(NULLIF(profile_row.`ItemFamily`, ''), formal_row.`item_family`),
    formal_row.`item_category` = CASE COALESCE(NULLIF(profile_row.`ItemFamily`, ''), formal_row.`item_family`)
        WHEN 'Weapon' THEN 'Equipment'
        WHEN 'Armor' THEN 'Equipment'
        WHEN 'Helmet' THEN 'Equipment'
        WHEN 'Accessory' THEN 'Equipment'
        WHEN 'Equipment' THEN 'Equipment'
        WHEN 'Consumable' THEN 'Consumable'
        WHEN 'QuestItem' THEN 'Quest'
        WHEN 'Material' THEN 'Material'
        WHEN 'Currency' THEN 'CurrencyLike'
        ELSE 'Generic'
    END,
    formal_row.`tradable` = CASE JSON_UNQUOTE(JSON_EXTRACT(source_row.`PayloadJson`, '$.rawFields[19]')) WHEN '1' THEN 1 WHEN '0' THEN 0 ELSE NULL END,
    formal_row.`droppable` = CASE JSON_UNQUOTE(JSON_EXTRACT(source_row.`PayloadJson`, '$.rawFields[20]')) WHEN '1' THEN 1 WHEN '0' THEN 0 ELSE NULL END,
    formal_row.`storable` = CASE JSON_UNQUOTE(JSON_EXTRACT(source_row.`PayloadJson`, '$.rawFields[21]')) WHEN '1' THEN 1 WHEN '0' THEN 0 ELSE NULL END,
    formal_row.`stackable` = CASE JSON_UNQUOTE(JSON_EXTRACT(source_row.`PayloadJson`, '$.rawFields[22]')) WHEN '1' THEN 1 WHEN '0' THEN 0 ELSE NULL END,
    formal_row.`usable` = CASE
        WHEN JSON_UNQUOTE(JSON_EXTRACT(source_row.`PayloadJson`, '$.rawFields[14]')) = '1'
          OR JSON_UNQUOTE(JSON_EXTRACT(source_row.`PayloadJson`, '$.rawFields[15]')) = '1' THEN 1
        WHEN JSON_UNQUOTE(JSON_EXTRACT(source_row.`PayloadJson`, '$.rawFields[14]')) = '0'
         AND JSON_UNQUOTE(JSON_EXTRACT(source_row.`PayloadJson`, '$.rawFields[15]')) = '0' THEN 0
        ELSE NULL
    END,
    formal_row.`equippable` = CASE JSON_UNQUOTE(JSON_EXTRACT(source_row.`PayloadJson`, '$.rawFields[16]')) WHEN '1' THEN 1 WHEN '0' THEN 0 ELSE NULL END,
    formal_row.`use_on_other` = CASE JSON_UNQUOTE(JSON_EXTRACT(source_row.`PayloadJson`, '$.rawFields[17]')) WHEN '1' THEN 1 WHEN '0' THEN 0 ELSE NULL END,
    formal_row.`evidence_status` = 'Recovered',
    formal_row.`admin_note` = CASE
        WHEN formal_row.`required_level` IS NULL OR formal_row.`maximum_stack` IS NULL
            THEN 'Client item identity and usage flags recovered; server-only limits remain blocked until runtime evidence is available.'
        ELSE formal_row.`admin_note`
    END;

CREATE TABLE IF NOT EXISTS `god2_game`.`item_usage_rules` (
    `item_id` bigint NOT NULL,
    `normal_use` tinyint(1) NULL COMMENT 'Can be used outside battle',
    `battle_use` tinyint(1) NULL COMMENT 'Can be used during battle',
    `equippable` tinyint(1) NULL COMMENT 'Can be equipped',
    `use_on_other` tinyint(1) NULL COMMENT 'Can target another actor',
    `hotkey_allowed` tinyint(1) NULL COMMENT 'Can be assigned to a hotkey',
    `class_restriction_zh_tw` varchar(200) NULL,
    `minimum_rebirth` int NULL,
    `gender_restriction_zh_tw` varchar(20) NULL,
    `equipment_target_restriction_zh_tw` varchar(200) NULL,
    `condition_text_zh_tw` text NULL,
    `field_evidence_status` varchar(30) NOT NULL DEFAULT 'Recovered',
    `text_rule_evidence_status` varchar(30) NOT NULL DEFAULT 'Unknown',
    `source_reference_zh_tw` varchar(300) NOT NULL,
    `enabled` tinyint(1) NOT NULL DEFAULT 0,
    `updated_at_utc` datetime(6) NOT NULL DEFAULT UTC_TIMESTAMP(6) ON UPDATE CURRENT_TIMESTAMP(6),
    PRIMARY KEY (`item_id`),
    KEY `ix_item_usage_rules_class` (`class_restriction_zh_tw`),
    KEY `ix_item_usage_rules_rebirth` (`minimum_rebirth`),
    CONSTRAINT `fk_item_usage_rules_item` FOREIGN KEY (`item_id`) REFERENCES `god2_game`.`items` (`item_id`) ON DELETE CASCADE,
    CONSTRAINT `ck_item_usage_rules_enabled` CHECK (`enabled` IN (0,1)),
    CONSTRAINT `ck_item_usage_rules_field_evidence` CHECK (`field_evidence_status` IN ('Verified','Recovered','Derived','Candidate','EvidenceBlocked','Unknown')),
    CONSTRAINT `ck_item_usage_rules_text_evidence` CHECK (`text_rule_evidence_status` IN ('Verified','Recovered','Derived','Candidate','EvidenceBlocked','Unknown'))
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci COMMENT='Readable item usage and restriction rules';

INSERT INTO `god2_game`.`item_usage_rules`
    (`item_id`,`normal_use`,`battle_use`,`equippable`,`use_on_other`,`hotkey_allowed`,
     `class_restriction_zh_tw`,`minimum_rebirth`,`gender_restriction_zh_tw`,`equipment_target_restriction_zh_tw`,
     `condition_text_zh_tw`,`field_evidence_status`,`text_rule_evidence_status`,`source_reference_zh_tw`,`enabled`)
SELECT formal_row.`item_id`,
    CASE JSON_UNQUOTE(JSON_EXTRACT(source_row.`PayloadJson`, '$.rawFields[14]')) WHEN '1' THEN 1 WHEN '0' THEN 0 ELSE NULL END,
    CASE JSON_UNQUOTE(JSON_EXTRACT(source_row.`PayloadJson`, '$.rawFields[15]')) WHEN '1' THEN 1 WHEN '0' THEN 0 ELSE NULL END,
    CASE JSON_UNQUOTE(JSON_EXTRACT(source_row.`PayloadJson`, '$.rawFields[16]')) WHEN '1' THEN 1 WHEN '0' THEN 0 ELSE NULL END,
    CASE JSON_UNQUOTE(JSON_EXTRACT(source_row.`PayloadJson`, '$.rawFields[17]')) WHEN '1' THEN 1 WHEN '0' THEN 0 ELSE NULL END,
    CASE JSON_UNQUOTE(JSON_EXTRACT(source_row.`PayloadJson`, '$.rawFields[18]')) WHEN '1' THEN 1 WHEN '0' THEN 0 ELSE NULL END,
    NULLIF(CONCAT_WS('、',
        IF(source_row.`DescriptionZhTw` LIKE '%劍客%', '劍客', NULL),
        IF(source_row.`DescriptionZhTw` LIKE '%謀士%', '謀士', NULL),
        IF(source_row.`DescriptionZhTw` LIKE '%藥師%', '藥師', NULL),
        IF(source_row.`DescriptionZhTw` LIKE '%仙道%', '仙道', NULL)), ''),
    CASE
        WHEN source_row.`DescriptionZhTw` LIKE '%十轉%' THEN 10
        WHEN source_row.`DescriptionZhTw` LIKE '%九轉%' THEN 9
        WHEN source_row.`DescriptionZhTw` LIKE '%八轉%' THEN 8
        WHEN source_row.`DescriptionZhTw` LIKE '%七轉%' THEN 7
        WHEN source_row.`DescriptionZhTw` LIKE '%六轉%' THEN 6
        WHEN source_row.`DescriptionZhTw` LIKE '%五轉%' THEN 5
        WHEN source_row.`DescriptionZhTw` LIKE '%四轉%' THEN 4
        WHEN source_row.`DescriptionZhTw` LIKE '%三轉%' THEN 3
        WHEN source_row.`DescriptionZhTw` LIKE '%二轉%' THEN 2
        WHEN source_row.`DescriptionZhTw` LIKE '%一轉%' THEN 1
        ELSE NULL
    END,
    CASE
        WHEN source_row.`DescriptionZhTw` REGEXP '限.*男性|男性裝備' THEN '男性'
        WHEN source_row.`DescriptionZhTw` REGEXP '限.*女性|女性裝備' THEN '女性'
        ELSE NULL
    END,
    CASE
        WHEN source_row.`DescriptionZhTw` LIKE '%插卡限定：%' THEN SUBSTRING_INDEX(SUBSTRING_INDEX(source_row.`DescriptionZhTw`, '插卡限定：', -1), '\n', 1)
        ELSE NULL
    END,
    CASE
        WHEN source_row.`DescriptionZhTw` REGEXP '限.*使用|限定|插卡限定|男性裝備|女性裝備|不可|不能'
            THEN source_row.`DescriptionZhTw`
        ELSE NULL
    END,
    'Recovered',
    CASE
        WHEN source_row.`DescriptionZhTw` REGEXP '限.*使用|限定|插卡限定|男性裝備|女性裝備|不可|不能' THEN 'Derived'
        ELSE 'Unknown'
    END,
    '官方客戶端 gamedata.csvZ 欄位與繁體道具說明',
    0
FROM `god2_game`.`items` formal_row
JOIN `god2`.`items` source_row ON source_row.`Id` = formal_row.`item_id`
ON DUPLICATE KEY UPDATE
    `normal_use`=VALUES(`normal_use`),`battle_use`=VALUES(`battle_use`),`equippable`=VALUES(`equippable`),
    `use_on_other`=VALUES(`use_on_other`),`hotkey_allowed`=VALUES(`hotkey_allowed`),
    `class_restriction_zh_tw`=VALUES(`class_restriction_zh_tw`),`minimum_rebirth`=VALUES(`minimum_rebirth`),
    `gender_restriction_zh_tw`=VALUES(`gender_restriction_zh_tw`),
    `equipment_target_restriction_zh_tw`=VALUES(`equipment_target_restriction_zh_tw`),
    `condition_text_zh_tw`=VALUES(`condition_text_zh_tw`),
    `field_evidence_status`=VALUES(`field_evidence_status`),`text_rule_evidence_status`=VALUES(`text_rule_evidence_status`),
    `source_reference_zh_tw`=VALUES(`source_reference_zh_tw`);

INSERT INTO `god2_game`.`equipment`
    (`item_id`,`equipment_type`,`equipment_slot`,`physical_attack_bonus`,`magic_attack_bonus`,
     `physical_defense_bonus`,`magic_defense_bonus`,`hp_bonus`,`mp_bonus`,`speed_bonus`,`enabled`,`admin_note`)
SELECT formal_row.`item_id`,profile_row.`ItemFamily`,profile_row.`EquipmentSlot`,
    profile_row.`PhysicalAttackBonus`,profile_row.`MagicAttackBonus`,profile_row.`PhysicalDefenseBonus`,
    profile_row.`MagicDefenseBonus`,profile_row.`HpBonus`,profile_row.`MpBonus`,profile_row.`SpeedBonus`,0,
    'Official client equipment fields recovered; server-side equip validation remains disabled until runtime verification.'
FROM `god2_game`.`items` formal_row
JOIN `god2`.`item_content_profiles` profile_row ON profile_row.`ItemId` = formal_row.`item_id`
WHERE formal_row.`equippable` = 1
ON DUPLICATE KEY UPDATE
    `equipment_type`=VALUES(`equipment_type`),`equipment_slot`=VALUES(`equipment_slot`),
    `physical_attack_bonus`=VALUES(`physical_attack_bonus`),`magic_attack_bonus`=VALUES(`magic_attack_bonus`),
    `physical_defense_bonus`=VALUES(`physical_defense_bonus`),`magic_defense_bonus`=VALUES(`magic_defense_bonus`),
    `hp_bonus`=VALUES(`hp_bonus`),`mp_bonus`=VALUES(`mp_bonus`),`speed_bonus`=VALUES(`speed_bonus`),
    `admin_note`=VALUES(`admin_note`);

CREATE OR REPLACE VIEW `god2_game`.`vw_items_classified` AS
SELECT item_row.`client_item_id` AS `客戶端道具ID`,item_row.`code` AS `可讀代碼`,item_row.`name_zh_tw` AS `繁體名稱`,
       item_row.`description_zh_tw` AS `用途說明`,item_row.`item_category` AS `主要分類`,item_row.`item_family` AS `細分類`,
       item_row.`source_item_type` AS `來源類型`,rule_row.`normal_use` AS `平時可用`,rule_row.`battle_use` AS `戰鬥可用`,
       item_row.`equippable` AS `可裝備`,item_row.`tradable` AS `可交易`,item_row.`droppable` AS `可丟棄`,
       item_row.`storable` AS `可存倉`,item_row.`stackable` AS `可堆疊`,item_row.`use_on_other` AS `可對他人使用`,
       rule_row.`class_restriction_zh_tw` AS `職業限制`,rule_row.`minimum_rebirth` AS `最低轉生`,
       rule_row.`gender_restriction_zh_tw` AS `性別限制`,rule_row.`equipment_target_restriction_zh_tw` AS `裝備部位限制`,
       rule_row.`text_rule_evidence_status` AS `限制證據狀態`,item_row.`evidence_status` AS `道具證據狀態`,item_row.`enabled` AS `服務端啟用`
FROM `god2_game`.`items` item_row
LEFT JOIN `god2_game`.`item_usage_rules` rule_row ON rule_row.`item_id` = item_row.`item_id`;

