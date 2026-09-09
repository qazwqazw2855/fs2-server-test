DROP VIEW IF EXISTS `god2`.`vw_battle_instances_readable`;
DROP VIEW IF EXISTS `god2`.`vw_battle_participants_readable`;
DROP VIEW IF EXISTS `god2`.`vw_battle_actions_readable`;
DROP VIEW IF EXISTS `god2`.`vw_battle_action_results_readable`;
DROP VIEW IF EXISTS `god2`.`vw_battle_event_outbox_readable`;
DROP VIEW IF EXISTS `god2`.`vw_blackbox_battle_runtime_observations_readable`;

CREATE VIEW `god2`.`vw_battle_instances_readable` AS
SELECT
    battle.`BattleInstanceId` AS `戰鬥場次ID`,
    battle.`BattleRequestId` AS `戰鬥請求ID`,
    battle.`WorldInstanceId` AS `世界實例ID`,
    battle.`SourceMapId` AS `來源地圖ID`,
    COALESCE(map_info.`name_zh_tw`, '') AS `來源地圖名稱`,
    battle.`EncounterDefinitionId` AS `遭遇定義ID`,
    battle.`State` AS `戰鬥狀態`,
    battle.`Phase` AS `戰鬥階段`,
    battle.`CurrentRoundNumber` AS `目前回合`,
    battle.`CurrentResolutionIndex` AS `目前解析序號`,
    battle.`BattleVersion` AS `戰鬥版本`,
    battle.`WinnerSide` AS `勝利方`,
    battle.`CompletionReason` AS `結束原因`,
    battle.`RewardState` AS `獎勵狀態`,
    battle.`RecoveryState` AS `恢復狀態`,
    CASE
        WHEN battle.`State` = 'Active' THEN '戰鬥進行中'
        WHEN battle.`State` = 'Completed' THEN '戰鬥已結束'
        ELSE '戰鬥狀態待確認'
    END AS `功能對照`,
    battle.`CreatedAtUtc` AS `建立時間UTC`,
    battle.`UpdatedAtUtc` AS `更新時間UTC`,
    battle.`CompletedAtUtc` AS `完成時間UTC`
FROM `god2`.`battle_instances` battle
LEFT JOIN `god2_game`.`maps` map_info
    ON map_info.`map_id` = battle.`SourceMapId`;

CREATE VIEW `god2`.`vw_battle_participants_readable` AS
SELECT
    participant.`BattleInstanceId` AS `戰鬥場次ID`,
    participant.`ParticipantId` AS `參戰者ID`,
    participant.`ParticipantType` AS `參戰者類型`,
    participant.`Side` AS `陣營`,
    participant.`FormationSlot` AS `站位`,
    participant.`CharacterId` AS `角色ID`,
    COALESCE(character_info.`name`, '') AS `角色名稱`,
    participant.`MonsterTemplateId` AS `怪物模板ID`,
    COALESCE(monster_info.`name_zh_tw`, '') AS `怪物名稱`,
    participant.`SourceRuntimeEntityId` AS `來源RuntimeID`,
    participant.`BattleRuntimeEntityId` AS `戰鬥RuntimeID`,
    participant.`CurrentHp` AS `目前HP`,
    CASE WHEN participant.`IsAlive` = 1 THEN '存活' ELSE '死亡' END AS `存活狀態`,
    CASE WHEN participant.`IsConnected` = 1 THEN '連線中' ELSE '未連線' END AS `連線狀態`,
    participant.`RuntimeVersion` AS `Runtime版本`,
    CASE
        WHEN participant.`ParticipantType` = 'Player' THEN '玩家參戰'
        WHEN participant.`ParticipantType` = 'Monster' THEN '怪物參戰'
        ELSE '參戰者'
    END AS `功能對照`,
    participant.`UpdatedAtUtc` AS `更新時間UTC`
FROM `god2`.`battle_participants` participant
LEFT JOIN `god2_player`.`characters` character_info
    ON character_info.`character_id` = participant.`CharacterId`
LEFT JOIN `god2_game`.`monsters` monster_info
    ON monster_info.`monster_id` = participant.`MonsterTemplateId`;

CREATE VIEW `god2`.`vw_battle_actions_readable` AS
SELECT
    action.`BattleInstanceId` AS `戰鬥場次ID`,
    action.`RoundNumber` AS `回合`,
    action.`ActionId` AS `行動ID`,
    action.`ParticipantId` AS `參戰者ID`,
    action.`ActionType` AS `行動類型`,
    action.`State` AS `行動狀態`,
    action.`ResolutionIndex` AS `解析順序`,
    CASE
        WHEN action.`ActionType` = 'BasicAttack' THEN '普通攻擊'
        WHEN action.`ActionType` = 'Skill' THEN '使用技能'
        WHEN action.`ActionType` = 'Item' THEN '使用道具'
        WHEN action.`ActionType` = 'Defend' THEN '防禦'
        WHEN action.`ActionType` = 'Flee' THEN '逃跑'
        WHEN action.`ActionType` = 'Pass' THEN '略過'
        ELSE '戰鬥行動'
    END AS `功能對照`,
    CASE
        WHEN action.`ResultJson` IS NULL OR action.`ResultJson` = '' THEN '尚無結果'
        ELSE '已有結果'
    END AS `結果摘要`,
    action.`SubmittedAtUtc` AS `提交時間UTC`,
    action.`ResolvedAtUtc` AS `解析時間UTC`
