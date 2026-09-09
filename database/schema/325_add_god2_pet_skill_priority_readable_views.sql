DROP VIEW IF EXISTS `god2`.`vw_pet_profile_summary_readable`;
DROP VIEW IF EXISTS `god2`.`vw_meditation_skill_priority_readable`;
DROP VIEW IF EXISTS `god2`.`vw_blackbox_pet_panel_priority_observations_readable`;

CREATE VIEW `god2`.`vw_pet_profile_summary_readable` AS
SELECT
    pet_view.`客戶端神獸ID`,
    pet_view.`物品ID`,
    COALESCE(item_view.`物品名稱`, MAX(pet_view.`對應物品名稱`), MAX(pet_view.`神獸名稱`)) AS `代表名稱`,
    pet_view.`神獸類型`,
    COUNT(*) AS `資料片段數`,
    GROUP_CONCAT(DISTINCT pet_view.`神獸名稱` ORDER BY pet_view.`神獸名稱` SEPARATOR '、') AS `已知片段名稱`,
    GROUP_CONCAT(DISTINCT pet_view.`成長類型` ORDER BY pet_view.`成長類型` SEPARATOR '、') AS `已知成長類型`,
    SUM(CASE WHEN pet_view.`基礎數值狀態` = '已有基礎數值證據' THEN 1 ELSE 0 END) AS `基礎數值證據數`,
    SUM(CASE WHEN pet_view.`技能參照狀態` = '已有技能參照證據' THEN 1 ELSE 0 END) AS `技能參照證據數`,
    SUM(CASE WHEN pet_view.`進化參照狀態` = '已有進化參照證據' THEN 1 ELSE 0 END) AS `進化參照證據數`,
    CASE
        WHEN SUM(CASE WHEN pet_view.`基礎數值狀態` = '已有基礎數值證據' THEN 1 ELSE 0 END) = 0 THEN '缺神獸基礎數值證據'
        WHEN SUM(CASE WHEN pet_view.`技能參照狀態` = '已有技能參照證據' THEN 1 ELSE 0 END) = 0 THEN '缺神獸技能參照證據'
        ELSE '神獸資料已有主要證據'
    END AS `資料完整度`,
    '神獸面板、出戰、升級、HP/MP、物攻物防、魔攻魔防、速度與技能欄' AS `功能對照`
FROM `god2`.`vw_pet_content_profiles_readable` pet_view
LEFT JOIN `god2`.`vw_items_readable` item_view
    ON item_view.`物品ID` = pet_view.`物品ID`
GROUP BY
    pet_view.`客戶端神獸ID`,
    pet_view.`物品ID`,
    pet_view.`神獸類型`;

CREATE VIEW `god2`.`vw_meditation_skill_priority_readable` AS
SELECT
    skill_view.`技能ID`,
    skill_view.`技能名稱`,
    skill_master.`技能說明`,
    skill_view.`技能家族`,
    skill_view.`目標規則`,
    skill_view.`MP消耗規則`,
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
    COALESCE(meditation_skill.`技能家族`, '') AS `分類`,
    meditation_skill.`功能對照`,
    meditation_skill.`黑箱測試優先級`,
    CONCAT(
        '目標=', COALESCE(meditation_skill.`目標規則`, ''),
        '；MP規則=', COALESCE(meditation_skill.`MP消耗規則`, ''),
        '；MP=', COALESCE(meditation_skill.`MP消耗`, 0),
        '；正式狀態=', meditation_skill.`正式狀態`,
        '；說明=', COALESCE(LEFT(meditation_skill.`技能說明`, 160), '')
    ) AS `測試重點`
FROM `god2`.`vw_meditation_skill_priority_readable` meditation_skill;
