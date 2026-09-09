CREATE TABLE IF NOT EXISTS `god2_game`.`monster_spawn_design_rules` (
    `rule_id` int NOT NULL AUTO_INCREMENT COMMENT '怪物出生設計規則 ID',
    `rule_code` varchar(64) NOT NULL COMMENT '穩定規則代碼',
    `level_min` int NOT NULL COMMENT '適用最低怪物等級',
    `level_max` int NOT NULL COMMENT '適用最高怪物等級',
    `monster_role_zh_tw` varchar(32) NOT NULL COMMENT '怪物定位：一般怪、任務怪、菁英、首領或等級未知',
    `spawn_count` int NOT NULL COMMENT '建議每點出生數量',
    `spawn_radius` int NOT NULL COMMENT '建議出生半徑',
    `random_x` int NOT NULL COMMENT '建議 X 隨機偏移',
    `random_y` int NOT NULL COMMENT '建議 Y 隨機偏移',
    `respawn_seconds_min` int NOT NULL COMMENT '建議最短重生秒數',
    `respawn_seconds_max` int NOT NULL COMMENT '建議最長重生秒數',
    `source_policy_zh_tw` varchar(256) NOT NULL COMMENT '來源政策與使用限制',
    `enabled` tinyint NOT NULL DEFAULT 1 COMMENT '是否啟用此設計規則',
    `created_at_utc` datetime NOT NULL DEFAULT utc_timestamp(6) COMMENT '建立 UTC 時間',
    `updated_at_utc` datetime NOT NULL DEFAULT utc_timestamp(6) COMMENT '更新 UTC 時間',
    PRIMARY KEY (`rule_id`),
    UNIQUE KEY `ux_monster_spawn_design_rules_code` (`rule_code`),
    KEY `ix_monster_spawn_design_rules_lookup` (`enabled`, `monster_role_zh_tw`, `level_min`, `level_max`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci COMMENT='服務端自訂怪物出生設計規則；非官方實測資料';

CREATE TABLE IF NOT EXISTS `god2_game`.`monster_drop_design_rules` (
    `rule_id` int NOT NULL AUTO_INCREMENT COMMENT '怪物掉落設計規則 ID',
    `rule_code` varchar(64) NOT NULL COMMENT '穩定規則代碼',
    `level_min` int NOT NULL COMMENT '適用最低怪物等級',
    `level_max` int NOT NULL COMMENT '適用最高怪物等級',
    `monster_role_zh_tw` varchar(32) NOT NULL COMMENT '怪物定位：一般怪、任務怪、菁英、首領或等級未知',
    `common_drop_rate` decimal(10,6) NOT NULL COMMENT '一般掉落建議機率',
    `uncommon_drop_rate` decimal(10,6) NOT NULL COMMENT '較稀有掉落建議機率',
    `rare_drop_rate` decimal(10,6) NOT NULL COMMENT '稀有掉落建議機率',
    `minimum_quantity` int NOT NULL COMMENT '建議最小數量',
    `maximum_quantity` int NOT NULL COMMENT '建議最大數量',
    `source_policy_zh_tw` varchar(256) NOT NULL COMMENT '來源政策與使用限制',
    `enabled` tinyint NOT NULL DEFAULT 1 COMMENT '是否啟用此設計規則',
    `created_at_utc` datetime NOT NULL DEFAULT utc_timestamp(6) COMMENT '建立 UTC 時間',
    `updated_at_utc` datetime NOT NULL DEFAULT utc_timestamp(6) COMMENT '更新 UTC 時間',
    PRIMARY KEY (`rule_id`),
    UNIQUE KEY `ux_monster_drop_design_rules_code` (`rule_code`),
    KEY `ix_monster_drop_design_rules_lookup` (`enabled`, `monster_role_zh_tw`, `level_min`, `level_max`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci COMMENT='服務端自訂怪物掉落設計規則；非官方實測資料';

INSERT INTO `god2_game`.`monster_spawn_design_rules`
    (`rule_code`,`level_min`,`level_max`,`monster_role_zh_tw`,`spawn_count`,`spawn_radius`,`random_x`,`random_y`,`respawn_seconds_min`,`respawn_seconds_max`,`source_policy_zh_tw`,`enabled`)
VALUES
    ('spawn_unknown_default',0,0,'等級未知',1,4,2,2,180,300,'等級未知怪物不應大量出生；正式啟用前應補等級或地圖定位。',1),
    ('spawn_general_001_030',1,30,'一般怪',3,8,4,4,60,120,'前期一般怪密度較高，供新手練功；需依地圖尺寸調整。',1),
    ('spawn_general_031_060',31,60,'一般怪',3,10,5,5,90,180,'中期一般怪標準出生規則。',1),
    ('spawn_general_061_999',61,999,'一般怪',2,12,6,6,120,240,'後期一般怪密度略低，避免高階地圖過度壓迫。',1),
    ('spawn_quest_001_999',1,999,'任務怪',2,6,3,3,45,90,'任務怪重生較快，避免任務卡住。',1),
    ('spawn_elite_001_999',1,999,'菁英',1,8,4,4,300,600,'菁英怪低密度、較慢重生。',1),
    ('spawn_boss_001_999',1,999,'首領',1,4,2,2,900,1800,'首領怪只作服務端候選；正式啟用前應做實戰測試。',1)
ON DUPLICATE KEY UPDATE
    `level_min`=VALUES(`level_min`),
    `level_max`=VALUES(`level_max`),
    `monster_role_zh_tw`=VALUES(`monster_role_zh_tw`),
    `spawn_count`=VALUES(`spawn_count`),
    `spawn_radius`=VALUES(`spawn_radius`),
    `random_x`=VALUES(`random_x`),
    `random_y`=VALUES(`random_y`),
    `respawn_seconds_min`=VALUES(`respawn_seconds_min`),
    `respawn_seconds_max`=VALUES(`respawn_seconds_max`),
    `source_policy_zh_tw`=VALUES(`source_policy_zh_tw`),
    `enabled`=VALUES(`enabled`),
    `updated_at_utc`=utc_timestamp(6);

INSERT INTO `god2_game`.`monster_drop_design_rules`
    (`rule_code`,`level_min`,`level_max`,`monster_role_zh_tw`,`common_drop_rate`,`uncommon_drop_rate`,`rare_drop_rate`,`minimum_quantity`,`maximum_quantity`,`source_policy_zh_tw`,`enabled`)
VALUES
    ('drop_unknown_default',0,0,'等級未知',0.100000,0.030000,0.005000,1,1,'等級未知怪物只給保守掉落候選；正式啟用前應補等級或定位。',1),
    ('drop_general_001_030',1,30,'一般怪',0.220000,0.060000,0.008000,1,1,'前期一般怪掉落偏寬，支援新手資源取得。',1),
    ('drop_general_031_060',31,60,'一般怪',0.200000,0.055000,0.010000,1,2,'中期一般怪標準掉落規則。',1),
    ('drop_general_061_999',61,999,'一般怪',0.180000,0.050000,0.012000,1,2,'後期一般怪掉落需搭配經濟測試校準。',1),
    ('drop_quest_001_999',1,999,'任務怪',0.350000,0.080000,0.010000,1,1,'任務怪可提高任務物或普通資源掉落，但任務必要物仍應獨立建規則。',1),
    ('drop_elite_001_999',1,999,'菁英',0.450000,0.120000,0.025000,1,2,'菁英怪提供較高掉落回饋。',1),
    ('drop_boss_001_999',1,999,'首領',0.800000,0.300000,0.080000,1,3,'首領掉落只作服務端候選；正式啟用前應做經濟與戰鬥測試。',1)
ON DUPLICATE KEY UPDATE
    `level_min`=VALUES(`level_min`),
    `level_max`=VALUES(`level_max`),
    `monster_role_zh_tw`=VALUES(`monster_role_zh_tw`),
    `common_drop_rate`=VALUES(`common_drop_rate`),
    `uncommon_drop_rate`=VALUES(`uncommon_drop_rate`),
    `rare_drop_rate`=VALUES(`rare_drop_rate`),
    `minimum_quantity`=VALUES(`minimum_quantity`),
    `maximum_quantity`=VALUES(`maximum_quantity`),
    `source_policy_zh_tw`=VALUES(`source_policy_zh_tw`),
    `enabled`=VALUES(`enabled`),
    `updated_at_utc`=utc_timestamp(6);

CREATE OR REPLACE VIEW `god2_game`.`vw_monster_spawn_design_candidates_readable` AS
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
    map_row.`map_id` AS `建議地圖ID`,
    map_row.`name_zh_tw` AS `建議地圖`,
    COALESCE(map_row.`minimum_x`, 0) + GREATEST(1, ROUND((COALESCE(map_row.`maximum_x`, 100) - COALESCE(map_row.`minimum_x`, 0)) / 2)) AS `建議X`,
    COALESCE(map_row.`minimum_y`, 0) + GREATEST(1, ROUND((COALESCE(map_row.`maximum_y`, 100) - COALESCE(map_row.`minimum_y`, 0)) / 2)) AS `建議Y`,
    spawn_rule.`spawn_count` AS `建議出生數量`,
    spawn_rule.`spawn_radius` AS `建議出生半徑`,
    spawn_rule.`random_x` AS `建議X隨機`,
    spawn_rule.`random_y` AS `建議Y隨機`,
    spawn_rule.`respawn_seconds_min` AS `建議最短重生秒數`,
    spawn_rule.`respawn_seconds_max` AS `建議最長重生秒數`,
    spawn_rule.`rule_code` AS `套用規則`,
    '服務端出生設計候選；需依地圖實際怪區調整後才正式啟用。' AS `候選值政策`
FROM `god2_game`.`monsters` monster_row
JOIN `god2_game`.`monster_spawn_design_rules` spawn_rule
    ON spawn_rule.`enabled` = 1
   AND spawn_rule.`monster_role_zh_tw` = CASE
        WHEN monster_row.`boss` = 1 THEN '首領'
        WHEN monster_row.`elite` = 1 THEN '菁英'
        WHEN monster_row.`is_quest_monster` = 1 THEN '任務怪'
        WHEN monster_row.`level` IS NULL THEN '等級未知'
        ELSE '一般怪'
   END
   AND COALESCE(monster_row.`level`, 0) BETWEEN spawn_rule.`level_min` AND spawn_rule.`level_max`
JOIN `god2_game`.`maps` map_row
    ON map_row.`enabled` = 1
WHERE monster_row.`enabled` = 1
  AND NOT EXISTS (
      SELECT 1
      FROM `god2_game`.`monster_spawns` spawn_row
      WHERE spawn_row.`monster_id` = monster_row.`monster_id`
  );

CREATE OR REPLACE VIEW `god2_game`.`vw_monster_drop_design_rules_readable` AS
SELECT
    drop_rule.`rule_code` AS `規則代碼`,
    CONCAT(drop_rule.`level_min`, ' 到 ', drop_rule.`level_max`) AS `等級範圍`,
    drop_rule.`monster_role_zh_tw` AS `怪物定位`,
    CONCAT('普通 ', drop_rule.`common_drop_rate`, '；較稀有 ', drop_rule.`uncommon_drop_rate`, '；稀有 ', drop_rule.`rare_drop_rate`) AS `建議掉落率`,
    CONCAT(drop_rule.`minimum_quantity`, ' 到 ', drop_rule.`maximum_quantity`) AS `建議數量`,
    drop_rule.`source_policy_zh_tw` AS `使用政策`,
    CASE drop_rule.`enabled`
        WHEN 1 THEN '啟用'
        ELSE '停用'
    END AS `規則狀態`
FROM `god2_game`.`monster_drop_design_rules` drop_rule;
