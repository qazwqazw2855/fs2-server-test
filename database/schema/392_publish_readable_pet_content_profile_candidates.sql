CREATE DATABASE IF NOT EXISTS `god2_research`
  DEFAULT CHARACTER SET utf8mb4
  COLLATE utf8mb4_unicode_ci;

CREATE TABLE IF NOT EXISTS `god2_research`.`pet_content_profile_candidates` (
    `client_pet_id` bigint NOT NULL COMMENT '客戶端寵物 profile ID',
    `item_id` bigint NULL COMMENT '關聯道具 ID',
    `item_name_zh_tw` varchar(200) NULL COMMENT '關聯道具繁體中文名稱',
    `profile_label_zh_tw` varchar(150) NOT NULL COMMENT 'profile 內的繁中標籤；不一定是寵物名稱',
    `pet_family_zh_tw` varchar(80) NOT NULL COMMENT '寵物資料分類',
    `growth_type_zh_tw` varchar(80) NULL COMMENT '成長類型；未知時為 NULL',
    `formal_pet_template_id` bigint NULL COMMENT '同 client_pet_id 命中的正式寵物模板 ID',
    `formal_pet_name_zh_tw` varchar(150) NULL COMMENT '正式寵物模板名稱',
    `sync_status_zh_tw` varchar(64) NOT NULL COMMENT '同步判定',
    `sync_policy_zh_tw` varchar(256) NOT NULL COMMENT '同步政策與原因',
    `updated_at_utc` datetime(6) NOT NULL DEFAULT UTC_TIMESTAMP(6) ON UPDATE CURRENT_TIMESTAMP(6) COMMENT '更新 UTC 時間',
    PRIMARY KEY (`client_pet_id`, `profile_label_zh_tw`),
    KEY `ix_pet_content_profile_candidates_item` (`item_id`),
    KEY `ix_pet_content_profile_candidates_formal_template` (`formal_pet_template_id`),
    KEY `ix_pet_content_profile_candidates_status` (`sync_status_zh_tw`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci
  COMMENT='寵物 profile 可讀候選摘要；移除 hash 與空 JSON，避免中間資料污染正式寵物模板';

INSERT INTO `god2_research`.`pet_content_profile_candidates`
    (`client_pet_id`,`item_id`,`item_name_zh_tw`,`profile_label_zh_tw`,`pet_family_zh_tw`,`growth_type_zh_tw`,
     `formal_pet_template_id`,`formal_pet_name_zh_tw`,`sync_status_zh_tw`,`sync_policy_zh_tw`)
SELECT
    profile_row.`ClientPetId`,
    profile_row.`ItemId`,
    item_row.`name_zh_tw`,
    profile_row.`NameZhTw`,
    profile_row.`PetFamily`,
    profile_row.`GrowthType`,
    template_row.`pet_template_id`,
    template_row.`name_zh_tw`,
    CASE
        WHEN profile_row.`BaseStatsJson` <> '{}' OR profile_row.`SkillReferencesJson` <> '[]' OR profile_row.`EvolutionReferencesJson` <> '[]'
            THEN '可後續解析'
        WHEN template_row.`pet_template_id` IS NOT NULL AND profile_row.`NameZhTw` = template_row.`name_zh_tw`
            THEN '可人工確認'
        WHEN template_row.`pet_template_id` IS NOT NULL
            THEN '禁止自動提升'
        ELSE '缺正式模板'
    END,
    CASE
        WHEN profile_row.`BaseStatsJson` <> '{}' OR profile_row.`SkillReferencesJson` <> '[]' OR profile_row.`EvolutionReferencesJson` <> '[]'
            THEN 'profile 含可解析 stats/skill/evolution JSON，需解析後才可提升。'
        WHEN template_row.`pet_template_id` IS NOT NULL AND profile_row.`NameZhTw` = template_row.`name_zh_tw`
            THEN 'profile 標籤與正式寵物名稱一致，可人工確認後提升。'
        WHEN template_row.`pet_template_id` IS NOT NULL
            THEN 'client_pet_id 可對到正式模板，但 profile 標籤不是寵物名稱，禁止自動覆蓋正式寵物。'
        ELSE '缺正式寵物模板對應，保留為研究候選。'
    END
FROM `god2`.`pet_content_profiles` profile_row
LEFT JOIN `god2_game`.`item_registry` item_row
  ON item_row.`item_id` = profile_row.`ItemId`
LEFT JOIN `god2_game`.`pet_templates` template_row
  ON template_row.`pet_template_id` = profile_row.`ClientPetId`
ON DUPLICATE KEY UPDATE
    `item_id`=VALUES(`item_id`),
    `item_name_zh_tw`=VALUES(`item_name_zh_tw`),
    `pet_family_zh_tw`=VALUES(`pet_family_zh_tw`),
    `growth_type_zh_tw`=VALUES(`growth_type_zh_tw`),
    `formal_pet_template_id`=VALUES(`formal_pet_template_id`),
    `formal_pet_name_zh_tw`=VALUES(`formal_pet_name_zh_tw`),
    `sync_status_zh_tw`=VALUES(`sync_status_zh_tw`),
    `sync_policy_zh_tw`=VALUES(`sync_policy_zh_tw`),
    `updated_at_utc`=UTC_TIMESTAMP(6);

GRANT SELECT, INSERT, UPDATE, DELETE ON `god2_research`.`pet_content_profile_candidates` TO 'god2_server'@'localhost';
GRANT SELECT, INSERT, UPDATE, DELETE ON `god2_research`.`pet_content_profile_candidates` TO 'god2_server'@'127.0.0.1';
GRANT SELECT, INSERT, UPDATE, DELETE ON `god2_research`.`pet_content_profile_candidates` TO 'god2_catalog_builder'@'localhost';
GRANT SELECT, INSERT, UPDATE, DELETE ON `god2_research`.`pet_content_profile_candidates` TO 'god2_catalog_builder'@'127.0.0.1';