FROM `god2`.`battle_actions` action;

CREATE VIEW `god2`.`vw_battle_action_results_readable` AS
SELECT
    result.`BattleInstanceId` AS `戰鬥場次ID`,
    result.`ActionExecutionId` AS `行動執行ID`,
    result.`RoundNumber` AS `回合`,
    result.`ExecutionOrder` AS `執行順序`,
    result.`ParticipantId` AS `參戰者ID`,
    result.`ResultCode` AS `結果代碼`,
    CASE
        WHEN result.`ResultCode` = 'Success' THEN '行動成功'
        WHEN result.`ResultCode` LIKE '%Failure%' THEN '行動失敗'
        ELSE '行動結果待確認'
    END AS `功能對照`,
    result.`CreatedAtUtc` AS `建立時間UTC`
FROM `god2`.`battle_action_results` result;

CREATE VIEW `god2`.`vw_battle_event_outbox_readable` AS
SELECT
    event_row.`BattleInstanceId` AS `戰鬥場次ID`,
    event_row.`EventSequence` AS `事件序號`,
    event_row.`EventId` AS `事件ID`,
    event_row.`EventType` AS `事件類型`,
    event_row.`RoundNumber` AS `回合`,
    event_row.`ActionExecutionId` AS `行動執行ID`,
    event_row.`DispatchState` AS `派發狀態`,
    event_row.`RetryCount` AS `重試次數`,
    event_row.`LastFailureCode` AS `最後失敗代碼`,
    CASE
        WHEN event_row.`EventType` LIKE '%Damage%' THEN '傷害事件'
        WHEN event_row.`EventType` LIKE '%Heal%' THEN '治療事件'
        WHEN event_row.`EventType` LIKE '%Status%' THEN '狀態事件'
        WHEN event_row.`EventType` LIKE '%Completed%' THEN '戰鬥結束事件'
        ELSE '戰鬥事件派發'
    END AS `功能對照`,
    event_row.`CreatedAtUtc` AS `建立時間UTC`,
    event_row.`UpdatedAtUtc` AS `更新時間UTC`
FROM `god2`.`battle_event_outbox` event_row;

CREATE VIEW `god2`.`vw_blackbox_battle_runtime_observations_readable` AS
SELECT
    battle_view.`戰鬥場次ID`,
    battle_view.`來源地圖ID`,
    battle_view.`來源地圖名稱`,
    battle_view.`戰鬥狀態`,
    battle_view.`戰鬥階段`,
    battle_view.`目前回合`,
    battle_view.`勝利方`,
    battle_view.`結束原因`,
    battle_view.`獎勵狀態`,
    COUNT(DISTINCT participant.`ParticipantId`) AS `參戰者數`,
    COUNT(DISTINCT action_row.`ActionId`) AS `行動數`,
    COUNT(DISTINCT result_row.`ActionExecutionId`) AS `結果數`,
    COUNT(DISTINCT event_row.`EventId`) AS `事件數`,
    CASE
        WHEN battle_view.`戰鬥狀態` = 'Completed' THEN '第一優先：核對勝負、獎勵、掉落、經驗金錢'
        WHEN battle_view.`戰鬥狀態` = 'Active' THEN '第一優先：核對回合、行動收集、傷害與狀態'
        ELSE '第二優先：確認戰鬥建立或恢復狀態'
    END AS `黑箱測試優先級`,
    '測遇怪進戰鬥、回合提交、普通攻擊、技能、道具、防禦x1.5、死亡、結算與事件派發。' AS `測試重點`,
    battle_view.`建立時間UTC`,
    battle_view.`完成時間UTC`
FROM `god2`.`vw_battle_instances_readable` battle_view
LEFT JOIN `god2`.`battle_participants` participant
    ON participant.`BattleInstanceId` = battle_view.`戰鬥場次ID`
LEFT JOIN `god2`.`battle_actions` action_row
    ON action_row.`BattleInstanceId` = battle_view.`戰鬥場次ID`
LEFT JOIN `god2`.`battle_action_results` result_row
    ON result_row.`BattleInstanceId` = battle_view.`戰鬥場次ID`
LEFT JOIN `god2`.`battle_event_outbox` event_row
    ON event_row.`BattleInstanceId` = battle_view.`戰鬥場次ID`
GROUP BY
    battle_view.`戰鬥場次ID`,
    battle_view.`來源地圖ID`,
    battle_view.`來源地圖名稱`,
    battle_view.`戰鬥狀態`,
    battle_view.`戰鬥階段`,
    battle_view.`目前回合`,
    battle_view.`勝利方`,
    battle_view.`結束原因`,
    battle_view.`獎勵狀態`,
    battle_view.`建立時間UTC`,
    battle_view.`完成時間UTC`;
