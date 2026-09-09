DROP VIEW IF EXISTS `god2_game`.`vw_blackbox_immortal_test_targets_readable`;
DROP VIEW IF EXISTS `god2_game`.`vw_blackbox_pet_test_targets_readable`;

CREATE VIEW `god2_game`.`vw_blackbox_immortal_test_targets_readable` AS
SELECT
    it.`immortal_template_id` AS `神仙模板ID`,
    it.`code` AS `服務端代碼`,
    it.`name_zh_tw` AS `神仙名稱`,
    COALESCE(it.`immortal_family`, '') AS `神仙系別`,
    COALESCE(it.`profession_code`, '') AS `對應職業`,
    CASE WHEN it.`is_special` = 1 THEN '特殊神仙' ELSE '一般神仙' END AS `神仙類型`,
    it.`initial_level` AS `初始等級`,
    COALESCE(rank_info.`name_zh_tw`, '') AS `初始品階`,
    it.`base_max_hp` AS `基礎最大HP`,
    it.`base_max_mp` AS `基礎最大MP`,
    it.`base_strength` AS `腕力`,
    it.`base_constitution` AS `體力`,
    it.`base_intelligence` AS `智力`,
    it.`base_speed` AS `速度`,
    it.`base_metal` AS `金`,
    it.`base_wood` AS `木`,
    it.`base_water` AS `水`,
    it.`base_fire` AS `火`,
    it.`base_earth` AS `土`,
    it.`base_physical_attack` AS `物理攻擊`,
    it.`base_physical_defense` AS `物理防禦`,
    it.`base_magic_attack` AS `法術攻擊`,
    it.`base_magic_defense` AS `法術防禦`,
    CASE
        WHEN it.`enabled` = 1 THEN '第一優先：測神仙取得、召喚、面板能力、戰鬥入場'
        ELSE '第二優先：資料存在但未啟用，先黑箱確認官方是否開放'
    END AS `黑箱測試優先級`,
    '測神仙清單顯示、召喚狀態、HP/MP、五行、攻防面板與戰鬥中是否套用。' AS `測試重點`,
    CASE WHEN it.`enabled` = 1 THEN '已啟用' ELSE '未啟用' END AS `服務端狀態`
FROM `god2_game`.`immortal_templates` it
LEFT JOIN `god2_game`.`immortal_ranks` rank_info ON rank_info.`rank_id` = it.`initial_rank_id`;

CREATE VIEW `god2_game`.`vw_blackbox_pet_test_targets_readable` AS
SELECT
    pt.`pet_template_id` AS `戰寵模板ID`,
    pt.`code` AS `服務端代碼`,
    pt.`name_zh_tw` AS `戰寵名稱`,
    COALESCE(pt.`pet_family`, '') AS `戰寵族系`,
    COALESCE(category_info.`name_zh_tw`, '') AS `戰寵族群`,
    COALESCE(pt.`element_zh_tw`, '') AS `五行`,
    pt.`wild_source_monster_id` AS `野生怪物ID`,
    COALESCE(monster_info.`name_zh_tw`, '') AS `野生怪物名稱`,
    COALESCE(pt.`capture_rule_zh_tw`, '') AS `捕捉轉換規則`,
    pt.`base_level` AS `玩家取得等級`,
    pt.`base_max_hp` AS `基礎最大HP`,
    pt.`base_max_mp` AS `基礎最大MP`,
    pt.`base_strength` AS `腕力`,
    pt.`base_constitution` AS `體力`,
    pt.`base_intelligence` AS `智力`,
    pt.`base_speed` AS `速度`,
    pt.`maximum_skill_slots` AS `最大技能格`,
    COALESCE(skill_summary.`技能數量`, 0) AS `已登錄技能數量`,
    COALESCE(skill_summary.`技能摘要`, '') AS `技能摘要`,
    COALESCE(pt.`level_20_skill_name_zh_tw`, '') AS `20級技能`,
    COALESCE(pt.`level_40_skill_name_zh_tw`, '') AS `40級技能`,
    COALESCE(pt.`level_60_skill_name_zh_tw`, '') AS `60級技能`,
    CASE
        WHEN pt.`enabled` = 1
             AND pt.`wild_source_monster_id` IS NOT NULL
             AND COALESCE(skill_summary.`技能數量`, 0) > 0
            THEN '第一優先：測捕捉、出戰、升級、技能'
        WHEN pt.`enabled` = 1
             AND pt.`wild_source_monster_id` IS NOT NULL
            THEN '第二優先：測捕捉與出戰，技能待補'
        WHEN pt.`enabled` = 1
            THEN '第三優先：測寵物面板與出戰來源'
        ELSE '擱置：資料存在但未啟用'
    END AS `黑箱測試優先級`,
    CASE
        WHEN pt.`wild_source_monster_id` IS NULL THEN '缺野生來源；測試時先確認官方取得方式。'
        WHEN COALESCE(skill_summary.`技能數量`, 0) = 0 THEN '缺技能登錄；測試時記錄升級技能與戰鬥可用技能。'
        ELSE '測捕捉成功率、出戰面板、HP/MP、升級成長、20/40/60級技能。'
    END AS `測試重點`,
    CASE WHEN pt.`enabled` = 1 THEN '已啟用' ELSE '未啟用' END AS `服務端狀態`
FROM `god2_game`.`pet_templates` pt
LEFT JOIN `god2_game`.`pet_categories` category_info ON category_info.`category_id` = pt.`pet_category_id`
LEFT JOIN `god2_game`.`monsters` monster_info ON monster_info.`monster_id` = pt.`wild_source_monster_id`
LEFT JOIN (
    SELECT
        pts.`pet_template_id`,
        COUNT(*) AS `技能數量`,
        GROUP_CONCAT(
            CONCAT(
                '格', pts.`slot_index`,
                ':', COALESCE(pts.`skill_name_cache`, ''),
                ' Lv', COALESCE(pts.`skill_level`, 0)
            )
            ORDER BY pts.`slot_index`
            SEPARATOR '；'
        ) AS `技能摘要`
    FROM `god2_game`.`pet_template_skills` pts
    GROUP BY pts.`pet_template_id`
) skill_summary ON skill_summary.`pet_template_id` = pt.`pet_template_id`;
