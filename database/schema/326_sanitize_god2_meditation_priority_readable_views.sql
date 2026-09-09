DROP VIEW IF EXISTS `god2`.`vw_blackbox_pet_panel_priority_observations_readable`;
DROP VIEW IF EXISTS `god2`.`vw_meditation_skill_priority_readable`;

CREATE VIEW `god2`.`vw_meditation_skill_priority_readable` AS
SELECT
    skill_view.`技能ID`,
    skill_view.`技能名稱`,
    CASE
        WHEN skill_master.`技能說明` IS NULL OR skill_master.`技能說明` = '' THEN '技能說明待黑箱確認'
        WHEN skill_master.`技能說明` LIKE '%\\%' OR skill_master.`技能說明` LIKE '%.spc%' THEN '技能說明來自客戶端資源，效果待黑箱確認'
        ELSE skill_master.`技能說明`
    END AS `技能說明狀態`,
    CASE WHEN skill_view.`技能家族` = 'Unknown' OR skill_view.`技能家族` IS NULL THEN '未分類' ELSE skill_view.`技能家族` END AS `技能分類`,
    CASE WHEN skill_view.`目標規則` = 'Unknown' OR skill_view.`目標規則` IS NULL THEN '目標未證實' ELSE skill_view.`目標規則` END AS `目標規則`,
    CASE WHEN skill_view.`MP消耗規則` = 'EvidenceBlocked' OR skill_view.`MP消耗規則` = 'Unknown' OR skill_view.`MP消耗規則` IS NULL THEN 'MP消耗未證實' ELSE skill_view.`MP消耗規則` END AS `MP消耗規則`,
    skill_view.`MP消耗`,
    skill_view.`正式狀態`,
    '冥思相關技能：實際效果以黑箱測試確認，優先測回復、加持、持續時間與戰鬥限制' AS `功能對照`,
    '第一優先：測冥思是否回復HP/MP、是否增加能力、是否只能戰鬥內或戰鬥外使用' AS `黑箱測試優先級`
FROM `god2`.`vw_skill_semantic_profiles_readable` skill_view
LEFT JOIN `god2`.`vw_skills_readable` skill_master
    ON skill_master.`技能ID` = skill_view.`技能ID`
WHERE skill_view.`技能名稱` LIKE '%冥思%'
   OR skill_master.`技能說明` LIKE '%冥思%';

CREATE VIEW `god2`.`vw_blackbox_pet_panel_priority_observations_readable` AS
SELECT
    '神獸面板' AS `觀察類型`,
    pet_summary.`客戶端神獸ID` AS `主要ID`,
    pet_summary.`代表名稱` AS `名稱`,
    pet_summary.`神獸類型` AS `分類`,
    pet_summary.`功能對照`,
    CASE
        WHEN pet_summary.`資料完整度` = '缺神獸基礎數值證據' THEN '第一優先：測神獸HP/MP/攻防/速度面板'
        WHEN pet_summary.`資料完整度` = '缺神獸技能參照證據' THEN '第二優先：測神獸技能欄與戰鬥技能'
        ELSE '第三優先：測升級、成長與進化'
    END AS `黑箱測試優先級`,
    CONCAT(
        '物品ID=', COALESCE(pet_summary.`物品ID`, 0),
        '；資料片段=', pet_summary.`資料片段數`,
        '；基礎數值證據=', pet_summary.`基礎數值證據數`,
        '；技能證據=', pet_summary.`技能參照證據數`,
        '；進化證據=', pet_summary.`進化參照證據數`,
        '；片段=', COALESCE(LEFT(pet_summary.`已知片段名稱`, 180), '')
    ) AS `測試重點`
FROM `god2`.`vw_pet_profile_summary_readable` pet_summary

UNION ALL

SELECT
    '冥思技能' AS `觀察類型`,
    meditation_skill.`技能ID` AS `主要ID`,
    meditation_skill.`技能名稱` AS `名稱`,
    meditation_skill.`技能分類` AS `分類`,
    meditation_skill.`功能對照`,
    meditation_skill.`黑箱測試優先級`,
    CONCAT(
        '目標=', meditation_skill.`目標規則`,
        '；MP規則=', meditation_skill.`MP消耗規則`,
        '；MP=', COALESCE(meditation_skill.`MP消耗`, 0),
        '；正式狀態=', meditation_skill.`正式狀態`,
        '；', meditation_skill.`技能說明狀態`
    ) AS `測試重點`
FROM `god2`.`vw_meditation_skill_priority_readable` meditation_skill;
