-- 259_translate_item_catalog_family_and_asset_status_labels.sql
-- Purpose: make item catalog categories, families, equipment types, weapon type labels, and asset validation statuses readable.

ALTER TABLE `god2_game`.`item_asset_mappings`
    DROP CONSTRAINT IF EXISTS `ck_item_asset_icon_status`,
    DROP CONSTRAINT IF EXISTS `ck_item_asset_model_status`,
    DROP CONSTRAINT IF EXISTS `ck_item_asset_overall_status`;

UPDATE `god2_game`.`item_registry`
SET `item_category` = CASE `item_category`
        WHEN 'Generic' THEN '一般'
        WHEN 'Equipment' THEN '裝備'
        WHEN 'Quest' THEN '任務'
        WHEN 'Material' THEN '材料'
        WHEN 'Consumable' THEN '消耗品'
        WHEN 'CurrencyLike' THEN '類貨幣'
        ELSE `item_category`
    END,
    `item_family` = CASE `item_family`
        WHEN 'PetItem' THEN '寵物物品'
        WHEN 'QuestItem' THEN '任務物品'
        WHEN 'Special' THEN '特殊'
        WHEN 'Armor' THEN '防具'
        WHEN 'Equipment' THEN '裝備'
        WHEN 'Weapon' THEN '武器'
        WHEN 'Material' THEN '材料'
        WHEN 'Accessory' THEN '飾品'
        WHEN 'Recipe' THEN '配方'
        WHEN 'Consumable' THEN '消耗品'
        WHEN 'Helmet' THEN '頭盔'
        WHEN 'Currency' THEN '貨幣'
        WHEN 'Egg' THEN '蛋'
        ELSE `item_family`
    END;

UPDATE `god2_game`.`items`
SET `item_category` = CASE `item_category`
        WHEN 'Generic' THEN '一般'
        WHEN 'Equipment' THEN '裝備'
        WHEN 'Quest' THEN '任務'
        WHEN 'Material' THEN '材料'
        WHEN 'Consumable' THEN '消耗品'
        WHEN 'CurrencyLike' THEN '類貨幣'
        ELSE `item_category`
    END,
    `item_family` = CASE `item_family`
        WHEN 'PetItem' THEN '寵物物品'
        WHEN 'QuestItem' THEN '任務物品'
        WHEN 'Special' THEN '特殊'
        WHEN 'Equipment' THEN '裝備'
        WHEN 'Material' THEN '材料'
        WHEN 'Recipe' THEN '配方'
        WHEN 'Consumable' THEN '消耗品'
        WHEN 'Accessory' THEN '飾品'
        WHEN 'Currency' THEN '貨幣'
        ELSE `item_family`
    END;

UPDATE `god2_game`.`equipment`
SET `equipment_type` = CASE `equipment_type`
        WHEN 'PetItem' THEN '寵物物品'
        WHEN 'Armor' THEN '防具'
        WHEN 'Accessory' THEN '飾品'
        WHEN 'Helmet' THEN '頭盔'
        WHEN 'Special' THEN '特殊'
        WHEN 'Egg' THEN '蛋'
        ELSE `equipment_type`
    END,
    `item_category` = CASE `item_category`
        WHEN 'Generic' THEN '一般'
        WHEN 'Equipment' THEN '裝備'
        ELSE `item_category`
    END,
    `item_family` = CASE `item_family`
        WHEN 'PetItem' THEN '寵物物品'
        WHEN 'Armor' THEN '防具'
        WHEN 'Accessory' THEN '飾品'
        WHEN 'Helmet' THEN '頭盔'
        WHEN 'Special' THEN '特殊'
        WHEN 'Egg' THEN '蛋'
        ELSE `item_family`
    END;

UPDATE `god2_game`.`weapons`
SET `item_category` = CASE `item_category`
        WHEN 'Equipment' THEN '裝備'
        ELSE `item_category`
    END,
    `item_family` = CASE `item_family`
        WHEN 'Weapon' THEN '武器'
        ELSE `item_family`
    END,
    `weapon_type_zh_tw` = CASE `weapon_type_zh_tw`
        WHEN 'Weapon' THEN '武器'
        ELSE `weapon_type_zh_tw`
    END;

UPDATE `god2_game`.`magic_treasures`
SET `item_category` = CASE `item_category`
        WHEN 'Generic' THEN '一般'
        ELSE `item_category`
    END,
    `item_family` = CASE `item_family`
        WHEN 'Special' THEN '特殊'
        ELSE `item_family`
    END;

UPDATE `god2_game`.`item_asset_mappings`
SET `icon_validation_status` = CASE `icon_validation_status`
        WHEN 'Verified' THEN '已驗證'
        WHEN 'RangeMissing' THEN '範圍缺失'
        WHEN 'AssetMissing' THEN '資產缺失'
        ELSE `icon_validation_status`
    END,
    `model_validation_status` = CASE `model_validation_status`
        WHEN 'ReferenceVerified' THEN '參照已驗證'
        WHEN 'AssetPathUnresolved' THEN '資產路徑未解析'
        ELSE `model_validation_status`
    END,
    `overall_validation_status` = CASE `overall_validation_status`
        WHEN 'Verified' THEN '已驗證'
        WHEN 'EvidenceBlocked' THEN '證據不足'
        ELSE `overall_validation_status`
    END;

ALTER TABLE `god2_game`.`item_asset_mappings`
    ADD CONSTRAINT `ck_item_asset_icon_status`
        CHECK (`icon_validation_status` IN ('Verified','RangeMissing','AssetMissing','已驗證','範圍缺失','資產缺失')),
    ADD CONSTRAINT `ck_item_asset_model_status`
        CHECK (`model_validation_status` IN ('ReferenceVerified','AssetPathUnresolved','參照已驗證','資產路徑未解析')),
    ADD CONSTRAINT `ck_item_asset_overall_status`
        CHECK (`overall_validation_status` IN ('Verified','EvidenceBlocked','已驗證','證據不足'));
