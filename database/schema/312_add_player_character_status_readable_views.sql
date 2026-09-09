DROP VIEW IF EXISTS `god2_player`.`vw_character_status_readable`;
DROP VIEW IF EXISTS `god2_player`.`vw_login_character_selection_readable`;
DROP VIEW IF EXISTS `god2_player`.`vw_blackbox_character_state_observations_readable`;

CREATE VIEW `god2_player`.`vw_character_status_readable` AS
SELECT
    c.`account_id` AS `帳號ID`,
    c.`character_id` AS `角色ID`,
    c.`name` AS `角色名稱`,
    c.`class_id` AS `職業ID`,
    COALESCE(class_info.`name_zh_tw`, c.`class_name_cache`, c.`class_code`, '') AS `職業名稱`,
    c.`gender_code` AS `性別代碼`,
    c.`life_skill_code` AS `先天技能代碼`,
    c.`level` AS `等級`,
    c.`experience` AS `經驗`,
    c.`reputation` AS `聲望`,
    c.`rebirth_count` AS `轉生次數`,
    c.`remaining_stat_points` AS `未分配屬性點`,
    c.`current_hp` AS `目前HP`,
    c.`max_hp` AS `最大HP`,
    c.`current_mp` AS `目前MP`,
    c.`max_mp` AS `最大MP`,
    c.`strength_base` + c.`strength_bonus` AS `腕力總值`,
    c.`constitution_base` + c.`constitution_bonus` AS `體力總值`,
    c.`intelligence_base` + c.`intelligence_bonus` AS `智力總值`,
    c.`speed_base` + c.`speed_bonus` AS `速度總值`,
    c.`metal_base` + c.`metal_bonus` AS `金總值`,
    c.`wood_base` + c.`wood_bonus` AS `木總值`,
    c.`water_base` + c.`water_bonus` AS `水總值`,
    c.`fire_base` + c.`fire_bonus` AS `火總值`,
    c.`earth_base` + c.`earth_bonus` AS `土總值`,
    c.`physical_attack_base` + c.`physical_attack_bonus` AS `物攻總值`,
    c.`physical_defense_base` + c.`physical_defense_bonus` AS `物防總值`,
    c.`magic_attack_base` + c.`magic_attack_bonus` AS `魔攻總值`,
    c.`magic_defense_base` + c.`magic_defense_bonus` AS `魔防總值`,
    c.`map_id` AS `地圖ID`,
    COALESCE(map_info.`name_zh_tw`, '') AS `地圖名稱`,
    c.`position_x` AS `座標X`,
    c.`position_y` AS `座標Y`,
    c.`direction` AS `方向`,
    c.`runtime_version` AS `角色版本`,
    c.`status` AS `角色狀態`,
    CASE
        WHEN c.`enabled` = 1 AND c.`deleted_at_utc` IS NULL THEN '可登入'
        WHEN c.`deleted_at_utc` IS NOT NULL THEN '已刪除'
        ELSE '停用'
    END AS `服務端狀態`,
    c.`last_played_at_utc` AS `最後遊玩時間UTC`,
    c.`updated_at_utc` AS `更新時間UTC`
FROM `god2_player`.`characters` c
LEFT JOIN `god2_game`.`character_classes` class_info
    ON class_info.`class_id` = c.`class_id`
LEFT JOIN `god2_game`.`maps` map_info
    ON map_info.`map_id` = c.`map_id`;

CREATE VIEW `god2_player`.`vw_login_character_selection_readable` AS
SELECT
    account_info.`account_id` AS `帳號ID`,
    account_info.`username` AS `帳號`,
    account_info.`status` AS `帳號狀態`,
    account_info.`last_login_at_utc` AS `最後登入時間UTC`,
    character_info.`character_id` AS `角色ID`,
    character_info.`name` AS `角色名稱`,
    COALESCE(class_info.`name_zh_tw`, character_info.`class_name_cache`, character_info.`class_code`, '') AS `職業名稱`,
    character_info.`level` AS `等級`,
    character_info.`map_id` AS `地圖ID`,
    COALESCE(map_info.`name_zh_tw`, '') AS `地圖名稱`,
    character_info.`position_x` AS `座標X`,
    character_info.`position_y` AS `座標Y`,
    CASE
        WHEN account_info.`status` <> 'Active' THEN '帳號不可登入'
        WHEN character_info.`character_id` IS NULL THEN '帳號尚無角色'
        WHEN character_info.`enabled` = 1 AND character_info.`deleted_at_utc` IS NULL THEN '選角可顯示'
        ELSE '角色不可顯示'
    END AS `選角狀態`,
    '測登入後角色列表、職業名稱、等級、所在地圖與座標是否和客戶端一致。' AS `功能對照`
FROM `god2_player`.`accounts` account_info
LEFT JOIN `god2_player`.`characters` character_info
    ON character_info.`account_id` = account_info.`account_id`
LEFT JOIN `god2_game`.`character_classes` class_info
    ON class_info.`class_id` = character_info.`class_id`
LEFT JOIN `god2_game`.`maps` map_info
    ON map_info.`map_id` = character_info.`map_id`;

CREATE VIEW `god2_player`.`vw_blackbox_character_state_observations_readable` AS
SELECT
    status_view.`帳號ID`,
    status_view.`角色ID`,
    status_view.`角色名稱`,
    status_view.`職業名稱`,
    status_view.`等級`,
    status_view.`目前HP`,
    status_view.`最大HP`,
    status_view.`目前MP`,
    status_view.`最大MP`,
    status_view.`物攻總值`,
    status_view.`物防總值`,
    status_view.`魔攻總值`,
    status_view.`魔防總值`,
    status_view.`地圖ID`,
    status_view.`地圖名稱`,
    status_view.`座標X`,
    status_view.`座標Y`,
    status_view.`服務端狀態`,
    CASE
        WHEN status_view.`服務端狀態` = '可登入'
            THEN '第一優先：登入選角、進入地圖、面板HP/MP與攻防總值'
        ELSE '擱置：角色目前不可登入'
    END AS `黑箱測試優先級`,
    '測登入、選角、進地圖、角色面板、升級加點、穿裝後能力重算。' AS `測試重點`
FROM `god2_player`.`vw_character_status_readable` status_view;
