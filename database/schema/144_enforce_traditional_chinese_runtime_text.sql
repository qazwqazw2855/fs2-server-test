-- Normalize player-visible and operator-authored runtime text to Traditional
-- Chinese. Raw client/source/evidence columns remain byte-for-byte unchanged.

UPDATE `god2_game`.`item_registry`
SET `name_zh_tw`=REPLACE(`name_zh_tw`,'倒霉','倒楣')
WHERE `name_zh_tw` LIKE '%倒霉%';

UPDATE `god2_game`.`equipment`
SET `name_zh_tw`=REPLACE(`name_zh_tw`,'倒霉','倒楣')
WHERE `name_zh_tw` LIKE '%倒霉%';

UPDATE `god2_game`.`item_registry`
SET `description_zh_tw`=REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(
    `description_zh_tw`,
    '活动结束后','活動結束後'),'仓','倉'),'归','歸'),'奖','獎'),'励','勵'),
    '绝','絕'),'风','風'),'动','動'),'无','無'),'倒霉','倒楣'),'结','結')
WHERE `description_zh_tw` REGEXP '[仓归奖励绝风动无结]' OR `description_zh_tw` LIKE '%倒霉%';

UPDATE `god2_game`.`items`
SET `description_zh_tw`=REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(
    `description_zh_tw`,
    '活动结束后','活動結束後'),'仓','倉'),'归','歸'),'奖','獎'),'励','勵'),
    '绝','絕'),'风','風'),'动','動'),'无','無'),'倒霉','倒楣'),'结','結')
WHERE `description_zh_tw` REGEXP '[仓归奖励绝风动无结]' OR `description_zh_tw` LIKE '%倒霉%';

UPDATE `god2_game`.`equipment`
SET `description_zh_tw`=REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(
    `description_zh_tw`,
    '活动结束后','活動結束後'),'仓','倉'),'归','歸'),'奖','獎'),'励','勵'),
    '绝','絕'),'风','風'),'动','動'),'无','無'),'倒霉','倒楣'),'结','結')
WHERE `description_zh_tw` REGEXP '[仓归奖励绝风动无结]' OR `description_zh_tw` LIKE '%倒霉%';

UPDATE `god2_game`.`weapons`
SET `description_zh_tw`=REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(
    `description_zh_tw`,
    '活动结束后','活動結束後'),'仓','倉'),'归','歸'),'奖','獎'),'励','勵'),
    '绝','絕'),'风','風'),'动','動'),'无','無'),'倒霉','倒楣'),'结','結')
WHERE `description_zh_tw` REGEXP '[仓归奖励绝风动无结]' OR `description_zh_tw` LIKE '%倒霉%';

UPDATE `god2_game`.`item_usage_rules`
SET `condition_text_zh_tw`=REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(
    `condition_text_zh_tw`,
    '活动结束后','活動結束後'),'仓','倉'),'归','歸'),'奖','獎'),'励','勵'),
    '绝','絕'),'风','風'),'动','動'),'无','無'),'倒霉','倒楣'),'结','結')
WHERE `condition_text_zh_tw` REGEXP '[仓归奖励绝风动无结]' OR `condition_text_zh_tw` LIKE '%倒霉%';

UPDATE `god2_game`.`immortal_templates`
SET `admin_note`=REPLACE(`admin_note`,'土行孙','土行孫')
WHERE `admin_note` LIKE '%土行孙%';

UPDATE `god2_game`.`magic_skill_damage_coefficients`
SET `admin_note`=REPLACE(`admin_note`,'台版','臺版')
WHERE `admin_note` LIKE '%台版%';

ALTER TABLE `god2_game`.`magic_skill_damage_coefficients`
    MODIFY COLUMN `region_compatibility_status` varchar(50) NOT NULL COMMENT '臺版相容狀態',
    COMMENT='跨版本社群實測魔法傷害公式；臺版相容性尚待驗證';

