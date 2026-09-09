ALTER TABLE `god2_game`.`immortal_templates`
    ADD COLUMN IF NOT EXISTS `profession_code` varchar(32) NULL COMMENT '對應角色職業代碼；特殊神仙為 NULL' AFTER `immortal_family`,
    ADD COLUMN IF NOT EXISTS `is_special` tinyint(1) NOT NULL DEFAULT 0 COMMENT '特殊神仙可供所有職業使用' AFTER `profession_code`,
    ADD KEY IF NOT EXISTS `ix_immortal_templates_profession` (`profession_code`,`is_special`);

CREATE TABLE IF NOT EXISTS `god2_game`.`immortal_base_stats` (
    `immortal_template_id` bigint NOT NULL COMMENT '神仙模板 ID',
    `level` int NOT NULL DEFAULT 1 COMMENT '目前只發布已驗證的等級 1 基礎能力',
    `maximum_hp` bigint NULL COMMENT '等級 1 最大 HP；未取得證據時保持 NULL',
    `maximum_mp` bigint NULL COMMENT '等級 1 最大 MP；未取得證據時保持 NULL',
    `strength` int NOT NULL COMMENT '腕力',
    `constitution` int NOT NULL COMMENT '體力',
    `intelligence` int NOT NULL COMMENT '智力',
    `speed` int NOT NULL COMMENT '速度',
    `metal` int NOT NULL DEFAULT 0 COMMENT '金屬性',
    `wood` int NOT NULL DEFAULT 0 COMMENT '木屬性',
    `water` int NOT NULL DEFAULT 0 COMMENT '水屬性',
    `fire` int NOT NULL DEFAULT 0 COMMENT '火屬性',
    `earth` int NOT NULL DEFAULT 0 COMMENT '土屬性',
    `evidence_status` varchar(30) NOT NULL COMMENT '證據狀態',
    `evidence_reference` varchar(500) NOT NULL COMMENT '證據來源',
    `enabled` tinyint(1) NOT NULL DEFAULT 1 COMMENT '是否可供正式服務端讀取',
    `admin_note` varchar(500) NULL COMMENT '管理備註',
    `created_at_utc` datetime(6) NOT NULL DEFAULT UTC_TIMESTAMP(6),
    `updated_at_utc` datetime(6) NOT NULL DEFAULT UTC_TIMESTAMP(6) ON UPDATE CURRENT_TIMESTAMP(6),
    PRIMARY KEY (`immortal_template_id`),
    CONSTRAINT `fk_immortal_base_stats_template` FOREIGN KEY (`immortal_template_id`)
        REFERENCES `god2_game`.`immortal_templates` (`immortal_template_id`),
    CONSTRAINT `ck_immortal_base_stats_level_one` CHECK (`level` = 1),
    CONSTRAINT `ck_immortal_base_stats_nonnegative` CHECK (
        (`maximum_hp` IS NULL OR `maximum_hp` >= 0) AND
        (`maximum_mp` IS NULL OR `maximum_mp` >= 0) AND
        `strength` >= 0 AND `constitution` >= 0 AND `intelligence` >= 0 AND `speed` >= 0 AND
        `metal` >= 0 AND `wood` >= 0 AND `water` >= 0 AND `fire` >= 0 AND `earth` >= 0),
    CONSTRAINT `ck_immortal_base_stats_enabled` CHECK (`enabled` IN (0,1))
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci COMMENT='神仙等級 1 基礎能力；與玩家、戰寵及未驗證成長資料分離';

INSERT INTO `god2_game`.`immortal_templates`
    (`immortal_template_id`,`code`,`name_zh_tw`,`name_original`,`profession_code`,`is_special`,
     `initial_level`,`evidence_status`,`enabled`,`admin_note`)
VALUES
    (1060001,'immortal_wuji','武吉','武吉','Swordsman',0,1,'Verified',1,'使用者遊戲內等級 1 截圖與巴哈姆特神仙介紹交叉驗證。'),
    (1060002,'immortal_tuxingsun','土行孫','土行孫','Taoist',0,1,'Recovered',1,'巴哈姆特封神 2：仙界傳神仙介紹；只發布等級 1 四基與五行。'),
    (1060003,'immortal_cihang_daoren','慈航道人','慈航道人','Pharmacist',0,1,'Recovered',1,'巴哈姆特封神 2：仙界傳神仙介紹；只發布等級 1 四基與五行。'),
    (1060004,'immortal_jiang_ziya','姜子牙','姜子牙','Warlock',0,1,'Recovered',1,'巴哈姆特封神 2：仙界傳神仙介紹；只發布等級 1 四基與五行。'),
    (1060005,'immortal_hu_ximei','胡喜媚','胡喜媚',NULL,1,1,'Recovered',1,'巴哈姆特標示適用全職業；只發布等級 1 四基與五行。')
ON DUPLICATE KEY UPDATE
    `name_zh_tw`=VALUES(`name_zh_tw`),
    `name_original`=VALUES(`name_original`),
    `profession_code`=VALUES(`profession_code`),
    `is_special`=VALUES(`is_special`),
    `initial_level`=VALUES(`initial_level`),
    `evidence_status`=VALUES(`evidence_status`),
    `enabled`=VALUES(`enabled`),
    `admin_note`=VALUES(`admin_note`);

INSERT INTO `god2_game`.`immortal_base_stats`
    (`immortal_template_id`,`level`,`maximum_hp`,`maximum_mp`,`strength`,`constitution`,`intelligence`,`speed`,
     `metal`,`wood`,`water`,`fire`,`earth`,`evidence_status`,`evidence_reference`,`enabled`,`admin_note`)
VALUES
    (1060001,1,400,156,30,20,10,5,0,10,0,0,0,'Verified',
     '使用者提供的官方客戶端等級 1 截圖；https://wiki2.gamer.com.tw/wiki.php?n=8591%3A%E6%AD%A6%E5%90%89',1,
     'HP/MP、四基與五行完整；啟用時以整數除法賦予角色五分之一。'),
    (1060002,1,NULL,NULL,1,9,25,10,0,0,0,0,50,'Recovered',
     'https://wiki2.gamer.com.tw/wiki.php?n=8591%3A%E5%9C%9F%E8%A1%8C%E5%AD%AB',1,
     '來源未列 HP/MP，因此保持 NULL。'),
    (1060003,1,NULL,NULL,10,10,30,10,0,0,0,10,10,'Recovered',
     'https://wiki2.gamer.com.tw/wiki.php?n=8591%3A%E6%85%88%E8%88%AA%E9%81%93%E4%BA%BA',1,
     '來源未列 HP/MP，因此保持 NULL。'),
    (1060004,1,NULL,NULL,2,8,25,5,0,30,30,0,0,'Recovered',
     'https://wiki2.gamer.com.tw/wiki.php?n=8591%3A%E5%A7%9C%E5%AD%90%E7%89%99',1,
     '來源未列 HP/MP，因此保持 NULL。'),
    (1060005,1,NULL,NULL,5,10,30,20,10,10,10,10,10,'Recovered',
     'https://wiki2.gamer.com.tw/wiki.php?n=8591%3A%E8%83%A1%E5%96%9C%E5%A6%B9',1,
     '特殊神仙，適用全職業；來源未列 HP/MP，因此保持 NULL。')
ON DUPLICATE KEY UPDATE
    `level`=VALUES(`level`),
    `maximum_hp`=VALUES(`maximum_hp`),
    `maximum_mp`=VALUES(`maximum_mp`),
    `strength`=VALUES(`strength`),
    `constitution`=VALUES(`constitution`),
    `intelligence`=VALUES(`intelligence`),
    `speed`=VALUES(`speed`),
    `metal`=VALUES(`metal`),
    `wood`=VALUES(`wood`),
    `water`=VALUES(`water`),
    `fire`=VALUES(`fire`),
    `earth`=VALUES(`earth`),
    `evidence_status`=VALUES(`evidence_status`),
    `evidence_reference`=VALUES(`evidence_reference`),
    `enabled`=VALUES(`enabled`),
    `admin_note`=VALUES(`admin_note`);

CREATE OR REPLACE VIEW `god2_game`.`vw_immortal_level_one_baseline` AS
SELECT template_row.`immortal_template_id` AS `神仙模板ID`,
       template_row.`code` AS `代碼`,
       template_row.`name_zh_tw` AS `名稱`,
       template_row.`profession_code` AS `對應職業`,
       template_row.`is_special` AS `特殊神仙`,
       stats_row.`level` AS `等級`,
       stats_row.`maximum_hp` AS `最大HP`,
       stats_row.`maximum_mp` AS `最大MP`,
       stats_row.`strength` AS `腕力`,
       stats_row.`constitution` AS `體力`,
       stats_row.`intelligence` AS `智力`,
       stats_row.`speed` AS `速度`,
       stats_row.`metal` AS `金`,
       stats_row.`wood` AS `木`,
       stats_row.`water` AS `水`,
       stats_row.`fire` AS `火`,
       stats_row.`earth` AS `土`,
       stats_row.`evidence_status` AS `證據狀態`,
       stats_row.`evidence_reference` AS `證據來源`
FROM `god2_game`.`immortal_templates` template_row
JOIN `god2_game`.`immortal_base_stats` stats_row
  ON stats_row.`immortal_template_id`=template_row.`immortal_template_id`
WHERE template_row.`enabled`=1 AND stats_row.`enabled`=1;
