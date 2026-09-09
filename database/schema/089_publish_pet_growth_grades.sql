-- Publish the four classic battle-pet growth grades. The automatic totals below
-- exclude the separately awarded one manual point per level.

CREATE TABLE IF NOT EXISTS `god2_game`.`pet_growth_grade_rules` (
    `growth_grade` varchar(30) NOT NULL COMMENT '成長品級：Normal、Top、LateBreakthrough或Breakthrough',
    `minimum_level` int NOT NULL COMMENT '規則起始等級',
    `maximum_level` int NOT NULL COMMENT '規則結束等級',
    `automatic_points_per_level` int NOT NULL COMMENT '每級四維自動成長總點數',
    `manual_points_per_level` int NOT NULL COMMENT '每級可自由分配點數',
    `initial_quality` varchar(20) NOT NULL COMMENT '初始品質：Normal或Top',
    `breakthrough_level` int NULL COMMENT '突破發生等級；普通與頂級為NULL',
    `evidence_status` varchar(30) NOT NULL COMMENT '證據狀態',
    `source_reference_zh_tw` varchar(500) NOT NULL COMMENT '可讀資料來源',
    `enabled` tinyint(1) NOT NULL DEFAULT 1 COMMENT '服務端是否啟用',
    `updated_at_utc` datetime(6) NOT NULL DEFAULT UTC_TIMESTAMP(6) ON UPDATE CURRENT_TIMESTAMP(6),
    PRIMARY KEY (`growth_grade`,`minimum_level`),
    CONSTRAINT `ck_pet_growth_grade_name` CHECK (`growth_grade` IN ('Normal','Top','LateBreakthrough','Breakthrough')),
    CONSTRAINT `ck_pet_growth_grade_levels` CHECK (`minimum_level` >= 1 AND `maximum_level` >= `minimum_level`),
    CONSTRAINT `ck_pet_growth_grade_points` CHECK (`automatic_points_per_level` > 0 AND `manual_points_per_level` >= 0),
    CONSTRAINT `ck_pet_growth_grade_initial` CHECK (`initial_quality` IN ('Normal','Top')),
    CONSTRAINT `ck_pet_growth_grade_breakthrough` CHECK (`breakthrough_level` IS NULL OR `breakthrough_level` IN (20,50)),
    CONSTRAINT `ck_pet_growth_grade_evidence` CHECK (`evidence_status` IN ('BahamutVerified','UserConfirmed')),
    CONSTRAINT `ck_pet_growth_grade_enabled` CHECK (`enabled` IN (0,1))
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci COMMENT='戰寵普通、頂級、晚破與破頂分段成長規則';

DELETE FROM `god2_game`.`pet_growth_grade_rules`;

INSERT INTO `god2_game`.`pet_growth_grade_rules`
    (`growth_grade`,`minimum_level`,`maximum_level`,`automatic_points_per_level`,`manual_points_per_level`,
     `initial_quality`,`breakthrough_level`,`evidence_status`,`source_reference_zh_tw`,`enabled`)
VALUES
    ('Normal',1,19,4,1,'Normal',NULL,'BahamutVerified','巴哈姆特封神2精華區：戰寵頂級與普級之分、破頂寵定義。',1),
    ('Normal',20,49,6,1,'Normal',NULL,'BahamutVerified','巴哈姆特封神2精華區：普通戰寵20至49級每級自動6點。',1),
    ('Normal',50,99,8,1,'Normal',NULL,'BahamutVerified','巴哈姆特封神2精華區：普通戰寵50至99級每級自動8點。',1),
    ('Top',1,19,5,1,'Top',NULL,'BahamutVerified','巴哈姆特封神2精華區：頂級戰寵1至19級每級自動5點，且不會破頂。',1),
    ('Top',20,49,6,1,'Top',NULL,'BahamutVerified','巴哈姆特封神2精華區：頂級戰寵20至49級每級自動6點。',1),
    ('Top',50,99,8,1,'Top',NULL,'BahamutVerified','巴哈姆特封神2精華區：頂級戰寵50至99級每級自動8點。',1),
    ('LateBreakthrough',1,19,4,1,'Normal',50,'BahamutVerified','巴哈姆特封神2精華區：晚破戰寵突破前依普通成長。',1),
    ('LateBreakthrough',20,49,6,1,'Normal',50,'BahamutVerified','巴哈姆特封神2精華區：晚破戰寵20至49級每級自動6點。',1),
    ('LateBreakthrough',50,99,10,1,'Normal',50,'BahamutVerified','巴哈姆特封神2精華區：晚破戰寵50級起每級自動10點。',1),
    ('Breakthrough',1,19,4,1,'Normal',20,'BahamutVerified','巴哈姆特封神2精華區：破頂戰寵突破前依普通成長。',1),
    ('Breakthrough',20,49,8,1,'Normal',20,'BahamutVerified','巴哈姆特封神2精華區：破頂戰寵20至49級每級自動8點。',1),
    ('Breakthrough',50,99,10,1,'Normal',20,'BahamutVerified','巴哈姆特封神2精華區：破頂戰寵50至99級每級自動10點。',1);

UPDATE `god2_game`.`pet_growth_profiles`
SET `name_zh_tw`='普通戰寵1至19級基礎成長',
    `admin_note`='每級自動4點只代表普通戰寵1至19級基線；完整普通、頂級、晚破與破頂規則請讀取pet_growth_grade_rules。'
WHERE `growth_profile_id`=1;

ALTER TABLE `god2_player`.`character_pets`
    ADD COLUMN IF NOT EXISTS `initial_growth_quality` varchar(20) NULL COMMENT '初始品質：Normal普通或Top頂級' AFTER `rebirth_count`,
    ADD COLUMN IF NOT EXISTS `growth_grade` varchar(30) NULL COMMENT '目前判定品級：Normal、Top、LateBreakthrough或Breakthrough' AFTER `initial_growth_quality`,
    ADD COLUMN IF NOT EXISTS `growth_grade_status` varchar(20) NOT NULL DEFAULT 'Unknown' COMMENT '品級判定狀態：Unknown、Provisional或Confirmed' AFTER `growth_grade`,
    ADD COLUMN IF NOT EXISTS `growth_grade_confirmed_level` int NULL COMMENT '品級正式確認等級' AFTER `growth_grade_status`,
    ADD COLUMN IF NOT EXISTS `last_automatic_growth_total` int NULL COMMENT '最近一次升級四維自動成長總點數' AFTER `growth_grade_confirmed_level`;

ALTER TABLE `god2_player`.`character_pets`
    DROP CONSTRAINT IF EXISTS `ck_character_pets_initial_growth_quality`,
    DROP CONSTRAINT IF EXISTS `ck_character_pets_growth_grade`,
    DROP CONSTRAINT IF EXISTS `ck_character_pets_growth_grade_status`,
    ADD CONSTRAINT `ck_character_pets_initial_growth_quality` CHECK (`initial_growth_quality` IS NULL OR `initial_growth_quality` IN ('Normal','Top')),
    ADD CONSTRAINT `ck_character_pets_growth_grade` CHECK (`growth_grade` IS NULL OR `growth_grade` IN ('Normal','Top','LateBreakthrough','Breakthrough')),
    ADD CONSTRAINT `ck_character_pets_growth_grade_status` CHECK (`growth_grade_status` IN ('Unknown','Provisional','Confirmed'));

CREATE OR REPLACE VIEW `god2_game`.`vw_pet_growth_grades_readable` AS
SELECT CASE rule_row.`growth_grade`
           WHEN 'Normal' THEN '普通'
           WHEN 'Top' THEN '頂級'
           WHEN 'LateBreakthrough' THEN '晚破'
           ELSE '破頂'
       END AS `戰寵品級`,
       rule_row.`minimum_level` AS `起始等級`,rule_row.`maximum_level` AS `結束等級`,
       rule_row.`automatic_points_per_level` AS `每級自動成長總點數`,
       rule_row.`manual_points_per_level` AS `每級自由配點`,
       CASE rule_row.`initial_quality` WHEN 'Top' THEN '頂級' ELSE '普通' END AS `初始品質`,
       rule_row.`breakthrough_level` AS `突破等級`,
       CASE rule_row.`evidence_status` WHEN 'BahamutVerified' THEN '巴哈精華區實證' ELSE '使用者確認' END AS `證據狀態`,
       rule_row.`source_reference_zh_tw` AS `規則說明`
FROM `god2_game`.`pet_growth_grade_rules` rule_row
WHERE rule_row.`enabled`=1
ORDER BY FIELD(rule_row.`growth_grade`,'Normal','Top','LateBreakthrough','Breakthrough'),rule_row.`minimum_level`;

CREATE OR REPLACE VIEW `god2_player`.`vw_character_pets_full` AS
SELECT pet_row.`pet_instance_id` AS `戰寵實例ID`,pet_row.`owner_character_id` AS `角色ID`,owner_row.`name` AS `角色名稱`,
       pet_row.`name` AS `戰寵名稱`,template_row.`name_zh_tw` AS `戰寵模板`,pet_row.`level` AS `等級`,pet_row.`experience` AS `經驗`,
       CASE pet_row.`initial_growth_quality` WHEN 'Normal' THEN '普通' WHEN 'Top' THEN '頂級' ELSE '尚未判定' END AS `初始品質`,
       CASE pet_row.`growth_grade` WHEN 'Normal' THEN '普通' WHEN 'Top' THEN '頂級'
            WHEN 'LateBreakthrough' THEN '晚破' WHEN 'Breakthrough' THEN '破頂' ELSE '尚未判定' END AS `成長品級`,
       CASE pet_row.`growth_grade_status` WHEN 'Provisional' THEN '暫定' WHEN 'Confirmed' THEN '已確認' ELSE '待觀察' END AS `品級狀態`,
       pet_row.`growth_grade_confirmed_level` AS `品級確認等級`,pet_row.`last_automatic_growth_total` AS `最近自動成長總點數`,
       pet_row.`current_hp` AS `目前HP`,pet_row.`max_hp` AS `最大HP`,pet_row.`current_mp` AS `目前MP`,pet_row.`max_mp` AS `最大MP`,
       pet_row.`strength_base` AS `力量`,pet_row.`constitution_base` AS `體力`,pet_row.`intelligence_base` AS `智力`,pet_row.`speed_base` AS `速度`,
       pet_row.`current_lifespan` AS `目前壽命`,pet_row.`max_lifespan` AS `最大壽命`,pet_row.`rebirth_count` AS `轉生次數`,
       pet_row.`remaining_stat_points` AS `剩餘配點`,pet_row.`fusion_expires_at_utc` AS `合體到期時間`,
       pet_row.`is_deployed` AS `是否出戰`,pet_row.`is_active` AS `目前選用`,pet_row.`enabled` AS `啟用狀態`
FROM `god2_player`.`character_pets` pet_row
JOIN `god2_player`.`characters` owner_row ON owner_row.`character_id`=pet_row.`owner_character_id`
JOIN `god2_game`.`pet_templates` template_row ON template_row.`pet_template_id`=pet_row.`pet_template_id`;
