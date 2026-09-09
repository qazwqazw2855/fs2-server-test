DROP VIEW IF EXISTS `god2`.`vw_skill_executions_readable`;
DROP VIEW IF EXISTS `god2`.`vw_skill_effect_executions_readable`;
DROP VIEW IF EXISTS `god2`.`vw_skill_cost_reservations_readable`;
DROP VIEW IF EXISTS `god2`.`vw_battle_skill_usage_readable`;
DROP VIEW IF EXISTS `god2`.`vw_battle_status_instances_readable`;
DROP VIEW IF EXISTS `god2`.`vw_battle_status_trigger_results_readable`;
DROP VIEW IF EXISTS `god2`.`vw_blackbox_skill_status_runtime_observations_readable`;

CREATE VIEW `god2`.`vw_skill_executions_readable` AS
SELECT
    execution.`SkillExecutionId` AS `技能執行ID`,
    execution.`BattleInstanceId` AS `戰鬥場次ID`,
    execution.`RoundNumber` AS `回合`,
    execution.`BattleActionId` AS `戰鬥行動ID`,
    execution.`ParticipantId` AS `施放者參戰ID`,
    execution.`SkillDefinitionId` AS `技能ID`,
    COALESCE(skill_info.`name_zh_tw`, '') AS `技能名稱`,
    execution.`State` AS `執行狀態`,
    execution.`ResolutionCursor` AS `解析游標`,
    execution.`RecoveryState` AS `恢復狀態`,
    CASE
        WHEN execution.`State` = 'Completed' THEN '技能施放完成'
        WHEN execution.`State` = 'Failed' THEN '技能施放失敗'
        ELSE '技能施放進行中或待確認'
    END AS `功能對照`,
    execution.`StartedAtUtc` AS `開始時間UTC`,
    execution.`UpdatedAtUtc` AS `更新時間UTC`,
    execution.`CompletedAtUtc` AS `完成時間UTC`
FROM `god2`.`skill_executions` execution
LEFT JOIN `god2_game`.`skills` skill_info
    ON skill_info.`skill_id` = execution.`SkillDefinitionId`;

CREATE VIEW `god2`.`vw_skill_effect_executions_readable` AS
SELECT
    effect.`EffectExecutionId` AS `效果執行ID`,
    effect.`SkillExecutionId` AS `技能執行ID`,
    skill_view.`戰鬥場次ID`,
    skill_view.`回合`,
    skill_view.`技能ID`,
    skill_view.`技能名稱`,
    effect.`EffectDefinitionId` AS `效果定義ID`,
    effect.`EffectIndex` AS `效果順序`,
    effect.`TargetIndex` AS `目標順序`,
    effect.`TargetParticipantId` AS `目標參戰ID`,
    effect.`EffectType` AS `效果類型`,
    effect.`State` AS `效果狀態`,
    effect.`HpBefore` AS `HP前`,
    effect.`Damage` AS `傷害`,
    effect.`Heal` AS `治療`,
    effect.`HpAfter` AS `HP後`,
    effect.`RuntimeVersionBefore` AS `Runtime版本前`,
    effect.`RuntimeVersionAfter` AS `Runtime版本後`,
    effect.`FailureCode` AS `失敗代碼`,
    CASE
        WHEN effect.`Damage` > 0 THEN '技能造成傷害'
        WHEN effect.`Heal` > 0 THEN '技能造成治療'
        WHEN effect.`EffectType` LIKE '%Status%' THEN '技能套用狀態'
        WHEN effect.`FailureCode` <> '' THEN '技能效果失敗'
        ELSE '技能效果'
    END AS `功能對照`,
    effect.`CompletedAtUtc` AS `完成時間UTC`
FROM `god2`.`skill_effect_executions` effect
LEFT JOIN `god2`.`vw_skill_executions_readable` skill_view
    ON skill_view.`技能執行ID` = effect.`SkillExecutionId`;

