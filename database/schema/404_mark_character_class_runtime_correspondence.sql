UPDATE `god2_research`.`formal_table_server_correspondence`
SET `direct_runtime_reference` = 1,
    `server_correspondence_status_zh_tw` = '服務端直接對應',
    `runtime_usage_policy_zh_tw` = '角色建立流程會直接檢查正式職業與 1 級職業基礎資料；清理時不可刪除。',
    `cleanup_decision_zh_tw` = '保留，正式創角 runtime 已對應。'
WHERE `schema_name` = 'god2_game'
  AND `table_name` IN ('character_classes','class_level_stats');

REPLACE INTO `god2_research`.`database_traditional_chinese_surface_audit`
    (`audit_name`,`checked_scope_zh_tw`,`issue_count`,`status_zh_tw`,`cleanup_policy_zh_tw`)
SELECT
    'formal_table_server_correspondence',
    'god2_game 正式資料表與服務端程式碼對應狀態',
    SUM(CASE WHEN `server_correspondence_status_zh_tw` = '正式保留待接服務端' THEN 1 ELSE 0 END),
    CASE
        WHEN SUM(CASE WHEN `server_correspondence_status_zh_tw` = '正式保留待接服務端' THEN 1 ELSE 0 END) = 0 THEN '通過'
        ELSE '有待接項目'
    END,
    '未直接刪除有正式語意的資料表；已先接入角色建立所需的正式職業與 1 級基礎資料，其他待接表後續依功能逐項處理。'
FROM `god2_research`.`formal_table_server_correspondence`;
