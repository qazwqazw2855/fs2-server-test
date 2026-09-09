CREATE OR REPLACE VIEW `god2_game`.`vw_monster_spawn_activation_review_queue_readable` AS
SELECT
    candidate_row.`怪物ID`,
    candidate_row.`怪物代碼`,
    candidate_row.`怪物名稱`,
    candidate_row.`等級`,
    candidate_row.`怪物定位`,
    candidate_row.`建議地圖ID`,
    candidate_row.`建議地圖`,
    candidate_row.`建議X`,
    candidate_row.`建議Y`,
    candidate_row.`建議出生數量`,
    candidate_row.`建議出生半徑`,
    candidate_row.`建議最短重生秒數`,
    candidate_row.`建議最長重生秒數`,
    candidate_row.`套用規則`,
    CASE
        WHEN candidate_row.`怪物定位` IN ('首領', '菁英') THEN '高風險：首領或菁英出生前需人工指定地圖與區域'
        WHEN candidate_row.`怪物定位` = '任務怪' THEN '高風險：任務怪出生前需確認任務地圖'
        WHEN candidate_row.`等級` = 0 THEN '高風險：怪物等級未知，不可直接出生'
        WHEN map_group.`same_level_band_map_count` > 12 THEN '中風險：同區間地圖過多，需先選地圖群'
        ELSE '低風險：可作服務端設計候選'
    END AS `啟用風險`,
    CASE
        WHEN candidate_row.`怪物定位` IN ('首領', '菁英') THEN '先建立專用首領/菁英出生點，不批次套全地圖'
        WHEN candidate_row.`怪物定位` = '任務怪' THEN '先與任務資料對照，確認任務所需地圖後再啟用'
        WHEN candidate_row.`等級` = 0 THEN '先補怪物等級或歸類'
        WHEN map_group.`same_level_band_map_count` > 12 THEN '先依地圖等級區間縮小到 1 到 3 張代表地圖'
        ELSE '可人工抽樣後批次建立服務端設計出生點'
    END AS `建議處理`,
    '候選資料，不會直接讓怪物在正式地圖出生。' AS `安全狀態`
FROM `god2_game`.`vw_monster_spawn_design_candidates_readable` candidate_row
JOIN (
    SELECT
        CASE
            WHEN map_row.`name_zh_tw` LIKE '%新手%' OR map_row.`name_zh_tw` LIKE '%村%' THEN '新手或村莊'
            WHEN map_row.`name_zh_tw` LIKE '%城%' THEN '城鎮'
            WHEN map_row.`name_zh_tw` LIKE '%洞%' OR map_row.`name_zh_tw` LIKE '%窟%' OR map_row.`name_zh_tw` LIKE '%穴%' THEN '洞窟'
            WHEN map_row.`name_zh_tw` LIKE '%山%' OR map_row.`name_zh_tw` LIKE '%林%' OR map_row.`name_zh_tw` LIKE '%谷%' THEN '野外'
            ELSE '一般地圖'
        END AS `map_band`,
        COUNT(*) AS `same_level_band_map_count`
    FROM `god2_game`.`maps` map_row
    WHERE map_row.`enabled` = 1
    GROUP BY
        CASE
            WHEN map_row.`name_zh_tw` LIKE '%新手%' OR map_row.`name_zh_tw` LIKE '%村%' THEN '新手或村莊'
            WHEN map_row.`name_zh_tw` LIKE '%城%' THEN '城鎮'
            WHEN map_row.`name_zh_tw` LIKE '%洞%' OR map_row.`name_zh_tw` LIKE '%窟%' OR map_row.`name_zh_tw` LIKE '%穴%' THEN '洞窟'
            WHEN map_row.`name_zh_tw` LIKE '%山%' OR map_row.`name_zh_tw` LIKE '%林%' OR map_row.`name_zh_tw` LIKE '%谷%' THEN '野外'
            ELSE '一般地圖'
        END
) map_group
    ON map_group.`map_band` = CASE
        WHEN candidate_row.`建議地圖` LIKE '%新手%' OR candidate_row.`建議地圖` LIKE '%村%' THEN '新手或村莊'
        WHEN candidate_row.`建議地圖` LIKE '%城%' THEN '城鎮'
        WHEN candidate_row.`建議地圖` LIKE '%洞%' OR candidate_row.`建議地圖` LIKE '%窟%' OR candidate_row.`建議地圖` LIKE '%穴%' THEN '洞窟'
        WHEN candidate_row.`建議地圖` LIKE '%山%' OR candidate_row.`建議地圖` LIKE '%林%' OR candidate_row.`建議地圖` LIKE '%谷%' THEN '野外'
        ELSE '一般地圖'
    END;

