CREATE OR REPLACE VIEW `god2`.`vw_battle_runtime_table_function_overview_readable` AS
SELECT
    table_map.`table_name` AS `資料表`,
    table_map.`game_function_zh_tw` AS `遊戲功能`,
    table_map.`runtime_role_zh_tw` AS `服務端用途`,
    table_map.`operator_hint_zh_tw` AS `查看重點`,
    COALESCE(table_stats.`TABLE_ROWS`, 0) AS `目前估計筆數`
FROM (
    SELECT 'battle_instances' AS `table_name`, '戰鬥主流程' AS `game_function_zh_tw`, '保存一場戰鬥的狀態、階段、目前回合、勝負與獎勵狀態' AS `runtime_role_zh_tw`, '查一場戰鬥是否開始、進行中、完成或復原中' AS `operator_hint_zh_tw`
    UNION ALL SELECT 'battle_participants', '戰鬥參與者', '保存玩家、怪物、寵物或其他戰鬥單位在戰鬥中的位置與陣營', '查誰參戰、站在哪邊、是否還有效'
    UNION ALL SELECT 'battle_actor_instances', '戰鬥角色快照', '保存參戰單位的戰鬥時屬性快照', '查進戰鬥時的 HP/MP/攻防等 runtime 狀態'
    UNION ALL SELECT 'battle_rounds', '戰鬥回合', '保存每一回合的狀態與處理進度', '查第幾回合卡住或完成'
    UNION ALL SELECT 'battle_round_plans', '回合行動計畫', '保存回合內預計處理的行動排序', '查服務端準備怎麼排技能、普攻、道具或逃跑'
    UNION ALL SELECT 'battle_round_checkpoints', '回合檢查點', '保存回合處理中的安全檢查與恢復點', '查斷線或例外後能從哪個點恢復'
    UNION ALL SELECT 'battle_actions', '玩家或單位戰鬥指令', '保存送出的普攻、技能、道具、防禦等指令', '查客戶端送了什麼動作、是否已解析'
    UNION ALL SELECT 'battle_action_results', '戰鬥指令結果', '保存指令執行後的命中、傷害、失敗原因或狀態變化結果', '查一個動作為什麼成功或失敗'
    UNION ALL SELECT 'battle_skill_usage', '戰鬥技能施放', '保存技能消耗、目標、施放規則與執行結果', '查技能是否扣 MP、目標是否正確、效果是否套用'
    UNION ALL SELECT 'battle_command_journal', '戰鬥命令日誌', '保存戰鬥命令處理軌跡', '查封包或指令從收到到處理的流程'
    UNION ALL SELECT 'battle_event_outbox', '戰鬥事件輸出', '保存要通知客戶端或其他服務的戰鬥事件', '查哪些戰鬥事件準備送出'
    UNION ALL SELECT 'battle_completion', '戰鬥完成流程', '保存戰鬥結束、勝負、獎勵結算入口', '查戰鬥是否走到結算'
    UNION ALL SELECT 'battle_finalizations', '戰鬥最終結算', '保存最終提交的戰鬥結算紀錄', '查獎勵、掉落、經驗是否已正式落地'
    UNION ALL SELECT 'battle_idempotency', '戰鬥重複請求保護', '避免同一戰鬥請求重送造成重複執行', '查重送封包是否被擋住'
    UNION ALL SELECT 'battle_status_instances', '戰鬥狀態實例', '保存中毒、睡眠、石化、封印、增益等狀態在目標身上的實例', '查狀態剩幾回合、幾層、是否移除'
    UNION ALL SELECT 'battle_status_applications', '戰鬥狀態套用', '保存狀態被技能或效果套到目標身上的紀錄', '查狀態是誰造成、何時套用'
    UNION ALL SELECT 'battle_status_removals', '戰鬥狀態移除', '保存淨化、到期、死亡或規則造成的狀態移除', '查狀態為什麼不見'
    UNION ALL SELECT 'battle_status_trigger_plans', '狀態觸發計畫', '保存狀態每回合要觸發的效果計畫', '查中毒扣血或持續效果是否排程'
    UNION ALL SELECT 'battle_status_trigger_results', '狀態觸發結果', '保存狀態觸發後造成的實際效果', '查持續傷害、治療或控制效果結果'
    UNION ALL SELECT 'battle_status_audit', '戰鬥狀態稽核', '保存狀態系統的檢查與異常紀錄', '查狀態規則是否有不一致'
    UNION ALL SELECT 'battle_status_idempotency', '狀態重複請求保護', '避免同一狀態套用、移除或觸發重複執行', '查狀態相關重送是否被擋住'
    UNION ALL SELECT 'battle_status_participant_versions', '參戰者狀態版本', '保存狀態變更時參戰者版本，用於一致性控制', '查同一角色狀態版本是否跳號或衝突'
    UNION ALL SELECT 'battle_status_recovery', '戰鬥狀態恢復', '保存狀態系統復原用資料', '查服務重啟後狀態能否恢復'
) table_map
LEFT JOIN `information_schema`.`TABLES` table_stats
    ON table_stats.`TABLE_SCHEMA` = 'god2'
   AND table_stats.`TABLE_NAME` = table_map.`table_name`;
