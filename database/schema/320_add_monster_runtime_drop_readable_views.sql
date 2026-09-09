DROP VIEW IF EXISTS `god2`.`vw_monster_combat_runtime_state_readable`;
DROP VIEW IF EXISTS `god2`.`vw_monster_death_records_readable`;
DROP VIEW IF EXISTS `god2`.`vw_monster_respawn_schedules_readable`;
DROP VIEW IF EXISTS `god2`.`vw_monster_drop_relationships_readable`;
DROP VIEW IF EXISTS `god2`.`vw_blackbox_monster_runtime_observations_readable`;

CREATE VIEW `god2`.`vw_monster_combat_runtime_state_readable` AS
SELECT
    runtime_state.`RuntimeEntityId` AS `怪物RuntimeID`,
    runtime_state.`MonsterTemplateId` AS `怪物模板ID`,
    COALESCE(monster_info.`name_zh_tw`, '') AS `怪物名稱`,
    runtime_state.`SpawnDefinitionId` AS `出生點ID`,
    runtime_state.`SpawnGroupId` AS `出生群組`,
    runtime_state.`WorldInstanceId` AS `世界實例ID`,
    runtime_state.`MapId` AS `地圖ID`,
    COALESCE(map_info.`name_zh_tw`, '') AS `地圖名稱`,
    runtime_state.`PositionX` AS `座標X`,
    runtime_state.`PositionY` AS `座標Y`,
    runtime_state.`PositionZ` AS `座標Z`,
    runtime_state.`Direction` AS `方向`,
    runtime_state.`LifecycleState` AS `生命週期狀態`,
    runtime_state.`CombatState` AS `戰鬥狀態`,
    runtime_state.`Level` AS `等級`,
    runtime_state.`CurrentHp` AS `目前HP`,
    runtime_state.`MaximumHp` AS `最大HP`,
    runtime_state.`AttackPower` AS `攻擊力`,
    runtime_state.`Defense` AS `防禦力`,
    runtime_state.`AggroState` AS `仇恨狀態`,
    runtime_state.`CurrentTargetRuntimeEntityId` AS `目前目標RuntimeID`,
    runtime_state.`RuntimeVersion` AS `Runtime版本`,
    runtime_state.`DirtyFlags` AS `待同步標記`,
    runtime_state.`StatPolicyStatus` AS `數值證據政策`,
    runtime_state.`ContentVersion` AS `內容版本`,
    CASE
        WHEN runtime_state.`LifecycleState` = 'Alive' THEN '怪物在場'
        WHEN runtime_state.`LifecycleState` = 'Dead' THEN '怪物死亡等待重生'
        ELSE '怪物runtime狀態待確認'
    END AS `功能對照`,
    runtime_state.`SpawnedAtUtc` AS `出生時間UTC`,
    runtime_state.`LastCombatAtUtc` AS `最後戰鬥時間UTC`,
    runtime_state.`DiedAtUtc` AS `死亡時間UTC`,
    runtime_state.`RespawnDueAtUtc` AS `預計重生時間UTC`,
    runtime_state.`UpdatedAtUtc` AS `更新時間UTC`
FROM `god2`.`monster_combat_runtime_state` runtime_state
LEFT JOIN `god2_game`.`monsters` monster_info
    ON monster_info.`monster_id` = runtime_state.`MonsterTemplateId`
LEFT JOIN `god2_game`.`maps` map_info
    ON map_info.`map_id` = runtime_state.`MapId`;

