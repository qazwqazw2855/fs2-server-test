-- 官方人物與寵物升級配點規則。
-- 人物四職業的自動分配為正式數據；本遷移以完整替換避免新舊規則疊加。

INSERT INTO `god2_game`.`character_classes`
    (`class_id`,`code`,`name_zh_tw`,`name_original`,`description_zh_tw`,`enabled`,`admin_note`)
VALUES
    (1,'Swordsman','劍士','剑士','每級自動增加體力2、腕力3、速度1，另取得4點自由配點。',1,'官方人物升級配點規則'),
    (2,'Taoist','仙道','仙道','每級自動增加體力1、智力3、速度2，另取得4點自由配點。',1,'官方人物升級配點規則'),
    (3,'Pharmacist','藥師','药师','每級自動增加體力2、智力2、速度2，另取得4點自由配點。',1,'官方人物升級配點規則'),
    (4,'Warlock','謀士','谋士','每級自動增加體力1、腕力1、智力1、速度3，另取得4點自由配點。',1,'官方人物升級配點規則')
ON DUPLICATE KEY UPDATE
    `code`=VALUES(`code`),
    `name_zh_tw`=VALUES(`name_zh_tw`),
    `name_original`=VALUES(`name_original`),
    `description_zh_tw`=VALUES(`description_zh_tw`),
    `enabled`=VALUES(`enabled`),
    `admin_note`=VALUES(`admin_note`);

DELETE FROM `god2_game`.`class_stat_growth`
WHERE `class_id` IN (1,2,3,4);

INSERT INTO `god2_game`.`class_stat_growth`
    (`class_id`,`stat_name`,`growth_type`,`growth_value`,`level_interval`,`evidence_status`,`enabled`,`admin_note`)
VALUES
    (1,'Constitution','AutomaticPerLevel',2,1,'Verified',1,'劍士每級自動配點'),
    (1,'Strength','AutomaticPerLevel',3,1,'Verified',1,'劍士每級自動配點'),
    (1,'Intelligence','AutomaticPerLevel',0,1,'Verified',1,'劍士每級自動配點'),
    (1,'Speed','AutomaticPerLevel',1,1,'Verified',1,'劍士每級自動配點'),
    (1,'UnspentAttributePoints','ManualPerLevel',4,1,'Verified',1,'劍士每級自由配點'),
    (2,'Constitution','AutomaticPerLevel',1,1,'Verified',1,'仙道每級自動配點'),
    (2,'Strength','AutomaticPerLevel',0,1,'Verified',1,'仙道每級自動配點'),
    (2,'Intelligence','AutomaticPerLevel',3,1,'Verified',1,'仙道每級自動配點'),
    (2,'Speed','AutomaticPerLevel',2,1,'Verified',1,'仙道每級自動配點'),
    (2,'UnspentAttributePoints','ManualPerLevel',4,1,'Verified',1,'仙道每級自由配點'),
    (3,'Constitution','AutomaticPerLevel',2,1,'Verified',1,'藥師每級自動配點'),
    (3,'Strength','AutomaticPerLevel',0,1,'Verified',1,'藥師每級自動配點'),
    (3,'Intelligence','AutomaticPerLevel',2,1,'Verified',1,'藥師每級自動配點'),
    (3,'Speed','AutomaticPerLevel',2,1,'Verified',1,'藥師每級自動配點'),
    (3,'UnspentAttributePoints','ManualPerLevel',4,1,'Verified',1,'藥師每級自由配點'),
    (4,'Constitution','AutomaticPerLevel',1,1,'Verified',1,'謀士每級自動配點'),
    (4,'Strength','AutomaticPerLevel',1,1,'Verified',1,'謀士每級自動配點'),
    (4,'Intelligence','AutomaticPerLevel',1,1,'Verified',1,'謀士每級自動配點'),
    (4,'Speed','AutomaticPerLevel',3,1,'Verified',1,'謀士每級自動配點'),
    (4,'UnspentAttributePoints','ManualPerLevel',4,1,'Verified',1,'謀士每級自由配點');

ALTER TABLE `god2_game`.`pet_growth_profiles`
    ADD COLUMN IF NOT EXISTS `automatic_points_per_level` INT NULL COMMENT '每級自動分配點數；分項未知時只保存總額' AFTER `name_zh_tw`,
    ADD COLUMN IF NOT EXISTS `manual_points_per_level` INT NULL COMMENT '每級可自由分配點數' AFTER `automatic_points_per_level`;

INSERT INTO `god2_game`.`pet_growth_profiles`
    (`growth_profile_id`,`name_zh_tw`,`automatic_points_per_level`,`manual_points_per_level`,
     `evidence_status`,`enabled`,`admin_note`)
VALUES
    (1,'通用寵物升級配點預算',4,1,'Verified',1,'官方已確認每級自動4點、自由1點；自動配點的四維比例尚未提供。')
ON DUPLICATE KEY UPDATE
    `name_zh_tw`=VALUES(`name_zh_tw`),
    `automatic_points_per_level`=VALUES(`automatic_points_per_level`),
    `manual_points_per_level`=VALUES(`manual_points_per_level`),
    `evidence_status`=VALUES(`evidence_status`),
    `enabled`=VALUES(`enabled`),
    `admin_note`=VALUES(`admin_note`);

CREATE OR REPLACE VIEW `god2_game`.`vw_official_character_attribute_growth` AS
SELECT class_row.`class_id` AS `職業ID`,
       class_row.`name_zh_tw` AS `職業`,
       MAX(CASE WHEN growth.`stat_name`='Constitution' THEN growth.`growth_value` END) AS `每級自動體力`,
       MAX(CASE WHEN growth.`stat_name`='Strength' THEN growth.`growth_value` END) AS `每級自動腕力`,
       MAX(CASE WHEN growth.`stat_name`='Intelligence' THEN growth.`growth_value` END) AS `每級自動智力`,
       MAX(CASE WHEN growth.`stat_name`='Speed' THEN growth.`growth_value` END) AS `每級自動速度`,
       MAX(CASE WHEN growth.`stat_name`='UnspentAttributePoints' THEN growth.`growth_value` END) AS `每級自由配點`,
       MIN(growth.`evidence_status`) AS `證據狀態`
FROM `god2_game`.`character_classes` class_row
JOIN `god2_game`.`class_stat_growth` growth ON growth.`class_id`=class_row.`class_id`
WHERE class_row.`class_id` IN (1,2,3,4) AND growth.`enabled`=1
GROUP BY class_row.`class_id`,class_row.`name_zh_tw`;

GRANT SELECT ON `god2_game`.`vw_official_character_attribute_growth` TO `god2_runtime_role`;
