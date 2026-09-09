-- 257_translate_catalog_bonus_element_and_numeric_effect_labels.sql
-- Purpose: replace remaining formal runtime-readable English catalog and skill-effect labels with Traditional Chinese
-- while keeping server-side parsers bilingual.

ALTER TABLE `god2_game`.`item_asset_mappings`
    DROP CONSTRAINT IF EXISTS `ck_item_asset_catalog_type`;

ALTER TABLE `god2_game`.`magic_skill_damage_coefficients`
    DROP CONSTRAINT IF EXISTS `ck_magic_skill_coefficients_element`;

UPDATE `god2_game`.`item_set_bonuses`
SET `bonus_type` = CASE `bonus_type`
    WHEN 'MagicAttack' THEN '法術攻擊'
    WHEN 'PhysicalAttack' THEN '物理攻擊'
    WHEN 'MagicDefense' THEN '法術防禦'
    WHEN 'PhysicalDefense' THEN '物理防禦'
    WHEN 'Intelligence' THEN '智力'
    WHEN 'Constitution' THEN '體質'
    WHEN 'MaximumHp' THEN '最大生命值'
    WHEN 'Strength' THEN '力量'
    WHEN 'Speed' THEN '速度'
    WHEN 'MaximumMp' THEN '最大法力值'
    ELSE `bonus_type`
END
WHERE `bonus_type` IN (
    'MagicAttack','PhysicalAttack','MagicDefense','PhysicalDefense',
    'Intelligence','Constitution','MaximumHp','Strength','Speed','MaximumMp');

UPDATE `god2_game`.`item_asset_mappings`
SET `catalog_type` = CASE `catalog_type`
    WHEN 'Item' THEN '物品'
    WHEN 'Equipment' THEN '裝備'
    WHEN 'Weapon' THEN '武器'
    WHEN 'MagicTreasure' THEN '法寶'
    ELSE `catalog_type`
END
WHERE `catalog_type` IN ('Item','Equipment','Weapon','MagicTreasure');

UPDATE `god2_game`.`magic_skill_damage_coefficients`
SET `attack_element` = CASE `attack_element`
    WHEN 'Metal' THEN '金'
    WHEN 'Wood' THEN '木'
    WHEN 'Water' THEN '水'
    WHEN 'Fire' THEN '火'
    WHEN 'Earth' THEN '土'
    ELSE `attack_element`
END
WHERE `attack_element` IN ('Metal','Wood','Water','Fire','Earth');

UPDATE `god2_game`.`public_beta_skill_effect_v0`
SET `numeric_effect_fields` =
    REPLACE(
    REPLACE(
    REPLACE(
    REPLACE(
    REPLACE(
    REPLACE(
    REPLACE(
    REPLACE(
    REPLACE(
    REPLACE(
    REPLACE(
    REPLACE(`numeric_effect_fields`,
        'physical_attack_s', '物理攻擊'),
        'physical_defense_s', '物理防禦'),
        'magical_attack_s', '法術攻擊'),
        'magical_defense_s', '法術防禦'),
        'intelligence_s', '智力'),
        'constitution_s', '體質'),
        'speed_s', '速度'),
        'wrist_s', '腕力'),
        'metal_s', '金'),
        'wood_s', '木'),
        'water_s', '水'),
        'fire_s', '火')
WHERE `numeric_effect_fields` IS NOT NULL;

UPDATE `god2_game`.`public_beta_skill_effect_v0`
SET `numeric_effect_fields` = REPLACE(`numeric_effect_fields`, 'earth_s', '土')
WHERE `numeric_effect_fields` IS NOT NULL;

ALTER TABLE `god2_game`.`item_asset_mappings`
    ADD CONSTRAINT `ck_item_asset_catalog_type`
    CHECK (`catalog_type` IN ('Item','Weapon','Equipment','MagicTreasure','物品','武器','裝備','法寶'));

ALTER TABLE `god2_game`.`magic_skill_damage_coefficients`
    ADD CONSTRAINT `ck_magic_skill_coefficients_element`
    CHECK (`attack_element` IN ('Metal','Wood','Water','Fire','Earth','金','木','水','火','土'));
