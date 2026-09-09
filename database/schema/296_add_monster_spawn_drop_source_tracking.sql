ALTER TABLE `god2_game`.`monster_spawns`
    ADD COLUMN IF NOT EXISTS `spawn_source_zh_tw` varchar(64) NOT NULL DEFAULT '待服務端設計' COMMENT '怪物出生來源：官方實測、服務端設計、混合來源或待服務端設計' AFTER `instance_key`,
    ADD COLUMN IF NOT EXISTS `spawn_policy_zh_tw` varchar(256) NOT NULL DEFAULT '尚未建立出生規則' COMMENT '怪物出生使用政策與注意事項' AFTER `spawn_source_zh_tw`;

ALTER TABLE `god2_game`.`monster_drops`
    ADD COLUMN IF NOT EXISTS `drop_source_zh_tw` varchar(64) NOT NULL DEFAULT '待服務端設計' COMMENT '怪物掉落來源：官方實測、服務端設計、混合來源或待服務端設計' AFTER `drop_group`,
    ADD COLUMN IF NOT EXISTS `drop_policy_zh_tw` varchar(256) NOT NULL DEFAULT '尚未建立掉落規則' COMMENT '怪物掉落使用政策與注意事項' AFTER `drop_source_zh_tw`;

UPDATE `god2_game`.`monster_spawns`
SET
    `spawn_source_zh_tw` = CASE
        WHEN `enabled` = 1 THEN '既有正式出生資料；待來源細分'
        ELSE '待服務端設計'
    END,
    `spawn_policy_zh_tw` = CASE
        WHEN `enabled` = 1 THEN '既有正式出生資料可保留測試，但後續需標明官方實測或服務端設計來源。'
        ELSE '可依地圖區間、怪物定位與服務端出生設計規則建立候選，審查後再啟用。'
    END
WHERE `spawn_source_zh_tw` IN ('待服務端設計', '既有正式出生資料；待來源細分')
   OR `spawn_policy_zh_tw` IN ('尚未建立出生規則', '');

UPDATE `god2_game`.`monster_drops`
SET
    `drop_source_zh_tw` = CASE
        WHEN `enabled` = 1 THEN '既有正式掉落資料；待來源細分'
        ELSE '待服務端設計'
    END,
    `drop_policy_zh_tw` = CASE
        WHEN `enabled` = 1 THEN '既有正式掉落資料可保留測試，但後續需標明官方實測或服務端設計來源。'
        ELSE '可依怪物定位、道具分類與服務端掉落設計規則建立候選，審查後再啟用。'
    END
WHERE `drop_source_zh_tw` IN ('待服務端設計', '既有正式掉落資料；待來源細分')
   OR `drop_policy_zh_tw` IN ('尚未建立掉落規則', '');

CREATE OR REPLACE VIEW `god2_game`.`vw_monster_spawn_source_readable` AS
SELECT
    spawn_row.`spawn_id` AS `出生點ID`,
    spawn_row.`monster_id` AS `怪物ID`,
    COALESCE(spawn_row.`monster_name_cache`, monster_row.`name_zh_tw`, '未知怪物') AS `怪物名稱`,
    spawn_row.`map_id` AS `地圖ID`,
    COALESCE(spawn_row.`map_name_cache`, map_row.`name_zh_tw`, '未知地圖') AS `地圖名稱`,
    CONCAT('X ', COALESCE(spawn_row.`position_x`, 0), '；Y ', COALESCE(spawn_row.`position_y`, 0)) AS `出生座標`,
    CONCAT('數量 ', COALESCE(spawn_row.`spawn_count`, 0), '；半徑 ', COALESCE(spawn_row.`spawn_radius`, 0)) AS `出生範圍`,
    CONCAT('重生 ', COALESCE(spawn_row.`respawn_seconds_min`, 0), ' 到 ', COALESCE(spawn_row.`respawn_seconds_max`, 0), ' 秒') AS `重生時間`,
    spawn_row.`spawn_source_zh_tw` AS `出生來源`,
    spawn_row.`spawn_policy_zh_tw` AS `使用政策`,
    CASE spawn_row.`enabled`
        WHEN 1 THEN '正式啟用'
        ELSE '未啟用'
    END AS `服務端狀態`,
    spawn_row.`updated_at_utc` AS `更新時間UTC`
FROM `god2_game`.`monster_spawns` spawn_row
LEFT JOIN `god2_game`.`monsters` monster_row
    ON monster_row.`monster_id` = spawn_row.`monster_id`
LEFT JOIN `god2_game`.`maps` map_row
    ON map_row.`map_id` = spawn_row.`map_id`;

CREATE OR REPLACE VIEW `god2_game`.`vw_monster_drop_source_readable` AS
SELECT
    drop_row.`drop_id` AS `掉落ID`,
    drop_row.`monster_id` AS `怪物ID`,
    COALESCE(drop_row.`monster_name_cache`, monster_row.`name_zh_tw`, '未知怪物') AS `怪物名稱`,
    drop_row.`item_id` AS `物品ID`,
    COALESCE(drop_row.`item_name_cache`, item_row.`name_zh_tw`, '未知物品') AS `物品名稱`,
    CONCAT(COALESCE(drop_row.`minimum_quantity`, 0), ' 到 ', COALESCE(drop_row.`maximum_quantity`, 0)) AS `掉落數量`,
    CASE
        WHEN drop_row.`drop_rate` IS NULL THEN '掉落率未建立'
        ELSE CONCAT('掉落率 ', drop_row.`drop_rate`, '；單位 ', drop_row.`drop_rate_unit`)
    END AS `掉落率`,
    CASE drop_row.`is_guaranteed`
        WHEN 1 THEN '保證掉落'
        WHEN 0 THEN '機率掉落'
        ELSE '未標示'
    END AS `掉落型態`,
    drop_row.`drop_source_zh_tw` AS `掉落來源`,
    drop_row.`drop_policy_zh_tw` AS `使用政策`,
    CASE drop_row.`enabled`
        WHEN 1 THEN '正式啟用'
        ELSE '未啟用'
    END AS `服務端狀態`,
    drop_row.`updated_at_utc` AS `更新時間UTC`
FROM `god2_game`.`monster_drops` drop_row
LEFT JOIN `god2_game`.`monsters` monster_row
    ON monster_row.`monster_id` = drop_row.`monster_id`
LEFT JOIN `god2_game`.`items` item_row
    ON item_row.`item_id` = drop_row.`item_id`;

CREATE OR REPLACE VIEW `god2_game`.`vw_monster_spawn_drop_runtime_readiness_readable` AS
SELECT
    '怪物出生' AS `資料範圍`,
    spawn_row.`spawn_source_zh_tw` AS `來源`,
    CASE spawn_row.`enabled`
        WHEN 1 THEN '正式啟用'
        ELSE '未啟用'
    END AS `服務端狀態`,
    COUNT(*) AS `資料筆數`
FROM `god2_game`.`monster_spawns` spawn_row
GROUP BY spawn_row.`spawn_source_zh_tw`, spawn_row.`enabled`
UNION ALL
SELECT
    '怪物掉落' AS `資料範圍`,
    drop_row.`drop_source_zh_tw` AS `來源`,
    CASE drop_row.`enabled`
        WHEN 1 THEN '正式啟用'
        ELSE '未啟用'
    END AS `服務端狀態`,
    COUNT(*) AS `資料筆數`
FROM `god2_game`.`monster_drops` drop_row
GROUP BY drop_row.`drop_source_zh_tw`, drop_row.`enabled`;
