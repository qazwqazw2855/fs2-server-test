CREATE OR REPLACE VIEW `god2_game`.`vw_legacy_dialogs_readable` AS
SELECT
    dialog_row.`Id` AS `對話ID`,
    dialog_row.`Code` AS `對話代碼`,
    dialog_row.`TextKey` AS `官方文字Key`,
    CASE
        WHEN dialog_row.`TextKey` LIKE 'official.dialogs.source_client_dialog_catalog_comm_npcmission%_csvz'
            THEN CONCAT('官方客戶端任務 NPC 對話目錄 ', REPLACE(REPLACE(SUBSTRING_INDEX(dialog_row.`TextKey`, 'npcmission', -1), '_csvz', ''), '.', ''))
        WHEN dialog_row.`TextKey` LIKE 'official.dialogs.source_client_dialog_catalog_comm_npcnormal%_csvz'
            THEN CONCAT('官方客戶端一般 NPC 對話目錄 ', REPLACE(REPLACE(SUBSTRING_INDEX(dialog_row.`TextKey`, 'npcnormal', -1), '_csvz', ''), '.', ''))
        ELSE dialog_row.`TextKey`
    END AS `對話來源功能`,
    dialog_row.`RecoveryStatus` AS `回收狀態Key`,
    CASE dialog_row.`RecoveryStatus`
        WHEN 'Recovered' THEN '已由證據回收'
        ELSE dialog_row.`RecoveryStatus`
    END AS `回收狀態`,
    dialog_row.`NpcId` AS `NPC_ID`,
    COALESCE(npc_row.`NameZhTw`, npc_row.`Name`) AS `NPC名稱`,
    npc_row.`NpcType` AS `NPC類型Key`,
    CASE npc_row.`InteractionFamily`
        WHEN 'Dialog' THEN '對話'
        WHEN 'Quest' THEN '任務'
        WHEN 'Merchant' THEN '商店'
        WHEN 'Portal' THEN '傳送'
        ELSE npc_row.`InteractionFamily`
    END AS `互動功能`,
    CASE npc_row.`ProductionSpawnEnabled`
        WHEN 1 THEN '正式生成啟用'
        WHEN 0 THEN '證據保留'
        ELSE '未標示'
    END AS `NPC正式狀態`,
    npc_row.`MapId` AS `地圖ID`,
    npc_row.`PositionX` AS `座標X`,
    npc_row.`PositionY` AS `座標Y`
FROM `god2`.`dialogs` dialog_row
LEFT JOIN `god2`.`npcs` npc_row
    ON npc_row.`Id` = dialog_row.`NpcId`;
