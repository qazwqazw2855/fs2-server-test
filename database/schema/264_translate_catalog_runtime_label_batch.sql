-- 264_translate_catalog_runtime_label_batch.sql
-- Game functions: class stat growth labels, equipment slots, equipment set member slots,
-- equipment enhancement material/rate grades, item effect use scope, and item icon atlas validation status.

ALTER TABLE `god2_game`.`item_effects`
    DROP CONSTRAINT IF EXISTS `ck_item_effects_scope`;

ALTER TABLE `god2_game`.`equipment_enhancement_materials`
    DROP CONSTRAINT IF EXISTS `ck_equipment_enhancement_material_grade`;

ALTER TABLE `god2_game`.`equipment_enhancement_rates`
    DROP CONSTRAINT IF EXISTS `ck_equipment_enhancement_rate_grade`;

ALTER TABLE `god2_game`.`item_icon_atlases`
    DROP CONSTRAINT IF EXISTS `ck_item_icon_atlas_status`;

UPDATE `god2_game`.`class_stat_growth`
SET `stat_name` = CASE `stat_name`
        WHEN 'Constitution' THEN '體質'
        WHEN 'Strength' THEN '力量'
        WHEN 'Intelligence' THEN '智力'
        WHEN 'Speed' THEN '速度'
        WHEN 'UnspentAttributePoints' THEN '未分配屬性點'
        ELSE `stat_name`
    END;

UPDATE `god2_game`.`equipment`
SET `equipment_slot` = CASE `equipment_slot`
        WHEN 'Armor' THEN '防具'
        WHEN 'Accessory' THEN '飾品'
        WHEN 'Helmet' THEN '頭盔'
        WHEN 'Gloves' THEN '手套'
        WHEN 'Shoes' THEN '鞋子'
        ELSE `equipment_slot`
    END;

UPDATE `god2_game`.`item_set_members`
SET `slot_name` = CASE `slot_name`
        WHEN 'Armor' THEN '防具'
        WHEN 'Helmet' THEN '頭盔'
        WHEN 'Gloves' THEN '手套'
        WHEN 'Shoes' THEN '鞋子'
        WHEN 'Weapon' THEN '武器'
        ELSE `slot_name`
    END;

UPDATE `god2_game`.`equipment_enhancement_materials`
SET `grade` = CASE `grade`
        WHEN 'General' THEN '一般'
        WHEN 'Advanced' THEN '進階'
        WHEN 'Special' THEN '特殊'
        ELSE `grade`
    END;

UPDATE `god2_game`.`equipment_enhancement_rates`
SET `grade` = CASE `grade`
        WHEN 'General' THEN '一般'
        WHEN 'Advanced' THEN '進階'
        WHEN 'Special' THEN '特殊'
        ELSE `grade`
    END;

UPDATE `god2_game`.`item_effects`
SET `usage_scope` = CASE `usage_scope`
        WHEN 'World' THEN '世界'
        WHEN 'Battle' THEN '戰鬥'
        WHEN 'Both' THEN '世界與戰鬥'
        ELSE `usage_scope`
    END;

UPDATE `god2_game`.`item_icon_atlases`
SET `validation_status` = CASE `validation_status`
        WHEN 'Verified' THEN '已驗證'
        ELSE `validation_status`
    END;

ALTER TABLE `god2_game`.`item_effects`
    ADD CONSTRAINT `ck_item_effects_scope`
        CHECK (`usage_scope` IN ('World','Battle','Both','世界','戰鬥','世界與戰鬥'));

ALTER TABLE `god2_game`.`equipment_enhancement_materials`
    ADD CONSTRAINT `ck_equipment_enhancement_material_grade`
        CHECK (`grade` IN ('General','Advanced','Special','一般','進階','特殊'));

ALTER TABLE `god2_game`.`equipment_enhancement_rates`
    ADD CONSTRAINT `ck_equipment_enhancement_rate_grade`
        CHECK (`grade` IN ('General','Advanced','Special','一般','進階','特殊'));

ALTER TABLE `god2_game`.`item_icon_atlases`
    ADD CONSTRAINT `ck_item_icon_atlas_status`
        CHECK (`validation_status` IN ('Verified','已驗證'));