CREATE VIEW `god2`.`vw_skill_cost_reservations_readable` AS
SELECT
    reservation.`ReservationId` AS `資源預扣ID`,
    reservation.`SkillExecutionId` AS `技能執行ID`,
    reservation.`ParticipantId` AS `參戰者ID`,
    reservation.`SkillDefinitionId` AS `技能ID`,
    COALESCE(skill_info.`name_zh_tw`, '') AS `技能名稱`,
    reservation.`ResourceType` AS `資源類型`,
    reservation.`Amount` AS `數量`,
    reservation.`State` AS `預扣狀態`,
    reservation.`RuntimeVersionBefore` AS `Runtime版本前`,
    CASE
        WHEN reservation.`ResourceType` = 'Mp' THEN '技能MP消耗'
        WHEN reservation.`ResourceType` = 'Hp' THEN '技能HP消耗'
        WHEN reservation.`ResourceType` = 'Item' THEN '技能道具消耗'
        ELSE '技能資源消耗'
    END AS `功能對照`,
    reservation.`CreatedAtUtc` AS `建立時間UTC`,
    reservation.`CommittedAtUtc` AS `提交時間UTC`,
    reservation.`ReleasedAtUtc` AS `釋放時間UTC`
FROM `god2`.`skill_cost_reservations` reservation
LEFT JOIN `god2_game`.`skills` skill_info
    ON skill_info.`skill_id` = reservation.`SkillDefinitionId`;

CREATE VIEW `god2`.`vw_battle_skill_usage_readable` AS
SELECT
    usage_row.`BattleInstanceId` AS `戰鬥場次ID`,
    usage_row.`ParticipantId` AS `參戰者ID`,
    usage_row.`SkillDefinitionId` AS `技能ID`,
    COALESCE(skill_info.`name_zh_tw`, '') AS `技能名稱`,
    usage_row.`LastUsedRound` AS `最後使用回合`,
    usage_row.`AvailableAtRound` AS `可再使用回合`,
    usage_row.`UsageCount` AS `使用次數`,
    usage_row.`RuntimeVersion` AS `Runtime版本`,
    usage_row.`PolicyStatus` AS `證據政策`,
    '技能使用次數與冷卻追蹤' AS `功能對照`,
    usage_row.`UpdatedAtUtc` AS `更新時間UTC`
FROM `god2`.`battle_skill_usage` usage_row
LEFT JOIN `god2_game`.`skills` skill_info
    ON skill_info.`skill_id` = usage_row.`SkillDefinitionId`;

CREATE VIEW `god2`.`vw_battle_status_instances_readable` AS
SELECT
    status_row.`StatusInstanceId` AS `狀態實例ID`,
    status_row.`StatusDefinitionId` AS `狀態ID`,
    COALESCE(status_info.`name_zh_tw`, '') AS `狀態名稱`,
    status_row.`BattleInstanceId` AS `戰鬥場次ID`,
    status_row.`TargetParticipantId` AS `目標參戰ID`,
    status_row.`SourceParticipantId` AS `來源參戰ID`,
    status_row.`SourceSkillExecutionId` AS `來源技能執行ID`,
    status_row.`StackCount` AS `層數`,
    status_row.`MaximumStacks` AS `最大層數`,
    status_row.`AppliedRound` AS `套用回合`,
    status_row.`LastRefreshedRound` AS `最後刷新回合`,
    status_row.`ExpiresAfterRound` AS `到期回合後`,
    status_row.`RemainingRounds` AS `剩餘回合`,
    status_row.`LifecycleState` AS `生命週期狀態`,
    status_row.`TriggerCursor` AS `觸發游標`,
    status_row.`RuntimeVersion` AS `Runtime版本`,
    status_row.`PolicyStatus` AS `證據政策`,
    CASE
        WHEN status_row.`LifecycleState` = 'Active' THEN '狀態作用中'
        WHEN status_row.`LifecycleState` = 'Removed' THEN '狀態已移除'
        ELSE '狀態待確認'
    END AS `功能對照`,
    status_row.`AppliedAtUtc` AS `套用時間UTC`,
    status_row.`UpdatedAtUtc` AS `更新時間UTC`,
    status_row.`RemovedAtUtc` AS `移除時間UTC`,
    status_row.`RemovalReason` AS `移除原因`
