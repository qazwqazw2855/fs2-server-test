-- Current Bahamut Wiki cross-check for general-map pet skills and per-level automatic growth.
-- Existing expanded Traditional Chinese skill names remain authoritative; the Wiki shorthand is not copied over them.

SET @general_pet_skill_url = 'https://wiki2.gamer.com.tw/wiki.php?n=8591%3A%E4%B8%80%E8%88%AC%E5%9C%B0%E5%9C%96%E6%88%B0%E5%AF%B5%E5%B1%AC%E6%80%A7%E5%8F%8A%E6%8A%80%E8%83%BD%E8%B3%87%E6%96%99';
SET @general_pet_growth_url = 'https://wiki2.gamer.com.tw/wiki.php?n=8591%3A%E4%B8%80%E8%88%AC%E5%9C%B0%E5%9C%96%E6%88%B0%E5%AF%B5%E5%8D%87%E7%B4%9A%E9%85%8D%E9%BB%9E%E8%B3%87%E6%96%99';

ALTER TABLE `god2_game`.`pet_categories`
    ADD COLUMN IF NOT EXISTS `source_url` varchar(500) NULL COMMENT '可直接查閱的資料來源網址' AFTER `source_article_sn`,
    MODIFY COLUMN `source_article_sn` int NULL COMMENT '舊巴哈精華區文章SN；Wiki頁面資料可為NULL',
    DROP CONSTRAINT IF EXISTS `ck_pet_categories_pattern`,
    DROP CONSTRAINT IF EXISTS `ck_pet_categories_source`,
    DROP CONSTRAINT IF EXISTS `ck_pet_categories_evidence`,
    ADD CONSTRAINT `ck_pet_categories_pattern` CHECK (`growth_pattern` IN ('Pure','Mixed','Average','Unknown')),
    ADD CONSTRAINT `ck_pet_categories_source` CHECK (`source_article_sn` IN (961,1400) OR `source_url` IS NOT NULL),
    ADD CONSTRAINT `ck_pet_categories_evidence` CHECK (`evidence_status` IN ('DirectlyLabeled','DerivedFromPublishedStats','WikiVerified'));

INSERT INTO `god2_game`.`pet_categories`
    (`category_id`,`name_zh_tw`,`article_section_zh_tw`,`growth_pattern`,`growth_archetype_id`,
     `source_article_sn`,`source_url`,`evidence_status`,`enabled`,`admin_note`)
VALUES
    (65,'魔女類','一般地圖戰寵','Unknown',NULL,NULL,@general_pet_skill_url,'WikiVerified',0,
     '族群與技能已由Wiki確認；完整四品級成長資料仍有未知格，服務端暫不啟用成長。'),
    (66,'鬼類','一般地圖戰寵','Unknown',NULL,NULL,@general_pet_skill_url,'WikiVerified',0,
     '族群與技能已由Wiki確認；尚無可逐格驗證的完整自動配點，服務端暫不啟用成長。')
ON DUPLICATE KEY UPDATE
    `name_zh_tw`=VALUES(`name_zh_tw`),
    `article_section_zh_tw`=VALUES(`article_section_zh_tw`),
    `growth_pattern`=VALUES(`growth_pattern`),
    `growth_archetype_id`=VALUES(`growth_archetype_id`),
    `source_article_sn`=VALUES(`source_article_sn`),
    `source_url`=VALUES(`source_url`),
    `evidence_status`=VALUES(`evidence_status`),
    `enabled`=VALUES(`enabled`),
    `admin_note`=VALUES(`admin_note`);

ALTER TABLE `god2_game`.`pet_templates`
    ADD COLUMN IF NOT EXISTS `source_url` varchar(500) NULL COMMENT '一般戰寵資料來源網址' AFTER `source_article_sn`;

-- Migration 096 removed growth_profile_id, but the schema-generated audit trigger still referenced OLD/NEW.growth_profile_id.
-- The catalog builder reinstalls this trigger from current information_schema after migrations complete.
DROP TRIGGER IF EXISTS `god2_game`.`trg_adm_u_god2_game_pet_templates`;

UPDATE `god2_game`.`pet_templates`
SET `source_url`=@general_pet_skill_url
WHERE `source_article_sn`=1405;

UPDATE `god2_game`.`pet_templates`
SET `pet_category_id`=65
WHERE `name_zh_tw` IN ('天雷魔女','大地魔女') AND `pet_family`='魔女類';

