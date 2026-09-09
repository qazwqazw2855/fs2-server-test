DROP VIEW IF EXISTS `god2_player`.`vw_world_interaction_audit_readable`;
DROP VIEW IF EXISTS `god2_player`.`vw_world_interaction_idempotency_readable`;
DROP VIEW IF EXISTS `god2_player`.`vw_blackbox_world_interaction_observations_readable`;

CREATE VIEW `god2_player`.`vw_world_interaction_audit_readable` AS
SELECT
    audit.`AuditId` AS `審計ID`,
    audit.`InteractionId` AS `互動ID`,
    audit.`TransitionId` AS `換圖ID`,
    audit.`CorrelationId` AS `關聯ID`,
    audit.`SessionId` AS `連線ID`,
    audit.`CharacterId` AS `角色ID`,
    COALESCE(character_info.`name`, '') AS `角色名稱`,
    audit.`PlayerRuntimeEntityId` AS `玩家RuntimeID`,
    audit.`TargetRuntimeEntityId` AS `目標RuntimeID`,
    audit.`TargetTemplateId` AS `目標模板ID`,
    audit.`InteractionType` AS `互動類型`,
    audit.`Handler` AS `服務端處理器`,
    audit.`SourceMapId` AS `來源地圖ID`,
    COALESCE(source_map.`name_zh_tw`, '') AS `來源地圖名稱`,
    audit.`TargetMapId` AS `目標地圖ID`,
    COALESCE(target_map.`name_zh_tw`, '') AS `目標地圖名稱`,
    audit.`SourcePosition` AS `來源座標`,
    audit.`TargetPosition` AS `目標座標`,
    audit.`RuntimeVersionBefore` AS `操作前角色版本`,
    audit.`RuntimeVersionAfter` AS `操作後角色版本`,
    audit.`Result` AS `結果`,
    audit.`FailureCode` AS `失敗代碼`,
    audit.`RollbackStatus` AS `回滾狀態`,
    CASE WHEN audit.`SessionRebound` = 1 THEN '已重新綁定連線' ELSE '未重新綁定連線' END AS `連線重綁`,
    CASE WHEN audit.`ReplicationCleared` = 1 THEN '已清除同步狀態' ELSE '未清除同步狀態' END AS `同步清除`,
    CASE WHEN audit.`ReplicationRebuilt` = 1 THEN '已重建同步狀態' ELSE '未重建同步狀態' END AS `同步重建`,
    CASE
        WHEN audit.`InteractionType` LIKE '%Portal%' OR audit.`Handler` LIKE '%Portal%' THEN '傳送門/換圖'
        WHEN audit.`InteractionType` LIKE '%Merchant%' OR audit.`Handler` LIKE '%Merchant%' THEN '商人開店'
        WHEN audit.`InteractionType` LIKE '%Npc%' OR audit.`Handler` LIKE '%Npc%' THEN 'NPC互動/對話'
        ELSE '世界互動'
    END AS `功能對照`,
    audit.`CreatedAtUtc` AS `建立時間UTC`,
    audit.`CompletedAtUtc` AS `完成時間UTC`
FROM `god2_player`.`world_interaction_audit` audit
LEFT JOIN `god2_player`.`characters` character_info
    ON character_info.`character_id` = audit.`CharacterId`
LEFT JOIN `god2_game`.`maps` source_map
    ON source_map.`map_id` = audit.`SourceMapId`
LEFT JOIN `god2_game`.`maps` target_map
    ON target_map.`map_id` = audit.`TargetMapId`;

CREATE VIEW `god2_player`.`vw_world_interaction_idempotency_readable` AS
SELECT
    idempotency.`IdempotencyKeyHash` AS `重放保護KeyHash`,
    idempotency.`CharacterId` AS `角色ID`,
    COALESCE(character_info.`name`, '') AS `角色名稱`,
    idempotency.`InteractionType` AS `互動類型`,
    idempotency.`InteractionFingerprintSha256` AS `互動指紋SHA256`,
    CASE
        WHEN idempotency.`ResultJson` IS NULL OR idempotency.`ResultJson` = '' THEN '尚無結果'
        ELSE '已有結果快取'
    END AS `重放保護狀態`,
    idempotency.`CreatedAtUtc` AS `建立時間UTC`,
    idempotency.`CompletedAtUtc` AS `完成時間UTC`
FROM `god2_player`.`world_interaction_idempotency` idempotency
LEFT JOIN `god2_player`.`characters` character_info
    ON character_info.`character_id` = idempotency.`CharacterId`;

CREATE VIEW `god2_player`.`vw_blackbox_world_interaction_observations_readable` AS
SELECT
    audit_view.`角色ID`,
    audit_view.`角色名稱`,
    audit_view.`互動類型`,
    audit_view.`服務端處理器`,
    audit_view.`功能對照`,
    audit_view.`來源地圖ID`,
    audit_view.`來源地圖名稱`,
    audit_view.`來源座標`,
    audit_view.`目標地圖ID`,
    audit_view.`目標地圖名稱`,
    audit_view.`目標座標`,
    audit_view.`操作前角色版本`,
    audit_view.`操作後角色版本`,
    audit_view.`結果`,
    audit_view.`失敗代碼`,
    audit_view.`回滾狀態`,
    audit_view.`連線重綁`,
    audit_view.`同步清除`,
    audit_view.`同步重建`,
    CASE
        WHEN audit_view.`結果` = 'Success' AND audit_view.`功能對照` = '傳送門/換圖'
            THEN '第一優先：確認客戶端地圖、座標、角色版本與同步狀態'
        WHEN audit_view.`結果` = 'Success'
            THEN '第一優先：確認客戶端互動視窗與服務端狀態一致'
        ELSE '第二優先：確認失敗提示、回滾與重放保護'
    END AS `黑箱測試優先級`,
    '測NPC互動、商人開店、傳送門換圖、失敗回滾、重放保護與同步重建。' AS `測試重點`,
    audit_view.`完成時間UTC`
FROM `god2_player`.`vw_world_interaction_audit_readable` audit_view;
