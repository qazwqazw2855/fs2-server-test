START TRANSACTION;

CREATE TABLE IF NOT EXISTS `god2_research`.`archived_unbound_empty_npc_dialog_shells` (
    `archived_at_utc` datetime(6) NOT NULL DEFAULT utc_timestamp(6),
    `archive_reason_zh_tw` varchar(300) NOT NULL,
    `dialog_id` bigint(20) NOT NULL,
    `code` varchar(100) NULL,
    `npc_id` bigint(20) NULL,
    `title_zh_tw` varchar(200) NULL,
    `body_zh_tw` text NULL,
    `dialog_type` varchar(50) NULL,
    `next_dialog_id` bigint(20) NULL,
    `enabled` tinyint(1) NOT NULL,
    `admin_note` varchar(500) NULL,
    PRIMARY KEY (`dialog_id`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

INSERT INTO `god2_research`.`archived_unbound_empty_npc_dialog_shells`
    (`archive_reason_zh_tw`, `dialog_id`, `code`, `npc_id`, `title_zh_tw`, `body_zh_tw`, `dialog_type`, `next_dialog_id`, `enabled`, `admin_note`)
SELECT '正式 NPC 對話空殼：沒有 NPC 綁定、沒有標題、沒有本文、沒有對話類型，且原註記為 CatalogOnlyEvidenceBlocked；歸檔到研究庫後自正式服務端資料移除。',
       dialog_row.`dialog_id`,
       dialog_row.`code`,
       dialog_row.`npc_id`,
       dialog_row.`title_zh_tw`,
       dialog_row.`body_zh_tw`,
       dialog_row.`dialog_type`,
       dialog_row.`next_dialog_id`,
       dialog_row.`enabled`,
       dialog_row.`admin_note`
FROM `god2_game`.`npc_dialogs` dialog_row
WHERE dialog_row.`enabled` = 0
  AND dialog_row.`npc_id` IS NULL
  AND (dialog_row.`title_zh_tw` IS NULL OR dialog_row.`title_zh_tw` = '')
  AND (dialog_row.`body_zh_tw` IS NULL OR dialog_row.`body_zh_tw` = '')
  AND (dialog_row.`dialog_type` IS NULL OR dialog_row.`dialog_type` = '')
  AND dialog_row.`admin_note` LIKE '%CatalogOnlyEvidenceBlocked%'
ON DUPLICATE KEY UPDATE
    `archived_at_utc` = utc_timestamp(6),
    `archive_reason_zh_tw` = VALUES(`archive_reason_zh_tw`),
    `code` = VALUES(`code`),
    `npc_id` = VALUES(`npc_id`),
    `title_zh_tw` = VALUES(`title_zh_tw`),
    `body_zh_tw` = VALUES(`body_zh_tw`),
    `dialog_type` = VALUES(`dialog_type`),
    `next_dialog_id` = VALUES(`next_dialog_id`),
    `enabled` = VALUES(`enabled`),
    `admin_note` = VALUES(`admin_note`);

DELETE dialog_row
FROM `god2_game`.`npc_dialogs` dialog_row
JOIN `god2_research`.`archived_unbound_empty_npc_dialog_shells` archived_row
  ON archived_row.`dialog_id` = dialog_row.`dialog_id`
WHERE dialog_row.`enabled` = 0
  AND dialog_row.`npc_id` IS NULL
  AND (dialog_row.`title_zh_tw` IS NULL OR dialog_row.`title_zh_tw` = '')
  AND (dialog_row.`body_zh_tw` IS NULL OR dialog_row.`body_zh_tw` = '')
  AND (dialog_row.`dialog_type` IS NULL OR dialog_row.`dialog_type` = '')
  AND dialog_row.`admin_note` LIKE '%CatalogOnlyEvidenceBlocked%';

COMMIT;
