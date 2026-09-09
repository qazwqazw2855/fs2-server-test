CREATE DATABASE IF NOT EXISTS `god2_research`
  DEFAULT CHARACTER SET utf8mb4
  COLLATE utf8mb4_unicode_ci;

CREATE TABLE IF NOT EXISTS `god2_research`.`quest_content_profile_candidate_rows` (
    `candidate_row_id` bigint NOT NULL AUTO_INCREMENT COMMENT '可讀候選流水號',
    `client_quest_id` bigint NOT NULL COMMENT '客戶端任務 ID',
    `formal_quest_id` bigint NULL COMMENT '對應正式任務 ID',
    `formal_quest_name_zh_tw` varchar(200) NULL COMMENT '正式任務繁體中文名稱',
    `profile_text_zh_tw` text NOT NULL COMMENT 'profile 繁體中文文字；不包含原始 JSON',
    `description_zh_tw` text NULL COMMENT '任務描述繁體中文',
    `start_npc_client_id` bigint NULL COMMENT '起始 NPC 客戶端 ID',
    `end_npc_client_id` bigint NULL COMMENT '結束 NPC 客戶端 ID',
    `reward_text_zh_tw` text NULL COMMENT '任務獎勵或完成文字繁體中文',
    `has_steps_payload` tinyint(1) NOT NULL DEFAULT 0 COMMENT '來源是否曾有步驟 payload；不儲存原始 JSON',
    `production_profile_enabled` tinyint(1) NOT NULL DEFAULT 0 COMMENT '來源 profile 是否標記正式啟用',
    `sync_status_zh_tw` varchar(64) NOT NULL COMMENT '同步判定',
    `sync_policy_zh_tw` varchar(256) NOT NULL COMMENT '同步政策與原因',
    `updated_at_utc` datetime(6) NOT NULL DEFAULT UTC_TIMESTAMP(6) ON UPDATE CURRENT_TIMESTAMP(6) COMMENT '更新 UTC 時間',
    PRIMARY KEY (`candidate_row_id`),
    KEY `ix_quest_content_profile_candidate_rows_client_quest` (`client_quest_id`),
    KEY `ix_quest_content_profile_candidate_rows_formal_quest` (`formal_quest_id`),
    KEY `ix_quest_content_profile_candidate_rows_status` (`sync_status_zh_tw`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci
  COMMENT='任務 profile 每列可讀候選摘要；移除 hash 與原始 StepsJson，避免簡體 escape payload 污染正式可讀資料';

INSERT INTO `god2_research`.`quest_content_profile_candidate_rows`
    (`client_quest_id`,`formal_quest_id`,`formal_quest_name_zh_tw`,`profile_text_zh_tw`,`description_zh_tw`,
     `start_npc_client_id`,`end_npc_client_id`,`reward_text_zh_tw`,`has_steps_payload`,`production_profile_enabled`,
     `sync_status_zh_tw`,`sync_policy_zh_tw`)
SELECT
    profile_row.`ClientQuestId`,
    formal_quest.`quest_id`,
    formal_quest.`name_zh_tw`,
    profile_row.`NameZhTw`,
    profile_row.`DescriptionZhTw`,
    profile_row.`StartNpcClientId`,
    profile_row.`EndNpcClientId`,
    profile_row.`RewardTextZhTw`,
    CASE WHEN profile_row.`StepsJson` IS NOT NULL AND profile_row.`StepsJson` NOT IN ('[]','{}','') THEN 1 ELSE 0 END,
    profile_row.`ProductionProfileEnabled`,
    CASE
        WHEN formal_quest.`quest_id` IS NOT NULL AND profile_row.`ProductionProfileEnabled` = 1 THEN '已對應正式任務'
        WHEN formal_quest.`quest_id` IS NOT NULL THEN '可人工確認'
        ELSE '缺正式任務'
    END,
    CASE
        WHEN formal_quest.`quest_id` IS NOT NULL AND profile_row.`ProductionProfileEnabled` = 1
            THEN '已可對應正式任務；保留繁中 profile 文字，不搬原始 StepsJson。'
        WHEN formal_quest.`quest_id` IS NOT NULL
            THEN '可對應正式任務，但來源尚未啟用；需人工確認後再提升。'
        ELSE '找不到正式任務對應，保留為研究候選；不得直接污染 god2_game.quests。'
    END
FROM `god2`.`quest_content_profiles` profile_row
LEFT JOIN `god2_game`.`quests` formal_quest
  ON formal_quest.`quest_id` = profile_row.`ClientQuestId`;

GRANT SELECT, INSERT, UPDATE, DELETE ON `god2_research`.`quest_content_profile_candidate_rows` TO 'god2_server'@'localhost';
GRANT SELECT, INSERT, UPDATE, DELETE ON `god2_research`.`quest_content_profile_candidate_rows` TO 'god2_server'@'127.0.0.1';
GRANT SELECT, INSERT, UPDATE, DELETE ON `god2_research`.`quest_content_profile_candidate_rows` TO 'god2_catalog_builder'@'localhost';
GRANT SELECT, INSERT, UPDATE, DELETE ON `god2_research`.`quest_content_profile_candidate_rows` TO 'god2_catalog_builder'@'127.0.0.1';
