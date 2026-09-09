CREATE TABLE IF NOT EXISTS `god2_research`.`monster_catalog_evidence` (
    `catalog_table` varchar(40) NOT NULL,
    `catalog_row_id` bigint NOT NULL,
    `evidence_status` varchar(30) NOT NULL,
    `admin_note` varchar(500) NULL,
    `moved_at_utc` datetime(6) NOT NULL DEFAULT CURRENT_TIMESTAMP(6),
    PRIMARY KEY (`catalog_table`,`catalog_row_id`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci
COMMENT='Research-only monster catalog evidence moved out of formal runtime tables.';

INSERT INTO `god2_research`.`monster_catalog_evidence`
    (`catalog_table`,`catalog_row_id`,`evidence_status`,`admin_note`)
SELECT 'monsters',`monster_id`,`evidence_status`,`admin_note`
FROM `god2_game`.`monsters`
ON DUPLICATE KEY UPDATE
    `evidence_status`=VALUES(`evidence_status`),
    `admin_note`=VALUES(`admin_note`);

INSERT INTO `god2_research`.`monster_catalog_evidence`
    (`catalog_table`,`catalog_row_id`,`evidence_status`,`admin_note`)
SELECT 'monster_spawns',`spawn_id`,`evidence_status`,`admin_note`
FROM `god2_game`.`monster_spawns`
ON DUPLICATE KEY UPDATE
    `evidence_status`=VALUES(`evidence_status`),
    `admin_note`=VALUES(`admin_note`);

INSERT INTO `god2_research`.`monster_catalog_evidence`
    (`catalog_table`,`catalog_row_id`,`evidence_status`,`admin_note`)
SELECT 'monster_drops',`drop_id`,`evidence_status`,`admin_note`
FROM `god2_game`.`monster_drops`
ON DUPLICATE KEY UPDATE
    `evidence_status`=VALUES(`evidence_status`),
    `admin_note`=VALUES(`admin_note`);

DROP VIEW IF EXISTS `god2_game`.`vw_monsters_full`;
DROP VIEW IF EXISTS `god2_game`.`vw_monsters_readable`;
DROP VIEW IF EXISTS `god2_game`.`vw_monster_spawns_full`;
DROP VIEW IF EXISTS `god2_game`.`vw_monster_spawns_readable`;
DROP VIEW IF EXISTS `god2_game`.`vw_monster_drops_full`;
DROP VIEW IF EXISTS `god2_game`.`vw_monster_drops_readable`;

ALTER TABLE `god2_game`.`monsters`
    DROP COLUMN IF EXISTS `evidence_status`,
    DROP COLUMN IF EXISTS `admin_note`;

ALTER TABLE `god2_game`.`monster_spawns`
    DROP COLUMN IF EXISTS `evidence_status`,
    DROP COLUMN IF EXISTS `admin_note`;

ALTER TABLE `god2_game`.`monster_drops`
    DROP COLUMN IF EXISTS `evidence_status`,
    DROP COLUMN IF EXISTS `admin_note`;

CREATE OR REPLACE VIEW `god2_game`.`vw_monsters_full` AS
SELECT `monster_id`,`code`,`name_zh_tw`,`name_original`,`monster_family`,`resource_id`,
       `level`,`max_hp`,`max_mp`,`strength`,`constitution`,`intelligence`,`speed`,
       `metal`,`wood`,`water`,`fire`,`earth`,
       `physical_attack`,`physical_defense`,`magic_attack`,`magic_defense`,
       `experience_reward`,`currency_reward`,`ai_profile_id`,
       `boss`,`elite`,`aggressive`,`enabled`,`created_at_utc`,`updated_at_utc`
FROM `god2_game`.`monsters`;

CREATE OR REPLACE VIEW `god2_game`.`vw_monsters_readable` AS
SELECT
    monster_row.`monster_id` AS `怪物編號`,
    monster_row.`code` AS `服務端代碼`,
    monster_row.`name_zh_tw` AS `怪物名稱`,
    monster_row.`monster_family` AS `怪物類型`,
    monster_row.`level` AS `等級`,
    monster_row.`max_hp` AS `最大HP`,
    monster_row.`max_mp` AS `最大MP`,
    monster_row.`strength` AS `力量`,
    monster_row.`constitution` AS `體力`,
    monster_row.`intelligence` AS `智力`,
    monster_row.`speed` AS `速度`,
    monster_row.`metal` AS `金屬性`,
    monster_row.`wood` AS `木屬性`,
    monster_row.`water` AS `水屬性`,
    monster_row.`fire` AS `火屬性`,
    monster_row.`earth` AS `土屬性`,
    monster_row.`physical_attack` AS `物理攻擊`,
    monster_row.`physical_defense` AS `物理防禦`,
    monster_row.`magic_attack` AS `法術攻擊`,
    monster_row.`magic_defense` AS `法術防禦`,
    monster_row.`experience_reward` AS `經驗獎勵`,
    monster_row.`currency_reward` AS `金錢獎勵`,
    CASE monster_row.`boss` WHEN 1 THEN '是' WHEN 0 THEN '否' ELSE NULL END AS `是否首領`,
    CASE monster_row.`elite` WHEN 1 THEN '是' WHEN 0 THEN '否' ELSE NULL END AS `是否菁英`,
    CASE monster_row.`aggressive` WHEN 1 THEN '主動' WHEN 0 THEN '被動' ELSE NULL END AS `攻擊模式`,
    CASE monster_row.`enabled` WHEN 1 THEN '已啟用' ELSE '未啟用' END AS `服務端狀態`
FROM `god2_game`.`monsters` monster_row;

CREATE OR REPLACE VIEW `god2_game`.`vw_monster_spawns_full` AS
SELECT spawn_row.`spawn_id`,spawn_row.`monster_id`,monster_row.`name_zh_tw` AS `monster_name_zh_tw`,
       spawn_row.`map_id`,map_row.`name_zh_tw` AS `map_name_zh_tw`,
       spawn_row.`position_x`,spawn_row.`position_y`,spawn_row.`spawn_count`,spawn_row.`spawn_radius`,
       spawn_row.`respawn_seconds_min`,spawn_row.`respawn_seconds_max`,
       spawn_row.`enabled`,spawn_row.`created_at_utc`,spawn_row.`updated_at_utc`
FROM `god2_game`.`monster_spawns` spawn_row
JOIN `god2_game`.`monsters` monster_row ON monster_row.`monster_id`=spawn_row.`monster_id`
JOIN `god2_game`.`maps` map_row ON map_row.`map_id`=spawn_row.`map_id`;

CREATE OR REPLACE VIEW `god2_game`.`vw_monster_spawns_readable` AS
SELECT
    spawn_row.`spawn_id` AS `出生點編號`,
    spawn_row.`monster_id` AS `怪物編號`,
    monster_row.`name_zh_tw` AS `怪物名稱`,
    spawn_row.`map_id` AS `地圖編號`,
    map_row.`name_zh_tw` AS `地圖名稱`,
    spawn_row.`position_x` AS `座標X`,
    spawn_row.`position_y` AS `座標Y`,
    spawn_row.`spawn_count` AS `出現數量`,
    spawn_row.`spawn_radius` AS `出生範圍`,
    spawn_row.`respawn_seconds_min` AS `最短重生秒數`,
    spawn_row.`respawn_seconds_max` AS `最長重生秒數`,
    CASE spawn_row.`enabled` WHEN 1 THEN '已啟用' ELSE '未啟用' END AS `服務端狀態`
FROM `god2_game`.`monster_spawns` spawn_row
JOIN `god2_game`.`monsters` monster_row ON monster_row.`monster_id`=spawn_row.`monster_id`
JOIN `god2_game`.`maps` map_row ON map_row.`map_id`=spawn_row.`map_id`;

CREATE OR REPLACE VIEW `god2_game`.`vw_monster_drops_full` AS
SELECT drop_row.`drop_id`,drop_row.`monster_id`,monster_row.`name_zh_tw` AS `monster_name_zh_tw`,
       drop_row.`item_id`,item_row.`name_zh_tw` AS `item_name_zh_tw`,
       drop_row.`minimum_quantity`,drop_row.`maximum_quantity`,
       drop_row.`drop_rate`,drop_row.`drop_rate_unit`,drop_row.`drop_group`,drop_row.`is_guaranteed`,
       drop_row.`enabled`,drop_row.`created_at_utc`,drop_row.`updated_at_utc`
FROM `god2_game`.`monster_drops` drop_row
JOIN `god2_game`.`monsters` monster_row ON monster_row.`monster_id`=drop_row.`monster_id`
JOIN `god2_game`.`items` item_row ON item_row.`item_id`=drop_row.`item_id`;

CREATE OR REPLACE VIEW `god2_game`.`vw_monster_drops_readable` AS
SELECT
    drop_row.`drop_id` AS `掉落編號`,
    drop_row.`monster_id` AS `怪物編號`,
    monster_row.`name_zh_tw` AS `怪物名稱`,
    drop_row.`item_id` AS `物品編號`,
    item_row.`name_zh_tw` AS `物品名稱`,
    drop_row.`minimum_quantity` AS `最少數量`,
    drop_row.`maximum_quantity` AS `最多數量`,
    drop_row.`drop_rate` AS `掉落機率`,
    drop_row.`drop_rate_unit` AS `機率單位`,
    drop_row.`drop_group` AS `掉落群組`,
    CASE drop_row.`is_guaranteed` WHEN 1 THEN '是' WHEN 0 THEN '否' ELSE NULL END AS `是否必定掉落`,
    CASE drop_row.`enabled` WHEN 1 THEN '已啟用' ELSE '未啟用' END AS `服務端狀態`
FROM `god2_game`.`monster_drops` drop_row
JOIN `god2_game`.`monsters` monster_row ON monster_row.`monster_id`=drop_row.`monster_id`
JOIN `god2_game`.`items` item_row ON item_row.`item_id`=drop_row.`item_id`;