UPDATE `god2_game`.`pet_templates`
SET `pet_category_id`=66
WHERE `name_zh_tw` IN ('大頭鬼','獠牙鬼','草球鬼','青面鬼','赤面鬼','滾石鬼') AND `pet_family`='鬼類';

ALTER TABLE `god2_game`.`pet_automatic_growth_allocations`
    ADD COLUMN IF NOT EXISTS `source_url` varchar(500) NULL COMMENT '目前核對用Wiki頁面網址' AFTER `source_article_sn`,
    MODIFY COLUMN `source_article_sn` int NULL COMMENT '舊巴哈精華區文章SN；Wiki頁面資料可為NULL',
    DROP CONSTRAINT IF EXISTS `ck_pet_auto_growth_values`,
    ADD CONSTRAINT `ck_pet_auto_growth_values` CHECK (
        (`evidence_status`='SourceMissing' AND `constitution_delta` IS NULL AND `strength_delta` IS NULL
            AND `intelligence_delta` IS NULL AND `speed_delta` IS NULL AND `published_total` IS NULL AND `enabled`=0)
        OR (`evidence_status` IN ('SourceConflict','UserDraft') AND `enabled`=0)
        OR (`evidence_status` IN ('BahamutVerified','WikiVerified')
            AND `published_total`=`expected_total` AND `enabled`=1)
        OR (`evidence_status`='UserConfirmed' AND `published_total`=`expected_total` AND `enabled`=1
            AND `source_reference_zh_tw` IS NOT NULL)
    );

INSERT INTO `god2_game`.`pet_automatic_growth_allocations`
    (`growth_archetype_id`,`growth_grade`,`minimum_level`,`maximum_level`,
     `constitution_delta`,`strength_delta`,`intelligence_delta`,`speed_delta`,
     `expected_total`,`source_value_zh_tw`,`source_article_sn`,`source_url`,`evidence_status`,`enabled`)