CREATE VIEW `god2`.`vw_monster_death_records_readable` AS
SELECT
    death.`DeathId` AS `死亡ID`,
    death.`CombatIntentId` AS `戰鬥意圖ID`,
    death.`MonsterRuntimeEntityId` AS `怪物RuntimeID`,
    death.`MonsterTemplateId` AS `怪物模板ID`,
    COALESCE(monster_info.`name_zh_tw`, '') AS `怪物名稱`,
    death.`KillerRuntimeEntityId` AS `擊殺者RuntimeID`,
    death.`KillerCharacterId` AS `擊殺者角色ID`,
    COALESCE(character_info.`name`, '') AS `擊殺者名稱`,
    death.`MapId` AS `地圖ID`,
    COALESCE(map_info.`name_zh_tw`, '') AS `地圖名稱`,
    death.`SpawnDefinitionId` AS `出生點ID`,
    death.`HpBefore` AS `死亡前HP`,
    death.`FinalDamage` AS `最後傷害`,
    death.`RewardPolicyStatus` AS `獎勵證據政策`,
    death.`DropPolicyStatus` AS `掉落證據政策`,
    death.`RespawnPolicyStatus` AS `重生證據政策`,
    death.`RuntimeVersionBefore` AS `Runtime版本前`,
    death.`RuntimeVersionAfter` AS `Runtime版本後`,
    death.`CorrelationId` AS `關聯ID`,
    '怪物死亡、擊殺者、最後傷害、掉落與重生政策' AS `功能對照`,
    death.`DiedAtUtc` AS `死亡時間UTC`
FROM `god2`.`monster_death_records` death
LEFT JOIN `god2_game`.`monsters` monster_info
    ON monster_info.`monster_id` = death.`MonsterTemplateId`
LEFT JOIN `god2_player`.`characters` character_info
    ON character_info.`character_id` = death.`KillerCharacterId`
LEFT JOIN `god2_game`.`maps` map_info
    ON map_info.`map_id` = death.`MapId`;

CREATE VIEW `god2`.`vw_monster_respawn_schedules_readable` AS
SELECT
    respawn.`RespawnId` AS `重生ID`,
    respawn.`DeathId` AS `死亡ID`,
    respawn.`MonsterTemplateId` AS `怪物模板ID`,
    COALESCE(monster_info.`name_zh_tw`, '') AS `怪物名稱`,
    respawn.`SpawnDefinitionId` AS `出生點ID`,
    respawn.`SpawnGroupId` AS `出生群組`,
    respawn.`WorldInstanceId` AS `世界實例ID`,
    respawn.`MapId` AS `地圖ID`,
    COALESCE(map_info.`name_zh_tw`, '') AS `地圖名稱`,
    respawn.`PositionX` AS `座標X`,
    respawn.`PositionY` AS `座標Y`,
    respawn.`PositionZ` AS `座標Z`,
    respawn.`Direction` AS `方向`,
    respawn.`RespawnDueAtUtc` AS `預計重生時間UTC`,
    respawn.`PolicyStatus` AS `證據政策`,
    respawn.`State` AS `排程狀態`,
    respawn.`PreviousRuntimeEntityId` AS `前RuntimeID`,
    respawn.`NewRuntimeEntityId` AS `新RuntimeID`,
    respawn.`FailureCode` AS `失敗代碼`,
    CASE
        WHEN respawn.`State` = 'Completed' THEN '怪物已重生'
        WHEN respawn.`State` = 'Pending' THEN '等待怪物重生'
        WHEN respawn.`FailureCode` <> '' THEN '怪物重生失敗'
        ELSE '怪物重生排程'
    END AS `功能對照`,
    respawn.`CreatedAtUtc` AS `建立時間UTC`,
    respawn.`CompletedAtUtc` AS `完成時間UTC`
FROM `god2`.`monster_respawn_schedules` respawn
LEFT JOIN `god2_game`.`monsters` monster_info
    ON monster_info.`monster_id` = respawn.`MonsterTemplateId`
LEFT JOIN `god2_game`.`maps` map_info
    ON map_info.`map_id` = respawn.`MapId`;

