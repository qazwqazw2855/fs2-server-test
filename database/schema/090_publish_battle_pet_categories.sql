-- Publish readable battle-pet families from Bahamut archive articles 961 and 1400.
-- Level-one pets start at level 1. Captured wild pets retain the encounter level but use battle-pet attributes.

CREATE TABLE IF NOT EXISTS `god2_game`.`pet_categories` (
    `category_id` smallint NOT NULL COMMENT '連號戰寵類別ID',
    `name_zh_tw` varchar(80) NOT NULL COMMENT '戰寵族群繁體中文名稱',
    `article_section_zh_tw` varchar(30) NOT NULL COMMENT '文章中的分類區段',
    `growth_pattern` varchar(20) NOT NULL COMMENT '成長型態：Pure純種、Mixed雜種、Average平均',
    `source_article_sn` int NOT NULL COMMENT '巴哈姆特精華區文章SN',
    `evidence_status` varchar(40) NOT NULL COMMENT '型態為文章直述或依公開配點推導',
    `enabled` tinyint(1) NOT NULL DEFAULT 1 COMMENT '服務端是否啟用',
    `admin_note` varchar(300) NULL COMMENT '繁體中文整理備註',
    PRIMARY KEY (`category_id`),
    UNIQUE KEY `ux_pet_categories_name_zh_tw` (`name_zh_tw`),
    CONSTRAINT `ck_pet_categories_id` CHECK (`category_id` BETWEEN 1 AND 999),
    CONSTRAINT `ck_pet_categories_pattern` CHECK (`growth_pattern` IN ('Pure','Mixed','Average')),
    CONSTRAINT `ck_pet_categories_source` CHECK (`source_article_sn` IN (961,1400)),
    CONSTRAINT `ck_pet_categories_evidence` CHECK (`evidence_status` IN ('DirectlyLabeled','DerivedFromPublishedStats')),
    CONSTRAINT `ck_pet_categories_enabled` CHECK (`enabled` IN (0,1))
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci COMMENT='戰寵族群與自動配點型態';

ALTER TABLE `god2_game`.`monsters`
    ADD COLUMN IF NOT EXISTS `name_color_zh_tw` varchar(10) NOT NULL DEFAULT '未知' COMMENT '怪物名稱顏色：白色、黃色、紅色或未知' AFTER `name_original`,
    ADD COLUMN IF NOT EXISTS `is_quest_monster` tinyint(1) NOT NULL DEFAULT 0 COMMENT '是否為任務怪' AFTER `name_color_zh_tw`,
    ADD COLUMN IF NOT EXISTS `is_formation_boss` tinyint(1) NOT NULL DEFAULT 0 COMMENT '是否為陣法魔王' AFTER `is_quest_monster`,
    ADD COLUMN IF NOT EXISTS `capture_eligibility` varchar(20) NOT NULL DEFAULT 'Unknown' COMMENT '捕捉資格：Unknown、Capturable或Blocked' AFTER `is_formation_boss`,
    ADD COLUMN IF NOT EXISTS `capture_block_reason_zh_tw` varchar(150) NULL COMMENT '不可捕捉原因' AFTER `capture_eligibility`,
    ADD COLUMN IF NOT EXISTS `capture_rule_source_article_sn` int NOT NULL DEFAULT 921 COMMENT '捕捉規則來源：巴哈姆特精華區文章SN' AFTER `capture_block_reason_zh_tw`;

UPDATE `god2_game`.`monsters`
SET `capture_eligibility`='Blocked',
    `capture_block_reason_zh_tw`=CASE
        WHEN `is_quest_monster`=1 THEN '任務怪不可捕捉'
        WHEN `is_formation_boss`=1 THEN '陣法魔王不可捕捉'
        WHEN `name_color_zh_tw`='黃色' THEN '黃字怪不可捕捉'
        WHEN `name_color_zh_tw`='紅色' THEN '紅字怪不可捕捉'
        ELSE `capture_block_reason_zh_tw`
    END
WHERE `is_quest_monster`=1 OR `is_formation_boss`=1 OR `name_color_zh_tw` IN ('黃色','紅色');

ALTER TABLE `god2_game`.`monsters`
    DROP CONSTRAINT IF EXISTS `ck_monsters_name_color`,
    DROP CONSTRAINT IF EXISTS `ck_monsters_quest_flag`,
    DROP CONSTRAINT IF EXISTS `ck_monsters_formation_boss_flag`,
    DROP CONSTRAINT IF EXISTS `ck_monsters_capture_eligibility`,
    DROP CONSTRAINT IF EXISTS `ck_monsters_capture_blocked_types`,
    DROP CONSTRAINT IF EXISTS `ck_monsters_capture_source`,
    ADD CONSTRAINT `ck_monsters_name_color` CHECK (`name_color_zh_tw` IN ('白色','黃色','紅色','未知')),
    ADD CONSTRAINT `ck_monsters_quest_flag` CHECK (`is_quest_monster` IN (0,1)),
    ADD CONSTRAINT `ck_monsters_formation_boss_flag` CHECK (`is_formation_boss` IN (0,1)),
    ADD CONSTRAINT `ck_monsters_capture_eligibility` CHECK (`capture_eligibility` IN ('Unknown','Capturable','Blocked')),
    ADD CONSTRAINT `ck_monsters_capture_blocked_types` CHECK (
        (`capture_eligibility`='Capturable' AND `name_color_zh_tw`='白色' AND `is_quest_monster`=0 AND `is_formation_boss`=0)
        OR (`capture_eligibility`='Blocked' AND `capture_block_reason_zh_tw` IS NOT NULL)
        OR (`capture_eligibility`='Unknown')
    ),
    ADD CONSTRAINT `ck_monsters_capture_source` CHECK (`capture_rule_source_article_sn`=921);

DELETE FROM `god2_game`.`pet_categories`;

INSERT INTO `god2_game`.`pet_categories`
    (`category_id`,`name_zh_tw`,`article_section_zh_tw`,`growth_pattern`,`source_article_sn`,`evidence_status`,`enabled`,`admin_note`)
VALUES
    (1,'狐類','純種型','Pure',961,'DirectlyLabeled',1,NULL),
    (2,'山叉類','純種型','Pure',961,'DirectlyLabeled',1,NULL),
    (3,'豹類','純種型','Pure',961,'DirectlyLabeled',1,NULL),
    (4,'火魅類','純種型','Pure',961,'DirectlyLabeled',1,NULL),
    (5,'螺類','純種型','Pure',961,'DirectlyLabeled',1,NULL),
    (6,'鷹類','純種型','Pure',961,'DirectlyLabeled',1,NULL),
    (7,'草類','純種型','Pure',961,'DirectlyLabeled',1,NULL),
    (8,'皮皮類','純種型','Pure',961,'DirectlyLabeled',1,NULL),
    (9,'鶴類','純種型','Pure',961,'DirectlyLabeled',1,NULL),
    (10,'蜻蜓類','純種型','Pure',961,'DirectlyLabeled',1,NULL),
    (11,'猴類','純種型','Pure',961,'DirectlyLabeled',1,NULL),
    (12,'巨熊類','純種型','Pure',961,'DirectlyLabeled',1,'文章註明配點同猴類。'),
    (13,'天牛類','純種型','Pure',961,'DirectlyLabeled',1,'文章註明配點同猴類。'),
    (14,'跳蚤類','純種型','Pure',961,'DirectlyLabeled',1,NULL),
    (15,'菇類','雜種型','Mixed',961,'DirectlyLabeled',1,NULL),
    (16,'海洋企鵝','雜種型','Mixed',961,'DirectlyLabeled',1,'文章註明配點同菇類。'),
    (17,'蚌類','雜種型','Mixed',961,'DirectlyLabeled',1,NULL),
    (18,'虎類','雜種型','Mixed',961,'DirectlyLabeled',1,NULL),
    (19,'武士類','雜種型','Mixed',961,'DirectlyLabeled',1,NULL),
    (20,'眼蠅類','雜種型','Mixed',961,'DirectlyLabeled',1,NULL),
    (21,'水蛭類','雜種型','Mixed',961,'DirectlyLabeled',1,NULL),
    (22,'穿山甲類','雜種型','Mixed',961,'DirectlyLabeled',1,NULL),
    (23,'花類','雜種型','Mixed',961,'DirectlyLabeled',1,NULL),
    (24,'法師類','雜種型','Mixed',961,'DirectlyLabeled',1,NULL),
    (25,'角虎類','雜種型','Mixed',961,'DirectlyLabeled',1,NULL),
    (26,'板牛類','雜種型','Mixed',961,'DirectlyLabeled',1,'與猴類分開整理，兩者成長不同。'),
    (27,'蝴蝶類','雜種型','Mixed',961,'DirectlyLabeled',1,'文章後段亦簡稱蝶類。'),
    (28,'火王蠍','雜種型','Mixed',961,'DirectlyLabeled',1,NULL),
    (29,'蟲類','雜種型','Mixed',961,'DirectlyLabeled',1,NULL),
    (30,'螃蟹類','雜種型','Mixed',961,'DirectlyLabeled',1,NULL),
    (31,'矮人類','雜種型','Mixed',961,'DirectlyLabeled',1,NULL),
    (32,'犀獸類','雜種型','Mixed',961,'DirectlyLabeled',1,NULL),
    (33,'骷髏類','平均型','Average',961,'DirectlyLabeled',1,NULL),
    (34,'殭屍類','平均型','Average',961,'DirectlyLabeled',1,NULL),
    (35,'半妖類','平均型','Average',961,'DirectlyLabeled',1,NULL),
    (36,'幽魂類','平均型','Average',961,'DirectlyLabeled',1,NULL),
    (37,'馬賊類','平均型','Average',961,'DirectlyLabeled',1,NULL),
    (38,'樹類','平均型','Average',961,'DirectlyLabeled',1,NULL),
    (39,'企鵝騎兵','平均型','Average',961,'DirectlyLabeled',1,NULL),
    (40,'魔軍士兵','平均型','Average',961,'DirectlyLabeled',1,NULL),
    (41,'魔法道士','平均型','Average',961,'DirectlyLabeled',1,NULL),
    (42,'蜂類','補充族群','Mixed',1400,'DerivedFromPublishedStats',1,'依文章公開配點涵蓋至少三種屬性推導。'),
    (43,'熊類','補充族群','Mixed',1400,'DerivedFromPublishedStats',1,'依文章公開配點涵蓋至少三種屬性推導。'),
    (44,'兔類','補充族群','Mixed',1400,'DerivedFromPublishedStats',1,'依文章公開配點涵蓋至少三種屬性推導。'),
    (45,'甲兵類','補充族群','Mixed',1400,'DerivedFromPublishedStats',1,'依文章公開配點涵蓋至少三種屬性推導。'),
    (46,'妖類','補充族群','Mixed',1400,'DerivedFromPublishedStats',1,'依文章公開配點涵蓋至少三種屬性推導。'),
    (47,'石怪類','補充族群','Pure',1400,'DerivedFromPublishedStats',1,'依文章公開配點只提升兩種屬性推導。'),
    (48,'狼類','補充族群','Mixed',1400,'DerivedFromPublishedStats',1,'依文章公開配點涵蓋至少三種屬性推導。'),
    (49,'鯨龍類','補充族群','Mixed',1400,'DerivedFromPublishedStats',1,'依文章公開配點涵蓋至少三種屬性推導。'),
    (50,'蒼鯨類','補充族群','Mixed',1400,'DerivedFromPublishedStats',1,'依文章公開配點涵蓋至少三種屬性推導。'),
    (51,'黑木怪類','補充族群','Mixed',1400,'DerivedFromPublishedStats',1,'依文章公開配點涵蓋至少三種屬性推導。'),
    (52,'鴨類','補充族群','Mixed',1400,'DerivedFromPublishedStats',1,'依文章公開配點涵蓋至少三種屬性推導。'),
    (53,'神類','補充族群','Mixed',1400,'DerivedFromPublishedStats',1,'依文章公開配點涵蓋至少三種屬性推導。'),
    (54,'五色牛類','補充族群','Mixed',1400,'DerivedFromPublishedStats',1,'依文章公開配點涵蓋至少三種屬性推導。'),
    (55,'海豹類','補充族群','Mixed',1400,'DerivedFromPublishedStats',1,'依文章公開配點涵蓋至少三種屬性推導。'),
    (56,'雪人類','補充族群','Mixed',1400,'DerivedFromPublishedStats',1,'依文章公開配點涵蓋至少三種屬性推導。'),
    (57,'火足獸','補充族群','Mixed',1400,'DerivedFromPublishedStats',1,'依文章公開配點涵蓋至少三種屬性推導。'),
    (58,'焚熇戰牛','補充族群','Mixed',1400,'DerivedFromPublishedStats',1,'依文章公開配點涵蓋至少三種屬性推導。'),
    (59,'五行魔寵','補充族群','Mixed',1400,'DerivedFromPublishedStats',1,'文章註明固定為破頂寵。'),
    (60,'轉樂PK寵','補充族群','Mixed',1400,'DerivedFromPublishedStats',1,'文章註明固定為破頂寵。'),
    (61,'小年獸','補充族群','Pure',1400,'DerivedFromPublishedStats',1,'依文章公開配點只提升兩種屬性推導。'),
    (62,'瓢蟲類','補充族群','Pure',1400,'DerivedFromPublishedStats',1,'依文章公開配點只提升兩種屬性推導。'),
    (63,'蜘蛛類','補充族群','Pure',1400,'DerivedFromPublishedStats',1,'依文章公開配點只提升兩種屬性推導。'),
    (64,'蝸牛類','補充族群','Mixed',1400,'DerivedFromPublishedStats',1,'依文章公開配點涵蓋至少三種屬性推導。');

ALTER TABLE `god2_game`.`pet_templates`
    ADD COLUMN IF NOT EXISTS `pet_category_id` smallint NULL COMMENT '戰寵族群ID' AFTER `pet_family`,
    ADD COLUMN IF NOT EXISTS `wild_source_monster_id` bigint NULL COMMENT '對應野生怪物；野怪等級讀取monsters.level' AFTER `pet_category_id`,
    ADD COLUMN IF NOT EXISTS `element_zh_tw` varchar(10) NULL COMMENT '戰寵五行；全、金、木、水、火、土或未知' AFTER `wild_source_monster_id`,
    ADD COLUMN IF NOT EXISTS `level_20_skill_name_zh_tw` varchar(150) NULL COMMENT '20級戰寵技能完整繁中名稱' AFTER `element_zh_tw`,
    ADD COLUMN IF NOT EXISTS `level_40_skill_name_zh_tw` varchar(150) NULL COMMENT '40級戰寵技能完整繁中名稱' AFTER `level_20_skill_name_zh_tw`,
    ADD COLUMN IF NOT EXISTS `level_60_skill_name_zh_tw` varchar(150) NULL COMMENT '60級戰寵技能完整繁中名稱' AFTER `level_40_skill_name_zh_tw`,
    ADD COLUMN IF NOT EXISTS `capture_rule_zh_tw` varchar(200) NOT NULL DEFAULT '捕捉後保留野生遭遇等級，但改用戰寵模板能力；不沿用野怪戰鬥能力值' COMMENT '野怪轉為戰寵的能力轉換規則' AFTER `level_60_skill_name_zh_tw`,
    ADD COLUMN IF NOT EXISTS `source_section_zh_tw` varchar(50) NULL COMMENT '來源文章內容區段' AFTER `capture_rule_zh_tw`,
    ADD COLUMN IF NOT EXISTS `source_article_sn` int NULL COMMENT '巴哈姆特精華區文章SN' AFTER `source_section_zh_tw`;

UPDATE `god2_game`.`pet_templates`
SET `base_level`=1;

ALTER TABLE `god2_game`.`pet_templates`
    MODIFY COLUMN `base_level` int NOT NULL DEFAULT 1 COMMENT '玩家戰寵初始等級；固定為1',
    DROP CONSTRAINT IF EXISTS `ck_pet_templates_player_initial_level`,
    DROP CONSTRAINT IF EXISTS `ck_pet_templates_element`,
    DROP CONSTRAINT IF EXISTS `ck_pet_templates_source_article`,
    DROP FOREIGN KEY IF EXISTS `fk_pet_templates_category`,
    DROP FOREIGN KEY IF EXISTS `fk_pet_templates_wild_source_monster`,
    ADD CONSTRAINT `ck_pet_templates_player_initial_level` CHECK (`base_level`=1),
    ADD CONSTRAINT `ck_pet_templates_element` CHECK (`element_zh_tw` IS NULL OR `element_zh_tw` IN ('全','金','木','水','火','土','未知')),
    ADD CONSTRAINT `ck_pet_templates_source_article` CHECK (`source_article_sn` IS NULL OR `source_article_sn`=1405),
    ADD CONSTRAINT `fk_pet_templates_category` FOREIGN KEY (`pet_category_id`) REFERENCES `god2_game`.`pet_categories` (`category_id`),
    ADD CONSTRAINT `fk_pet_templates_wild_source_monster` FOREIGN KEY (`wild_source_monster_id`) REFERENCES `god2_game`.`monsters` (`monster_id`);

ALTER TABLE `god2_player`.`character_pets`
    ADD COLUMN IF NOT EXISTS `initial_level` int NOT NULL DEFAULT 1 COMMENT '玩家取得戰寵時的等級；一般為1，野生捕捉寵為遭遇等級' AFTER `name`,
    ADD COLUMN IF NOT EXISTS `acquisition_origin` varchar(20) NOT NULL DEFAULT 'LevelOnePet' COMMENT '取得來源：LevelOnePet一級戰寵或CapturedWild野生捕捉' AFTER `initial_level`,
    ADD COLUMN IF NOT EXISTS `wild_source_monster_id` bigint NULL COMMENT '野生捕捉來源怪物ID' AFTER `acquisition_origin`,
    ADD COLUMN IF NOT EXISTS `wild_encounter_level` int NULL COMMENT '野生怪捕捉時遭遇等級' AFTER `wild_source_monster_id`,
    DROP CONSTRAINT IF EXISTS `ck_character_pets_initial_level`,
    DROP CONSTRAINT IF EXISTS `ck_character_pets_acquisition`,
    DROP FOREIGN KEY IF EXISTS `fk_character_pets_wild_source_monster`,
    ADD CONSTRAINT `ck_character_pets_initial_level` CHECK (`initial_level` BETWEEN 1 AND 99),
    ADD CONSTRAINT `ck_character_pets_acquisition` CHECK (
        (`acquisition_origin`='LevelOnePet' AND `initial_level`=1 AND `wild_source_monster_id` IS NULL AND `wild_encounter_level` IS NULL)
        OR (`acquisition_origin`='CapturedWild' AND `wild_source_monster_id` IS NOT NULL
            AND `wild_encounter_level` BETWEEN 1 AND 99 AND `initial_level`=`wild_encounter_level`)
    ),
    ADD CONSTRAINT `fk_character_pets_wild_source_monster` FOREIGN KEY (`wild_source_monster_id`) REFERENCES `god2_game`.`monsters` (`monster_id`);

ALTER TABLE `god2_player`.`character_pet_skills`
    ADD COLUMN IF NOT EXISTS `unlock_source` varchar(20) NULL COMMENT '技能來源：InnateLevel等級固有或FedItem餵食道具' AFTER `skill_level`,
    ADD COLUMN IF NOT EXISTS `learned_from_item_id` bigint NULL COMMENT '第四技能消耗的戰寵技能道具ID' AFTER `unlock_source`,
    ADD COLUMN IF NOT EXISTS `learned_at_utc` datetime(6) NULL COMMENT '第四技能習得UTC時間' AFTER `learned_from_item_id`,
    ADD COLUMN IF NOT EXISTS `learning_rule_source_article_sn` int NOT NULL DEFAULT 921 COMMENT '第四技能規則來源：巴哈姆特精華區文章SN' AFTER `learned_at_utc`;

UPDATE `god2_player`.`character_pet_skills`
SET `unlock_source`=CASE WHEN `slot_index`=4 THEN 'FedItem' ELSE 'InnateLevel' END
WHERE `unlock_source` IS NULL;

ALTER TABLE `god2_player`.`character_pet_skills`
    MODIFY COLUMN `unlock_source` varchar(20) NOT NULL COMMENT '技能來源：InnateLevel等級固有或FedItem餵食道具',
    DROP CONSTRAINT IF EXISTS `ck_character_pet_skills_slot`,
    DROP CONSTRAINT IF EXISTS `ck_character_pet_skills_unlock_source`,
    DROP CONSTRAINT IF EXISTS `ck_character_pet_skills_fourth_source`,
    DROP CONSTRAINT IF EXISTS `ck_character_pet_skills_learning_source`,
    DROP FOREIGN KEY IF EXISTS `fk_character_pet_skills_learning_item`,
    ADD CONSTRAINT `ck_character_pet_skills_slot` CHECK (`slot_index` BETWEEN 1 AND 4),
    ADD CONSTRAINT `ck_character_pet_skills_unlock_source` CHECK (`unlock_source` IN ('InnateLevel','FedItem')),
    ADD CONSTRAINT `ck_character_pet_skills_fourth_source` CHECK (
        (`slot_index` BETWEEN 1 AND 3 AND `unlock_source`='InnateLevel' AND `learned_from_item_id` IS NULL)
        OR (`slot_index`=4 AND `unlock_source`='FedItem' AND (`is_unlocked`=0 OR `learned_from_item_id` IS NOT NULL))
    ),
    ADD CONSTRAINT `ck_character_pet_skills_learning_source` CHECK (`learning_rule_source_article_sn`=921),
    ADD CONSTRAINT `fk_character_pet_skills_learning_item` FOREIGN KEY (`learned_from_item_id`) REFERENCES `god2_game`.`item_registry` (`item_id`);

CREATE OR REPLACE VIEW `god2_game`.`vw_pet_categories_readable` AS
SELECT category_row.`category_id` AS `類別ID`,category_row.`name_zh_tw` AS `戰寵族群`,
       category_row.`article_section_zh_tw` AS `文章分類`,
       CASE category_row.`growth_pattern`
           WHEN 'Pure' THEN '純種型'
           WHEN 'Mixed' THEN '雜種型'
           ELSE '平均型'
       END AS `成長型態`,
       CONCAT('巴哈姆特封神2精華區 SN ',category_row.`source_article_sn`) AS `資料來源`,
       CASE category_row.`evidence_status`
           WHEN 'DirectlyLabeled' THEN '文章直接分類'
           ELSE '依文章公開配點推導'
       END AS `證據狀態`,
       category_row.`admin_note` AS `整理備註`
FROM `god2_game`.`pet_categories` category_row
WHERE category_row.`enabled`=1
ORDER BY category_row.`category_id`;

CREATE OR REPLACE VIEW `god2_game`.`vw_monster_capture_eligibility_readable` AS
SELECT monster_row.`monster_id` AS `怪物ID`,monster_row.`name_zh_tw` AS `怪物名稱`,
       monster_row.`level` AS `野生等級`,monster_row.`name_color_zh_tw` AS `名稱顏色`,
       monster_row.`is_quest_monster` AS `是否任務怪`,
       monster_row.`is_formation_boss` AS `是否陣法魔王`,
       CASE monster_row.`capture_eligibility`
           WHEN 'Capturable' THEN '可捕捉'
           WHEN 'Blocked' THEN '不可捕捉'
           ELSE '尚未確認'
       END AS `捕捉資格`,monster_row.`capture_block_reason_zh_tw` AS `不可捕捉原因`,
       CONCAT('巴哈姆特封神2精華區 SN ',monster_row.`capture_rule_source_article_sn`) AS `規則來源`
FROM `god2_game`.`monsters` monster_row
ORDER BY monster_row.`monster_id`;

CREATE OR REPLACE VIEW `god2_game`.`vw_pet_templates_readable` AS
SELECT template_row.`pet_template_id` AS `戰寵模板ID`,template_row.`name_zh_tw` AS `戰寵名稱`,
       category_row.`name_zh_tw` AS `戰寵族群`,template_row.`element_zh_tw` AS `五行`,template_row.`base_level` AS `玩家初始等級`,
       monster_row.`name_zh_tw` AS `野生怪物來源`,monster_row.`level` AS `野生遭遇等級`,
       template_row.`level_20_skill_name_zh_tw` AS `20級技能`,
       template_row.`level_40_skill_name_zh_tw` AS `40級技能`,
       template_row.`level_60_skill_name_zh_tw` AS `60級技能`,
       template_row.`capture_rule_zh_tw` AS `捕捉轉換規則`,
       template_row.`base_max_hp` AS `基礎最大HP`,template_row.`base_max_mp` AS `基礎最大MP`,
       template_row.`base_strength` AS `基礎力量`,template_row.`base_constitution` AS `基礎體力`,
       template_row.`base_intelligence` AS `基礎智力`,template_row.`base_speed` AS `基礎速度`,
       template_row.`evidence_status` AS `證據狀態`,template_row.`enabled` AS `啟用狀態`
FROM `god2_game`.`pet_templates` template_row
LEFT JOIN `god2_game`.`pet_categories` category_row ON category_row.`category_id`=template_row.`pet_category_id`
LEFT JOIN `god2_game`.`monsters` monster_row ON monster_row.`monster_id`=template_row.`wild_source_monster_id`;

CREATE OR REPLACE VIEW `god2_player`.`vw_character_pet_skills_readable` AS
SELECT skill_row.`pet_instance_id` AS `戰寵實例ID`,pet_row.`name` AS `戰寵名稱`,
       skill_row.`slot_index` AS `技能格`,skill_row.`skill_name_cache` AS `技能名稱`,
       CASE skill_row.`unlock_source` WHEN 'FedItem' THEN '餵食道具習得' ELSE '等級固有技能' END AS `技能來源`,
       item_row.`name_zh_tw` AS `消耗道具`,skill_row.`learned_at_utc` AS `習得時間`,
       skill_row.`is_unlocked` AS `已解鎖`,skill_row.`is_enabled` AS `已啟用`,
       CONCAT('巴哈姆特封神2精華區 SN ',skill_row.`learning_rule_source_article_sn`) AS `規則來源`
FROM `god2_player`.`character_pet_skills` skill_row
JOIN `god2_player`.`character_pets` pet_row ON pet_row.`pet_instance_id`=skill_row.`pet_instance_id`
LEFT JOIN `god2_game`.`item_registry` item_row ON item_row.`item_id`=skill_row.`learned_from_item_id`;
