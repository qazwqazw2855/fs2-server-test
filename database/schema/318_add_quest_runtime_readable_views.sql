DROP VIEW IF EXISTS `god2`.`vw_quest_instances_readable`;
DROP VIEW IF EXISTS `god2`.`vw_quest_objective_states_readable`;
DROP VIEW IF EXISTS `god2`.`vw_quest_progress_mutations_readable`;
DROP VIEW IF EXISTS `god2`.`vw_quest_reward_finalization_readable`;
DROP VIEW IF EXISTS `god2`.`vw_quest_operation_idempotency_readable`;
DROP VIEW IF EXISTS `god2`.`vw_blackbox_quest_runtime_observations_readable`;

CREATE VIEW `god2`.`vw_quest_instances_readable` AS
SELECT
    instance.`QuestInstanceId` AS `任務實例ID`,
    instance.`CharacterId` AS `角色ID`,
    COALESCE(character_info.`name`, '') AS `角色名稱`,
    instance.`QuestDefinitionId` AS `任務定義ID`,
    COALESCE(quest_info.`name_zh_tw`, '') AS `任務名稱`,
    instance.`State` AS `任務狀態`,
    instance.`QuestVersion` AS `任務版本`,
    instance.`DefinitionContentVersion` AS `定義內容版本`,
    instance.`RepeatIteration` AS `重複次數`,
    instance.`RewardState` AS `獎勵狀態`,
    instance.`RecoveryState` AS `恢復狀態`,
    CASE
        WHEN instance.`State` = 'Accepted' THEN '已接任務'
        WHEN instance.`State` = 'ReadyToComplete' THEN '可回報任務'
        WHEN instance.`State` = 'Completed' THEN '任務完成'
        WHEN instance.`State` = 'Abandoned' THEN '任務放棄'
        ELSE '任務狀態待確認'
    END AS `功能對照`,
    instance.`AcceptedAtUtc` AS `接取時間UTC`,
    instance.`ReadyAtUtc` AS `可回報時間UTC`,
    instance.`CompletedAtUtc` AS `完成時間UTC`,
    instance.`AbandonedAtUtc` AS `放棄時間UTC`,
    instance.`LastProgressAtUtc` AS `最後進度時間UTC`,
    instance.`UpdatedAtUtc` AS `更新時間UTC`
FROM `god2`.`quest_instances` instance
LEFT JOIN `god2_player`.`characters` character_info
    ON character_info.`character_id` = instance.`CharacterId`
LEFT JOIN `god2_game`.`quests` quest_info
    ON quest_info.`quest_id` = instance.`QuestDefinitionId`;

CREATE VIEW `god2`.`vw_quest_objective_states_readable` AS
SELECT
    objective.`QuestInstanceId` AS `任務實例ID`,
    quest_view.`角色ID`,
    quest_view.`角色名稱`,
    quest_view.`任務定義ID`,
    quest_view.`任務名稱`,
    objective.`ObjectiveDefinitionId` AS `目標定義ID`,
    objective.`ObjectiveIndex` AS `目標順序`,
    objective.`ObjectiveType` AS `目標類型`,
    objective.`TargetTemplateId` AS `目標模板ID`,
    objective.`RequiredCount` AS `需求數量`,
    objective.`CurrentProgress` AS `目前進度`,
    objective.`State` AS `目標狀態`,
    objective.`ObjectiveVersion` AS `目標版本`,
    objective.`PolicyStatus` AS `證據政策`,
    CASE
        WHEN objective.`State` = 'Completed' THEN '目標完成'
        WHEN objective.`CurrentProgress` > 0 THEN '目標進行中'
        ELSE '目標尚未進行'
    END AS `功能對照`,
    objective.`UpdatedAtUtc` AS `更新時間UTC`
FROM `god2`.`quest_objective_states` objective
LEFT JOIN `god2`.`vw_quest_instances_readable` quest_view
    ON quest_view.`任務實例ID` = objective.`QuestInstanceId`;

CREATE VIEW `god2`.`vw_quest_progress_mutations_readable` AS
SELECT
    mutation.`ProgressMutationId` AS `進度異動ID`,
    mutation.`SemanticEventId` AS `語意事件ID`,
    mutation.`QuestInstanceId` AS `任務實例ID`,
    mutation.`ObjectiveDefinitionId` AS `目標定義ID`,
    mutation.`CharacterId` AS `角色ID`,
    COALESCE(character_info.`name`, '') AS `角色名稱`,
    mutation.`ProgressBefore` AS `異動前進度`,
    mutation.`ProgressDelta` AS `進度變化`,
    mutation.`ProgressAfter` AS `異動後進度`,
    mutation.`ExpectedQuestVersion` AS `預期任務版本`,
    mutation.`ExpectedObjectiveVersion` AS `預期目標版本`,
    mutation.`ResultCode` AS `結果代碼`,
    mutation.`FailureCode` AS `失敗代碼`,
    CASE
        WHEN mutation.`ResultCode` = 'Success' THEN '任務目標進度已更新'
        WHEN mutation.`FailureCode` <> '' THEN '任務目標進度更新失敗'
        ELSE '任務進度異動待確認'
    END AS `功能對照`,
    mutation.`CreatedAtUtc` AS `建立時間UTC`
