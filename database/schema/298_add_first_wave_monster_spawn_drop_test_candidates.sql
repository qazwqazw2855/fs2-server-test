CREATE OR REPLACE VIEW `god2_game`.`vw_monster_spawn_first_wave_test_candidates_readable` AS
SELECT
    ranked_row.`怪物ID`,
    ranked_row.`怪物代碼`,
    ranked_row.`怪物名稱`,
    ranked_row.`等級`,
    ranked_row.`怪物定位`,
    ranked_row.`建議地圖ID`,
    ranked_row.`建議地圖`,
    ranked_row.`建議X`,
    ranked_row.`建議Y`,
    ranked_row.`建議出生數量`,
    ranked_row.`建議出生半徑`,
    ranked_row.`建議最短重生秒數`,
    ranked_row.`建議最長重生秒數`,
    ranked_row.`套用規則`,
    '第一輪開服測試候選：每隻怪只取一張低風險地圖，不直接批量啟用。' AS `測試策略`,
    '候選資料，不會直接讓怪物出生。' AS `安全狀態`
FROM (
    SELECT
        review_row.*,
        ROW_NUMBER() OVER (
            PARTITION BY review_row.`怪物ID`
            ORDER BY
                CASE
                    WHEN review_row.`建議地圖` LIKE '%村%' THEN 10
                    WHEN review_row.`建議地圖` LIKE '%林%' OR review_row.`建議地圖` LIKE '%山%' OR review_row.`建議地圖` LIKE '%谷%' THEN 20
                    ELSE 30
                END,
                review_row.`建議地圖ID`
        ) AS `candidate_rank`
    FROM `god2_game`.`vw_monster_spawn_activation_review_queue_readable` review_row
    WHERE review_row.`啟用風險` LIKE '低風險%'
) ranked_row
WHERE ranked_row.`candidate_rank` = 1
ORDER BY ranked_row.`等級`, ranked_row.`怪物ID`;

CREATE OR REPLACE VIEW `god2_game`.`vw_monster_drop_first_wave_test_candidates_readable` AS
SELECT
    review_row.`怪物ID`,
    review_row.`怪物代碼`,
    review_row.`怪物名稱`,
    review_row.`等級`,
    review_row.`怪物定位`,
    review_row.`套用規則`,
    review_row.`建議掉落率`,
    review_row.`建議數量`,
    review_row.`啟用風險`,
    '第一輪開服測試候選：先測每隻已啟用怪物的一組服務端設計掉落，不直接批量啟用物品清單。' AS `測試策略`,
    '候選資料，不會直接讓怪物掉落。' AS `安全狀態`
FROM `god2_game`.`vw_monster_drop_activation_review_queue_readable` review_row
WHERE review_row.`啟用風險` LIKE '低風險%'
ORDER BY review_row.`等級`, review_row.`怪物ID`;

CREATE OR REPLACE VIEW `god2_game`.`vw_monster_first_wave_test_candidate_summary_readable` AS
SELECT
    '第一輪怪物出生測試' AS `測試範圍`,
    COUNT(*) AS `候選筆數`,
    COUNT(DISTINCT spawn_row.`怪物ID`) AS `怪物數量`,
    COUNT(DISTINCT spawn_row.`建議地圖ID`) AS `地圖數量`,
    MIN(spawn_row.`等級`) AS `最低怪物等級`,
    MAX(spawn_row.`等級`) AS `最高怪物等級`
FROM `god2_game`.`vw_monster_spawn_first_wave_test_candidates_readable` spawn_row
UNION ALL
SELECT
    '第一輪怪物掉落測試' AS `測試範圍`,
    COUNT(*) AS `候選筆數`,
    COUNT(DISTINCT drop_row.`怪物ID`) AS `怪物數量`,
    0 AS `地圖數量`,
    MIN(drop_row.`等級`) AS `最低怪物等級`,
    MAX(drop_row.`等級`) AS `最高怪物等級`
FROM `god2_game`.`vw_monster_drop_first_wave_test_candidates_readable` drop_row;