VALUES
    (4,'Top',1,19,0,0,2,3,5,'2智3速',NULL,@general_pet_growth_url,'WikiVerified',1),
    (6,'Top',1,19,3,2,0,0,5,'3體2力',NULL,@general_pet_growth_url,'WikiVerified',1),
    (6,'Top',20,49,3,3,0,0,6,'3體3力',NULL,@general_pet_growth_url,'WikiVerified',1),
    (6,'Top',50,99,4,4,0,0,8,'4體4力',NULL,@general_pet_growth_url,'WikiVerified',1),
    (6,'LateBreakthrough',1,19,2,2,0,0,4,'2體2力',NULL,@general_pet_growth_url,'WikiVerified',1),
    (6,'LateBreakthrough',20,49,3,3,0,0,6,'3體3力',NULL,@general_pet_growth_url,'WikiVerified',1),
    (6,'LateBreakthrough',50,99,5,5,0,0,10,'5體5力',NULL,@general_pet_growth_url,'WikiVerified',1),
    (6,'Breakthrough',1,19,2,2,0,0,4,'2體2力',NULL,@general_pet_growth_url,'WikiVerified',1),
    (6,'Breakthrough',20,49,4,4,0,0,8,'4體4力',NULL,@general_pet_growth_url,'WikiVerified',1),
    (6,'Breakthrough',50,99,5,5,0,0,10,'5體5力',NULL,@general_pet_growth_url,'WikiVerified',1),
    (12,'LateBreakthrough',50,99,3,3,0,4,10,'3體3力4速',NULL,@general_pet_growth_url,'WikiVerified',1),
    (12,'Breakthrough',50,99,3,3,0,4,10,'3體3力4速',NULL,@general_pet_growth_url,'WikiVerified',1),
    (18,'LateBreakthrough',50,99,4,5,0,1,10,'4體5力1速',NULL,@general_pet_growth_url,'WikiVerified',1),
    (26,'Normal',50,99,1,2,0,5,8,'1體2力5速',NULL,@general_pet_growth_url,'WikiVerified',1),
    (26,'Top',50,99,1,2,0,5,8,'1體2力5速',NULL,@general_pet_growth_url,'WikiVerified',1),
    (28,'Normal',50,99,1,1,3,3,8,'1體1力3智3速',NULL,@general_pet_growth_url,'WikiVerified',1),
    (28,'Top',50,99,1,1,3,3,8,'1體1力3智3速',NULL,@general_pet_growth_url,'WikiVerified',1),
    (28,'LateBreakthrough',20,49,1,1,2,2,6,'1體1力2智2速',NULL,@general_pet_growth_url,'WikiVerified',1),
    (28,'Breakthrough',20,49,1,1,3,3,8,'1體1力3智3速',NULL,@general_pet_growth_url,'WikiVerified',1),
    (29,'Normal',50,99,4,2,2,0,8,'4體2力2智',NULL,@general_pet_growth_url,'WikiVerified',1),
    (29,'Top',50,99,4,2,2,0,8,'4體2力2智',NULL,@general_pet_growth_url,'WikiVerified',1),
    (32,'Normal',50,99,4,3,0,1,8,'4體3力1速',NULL,@general_pet_growth_url,'WikiVerified',1),
    (32,'Top',1,19,3,1,0,1,5,'3體1力1速',NULL,@general_pet_growth_url,'WikiVerified',1),
    (32,'Top',50,99,4,3,0,1,8,'4體3力1速',NULL,@general_pet_growth_url,'WikiVerified',1),
    (32,'LateBreakthrough',50,99,2,2,5,1,10,'2體2力5智1速',NULL,@general_pet_growth_url,'WikiVerified',1),
    (32,'Breakthrough',50,99,2,2,5,1,10,'2體2力5智1速',NULL,@general_pet_growth_url,'WikiVerified',1),
    (34,'Normal',50,99,4,1,0,3,8,'4體1力3速',NULL,@general_pet_growth_url,'WikiVerified',1),
    (34,'Top',1,19,3,0,0,2,5,'3體2速',NULL,@general_pet_growth_url,'WikiVerified',1),
    (34,'Top',50,99,4,1,0,3,8,'4體1力3速',NULL,@general_pet_growth_url,'WikiVerified',1),
    (36,'Normal',50,99,3,1,2,2,8,'3體1力2智2速',NULL,@general_pet_growth_url,'WikiVerified',1),
    (36,'Top',50,99,3,1,2,2,8,'3體1力2智2速',NULL,@general_pet_growth_url,'WikiVerified',1),
    (36,'LateBreakthrough',50,99,4,2,2,2,10,'4體2力2智2速',NULL,@general_pet_growth_url,'WikiVerified',1),
    (36,'Breakthrough',50,99,4,2,2,2,10,'4體2力2智2速',NULL,@general_pet_growth_url,'WikiVerified',1),
    (38,'Normal',50,99,4,2,1,1,8,'4體2力1智1速',NULL,@general_pet_growth_url,'WikiVerified',1),
    (38,'Top',50,99,4,2,1,1,8,'4體2力1智1速',NULL,@general_pet_growth_url,'WikiVerified',1),
    (39,'Normal',50,99,4,3,0,1,8,'4體3力1速',NULL,@general_pet_growth_url,'WikiVerified',1),
    (39,'Top',50,99,4,3,0,1,8,'4體3力1速',NULL,@general_pet_growth_url,'WikiVerified',1)
ON DUPLICATE KEY UPDATE
    `maximum_level`=VALUES(`maximum_level`),
    `constitution_delta`=VALUES(`constitution_delta`),
    `strength_delta`=VALUES(`strength_delta`),
    `intelligence_delta`=VALUES(`intelligence_delta`),
    `speed_delta`=VALUES(`speed_delta`),
    `expected_total`=VALUES(`expected_total`),
    `source_value_zh_tw`=VALUES(`source_value_zh_tw`),
    `source_article_sn`=VALUES(`source_article_sn`),
    `source_url`=VALUES(`source_url`),
    `evidence_status`=VALUES(`evidence_status`),
    `enabled`=VALUES(`enabled`);

CREATE OR REPLACE VIEW `god2_game`.`vw_pet_wiki_refresh_readable` AS
SELECT category_row.`category_id` AS `類別ID`,category_row.`name_zh_tw` AS `戰寵族群`,
       category_row.`growth_pattern` AS `成長分類`,category_row.`enabled` AS `服務端成長啟用`,
       category_row.`source_url` AS `族群資料來源`,COUNT(template_row.`pet_template_id`) AS `戰寵模板數`
FROM `god2_game`.`pet_categories` category_row
LEFT JOIN `god2_game`.`pet_templates` template_row ON template_row.`pet_category_id`=category_row.`category_id`
GROUP BY category_row.`category_id`,category_row.`name_zh_tw`,category_row.`growth_pattern`,category_row.`enabled`,category_row.`source_url`;
