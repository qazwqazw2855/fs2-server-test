CREATE OR REPLACE VIEW `god2_game`.`vw_monsters_readable` AS
SELECT
    `monster_id` AS `怪物編號`,
    `name_zh_tw` AS `怪物名稱`,
    `monster_family` AS `怪物類型`,
    `level` AS `等級`,
    `max_hp` AS `最大生命`,
    `max_mp` AS `最大法力`,
    `strength` AS `力量`,
    `constitution` AS `體力`,
    `intelligence` AS `智力`,
    `speed` AS `速度`,
    `metal` AS `金屬性`,
    `wood` AS `木屬性`,
    `water` AS `水屬性`,
    `fire` AS `火屬性`,
    `earth` AS `土屬性`,
    `physical_attack` AS `物理攻擊`,
    `physical_defense` AS `物理防禦`,
    `magic_attack` AS `法術攻擊`,
    `magic_defense` AS `法術防禦`,
    `experience_reward` AS `經驗獎勵`,
    `currency_reward` AS `金錢獎勵`,
    CASE `boss` WHEN 1 THEN '是' WHEN 0 THEN '否' ELSE NULL END AS `是否首領`,
    CASE `elite` WHEN 1 THEN '是' WHEN 0 THEN '否' ELSE NULL END AS `是否菁英`,
    CASE `aggressive` WHEN 1 THEN '主動' WHEN 0 THEN '被動' ELSE NULL END AS `攻擊模式`,
    CASE `evidence_status`
        WHEN 'Verified' THEN '現行封包已驗證'
        WHEN 'Recovered' THEN '客戶端資料已恢復'
        WHEN 'Derived' THEN '交叉資料推導'
        WHEN 'Candidate' THEN '候選待驗證'
        WHEN 'EvidenceBlocked' THEN '證據不足'
        ELSE '未知'
    END AS `資料狀態`,
    CASE `enabled` WHEN 1 THEN '已啟用' ELSE '未啟用' END AS `服務端狀態`
FROM `god2_game`.`monsters`;

CREATE OR REPLACE VIEW `god2_game`.`vw_monster_skills_readable` AS
SELECT
    relation_row.`monster_id` AS `怪物編號`,
    monster_row.`name_zh_tw` AS `怪物名稱`,
    relation_row.`skill_id` AS `技能編號`,
    skill_row.`name_zh_tw` AS `技能名稱`,
    relation_row.`priority` AS `使用優先順序`,
    relation_row.`trigger_type` AS `觸發條件`,
    relation_row.`trigger_hp_percent` AS `觸發生命比例`,
    relation_row.`trigger_round_min` AS `最早觸發回合`,
    relation_row.`trigger_round_max` AS `最晚觸發回合`,
    relation_row.`target_type` AS `目標類型`,
    relation_row.`use_probability` AS `使用機率`,
    relation_row.`cooldown_rounds` AS `冷卻回合`,
    relation_row.`maximum_uses` AS `最多使用次數`,
    CASE relation_row.`enabled` WHEN 1 THEN '已啟用' ELSE '未啟用' END AS `服務端狀態`
FROM `god2_game`.`monster_skills` relation_row
JOIN `god2_game`.`monsters` monster_row ON monster_row.`monster_id`=relation_row.`monster_id`
JOIN `god2_game`.`skills` skill_row ON skill_row.`skill_id`=relation_row.`skill_id`;

CREATE OR REPLACE VIEW `god2_game`.`vw_monster_spawns_readable` AS
SELECT
    spawn_row.`monster_id` AS `怪物編號`,
    monster_row.`name_zh_tw` AS `怪物名稱`,
    spawn_row.`map_id` AS `地圖編號`,
    map_row.`name_zh_tw` AS `地圖名稱`,
    spawn_row.`position_x` AS `座標X`,
    spawn_row.`position_y` AS `座標Y`,
    spawn_row.`spawn_count` AS `出現數量`,
    spawn_row.`spawn_radius` AS `出現範圍`,
    spawn_row.`respawn_seconds_min` AS `最短重生秒數`,
    spawn_row.`respawn_seconds_max` AS `最長重生秒數`,
    CASE spawn_row.`enabled` WHEN 1 THEN '已啟用' ELSE '未啟用' END AS `服務端狀態`
FROM `god2_game`.`monster_spawns` spawn_row
JOIN `god2_game`.`monsters` monster_row ON monster_row.`monster_id`=spawn_row.`monster_id`
JOIN `god2_game`.`maps` map_row ON map_row.`map_id`=spawn_row.`map_id`;

CREATE OR REPLACE VIEW `god2_game`.`vw_monster_drops_readable` AS
SELECT
    drop_row.`monster_id` AS `怪物編號`,
    monster_row.`name_zh_tw` AS `怪物名稱`,
    drop_row.`item_id` AS `物品編號`,
    item_row.`name_zh_tw` AS `物品名稱`,
    drop_row.`minimum_quantity` AS `最少數量`,
    drop_row.`maximum_quantity` AS `最多數量`,
    drop_row.`drop_rate` AS `掉落機率`,
    drop_row.`drop_rate_unit` AS `機率單位`,
    CASE drop_row.`is_guaranteed` WHEN 1 THEN '是' WHEN 0 THEN '否' ELSE NULL END AS `是否必定掉落`,
    CASE drop_row.`enabled` WHEN 1 THEN '已啟用' ELSE '未啟用' END AS `服務端狀態`
FROM `god2_game`.`monster_drops` drop_row
JOIN `god2_game`.`monsters` monster_row ON monster_row.`monster_id`=drop_row.`monster_id`
JOIN `god2_game`.`items` item_row ON item_row.`item_id`=drop_row.`item_id`;
