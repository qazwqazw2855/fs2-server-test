CREATE OR REPLACE VIEW `god2`.`vw_player_runtime_table_function_overview_readable` AS
SELECT
    table_map.`schema_name` AS `資料庫`,
    table_map.`table_name` AS `資料表`,
    table_map.`game_function_zh_tw` AS `遊戲功能`,
    table_map.`runtime_role_zh_tw` AS `服務端用途`,
    table_map.`operator_hint_zh_tw` AS `查看重點`,
    COALESCE(table_stats.`TABLE_ROWS`, 0) AS `目前估計筆數`
FROM (
    SELECT 'god2' AS `schema_name`, 'accounts' AS `table_name`, '帳號登入' AS `game_function_zh_tw`, '保存玩家帳號、登入狀態與帳號啟用狀態；不在 readable view 暴露密碼雜湊或 token' AS `runtime_role_zh_tw`, '查帳號是否存在、是否啟用、是否能登入' AS `operator_hint_zh_tw`
    UNION ALL SELECT 'god2', 'characters', '角色資料', '保存角色名稱、職業、等級、地圖位置與基本狀態', '查角色是否建立、是否刪除、目前在哪張地圖'
    UNION ALL SELECT 'god2', 'character_creation_profiles', '創角證據與職業模板', '保存創角時可用職業、性別、初始外觀或模板候選', '查創角資料是否有官方證據與正式限制'
    UNION ALL SELECT 'god2_game', 'character_classes', '正式職業定義', '保存正式服務端職業分類與可用狀態', '查仙道等職業是否進正式職業表'
    UNION ALL SELECT 'god2_game', 'character_creation_profiles', '正式創角設定', '保存正式服務端創角可選資料與初始規則', '查玩家能不能正式建立該職業角色'
    UNION ALL SELECT 'god2_game', 'class_level_stats', '職業等級能力', '保存各職業各等級的 HP/MP/攻防等級成長資料', '查升級後能力是否有表可用'
    UNION ALL SELECT 'god2_game', 'class_stat_growth', '職業成長曲線', '保存職業屬性成長規則', '查力量、體質、敏捷等成長來源'
    UNION ALL SELECT 'god2', 'player_inventory_state', '背包狀態', '保存玩家背包容量、版本與整體狀態', '查背包是否已建立、版本是否正常'
    UNION ALL SELECT 'god2', 'inventory_slots', '背包格子', '保存玩家每一格道具、數量、綁定與堆疊狀態', '查道具是否在正確格子、數量是否正確'
    UNION ALL SELECT 'god2', 'equipment_slots', '角色裝備欄', '保存角色目前穿戴的武器、防具、飾品或其他裝備', '查穿脫裝備是否落到正確欄位'
    UNION ALL SELECT 'god2', 'player_currency_balances', '玩家貨幣', '保存金錢、特殊貨幣或活動代幣餘額', '查交易、任務獎勵、商店購買後餘額'
    UNION ALL SELECT 'god2', 'inventory_audit_ledger', '背包稽核', '保存背包新增、移動、移除、使用、交易等操作紀錄', '查道具為什麼變多、變少或移動'
    UNION ALL SELECT 'god2', 'inventory_transaction_idempotency', '背包重複操作保護', '避免背包同一筆操作因重送封包而重複執行', '查重送使用道具、移動道具是否被擋住'
    UNION ALL SELECT 'god2', 'quest_instances', '玩家任務實例', '保存玩家已接任務、任務狀態與目前進度', '查玩家是否已接、完成或放棄任務'
    UNION ALL SELECT 'god2', 'quest_objective_states', '任務目標進度', '保存玩家每個任務目標的擊殺、收集、對話或到達進度', '查任務卡在哪個目標'
    UNION ALL SELECT 'god2', 'quest_progress_mutations', '任務進度變更', '保存任務進度每次變更的來源與結果', '查打怪、交物品或對話是否推進任務'
    UNION ALL SELECT 'god2', 'quest_reward_finalization', '任務獎勵結算', '保存任務完成後獎勵是否已發放與結算', '查獎勵是否重複發或漏發'
    UNION ALL SELECT 'god2', 'quest_operation_idempotency', '任務重複操作保護', '避免接任務、回報任務、領獎等封包重送造成重複執行', '查任務操作重送是否被擋住'
    UNION ALL SELECT 'god2', 'quest_recovery_state', '任務復原狀態', '保存任務系統重啟或異常後的恢復資料', '查服務重啟後任務進度是否可恢復'
) table_map
LEFT JOIN `information_schema`.`TABLES` table_stats
    ON table_stats.`TABLE_SCHEMA` = table_map.`schema_name`
   AND table_stats.`TABLE_NAME` = table_map.`table_name`;
