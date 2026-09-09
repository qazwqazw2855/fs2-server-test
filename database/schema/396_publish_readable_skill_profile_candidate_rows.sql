CREATE DATABASE IF NOT EXISTS `god2_research`
  DEFAULT CHARACTER SET utf8mb4
  COLLATE utf8mb4_unicode_ci;

CREATE TABLE IF NOT EXISTS `god2_research`.`skill_content_profile_candidate_rows` (
    `candidate_row_id` bigint NOT NULL AUTO_INCREMENT COMMENT '可讀候選流水號',
    `skill_id` bigint NULL COMMENT '正式技能 ID',
    `client_skill_id` int NULL COMMENT '客戶端技能 ID',
    `formal_skill_name_zh_tw` varchar(200) NULL COMMENT '正式技能繁體中文名稱',
    `profile_name_zh_tw` varchar(256) NULL COMMENT 'profile 技能繁體中文名稱',
    `skill_family_zh_tw` varchar(80) NOT NULL COMMENT '技能家族',
    `target_policy_zh_tw` varchar(80) NOT NULL COMMENT '目標政策',
    `mp_cost_policy_zh_tw` varchar(80) NOT NULL COMMENT 'MP 消耗政策',
    `mp_cost` int NULL COMMENT 'MP 消耗',
    `has_effect_reference` tinyint(1) NOT NULL DEFAULT 0 COMMENT '來源是否有技能效果參照；不儲存原始 JSON',
    `has_status_reference` tinyint(1) NOT NULL DEFAULT 0 COMMENT '來源是否有狀態效果參照；不儲存原始 JSON',
    `animation_key` varchar(256) NULL COMMENT '動畫 key',
    `presentation_key` varchar(256) NULL COMMENT '呈現 key',
    `source_enabled` tinyint(1) NOT NULL DEFAULT 0 COMMENT '來源 profile 是否啟用',
    `sync_status_zh_tw` varchar(64) NOT NULL COMMENT '同步判定',
    `sync_policy_zh_tw` varchar(256) NOT NULL COMMENT '同步政策與原因',
    `updated_at_utc` datetime(6) NOT NULL DEFAULT UTC_TIMESTAMP(6) ON UPDATE CURRENT_TIMESTAMP(6) COMMENT '更新 UTC 時間',
    PRIMARY KEY (`candidate_row_id`),
    KEY `ix_skill_content_profile_candidate_rows_skill` (`skill_id`),
    KEY `ix_skill_content_profile_candidate_rows_client_skill` (`client_skill_id`),
    KEY `ix_skill_content_profile_candidate_rows_status` (`sync_status_zh_tw`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci
  COMMENT='技能 content profile 可讀候選列；不包含機器碼、hash 或原始效果資料';

CREATE TABLE IF NOT EXISTS `god2_research`.`skill_semantic_profile_candidate_rows` (
    `candidate_row_id` bigint NOT NULL AUTO_INCREMENT COMMENT '可讀候選流水號',
    `skill_id` bigint NOT NULL COMMENT '正式技能 ID',
    `client_skill_id` int NULL COMMENT '客戶端技能 ID',
    `formal_skill_name_zh_tw` varchar(200) NULL COMMENT '正式技能繁體中文名稱',
    `profession_zh_tw` varchar(80) NULL COMMENT '職業',
    `skill_rank` int NULL COMMENT '技能階級',
    `display_name_key` varchar(256) NULL COMMENT '顯示名稱 key',
    `skill_family_zh_tw` varchar(80) NOT NULL COMMENT '技能家族',
    `target_policy_zh_tw` varchar(80) NOT NULL COMMENT '目標政策',
    `mp_cost_policy_zh_tw` varchar(80) NOT NULL COMMENT 'MP 消耗政策',
    `mp_cost` int NULL COMMENT 'MP 消耗',
    `has_effect_reference` tinyint(1) NOT NULL DEFAULT 0 COMMENT '來源是否有技能效果參照；不儲存原始 JSON',
    `has_status_reference` tinyint(1) NOT NULL DEFAULT 0 COMMENT '來源是否有狀態效果參照；不儲存原始 JSON',
    `production_enabled` tinyint(1) NOT NULL DEFAULT 0 COMMENT '來源 semantic profile 是否正式啟用',
    `sync_status_zh_tw` varchar(64) NOT NULL COMMENT '同步判定',
    `sync_policy_zh_tw` varchar(256) NOT NULL COMMENT '同步政策與原因',
    `updated_at_utc` datetime(6) NOT NULL DEFAULT UTC_TIMESTAMP(6) ON UPDATE CURRENT_TIMESTAMP(6) COMMENT '更新 UTC 時間',
    PRIMARY KEY (`candidate_row_id`),
    KEY `ix_skill_semantic_profile_candidate_rows_skill` (`skill_id`),
    KEY `ix_skill_semantic_profile_candidate_rows_client_skill` (`client_skill_id`),
    KEY `ix_skill_semantic_profile_candidate_rows_status` (`sync_status_zh_tw`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci
  COMMENT='技能 semantic profile 可讀候選列；不包含機器碼、hash 或原始效果資料';

INSERT INTO `god2_research`.`skill_content_profile_candidate_rows`
    (`skill_id`,`client_skill_id`,`formal_skill_name_zh_tw`,`profile_name_zh_tw`,`skill_family_zh_tw`,`target_policy_zh_tw`,
     `mp_cost_policy_zh_tw`,`mp_cost`,`has_effect_reference`,`has_status_reference`,`animation_key`,`presentation_key`,
     `source_enabled`,`sync_status_zh_tw`,`sync_policy_zh_tw`)
SELECT
    profile_row.`SkillId`,
    profile_row.`ClientSkillId`,
    formal_skill.`name_zh_tw`,
    profile_row.`NameZhTw`,
    profile_row.`SkillFamily`,
    profile_row.`TargetPolicy`,
    profile_row.`MpCostPolicy`,
    profile_row.`MpCost`,
    CASE WHEN profile_row.`EffectReferencesJson` IS NOT NULL AND profile_row.`EffectReferencesJson` NOT IN ('[]','{}','') THEN 1 ELSE 0 END,
    CASE WHEN profile_row.`StatusReferencesJson` IS NOT NULL AND profile_row.`StatusReferencesJson` NOT IN ('[]','{}','') THEN 1 ELSE 0 END,
    profile_row.`AnimationKey`,
    profile_row.`PresentationKey`,
    profile_row.`Enabled`,
    CASE
        WHEN formal_skill.`skill_id` IS NULL THEN '缺正式技能'
        WHEN profile_row.`Enabled` = 1 THEN '已對應正式技能'
        WHEN profile_row.`EffectReferencesJson` IS NOT NULL AND profile_row.`EffectReferencesJson` NOT IN ('[]','{}','') THEN '效果候選'
        ELSE '可人工確認'
    END,
    CASE
        WHEN formal_skill.`skill_id` IS NULL THEN '找不到正式技能對應，保留為研究候選。'
        WHEN profile_row.`Enabled` = 1 THEN '已對應正式技能；仍不直接啟用未解析效果。'
        WHEN profile_row.`EffectReferencesJson` IS NOT NULL AND profile_row.`EffectReferencesJson` NOT IN ('[]','{}','')
            THEN '來源有技能效果參照；需解析後才可實裝技能效果。'
        ELSE '已對應正式技能；保留名稱、目標、MP 與動畫呈現資訊供人工確認。'
    END
FROM `god2`.`skill_content_profiles` profile_row
LEFT JOIN `god2_game`.`skills` formal_skill
  ON formal_skill.`skill_id` = profile_row.`SkillId`;

INSERT INTO `god2_research`.`skill_semantic_profile_candidate_rows`
    (`skill_id`,`client_skill_id`,`formal_skill_name_zh_tw`,`profession_zh_tw`,`skill_rank`,`display_name_key`,`skill_family_zh_tw`,
     `target_policy_zh_tw`,`mp_cost_policy_zh_tw`,`mp_cost`,`has_effect_reference`,`has_status_reference`,`production_enabled`,
     `sync_status_zh_tw`,`sync_policy_zh_tw`)
SELECT
    profile_row.`SkillId`,
    profile_row.`ClientSkillId`,
    formal_skill.`name_zh_tw`,
    profile_row.`Profession`,
    profile_row.`SkillRank`,
    profile_row.`DisplayNameKey`,
    profile_row.`SkillFamily`,
    profile_row.`TargetPolicy`,
    profile_row.`MpCostPolicy`,
    profile_row.`MpCost`,
    CASE WHEN profile_row.`EffectReferencesJson` IS NOT NULL AND profile_row.`EffectReferencesJson` NOT IN ('[]','{}','') THEN 1 ELSE 0 END,
    CASE WHEN profile_row.`StatusReferencesJson` IS NOT NULL AND profile_row.`StatusReferencesJson` NOT IN ('[]','{}','') THEN 1 ELSE 0 END,
    profile_row.`ProductionEnabled`,
    CASE
        WHEN formal_skill.`skill_id` IS NULL THEN '缺正式技能'
        WHEN profile_row.`ProductionEnabled` = 1 THEN '已對應正式技能'
        WHEN profile_row.`MpCostPolicy` = 'EvidenceBlocked' THEN '證據不足'
        ELSE '可人工確認'
    END,
    CASE
        WHEN formal_skill.`skill_id` IS NULL THEN '找不到正式技能對應，保留為研究候選。'
        WHEN profile_row.`ProductionEnabled` = 1 THEN '已對應正式技能；仍不直接啟用未解析效果。'
        WHEN profile_row.`MpCostPolicy` = 'EvidenceBlocked' THEN 'MP 或效果證據不足，禁止自動覆蓋正式技能。'
        ELSE '已對應正式技能；保留職業、階級、目標與 MP 政策供人工確認。'
    END
FROM `god2`.`skill_semantic_profiles` profile_row
LEFT JOIN `god2_game`.`skills` formal_skill
  ON formal_skill.`skill_id` = profile_row.`SkillId`;

GRANT SELECT, INSERT, UPDATE, DELETE ON `god2_research`.`skill_content_profile_candidate_rows` TO 'god2_server'@'localhost';
GRANT SELECT, INSERT, UPDATE, DELETE ON `god2_research`.`skill_content_profile_candidate_rows` TO 'god2_server'@'127.0.0.1';
GRANT SELECT, INSERT, UPDATE, DELETE ON `god2_research`.`skill_content_profile_candidate_rows` TO 'god2_catalog_builder'@'localhost';
GRANT SELECT, INSERT, UPDATE, DELETE ON `god2_research`.`skill_content_profile_candidate_rows` TO 'god2_catalog_builder'@'127.0.0.1';
GRANT SELECT, INSERT, UPDATE, DELETE ON `god2_research`.`skill_semantic_profile_candidate_rows` TO 'god2_server'@'localhost';
GRANT SELECT, INSERT, UPDATE, DELETE ON `god2_research`.`skill_semantic_profile_candidate_rows` TO 'god2_server'@'127.0.0.1';
GRANT SELECT, INSERT, UPDATE, DELETE ON `god2_research`.`skill_semantic_profile_candidate_rows` TO 'god2_catalog_builder'@'localhost';
GRANT SELECT, INSERT, UPDATE, DELETE ON `god2_research`.`skill_semantic_profile_candidate_rows` TO 'god2_catalog_builder'@'127.0.0.1';