FROM `god2`.`quest_progress_mutations` mutation
LEFT JOIN `god2_player`.`characters` character_info
    ON character_info.`character_id` = mutation.`CharacterId`;

CREATE VIEW `god2`.`vw_quest_reward_finalization_readable` AS
SELECT
    reward.`QuestRewardPlanId` AS `獎勵計畫ID`,
    reward.`CompletionPlanId` AS `完成計畫ID`,
    reward.`QuestInstanceId` AS `任務實例ID`,
    reward.`CharacterId` AS `角色ID`,
    COALESCE(character_info.`name`, '') AS `角色名稱`,
    reward.`State` AS `獎勵狀態`,
    reward.`ResultCode` AS `結果代碼`,
    reward.`FailureCode` AS `失敗代碼`,
    CASE
        WHEN reward.`State` = 'Completed' OR reward.`ResultCode` = 'Success' THEN '任務獎勵已發放'
        WHEN reward.`FailureCode` <> '' THEN '任務獎勵發放失敗'
        ELSE '任務獎勵待處理'
    END AS `功能對照`,
    reward.`CreatedAtUtc` AS `建立時間UTC`,
    reward.`CompletedAtUtc` AS `完成時間UTC`
FROM `god2`.`quest_reward_finalization` reward
LEFT JOIN `god2_player`.`characters` character_info
    ON character_info.`character_id` = reward.`CharacterId`;

CREATE VIEW `god2`.`vw_quest_operation_idempotency_readable` AS
SELECT
    idempotency.`Operation` AS `操作`,
    idempotency.`QuestIntentId` AS `任務意圖ID`,
    idempotency.`QuestInstanceId` AS `任務實例ID`,
    idempotency.`CharacterId` AS `角色ID`,
    COALESCE(character_info.`name`, '') AS `角色名稱`,
    idempotency.`ResultCode` AS `結果代碼`,
    CASE
        WHEN idempotency.`ResultJson` IS NULL OR idempotency.`ResultJson` = '' THEN '尚無結果快取'
        ELSE '已有結果快取'
    END AS `重放保護狀態`,
    CASE
        WHEN idempotency.`Operation` LIKE '%Accept%' THEN '接任務重放保護'
        WHEN idempotency.`Operation` LIKE '%Complete%' THEN '回報任務重放保護'
        WHEN idempotency.`Operation` LIKE '%Abandon%' THEN '放棄任務重放保護'
        ELSE '任務操作重放保護'
    END AS `功能對照`,
    idempotency.`CreatedAtUtc` AS `建立時間UTC`,
    idempotency.`CompletedAtUtc` AS `完成時間UTC`
FROM `god2`.`quest_operation_idempotency` idempotency
LEFT JOIN `god2_player`.`characters` character_info
    ON character_info.`character_id` = idempotency.`CharacterId`;

CREATE VIEW `god2`.`vw_blackbox_quest_runtime_observations_readable` AS
SELECT
    quest_view.`任務實例ID`,
    quest_view.`角色ID`,
    quest_view.`角色名稱`,
    quest_view.`任務定義ID`,
    quest_view.`任務名稱`,
    quest_view.`任務狀態`,
    quest_view.`獎勵狀態`,
    quest_view.`恢復狀態`,
    COUNT(DISTINCT objective.`ObjectiveDefinitionId`) AS `目標數`,
    SUM(CASE WHEN objective.`State` = 'Completed' THEN 1 ELSE 0 END) AS `已完成目標數`,
    COUNT(DISTINCT mutation.`ProgressMutationId`) AS `進度異動數`,
    COUNT(DISTINCT reward.`獎勵計畫ID`) AS `獎勵處理數`,
    CASE
        WHEN quest_view.`任務狀態` = 'Completed' THEN '第一優先：核對回報、獎勵、背包與金錢'
        WHEN quest_view.`任務狀態` = 'ReadyToComplete' THEN '第一優先：核對目標完成與回報NPC'
        WHEN quest_view.`任務狀態` = 'Accepted' THEN '第一優先：核對目標進度與任務列表'
        ELSE '第二優先：確認任務狀態與重放保護'
    END AS `黑箱測試優先級`,
    '測接任務、目標進度、回報NPC、獎勵發放、放棄任務、重放保護與恢復狀態。' AS `測試重點`,
    quest_view.`接取時間UTC`,
    quest_view.`完成時間UTC`
FROM `god2`.`vw_quest_instances_readable` quest_view
LEFT JOIN `god2`.`quest_objective_states` objective
    ON objective.`QuestInstanceId` = quest_view.`任務實例ID`
LEFT JOIN `god2`.`quest_progress_mutations` mutation
    ON mutation.`QuestInstanceId` = quest_view.`任務實例ID`
LEFT JOIN `god2`.`vw_quest_reward_finalization_readable` reward
    ON reward.`任務實例ID` = quest_view.`任務實例ID`
GROUP BY
    quest_view.`任務實例ID`,
    quest_view.`角色ID`,
    quest_view.`角色名稱`,
    quest_view.`任務定義ID`,
    quest_view.`任務名稱`,
    quest_view.`任務狀態`,
    quest_view.`獎勵狀態`,
    quest_view.`恢復狀態`,
    quest_view.`接取時間UTC`,
    quest_view.`完成時間UTC`;