FROM `god2`.`battle_status_instances` status_row
LEFT JOIN `god2_game`.`status_effects` status_info
    ON status_info.`status_effect_id` = status_row.`StatusDefinitionId`;

CREATE VIEW `god2`.`vw_battle_status_trigger_results_readable` AS
SELECT
    trigger_result.`TriggerExecutionId` AS `觸發執行ID`,
    trigger_result.`StatusTriggerExecutionPlanId` AS `觸發計畫ID`,
    trigger_result.`StatusInstanceId` AS `狀態實例ID`,
    status_view.`狀態ID`,
    status_view.`狀態名稱`,
    status_view.`戰鬥場次ID`,
    trigger_result.`TriggerIndex` AS `觸發順序`,
    trigger_result.`ExecutionOrder` AS `執行順序`,
    trigger_result.`State` AS `觸發狀態`,
    trigger_result.`Result` AS `結果`,
    trigger_result.`FailureCode` AS `失敗代碼`,
    trigger_result.`Damage` AS `傷害`,
    trigger_result.`Heal` AS `治療`,
    trigger_result.`HpBefore` AS `HP前`,
    trigger_result.`HpAfter` AS `HP後`,
    trigger_result.`RecoveryState` AS `恢復狀態`,
    CASE
        WHEN trigger_result.`Damage` > 0 THEN '狀態觸發傷害'
        WHEN trigger_result.`Heal` > 0 THEN '狀態觸發治療'
        WHEN trigger_result.`FailureCode` <> '' THEN '狀態觸發失敗'
        ELSE '狀態觸發'
    END AS `功能對照`,
    trigger_result.`CompletedAtUtc` AS `完成時間UTC`
FROM `god2`.`battle_status_trigger_results` trigger_result
LEFT JOIN `god2`.`vw_battle_status_instances_readable` status_view
    ON status_view.`狀態實例ID` = trigger_result.`StatusInstanceId`;

CREATE VIEW `god2`.`vw_blackbox_skill_status_runtime_observations_readable` AS
SELECT
    skill_view.`技能執行ID`,
    skill_view.`戰鬥場次ID`,
    skill_view.`回合`,
    skill_view.`施放者參戰ID`,
    skill_view.`技能ID`,
    skill_view.`技能名稱`,
    skill_view.`執行狀態`,
    COUNT(DISTINCT effect_view.`效果執行ID`) AS `效果數`,
    COALESCE(SUM(effect_view.`傷害`), 0) AS `總傷害`,
    COALESCE(SUM(effect_view.`治療`), 0) AS `總治療`,
    COUNT(DISTINCT cost_view.`資源預扣ID`) AS `資源消耗筆數`,
    CASE
        WHEN skill_view.`執行狀態` = 'Completed' THEN '第一優先：核對MP消耗、傷害/治療、狀態套用與冷卻'
        WHEN skill_view.`執行狀態` = 'Failed' THEN '第一優先：核對失敗代碼與資源是否釋放'
        ELSE '第二優先：確認技能執行恢復狀態'
    END AS `黑箱測試優先級`,
    '測技能施放、MP/HP/道具消耗、傷害、治療、狀態套用、狀態每回合觸發、冷卻與重放保護。' AS `測試重點`,
    skill_view.`開始時間UTC`,
    skill_view.`完成時間UTC`
FROM `god2`.`vw_skill_executions_readable` skill_view
LEFT JOIN `god2`.`vw_skill_effect_executions_readable` effect_view
    ON effect_view.`技能執行ID` = skill_view.`技能執行ID`
LEFT JOIN `god2`.`vw_skill_cost_reservations_readable` cost_view
    ON cost_view.`技能執行ID` = skill_view.`技能執行ID`
GROUP BY
    skill_view.`技能執行ID`,
    skill_view.`戰鬥場次ID`,
    skill_view.`回合`,
    skill_view.`施放者參戰ID`,
    skill_view.`技能ID`,
    skill_view.`技能名稱`,
    skill_view.`執行狀態`,
    skill_view.`開始時間UTC`,
    skill_view.`完成時間UTC`;
