-- 260_translate_item_effect_enhancement_and_physical_skill_labels.sql
-- Purpose: make item effects, equipment enhancement material rules, and physical skill damage families readable.

ALTER TABLE `god2_game`.`client_item_effect_visuals`
    DROP CONSTRAINT IF EXISTS `ck_client_item_effect_visual_mechanics`;

ALTER TABLE `god2_game`.`item_effects`
    DROP CONSTRAINT IF EXISTS `ck_item_effects_type`,
    DROP CONSTRAINT IF EXISTS `ck_item_effects_target`;

ALTER TABLE `god2_game`.`equipment_enhancement_materials`
    DROP CONSTRAINT IF EXISTS `ck_equipment_enhancement_material_failure`,
    DROP CONSTRAINT IF EXISTS `ck_equipment_enhancement_material_target`;

ALTER TABLE `god2_game`.`physical_skill_damage_coefficients`
    DROP CONSTRAINT IF EXISTS `ck_physical_skill_coefficients_family`;

UPDATE `god2_game`.`client_item_effect_visuals`
SET `mechanical_semantics_status` = CASE `mechanical_semantics_status`
    WHEN 'DisplayTextOnlyServerAuthorityBlocked' THEN '只顯示文字，服務端權限封鎖'
    ELSE `mechanical_semantics_status`
END;

UPDATE `god2_game`.`item_effects`
SET `effect_type` = CASE `effect_type`
        WHEN 'RestoreHp' THEN '生命恢復'
        WHEN 'RestoreMp' THEN '法力恢復'
        ELSE `effect_type`
    END,
    `target_policy` = CASE `target_policy`
        WHEN 'Self' THEN '自身'
        WHEN 'OtherAllowed' THEN '可對他人'
        ELSE `target_policy`
    END;

UPDATE `god2_game`.`equipment_enhancement_materials`
SET `failure_policy` = CASE `failure_policy`
        WHEN 'DestroyTarget' THEN '強化失敗破壞目標'
        WHEN 'PreserveTarget' THEN '強化失敗保留目標'
        ELSE `failure_policy`
    END,
    `target_type` = CASE `target_type`
        WHEN 'Weapon' THEN '武器'
        WHEN 'Equipment' THEN '裝備'
        ELSE `target_type`
    END;

UPDATE `god2_game`.`physical_skill_damage_coefficients`
SET `skill_family` = CASE `skill_family`
    WHEN 'Blade' THEN '刀技'
    WHEN 'Sword' THEN '劍技'
    WHEN 'Staff' THEN '杖技'
    WHEN 'Whip' THEN '鞭技'
    WHEN 'Spear' THEN '槍技'
    WHEN 'ThrowingKnife' THEN '飛刀技'
    ELSE `skill_family`
END;

ALTER TABLE `god2_game`.`client_item_effect_visuals`
    ADD CONSTRAINT `ck_client_item_effect_visual_mechanics`
        CHECK (`mechanical_semantics_status` IN ('DisplayTextOnlyServerAuthorityBlocked','只顯示文字，服務端權限封鎖'));

ALTER TABLE `god2_game`.`item_effects`
    ADD CONSTRAINT `ck_item_effects_type`
        CHECK (`effect_type` IN ('RestoreHp','RestoreMp','生命恢復','法力恢復')),
    ADD CONSTRAINT `ck_item_effects_target`
        CHECK (`target_policy` IN ('Self','OtherAllowed','自身','可對他人'));

ALTER TABLE `god2_game`.`equipment_enhancement_materials`
    ADD CONSTRAINT `ck_equipment_enhancement_material_failure`
        CHECK (`failure_policy` IN ('DestroyTarget','PreserveTarget','強化失敗破壞目標','強化失敗保留目標')),
    ADD CONSTRAINT `ck_equipment_enhancement_material_target`
        CHECK (`target_type` IN ('Weapon','Equipment','武器','裝備'));

ALTER TABLE `god2_game`.`physical_skill_damage_coefficients`
    ADD CONSTRAINT `ck_physical_skill_coefficients_family`
        CHECK (`skill_family` IN ('Blade','Sword','Staff','Whip','Spear','ThrowingKnife','刀技','劍技','杖技','鞭技','槍技','飛刀技'));
