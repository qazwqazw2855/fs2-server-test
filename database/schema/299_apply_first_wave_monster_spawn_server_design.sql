INSERT INTO `god2_game`.`monster_spawns`
    (`spawn_id`,
     `monster_id`,`monster_name_cache`,
     `map_id`,`map_name_cache`,
     `position_x`,`position_y`,`direction`,
     `spawn_count`,`random_x`,`random_y`,`spawn_radius`,
     `respawn_seconds_min`,`respawn_seconds_max`,
     `group_id`,`instance_key`,
     `spawn_source_zh_tw`,`spawn_policy_zh_tw`,
     `enabled`,`created_at_utc`,`updated_at_utc`)
SELECT
    candidate_row.`怪物ID` + 900000000000 AS `spawn_id`,
    candidate_row.`怪物ID`,
    candidate_row.`怪物名稱`,
    candidate_row.`建議地圖ID`,
    candidate_row.`建議地圖`,
    candidate_row.`建議X`,
    candidate_row.`建議Y`,
    0 AS `direction`,
    candidate_row.`建議出生數量`,
    2 AS `random_x`,
    2 AS `random_y`,
    candidate_row.`建議出生半徑`,
    candidate_row.`建議最短重生秒數`,
    candidate_row.`建議最長重生秒數`,
    candidate_row.`怪物ID` + 910000000000 AS `group_id`,
    CONCAT('first-wave-server-design-', candidate_row.`怪物ID`) AS `instance_key`,
    '服務端設計值' AS `spawn_source_zh_tw`,
    CONCAT('第一輪開服測試出生點；依 ', candidate_row.`套用規則`, ' 建立，每隻怪只啟用一筆低風險地圖候選。') AS `spawn_policy_zh_tw`,
    1 AS `enabled`,
    utc_timestamp(6) AS `created_at_utc`,
    utc_timestamp(6) AS `updated_at_utc`
FROM `god2_game`.`vw_monster_spawn_first_wave_test_candidates_readable` candidate_row
WHERE NOT EXISTS (
    SELECT 1
    FROM `god2_game`.`monster_spawns` existing_row
    WHERE existing_row.`monster_id` = candidate_row.`怪物ID`
      AND existing_row.`instance_key` = CONCAT('first-wave-server-design-', candidate_row.`怪物ID`)
)
ON DUPLICATE KEY UPDATE
    `monster_name_cache`=VALUES(`monster_name_cache`),
    `map_id`=VALUES(`map_id`),
    `map_name_cache`=VALUES(`map_name_cache`),
    `position_x`=VALUES(`position_x`),
    `position_y`=VALUES(`position_y`),
    `direction`=VALUES(`direction`),
    `spawn_count`=VALUES(`spawn_count`),
    `random_x`=VALUES(`random_x`),
    `random_y`=VALUES(`random_y`),
    `spawn_radius`=VALUES(`spawn_radius`),
    `respawn_seconds_min`=VALUES(`respawn_seconds_min`),
    `respawn_seconds_max`=VALUES(`respawn_seconds_max`),
    `group_id`=VALUES(`group_id`),
    `instance_key`=VALUES(`instance_key`),
    `spawn_source_zh_tw`=VALUES(`spawn_source_zh_tw`),
    `spawn_policy_zh_tw`=VALUES(`spawn_policy_zh_tw`),
    `enabled`=VALUES(`enabled`),
    `updated_at_utc`=utc_timestamp(6);

CREATE OR REPLACE VIEW `god2_game`.`vw_monster_first_wave_spawn_runtime_readiness_readable` AS
SELECT
    spawn_row.`spawn_source_zh_tw` AS `出生來源`,
    CASE spawn_row.`enabled`
        WHEN 1 THEN '正式啟用'
        ELSE '未啟用'
    END AS `服務端狀態`,
    COUNT(*) AS `出生點數量`,
    COUNT(DISTINCT spawn_row.`monster_id`) AS `怪物數量`,
    COUNT(DISTINCT spawn_row.`map_id`) AS `地圖數量`,
    SUM(CASE WHEN spawn_row.`instance_key` LIKE 'first-wave-server-design-%' THEN 1 ELSE 0 END) AS `第一輪測試出生點`
FROM `god2_game`.`monster_spawns` spawn_row
GROUP BY spawn_row.`spawn_source_zh_tw`, spawn_row.`enabled`
ORDER BY `出生點數量` DESC, `出生來源`;