CREATE VIEW `god2`.`vw_monster_drop_relationships_readable` AS
SELECT
    relationship.`RelationshipId` AS `掉落關聯ID`,
    relationship.`RunId` AS `匯入批次ID`,
    relationship.`MonsterId` AS `怪物ID`,
    COALESCE(monster_info.`name_zh_tw`, '') AS `怪物名稱`,
    relationship.`DropTableId` AS `掉落表ID`,
    relationship.`DropGroupId` AS `掉落群組ID`,
    relationship.`DropEntryId` AS `掉落項目ID`,
    relationship.`ItemId` AS `物品ID`,
    COALESCE(item_info.`name_zh_tw`, '') AS `物品名稱`,
    relationship.`MinimumQuantity` AS `最小數量`,
    relationship.`MaximumQuantity` AS `最大數量`,
    relationship.`DeclaredDropChance` AS `宣告掉落率`,
    relationship.`EffectiveDropChance` AS `實際掉落率`,
    relationship.`Weight` AS `權重`,
    relationship.`RollType` AS `擲骰類型`,
    relationship.`ExclusiveGroup` AS `互斥群組`,
    CASE WHEN relationship.`Guaranteed` = 1 THEN '必掉' ELSE '非必掉' END AS `是否必掉`,
    relationship.`QuestCondition` AS `任務條件`,
    relationship.`MapCondition` AS `地圖條件`,
    relationship.`LevelCondition` AS `等級條件`,
    relationship.`EventCondition` AS `活動條件`,
    relationship.`DropRelationshipStatus` AS `關聯狀態`,
    CASE WHEN relationship.`IsDropEnabled` = 1 THEN '掉落啟用' ELSE '掉落未啟用' END AS `掉落狀態`,
    CASE WHEN relationship.`ProductionDropEnabled` = 1 THEN '正式掉落啟用' ELSE '正式掉落未啟用' END AS `正式狀態`,
    '怪物掉落物品、數量、機率、條件與正式啟用狀態' AS `功能對照`,
    relationship.`CreatedAtUtc` AS `建立時間UTC`,
    relationship.`UpdatedAtUtc` AS `更新時間UTC`
FROM `god2`.`monster_drop_relationships` relationship
LEFT JOIN `god2_game`.`monsters` monster_info
    ON monster_info.`monster_id` = relationship.`MonsterId`
LEFT JOIN `god2_game`.`items` item_info
    ON item_info.`item_id` = relationship.`ItemId`;

CREATE VIEW `god2`.`vw_blackbox_monster_runtime_observations_readable` AS
SELECT
    death_view.`死亡ID`,
    death_view.`怪物RuntimeID`,
    death_view.`怪物模板ID`,
    death_view.`怪物名稱`,
    death_view.`擊殺者角色ID`,
    death_view.`擊殺者名稱`,
    death_view.`地圖ID`,
    death_view.`地圖名稱`,
    death_view.`死亡前HP`,
    death_view.`最後傷害`,
    death_view.`獎勵證據政策`,
    death_view.`掉落證據政策`,
    death_view.`重生證據政策`,
    respawn_view.`重生ID`,
    respawn_view.`排程狀態` AS `重生排程狀態`,
    respawn_view.`預計重生時間UTC`,
    respawn_view.`新RuntimeID`,
    CASE
        WHEN respawn_view.`重生ID` IS NULL THEN '第一優先：確認擊殺後是否建立重生排程'
        WHEN respawn_view.`排程狀態` = 'Completed' THEN '第一優先：確認怪物重生位置與RuntimeID'
        ELSE '第一優先：確認重生等待時間與排程狀態'
    END AS `黑箱測試優先級`,
    '測怪物死亡、HP歸零、擊殺者、經驗金錢、掉落、重生排程與實際重生。' AS `測試重點`,
    death_view.`死亡時間UTC`
FROM `god2`.`vw_monster_death_records_readable` death_view
LEFT JOIN `god2`.`vw_monster_respawn_schedules_readable` respawn_view
    ON respawn_view.`死亡ID` = death_view.`死亡ID`;