CREATE OR REPLACE VIEW `god2_game`.`vw_magic_skill_damage_coefficients_readable` AS
SELECT CASE coefficient.`attack_element`
           WHEN 'Metal' THEN '金系'
           WHEN 'Wood' THEN '木系'
           WHEN 'Water' THEN '水系'
           WHEN 'Fire' THEN '火系'
           WHEN 'Earth' THEN '土系'
       END AS `法術系別`,
       coefficient.`skill_tier` AS `技能階級`,
       coefficient.`minimum_multiplier` AS `最低倍率`,
       coefficient.`midpoint_multiplier` AS `建議倍率`,
       coefficient.`maximum_multiplier` AS `最高倍率`,
       coefficient.`damage_formula_zh_tw` AS `完整傷害公式`,
       coefficient.`pet_meditation_multiplier` AS `戰寵冥思修正`,
       coefficient.`character_meditation_candidate` AS `人物冥思候選修正`,
       '候選，尚未確認' AS `人物冥思證據`,
       '大陸版玩家實測' AS `來源版本`,
       '臺版尚待驗證' AS `臺版相容狀態`,
       coefficient.`source_url` AS `資料來源`,
       coefficient.`enabled` AS `近似公式可解析`
FROM `god2_game`.`magic_skill_damage_coefficients` coefficient
ORDER BY FIELD(coefficient.`attack_element`,'Metal','Wood','Water','Fire','Earth'),
         coefficient.`skill_tier`;

DROP PROCEDURE IF EXISTS `god2`.`assert_traditional_chinese_runtime_text`;
DELIMITER $$
CREATE PROCEDURE `god2`.`assert_traditional_chinese_runtime_text`()
BEGIN
    DECLARE remaining_count bigint DEFAULT 0;

    SELECT SUM(problem_count) INTO remaining_count
    FROM (
        SELECT COUNT(*) AS problem_count FROM `god2_game`.`item_registry`
        WHERE `name_zh_tw` LIKE '%倒霉%'
           OR `description_zh_tw` REGEXP '[仓归奖励绝风动无结]'
           OR `description_zh_tw` LIKE '%倒霉%'
        UNION ALL
        SELECT COUNT(*) FROM `god2_game`.`items`
        WHERE `description_zh_tw` REGEXP '[仓归奖励绝风动无结]'
           OR `description_zh_tw` LIKE '%倒霉%'
        UNION ALL
        SELECT COUNT(*) FROM `god2_game`.`equipment`
        WHERE `name_zh_tw` LIKE '%倒霉%'
           OR `description_zh_tw` REGEXP '[仓归奖励绝风动无结]'
           OR `description_zh_tw` LIKE '%倒霉%'
        UNION ALL
        SELECT COUNT(*) FROM `god2_game`.`weapons`
        WHERE `description_zh_tw` REGEXP '[仓归奖励绝风动无结]'
           OR `description_zh_tw` LIKE '%倒霉%'
        UNION ALL
        SELECT COUNT(*) FROM `god2_game`.`item_usage_rules`
        WHERE `condition_text_zh_tw` REGEXP '[仓归奖励绝风动无结]'
           OR `condition_text_zh_tw` LIKE '%倒霉%'
        UNION ALL
        SELECT COUNT(*) FROM `god2_game`.`immortal_templates` WHERE `admin_note` LIKE '%土行孙%'
        UNION ALL
        SELECT COUNT(*) FROM `god2_game`.`magic_skill_damage_coefficients` WHERE `admin_note` LIKE '%台版%'
        UNION ALL
        SELECT COUNT(*) FROM `information_schema`.`COLUMNS`
        WHERE `TABLE_SCHEMA`='god2_game'
          AND `TABLE_NAME`='magic_skill_damage_coefficients'
          AND `COLUMN_COMMENT` LIKE '%台版%'
    ) remaining;

    IF remaining_count<>0 THEN
        SIGNAL SQLSTATE '45000' SET MESSAGE_TEXT='Traditional Chinese runtime text normalization incomplete';
    END IF;
END$$
DELIMITER ;
CALL `god2`.`assert_traditional_chinese_runtime_text`();
DROP PROCEDURE `god2`.`assert_traditional_chinese_runtime_text`;
