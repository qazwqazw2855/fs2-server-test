UPDATE `god2_research`.`formal_table_server_correspondence`
SET `direct_runtime_reference` = 1,
    `server_correspondence_status_zh_tw` = '服務端直接對應',
    `runtime_usage_policy_zh_tw` = '正式 runtime catalog 啟動盤點會讀取並指紋化怪物戰鬥、掉落、出生設計規則表；目前不自動套用設計規則覆蓋正式怪物資料。',
    `cleanup_decision_zh_tw` = '保留，正式 runtime catalog 已對應；啟用自動設計前需另有黑箱測試或人工決策。'
WHERE `schema_name` = 'god2_game'
  AND `table_name` IN (
      'monster_combat_stat_design_rules',
      'monster_drop_design_rules',
      'monster_spawn_design_rules'
  );

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
    '未直接刪除有正式語意的資料表；怪物設計規則已納入正式 runtime catalog 盤點與 release manifest，但未自動套用到正式怪物資料。'
FROM `god2_research`.`formal_table_server_correspondence`;