CREATE OR REPLACE VIEW `god2_game`.`vw_monster_spawn_activation_review_summary_readable` AS
SELECT
    review_row.`啟用風險`,
    review_row.`怪物定位`,
    COUNT(*) AS `候選筆數`,
    COUNT(DISTINCT review_row.`怪物ID`) AS `怪物數量`,
    COUNT(DISTINCT review_row.`建議地圖ID`) AS `地圖數量`
FROM `god2_game`.`vw_monster_spawn_activation_review_queue_readable` review_row
GROUP BY review_row.`啟用風險`, review_row.`怪物定位`
ORDER BY
    CASE
        WHEN review_row.`啟用風險` LIKE '高風險%' THEN 10
        WHEN review_row.`啟用風險` LIKE '中風險%' THEN 20
        ELSE 30
    END,
    review_row.`怪物定位`;

CREATE OR REPLACE VIEW `god2_game`.`vw_monster_drop_activation_review_queue_readable` AS
SELECT
    monster_row.`monster_id` AS `怪物ID`,
    monster_row.`code` AS `怪物代碼`,
    monster_row.`name_zh_tw` AS `怪物名稱`,
    COALESCE(monster_row.`level`, 0) AS `等級`,
    CASE
        WHEN monster_row.`boss` = 1 THEN '首領'
        WHEN monster_row.`elite` = 1 THEN '菁英'
        WHEN monster_row.`is_quest_monster` = 1 THEN '任務怪'
        WHEN monster_row.`level` IS NULL THEN '等級未知'
        ELSE '一般怪'
    END AS `怪物定位`,
    drop_rule.`rule_code` AS `套用規則`,
    CONCAT('普通 ', drop_rule.`common_drop_rate`, '；較稀有 ', drop_rule.`uncommon_drop_rate`, '；稀有 ', drop_rule.`rare_drop_rate`) AS `建議掉落率`,
    CONCAT(drop_rule.`minimum_quantity`, ' 到 ', drop_rule.`maximum_quantity`) AS `建議數量`,
    CASE
        WHEN monster_row.`boss` = 1 THEN '高風險：首領掉落會影響經濟，需人工審查'
        WHEN monster_row.`elite` = 1 THEN '中風險：菁英掉落需抽樣測試'
        WHEN monster_row.`is_quest_monster` = 1 THEN '高風險：任務怪掉落需確認任務物'
        WHEN monster_row.`level` IS NULL THEN '高風險：等級未知，不可直接建立掉落'
        ELSE '低風險：可作服務端設計候選'
    END AS `啟用風險`,
    CASE
        WHEN monster_row.`boss` = 1 THEN '先建立首領專屬掉落群並做經濟測試'
        WHEN monster_row.`elite` = 1 THEN '先用少量通用掉落測試'
        WHEN monster_row.`is_quest_monster` = 1 THEN '先與任務需求對照，任務必要物獨立建規則'
        WHEN monster_row.`level` IS NULL THEN '先補等級或怪物定位'
        ELSE '可依怪物等級與道具分類批次建立服務端設計掉落'
    END AS `建議處理`,
    '候選資料，不會直接讓怪物正式掉落物品。' AS `安全狀態`
FROM `god2_game`.`monsters` monster_row
JOIN `god2_game`.`monster_drop_design_rules` drop_rule
    ON drop_rule.`enabled` = 1
   AND drop_rule.`monster_role_zh_tw` = CASE
        WHEN monster_row.`boss` = 1 THEN '首領'
        WHEN monster_row.`elite` = 1 THEN '菁英'
        WHEN monster_row.`is_quest_monster` = 1 THEN '任務怪'
        WHEN monster_row.`level` IS NULL THEN '等級未知'
        ELSE '一般怪'
   END
   AND COALESCE(monster_row.`level`, 0) BETWEEN drop_rule.`level_min` AND drop_rule.`level_max`
WHERE monster_row.`enabled` = 1
  AND NOT EXISTS (
      SELECT 1
      FROM `god2_game`.`monster_drops` drop_row
      WHERE drop_row.`monster_id` = monster_row.`monster_id`
  );

CREATE OR REPLACE VIEW `god2_game`.`vw_monster_drop_activation_review_summary_readable` AS
SELECT
    review_row.`啟用風險`,
    review_row.`怪物定位`,
    COUNT(*) AS `候選怪物數`
FROM `god2_game`.`vw_monster_drop_activation_review_queue_readable` review_row
GROUP BY review_row.`啟用風險`, review_row.`怪物定位`
ORDER BY
    CASE
        WHEN review_row.`啟用風險` LIKE '高風險%' THEN 10
        WHEN review_row.`啟用風險` LIKE '中風險%' THEN 20
        ELSE 30
    END,
    review_row.`怪物定位`;
