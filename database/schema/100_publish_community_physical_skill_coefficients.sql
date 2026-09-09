CREATE TABLE IF NOT EXISTS `god2_game`.`physical_skill_damage_coefficients` (
    `skill_family` varchar(30) NOT NULL COMMENT '物理技能系別',
    `skill_tier` int NOT NULL COMMENT '技能階級，一至八階',
    `minimum_multiplier` decimal(8,4) NOT NULL COMMENT '社群實測最低傷害倍率',
    `midpoint_multiplier` decimal(8,4) NOT NULL COMMENT '服務端近似運算建議倍率',
    `maximum_multiplier` decimal(8,4) NOT NULL COMMENT '社群實測最高傷害倍率',
    `damage_formula_zh_tw` varchar(200) NOT NULL COMMENT '繁體中文傷害公式',
    `evidence_status` varchar(40) NOT NULL COMMENT '證據狀態',
    `independent_source_count` int NOT NULL COMMENT '相互印證來源數',
    `primary_source_url` varchar(500) NOT NULL COMMENT '主要資料來源',
    `corroborating_source_url` varchar(500) NULL COMMENT '交叉印證資料來源',
    `enabled` tinyint(1) NOT NULL DEFAULT 1 COMMENT '是否可供服務端解析',
    `admin_note` varchar(500) NULL COMMENT '管理備註',
    `created_at_utc` datetime(6) NOT NULL DEFAULT UTC_TIMESTAMP(6),
    `updated_at_utc` datetime(6) NOT NULL DEFAULT UTC_TIMESTAMP(6) ON UPDATE CURRENT_TIMESTAMP(6),
    PRIMARY KEY (`skill_family`,`skill_tier`),
    CONSTRAINT `ck_physical_skill_coefficients_family` CHECK (`skill_family` IN ('Blade','Sword','Staff','Whip','Spear','ThrowingKnife')),
    CONSTRAINT `ck_physical_skill_coefficients_tier` CHECK (`skill_tier` BETWEEN 1 AND 8),
    CONSTRAINT `ck_physical_skill_coefficients_range` CHECK (`minimum_multiplier` > 0 AND `minimum_multiplier` <= `midpoint_multiplier` AND `midpoint_multiplier` <= `maximum_multiplier`),
    CONSTRAINT `ck_physical_skill_coefficients_sources` CHECK (`independent_source_count` IN (1,2) AND (`independent_source_count`=1 OR `corroborating_source_url` IS NOT NULL)),
    CONSTRAINT `ck_physical_skill_coefficients_enabled` CHECK (`enabled` IN (0,1))
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci COMMENT='玩家實測物理技能傷害倍率區間；不是官方服務端常數';

INSERT INTO `god2_game`.`physical_skill_damage_coefficients`
    (`skill_family`,`skill_tier`,`minimum_multiplier`,`midpoint_multiplier`,`maximum_multiplier`,
     `damage_formula_zh_tw`,`evidence_status`,`independent_source_count`,`primary_source_url`,
     `corroborating_source_url`,`enabled`,`admin_note`)
VALUES
    ('Blade',1,1.5,1.8,2.1,'（攻擊方物理攻擊－受擊方物理防禦）×技能倍率','CommunityCorroborated',2,'https://forum.gamer.com.tw/G2.php?bsn=8395&parent=40&sn=2481','https://forum.gamer.com.tw/G2.php?bsn=8395&parent=40&sn=1794',1,'兩篇社群實測一致；不宣稱為官方服務端常數。'),
    ('Blade',2,2.2,2.5,2.8,'（攻擊方物理攻擊－受擊方物理防禦）×技能倍率','CommunityCorroborated',2,'https://forum.gamer.com.tw/G2.php?bsn=8395&parent=40&sn=2481','https://forum.gamer.com.tw/G2.php?bsn=8395&parent=40&sn=1794',1,'兩篇社群實測一致；不宣稱為官方服務端常數。'),
    ('Blade',3,2.9,3.2,3.5,'（攻擊方物理攻擊－受擊方物理防禦）×技能倍率','CommunityCorroborated',2,'https://forum.gamer.com.tw/G2.php?bsn=8395&parent=40&sn=2481','https://forum.gamer.com.tw/G2.php?bsn=8395&parent=40&sn=1794',1,'兩篇社群實測一致；不宣稱為官方服務端常數。'),
    ('Blade',4,3.6,3.9,4.2,'（攻擊方物理攻擊－受擊方物理防禦）×技能倍率','CommunityCorroborated',2,'https://forum.gamer.com.tw/G2.php?bsn=8395&parent=40&sn=2481','https://forum.gamer.com.tw/G2.php?bsn=8395&parent=40&sn=1794',1,'兩篇社群實測一致；不宣稱為官方服務端常數。'),
    ('Blade',5,4.3,4.6,4.9,'（攻擊方物理攻擊－受擊方物理防禦）×技能倍率','CommunityCorroborated',2,'https://forum.gamer.com.tw/G2.php?bsn=8395&parent=40&sn=2481','https://forum.gamer.com.tw/G2.php?bsn=8395&parent=40&sn=1794',1,'兩篇社群實測一致；不宣稱為官方服務端常數。'),
    ('Blade',6,5.0,5.3,5.6,'（攻擊方物理攻擊－受擊方物理防禦）×技能倍率','CommunityMeasured',1,'https://forum.gamer.com.tw/G2.php?bsn=8395&parent=40&sn=2481',NULL,1,'單一社群實測來源；不宣稱為官方服務端常數。'),
    ('Blade',7,5.7,6.0,6.3,'（攻擊方物理攻擊－受擊方物理防禦）×技能倍率','CommunityMeasured',1,'https://forum.gamer.com.tw/G2.php?bsn=8395&parent=40&sn=2481',NULL,1,'單一社群實測來源；不宣稱為官方服務端常數。'),
    ('Blade',8,6.4,6.7,7.0,'（攻擊方物理攻擊－受擊方物理防禦）×技能倍率','CommunityMeasured',1,'https://forum.gamer.com.tw/G2.php?bsn=8395&parent=40&sn=2481',NULL,1,'單一社群實測來源；不宣稱為官方服務端常數。'),
    ('Sword',1,1.3,1.5,1.7,'（攻擊方物理攻擊－受擊方物理防禦）×技能倍率','CommunityCorroborated',2,'https://forum.gamer.com.tw/G2.php?bsn=8395&parent=40&sn=2481','https://forum.gamer.com.tw/G2.php?bsn=8395&parent=40&sn=1794',1,'兩篇社群實測一致；不宣稱為官方服務端常數。'),
    ('Sword',2,1.7,1.9,2.1,'（攻擊方物理攻擊－受擊方物理防禦）×技能倍率','CommunityCorroborated',2,'https://forum.gamer.com.tw/G2.php?bsn=8395&parent=40&sn=2481','https://forum.gamer.com.tw/G2.php?bsn=8395&parent=40&sn=1794',1,'兩篇社群實測一致；不宣稱為官方服務端常數。'),
    ('Sword',3,2.1,2.3,2.5,'（攻擊方物理攻擊－受擊方物理防禦）×技能倍率','CommunityCorroborated',2,'https://forum.gamer.com.tw/G2.php?bsn=8395&parent=40&sn=2481','https://forum.gamer.com.tw/G2.php?bsn=8395&parent=40&sn=1794',1,'兩篇社群實測一致；不宣稱為官方服務端常數。'),
    ('Sword',4,2.5,2.7,2.9,'（攻擊方物理攻擊－受擊方物理防禦）×技能倍率','CommunityCorroborated',2,'https://forum.gamer.com.tw/G2.php?bsn=8395&parent=40&sn=2481','https://forum.gamer.com.tw/G2.php?bsn=8395&parent=40&sn=1794',1,'兩篇社群實測一致；不宣稱為官方服務端常數。'),
    ('Sword',5,2.9,3.1,3.3,'（攻擊方物理攻擊－受擊方物理防禦）×技能倍率','CommunityCorroborated',2,'https://forum.gamer.com.tw/G2.php?bsn=8395&parent=40&sn=2481','https://forum.gamer.com.tw/G2.php?bsn=8395&parent=40&sn=1794',1,'兩篇社群實測一致；不宣稱為官方服務端常數。'),
    ('Sword',6,3.3,3.5,3.7,'（攻擊方物理攻擊－受擊方物理防禦）×技能倍率','CommunityMeasured',1,'https://forum.gamer.com.tw/G2.php?bsn=8395&parent=40&sn=2481',NULL,1,'單一社群實測來源；不宣稱為官方服務端常數。'),
    ('Sword',7,3.7,3.9,4.1,'（攻擊方物理攻擊－受擊方物理防禦）×技能倍率','CommunityMeasured',1,'https://forum.gamer.com.tw/G2.php?bsn=8395&parent=40&sn=2481',NULL,1,'單一社群實測來源；不宣稱為官方服務端常數。'),
    ('Sword',8,4.1,4.3,4.5,'（攻擊方物理攻擊－受擊方物理防禦）×技能倍率','CommunityMeasured',1,'https://forum.gamer.com.tw/G2.php?bsn=8395&parent=40&sn=2481',NULL,1,'單一社群實測來源；不宣稱為官方服務端常數。'),
    ('Staff',1,1.3,1.4,1.5,'（攻擊方物理攻擊－受擊方物理防禦）×技能倍率','CommunityCorroborated',2,'https://forum.gamer.com.tw/G2.php?bsn=8395&parent=40&sn=2481','https://forum.gamer.com.tw/G2.php?bsn=8395&parent=40&sn=1794',1,'兩篇社群實測一致；不宣稱為官方服務端常數。'),
    ('Staff',2,1.6,1.7,1.8,'（攻擊方物理攻擊－受擊方物理防禦）×技能倍率','CommunityCorroborated',2,'https://forum.gamer.com.tw/G2.php?bsn=8395&parent=40&sn=2481','https://forum.gamer.com.tw/G2.php?bsn=8395&parent=40&sn=1794',1,'兩篇社群實測一致；不宣稱為官方服務端常數。'),
    ('Staff',3,1.9,2.0,2.1,'（攻擊方物理攻擊－受擊方物理防禦）×技能倍率','CommunityCorroborated',2,'https://forum.gamer.com.tw/G2.php?bsn=8395&parent=40&sn=2481','https://forum.gamer.com.tw/G2.php?bsn=8395&parent=40&sn=1794',1,'兩篇社群實測一致；不宣稱為官方服務端常數。'),
    ('Staff',4,2.2,2.3,2.4,'（攻擊方物理攻擊－受擊方物理防禦）×技能倍率','CommunityCorroborated',2,'https://forum.gamer.com.tw/G2.php?bsn=8395&parent=40&sn=2481','https://forum.gamer.com.tw/G2.php?bsn=8395&parent=40&sn=1794',1,'兩篇社群實測一致；不宣稱為官方服務端常數。'),
    ('Staff',5,2.5,2.6,2.7,'（攻擊方物理攻擊－受擊方物理防禦）×技能倍率','CommunityCorroborated',2,'https://forum.gamer.com.tw/G2.php?bsn=8395&parent=40&sn=2481','https://forum.gamer.com.tw/G2.php?bsn=8395&parent=40&sn=1794',1,'兩篇社群實測一致；不宣稱為官方服務端常數。'),
    ('Staff',6,2.8,2.9,3.0,'（攻擊方物理攻擊－受擊方物理防禦）×技能倍率','CommunityMeasured',1,'https://forum.gamer.com.tw/G2.php?bsn=8395&parent=40&sn=2481',NULL,1,'單一社群實測來源；不宣稱為官方服務端常數。'),
    ('Staff',7,3.1,3.2,3.3,'（攻擊方物理攻擊－受擊方物理防禦）×技能倍率','CommunityMeasured',1,'https://forum.gamer.com.tw/G2.php?bsn=8395&parent=40&sn=2481',NULL,1,'單一社群實測來源；不宣稱為官方服務端常數。'),
    ('Staff',8,3.4,3.5,3.6,'（攻擊方物理攻擊－受擊方物理防禦）×技能倍率','CommunityMeasured',1,'https://forum.gamer.com.tw/G2.php?bsn=8395&parent=40&sn=2481',NULL,1,'單一社群實測來源；不宣稱為官方服務端常數。'),
    ('Whip',1,1.2,1.3,1.4,'（攻擊方物理攻擊－受擊方物理防禦）×技能倍率','CommunityCorroborated',2,'https://forum.gamer.com.tw/G2.php?bsn=8395&parent=40&sn=2481','https://forum.gamer.com.tw/G2.php?bsn=8395&parent=40&sn=1794',1,'兩篇社群實測一致；不宣稱為官方服務端常數。'),
    ('Whip',2,1.4,1.5,1.6,'（攻擊方物理攻擊－受擊方物理防禦）×技能倍率','CommunityCorroborated',2,'https://forum.gamer.com.tw/G2.php?bsn=8395&parent=40&sn=2481','https://forum.gamer.com.tw/G2.php?bsn=8395&parent=40&sn=1794',1,'兩篇社群實測一致；不宣稱為官方服務端常數。'),
    ('Whip',3,1.6,1.7,1.8,'（攻擊方物理攻擊－受擊方物理防禦）×技能倍率','CommunityCorroborated',2,'https://forum.gamer.com.tw/G2.php?bsn=8395&parent=40&sn=2481','https://forum.gamer.com.tw/G2.php?bsn=8395&parent=40&sn=1794',1,'兩篇社群實測一致；不宣稱為官方服務端常數。'),
    ('Whip',4,1.8,1.9,2.0,'（攻擊方物理攻擊－受擊方物理防禦）×技能倍率','CommunityCorroborated',2,'https://forum.gamer.com.tw/G2.php?bsn=8395&parent=40&sn=2481','https://forum.gamer.com.tw/G2.php?bsn=8395&parent=40&sn=1794',1,'兩篇社群實測一致；不宣稱為官方服務端常數。'),
    ('Whip',5,2.0,2.1,2.2,'（攻擊方物理攻擊－受擊方物理防禦）×技能倍率','CommunityCorroborated',2,'https://forum.gamer.com.tw/G2.php?bsn=8395&parent=40&sn=2481','https://forum.gamer.com.tw/G2.php?bsn=8395&parent=40&sn=1794',1,'兩篇社群實測一致；不宣稱為官方服務端常數。'),
    ('Whip',6,2.2,2.3,2.4,'（攻擊方物理攻擊－受擊方物理防禦）×技能倍率','CommunityMeasured',1,'https://forum.gamer.com.tw/G2.php?bsn=8395&parent=40&sn=2481',NULL,1,'單一社群實測來源；不宣稱為官方服務端常數。'),
    ('Whip',7,2.4,2.5,2.6,'（攻擊方物理攻擊－受擊方物理防禦）×技能倍率','CommunityMeasured',1,'https://forum.gamer.com.tw/G2.php?bsn=8395&parent=40&sn=2481',NULL,1,'單一社群實測來源；不宣稱為官方服務端常數。'),
    ('Whip',8,2.6,2.7,2.8,'（攻擊方物理攻擊－受擊方物理防禦）×技能倍率','CommunityMeasured',1,'https://forum.gamer.com.tw/G2.php?bsn=8395&parent=40&sn=2481',NULL,1,'單一社群實測來源；不宣稱為官方服務端常數。'),
    ('Spear',1,1.2,1.3,1.4,'（攻擊方物理攻擊－受擊方物理防禦）×技能倍率','CommunityCorroborated',2,'https://forum.gamer.com.tw/G2.php?bsn=8395&parent=40&sn=2481','https://forum.gamer.com.tw/G2.php?bsn=8395&parent=40&sn=1794',1,'兩篇社群實測一致；不宣稱為官方服務端常數。'),
    ('Spear',2,1.4,1.5,1.6,'（攻擊方物理攻擊－受擊方物理防禦）×技能倍率','CommunityCorroborated',2,'https://forum.gamer.com.tw/G2.php?bsn=8395&parent=40&sn=2481','https://forum.gamer.com.tw/G2.php?bsn=8395&parent=40&sn=1794',1,'兩篇社群實測一致；不宣稱為官方服務端常數。'),
    ('Spear',3,1.6,1.7,1.8,'（攻擊方物理攻擊－受擊方物理防禦）×技能倍率','CommunityCorroborated',2,'https://forum.gamer.com.tw/G2.php?bsn=8395&parent=40&sn=2481','https://forum.gamer.com.tw/G2.php?bsn=8395&parent=40&sn=1794',1,'兩篇社群實測一致；不宣稱為官方服務端常數。'),
    ('Spear',4,1.8,1.9,2.0,'（攻擊方物理攻擊－受擊方物理防禦）×技能倍率','CommunityCorroborated',2,'https://forum.gamer.com.tw/G2.php?bsn=8395&parent=40&sn=2481','https://forum.gamer.com.tw/G2.php?bsn=8395&parent=40&sn=1794',1,'兩篇社群實測一致；不宣稱為官方服務端常數。'),
    ('Spear',5,2.0,2.1,2.2,'（攻擊方物理攻擊－受擊方物理防禦）×技能倍率','CommunityCorroborated',2,'https://forum.gamer.com.tw/G2.php?bsn=8395&parent=40&sn=2481','https://forum.gamer.com.tw/G2.php?bsn=8395&parent=40&sn=1794',1,'兩篇社群實測一致；不宣稱為官方服務端常數。'),
    ('Spear',6,2.2,2.3,2.4,'（攻擊方物理攻擊－受擊方物理防禦）×技能倍率','CommunityMeasured',1,'https://forum.gamer.com.tw/G2.php?bsn=8395&parent=40&sn=2481',NULL,1,'單一社群實測來源；不宣稱為官方服務端常數。'),
    ('Spear',7,2.4,2.5,2.6,'（攻擊方物理攻擊－受擊方物理防禦）×技能倍率','CommunityMeasured',1,'https://forum.gamer.com.tw/G2.php?bsn=8395&parent=40&sn=2481',NULL,1,'單一社群實測來源；不宣稱為官方服務端常數。'),
    ('Spear',8,2.6,2.7,2.8,'（攻擊方物理攻擊－受擊方物理防禦）×技能倍率','CommunityMeasured',1,'https://forum.gamer.com.tw/G2.php?bsn=8395&parent=40&sn=2481',NULL,1,'單一社群實測來源；不宣稱為官方服務端常數。'),
    ('ThrowingKnife',1,1.1,1.2,1.3,'（攻擊方物理攻擊－受擊方物理防禦）×技能倍率','CommunityCorroborated',2,'https://forum.gamer.com.tw/G2.php?bsn=8395&parent=40&sn=2481','https://forum.gamer.com.tw/G2.php?bsn=8395&parent=40&sn=1794',1,'兩篇社群實測一致；不宣稱為官方服務端常數。'),
    ('ThrowingKnife',2,1.2,1.3,1.4,'（攻擊方物理攻擊－受擊方物理防禦）×技能倍率','CommunityCorroborated',2,'https://forum.gamer.com.tw/G2.php?bsn=8395&parent=40&sn=2481','https://forum.gamer.com.tw/G2.php?bsn=8395&parent=40&sn=1794',1,'兩篇社群實測一致；不宣稱為官方服務端常數。'),
    ('ThrowingKnife',3,1.3,1.4,1.5,'（攻擊方物理攻擊－受擊方物理防禦）×技能倍率','CommunityCorroborated',2,'https://forum.gamer.com.tw/G2.php?bsn=8395&parent=40&sn=2481','https://forum.gamer.com.tw/G2.php?bsn=8395&parent=40&sn=1794',1,'兩篇社群實測一致；不宣稱為官方服務端常數。'),
    ('ThrowingKnife',4,1.4,1.5,1.6,'（攻擊方物理攻擊－受擊方物理防禦）×技能倍率','CommunityCorroborated',2,'https://forum.gamer.com.tw/G2.php?bsn=8395&parent=40&sn=2481','https://forum.gamer.com.tw/G2.php?bsn=8395&parent=40&sn=1794',1,'兩篇社群實測一致；不宣稱為官方服務端常數。'),
    ('ThrowingKnife',5,1.5,1.6,1.7,'（攻擊方物理攻擊－受擊方物理防禦）×技能倍率','CommunityCorroborated',2,'https://forum.gamer.com.tw/G2.php?bsn=8395&parent=40&sn=2481','https://forum.gamer.com.tw/G2.php?bsn=8395&parent=40&sn=1794',1,'兩篇社群實測一致；不宣稱為官方服務端常數。'),
    ('ThrowingKnife',6,1.6,1.7,1.8,'（攻擊方物理攻擊－受擊方物理防禦）×技能倍率','CommunityMeasured',1,'https://forum.gamer.com.tw/G2.php?bsn=8395&parent=40&sn=2481',NULL,1,'單一社群實測來源；不宣稱為官方服務端常數。'),
    ('ThrowingKnife',7,1.7,1.8,1.9,'（攻擊方物理攻擊－受擊方物理防禦）×技能倍率','CommunityMeasured',1,'https://forum.gamer.com.tw/G2.php?bsn=8395&parent=40&sn=2481',NULL,1,'單一社群實測來源；不宣稱為官方服務端常數。'),
    ('ThrowingKnife',8,1.8,1.9,2.0,'（攻擊方物理攻擊－受擊方物理防禦）×技能倍率','CommunityMeasured',1,'https://forum.gamer.com.tw/G2.php?bsn=8395&parent=40&sn=2481',NULL,1,'單一社群實測來源；不宣稱為官方服務端常數。')
ON DUPLICATE KEY UPDATE
    `minimum_multiplier`=VALUES(`minimum_multiplier`),
    `midpoint_multiplier`=VALUES(`midpoint_multiplier`),
    `maximum_multiplier`=VALUES(`maximum_multiplier`),
    `damage_formula_zh_tw`=VALUES(`damage_formula_zh_tw`),
    `evidence_status`=VALUES(`evidence_status`),
    `independent_source_count`=VALUES(`independent_source_count`),
    `primary_source_url`=VALUES(`primary_source_url`),
    `corroborating_source_url`=VALUES(`corroborating_source_url`),
    `enabled`=VALUES(`enabled`),
    `admin_note`=VALUES(`admin_note`);

CREATE OR REPLACE VIEW `god2_game`.`vw_physical_skill_damage_coefficients_readable` AS
SELECT CASE coefficient.`skill_family`
           WHEN 'Blade' THEN '刀技'
           WHEN 'Sword' THEN '劍技'
           WHEN 'Staff' THEN '杖技'
           WHEN 'Whip' THEN '鞭技'
           WHEN 'Spear' THEN '槍技'
           WHEN 'ThrowingKnife' THEN '飛刀技'
       END AS `技能系別`,
       coefficient.`skill_tier` AS `技能階級`,
       coefficient.`minimum_multiplier` AS `最低倍率`,
       coefficient.`midpoint_multiplier` AS `建議倍率`,
       coefficient.`maximum_multiplier` AS `最高倍率`,
       coefficient.`damage_formula_zh_tw` AS `傷害公式`,
       CASE coefficient.`evidence_status`
           WHEN 'CommunityCorroborated' THEN '雙來源社群實測'
           WHEN 'CommunityMeasured' THEN '單一來源社群實測'
           ELSE coefficient.`evidence_status`
       END AS `證據狀態`,
       coefficient.`independent_source_count` AS `來源數`,
       coefficient.`primary_source_url` AS `主要來源`,
       coefficient.`corroborating_source_url` AS `交叉來源`,
       coefficient.`enabled` AS `服務端可解析`
FROM `god2_game`.`physical_skill_damage_coefficients` coefficient
ORDER BY FIELD(coefficient.`skill_family`,'Blade','Sword','Staff','Whip','Spear','ThrowingKnife'),
         coefficient.`skill_tier`;
