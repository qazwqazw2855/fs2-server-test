-- 將四職業已由官方角色建立畫面逐一確認的 Lv1 基準物化到正式資料表。
-- 五行與升級需求經驗尚無證據，因此明確保留 NULL；不得以推測值補齊。

INSERT INTO `god2_game`.`class_level_stats`
    (`class_id`,`level`,`base_max_hp`,`base_max_mp`,
     `base_strength`,`base_constitution`,`base_intelligence`,`base_speed`,
     `base_metal`,`base_wood`,`base_water`,`base_fire`,`base_earth`,
     `available_stat_points`,`required_experience`,`enabled`,`admin_note`)
VALUES
    (1,1,161,45,32,28,20,20,NULL,NULL,NULL,NULL,NULL,0,NULL,1,'官方角色建立驗證：劍客 Lv1 基準；五行與升級需求經驗維持證據閘門'),
    (2,1,142,54,20,24,32,24,NULL,NULL,NULL,NULL,NULL,0,NULL,1,'官方角色建立驗證：仙道 Lv1 基準；五行與升級需求經驗維持證據閘門'),
    (3,1,172,47,24,32,24,20,NULL,NULL,NULL,NULL,NULL,0,NULL,1,'官方角色建立驗證：藥師 Lv1 基準；五行與升級需求經驗維持證據閘門'),
    (4,1,150,50,25,25,25,25,NULL,NULL,NULL,NULL,NULL,0,NULL,1,'官方角色建立驗證：謀士 Lv1 基準；五行與升級需求經驗維持證據閘門')
ON DUPLICATE KEY UPDATE
    `base_max_hp`=VALUES(`base_max_hp`),
    `base_max_mp`=VALUES(`base_max_mp`),
    `base_strength`=VALUES(`base_strength`),
    `base_constitution`=VALUES(`base_constitution`),
    `base_intelligence`=VALUES(`base_intelligence`),
    `base_speed`=VALUES(`base_speed`),
    `base_metal`=VALUES(`base_metal`),
    `base_wood`=VALUES(`base_wood`),
    `base_water`=VALUES(`base_water`),
    `base_fire`=VALUES(`base_fire`),
    `base_earth`=VALUES(`base_earth`),
    `available_stat_points`=VALUES(`available_stat_points`),
    `required_experience`=VALUES(`required_experience`),
    `enabled`=VALUES(`enabled`),
    `admin_note`=VALUES(`admin_note`);

CREATE OR REPLACE VIEW `god2_game`.`vw_official_level1_class_profiles` AS
SELECT class_row.`class_id` AS `職業ID`,
       class_row.`name_zh_tw` AS `職業`,
       stats.`level` AS `等級`,
       stats.`base_max_hp` AS `最大HP`,
       stats.`base_max_mp` AS `最大MP`,
       stats.`base_constitution` AS `體力`,
       stats.`base_strength` AS `腕力`,
       stats.`base_intelligence` AS `智力`,
       stats.`base_speed` AS `速度`,
       stats.`available_stat_points` AS `可用配點`,
       stats.`admin_note` AS `證據註記`
FROM `god2_game`.`class_level_stats` stats
JOIN `god2_game`.`character_classes` class_row ON class_row.`class_id`=stats.`class_id`
WHERE stats.`level`=1 AND stats.`enabled`=1 AND stats.`class_id` IN (1,2,3,4);

GRANT SELECT ON `god2_game`.`vw_official_level1_class_profiles` TO `god2_runtime_role`;
