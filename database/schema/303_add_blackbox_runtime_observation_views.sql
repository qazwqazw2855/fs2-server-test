-- Owner-facing black-box testing views.
-- These views expose formal runtime combat data in Traditional Chinese so
-- observed live-server battles can be used to tune monster HP/MP, damage,
-- drops, rewards, and respawn behavior without reintroducing research schemas.

CREATE OR REPLACE VIEW `god2`.`vw_blackbox_combat_observations_readable` AS
SELECT
    audit_row.`CreatedAtUtc` AS `觀測時間UTC`,
    audit_row.`CombatIntentId` AS `戰鬥意圖ID`,
    audit_row.`SessionId` AS `連線ID`,
    audit_row.`CharacterId` AS `角色ID`,
    audit_row.`MapId` AS `地圖ID`,
    map_row.`name_zh_tw` AS `地圖名稱`,
    audit_row.`ActionType` AS `動作類型`,
    audit_row.`TargetRuntimeEntityId` AS `怪物RuntimeID`,
    audit_row.`TargetTemplateId` AS `怪物模板ID`,
    monster_row.`name_zh_tw` AS `怪物名稱`,
    monster_row.`level` AS `怪物等級`,
    monster_row.`max_hp` AS `資料庫HP`,
    monster_row.`max_mp` AS `資料庫MP`,
    monster_row.`physical_attack` AS `資料庫物攻`,
    monster_row.`physical_defense` AS `資料庫物防`,
    monster_row.`magic_attack` AS `資料庫魔攻`,
    monster_row.`magic_defense` AS `資料庫魔防`,
    audit_row.`HpBefore` AS `攻擊前HP`,
    audit_row.`Damage` AS `本次傷害`,
    audit_row.`HpAfter` AS `攻擊後HP`,
    audit_row.`DeathCommitted` AS `是否死亡`,
    audit_row.`RewardCommitted` AS `是否已發獎勵`,
    audit_row.`RespawnScheduled` AS `是否排程重生`,
    audit_row.`Result` AS `服務端結果`,
    audit_row.`FailureCode` AS `失敗原因`,
    audit_row.`PolicyStatus` AS `政策狀態`,
    audit_row.`CompletedAtUtc` AS `完成時間UTC`
FROM `god2`.`combat_audit` audit_row
LEFT JOIN `god2_game`.`monsters` monster_row
  ON monster_row.`monster_id` = audit_row.`TargetTemplateId`
LEFT JOIN `god2_game`.`maps` map_row
  ON map_row.`map_id` = audit_row.`MapId`;

CREATE OR REPLACE VIEW `god2_game`.`vw_blackbox_monster_test_targets_readable` AS
SELECT
    monster_row.`monster_id` AS `怪物ID`,
    monster_row.`name_zh_tw` AS `怪物名稱`,
    monster_row.`level` AS `等級`,
    monster_row.`max_hp` AS `HP`,
    monster_row.`max_mp` AS `MP`,
    monster_row.`physical_attack` AS `物攻`,
    monster_row.`physical_defense` AS `物防`,
    monster_row.`magic_attack` AS `魔攻`,
    monster_row.`magic_defense` AS `魔防`,
    monster_row.`experience_reward` AS `經驗`,
    monster_row.`currency_reward` AS `金錢`,
    monster_row.`combat_stat_source_zh_tw` AS `數值來源`,
    monster_row.`combat_stat_policy_zh_tw` AS `數值政策`,
    monster_row.`enabled` AS `怪物啟用`,
    COUNT(DISTINCT spawn_row.`spawn_id`) AS `出生點數`,
    COUNT(DISTINCT CASE WHEN spawn_row.`enabled` = 1 THEN spawn_row.`spawn_id` END) AS `已啟用出生點數`,
    MIN(spawn_row.`map_id`) AS `第一個地圖ID`,
    MIN(spawn_row.`map_name_cache`) AS `第一個地圖名稱`,
    MIN(spawn_row.`position_x`) AS `第一個X`,
    MIN(spawn_row.`position_y`) AS `第一個Y`,
    COUNT(DISTINCT drop_row.`drop_id`) AS `掉落資料數`,
    COUNT(DISTINCT CASE WHEN drop_row.`enabled` = 1 THEN drop_row.`drop_id` END) AS `已啟用掉落數`
FROM `god2_game`.`monsters` monster_row
LEFT JOIN `god2_game`.`monster_spawns` spawn_row
  ON spawn_row.`monster_id` = monster_row.`monster_id`
LEFT JOIN `god2_game`.`monster_drops` drop_row
  ON drop_row.`monster_id` = monster_row.`monster_id`
GROUP BY
    monster_row.`monster_id`,
    monster_row.`name_zh_tw`,
    monster_row.`level`,
    monster_row.`max_hp`,
    monster_row.`max_mp`,
    monster_row.`physical_attack`,
    monster_row.`physical_defense`,
    monster_row.`magic_attack`,
    monster_row.`magic_defense`,
    monster_row.`experience_reward`,
    monster_row.`currency_reward`,
    monster_row.`combat_stat_source_zh_tw`,
    monster_row.`combat_stat_policy_zh_tw`,
    monster_row.`enabled`
ORDER BY
    monster_row.`enabled` DESC,
    `已啟用出生點數` DESC,
    monster_row.`level`,
    monster_row.`monster_id`;
