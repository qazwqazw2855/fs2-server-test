CREATE OR REPLACE VIEW `god2_game`.`vw_legacy_world_interactions_readable` AS
SELECT
    '地圖' AS `資料類型`,
    map_row.`Id` AS `資料ID`,
    map_row.`Code` AS `資料代碼`,
    COALESCE(map_row.`NameZhTw`, map_row.`Name`) AS `名稱`,
    map_row.`Id` AS `地圖ID`,
    COALESCE(map_row.`NameZhTw`, map_row.`Name`) AS `地圖名稱`,
    NULL AS `座標X`,
    NULL AS `座標Y`,
    NULL AS `方向`,
    NULL AS `目的地地圖ID`,
    NULL AS `目的地地圖名稱`,
    NULL AS `目的地X`,
    NULL AS `目的地Y`,
    CONCAT('尺寸 ', COALESCE(CAST(map_row.`Width` AS CHAR), '未知'), ' x ', COALESCE(CAST(map_row.`Height` AS CHAR), '未知')) AS `功能說明`,
    map_row.`RecoveryStatus` AS `回收狀態Key`,
    CASE map_row.`RecoveryStatus`
        WHEN 'Recovered' THEN '已由證據回收'
        WHEN 'EvidenceOnly' THEN '僅保留證據'
        ELSE map_row.`RecoveryStatus`
    END AS `回收狀態`,
    map_row.`LocalizationStatus` AS `在地化狀態Key`,
    CASE map_row.`LocalizationStatus`
        WHEN 'ConvertedToTraditional' THEN '已轉為繁體中文'
        WHEN 'TraditionalVerified' THEN '繁體中文已驗證'
        WHEN 'MixedLanguageNormalized' THEN '混合語系已正規化'
        WHEN 'NotApplicable' THEN '不適用'
        ELSE map_row.`LocalizationStatus`
    END AS `在地化狀態`,
    '地圖資料' AS `正式啟用狀態`,
    map_row.`ContentRecoveryRunId` AS `內容回收批次`
FROM `god2`.`maps` map_row
UNION ALL
SELECT
    'NPC' AS `資料類型`,
    npc_row.`Id` AS `資料ID`,
    npc_row.`Code` AS `資料代碼`,
    COALESCE(npc_row.`NameZhTw`, npc_row.`Name`) AS `名稱`,
    npc_row.`MapId` AS `地圖ID`,
    COALESCE(map_row.`NameZhTw`, map_row.`Name`) AS `地圖名稱`,
    npc_row.`PositionX` AS `座標X`,
    npc_row.`PositionY` AS `座標Y`,
    npc_row.`Direction` AS `方向`,
    NULL AS `目的地地圖ID`,
    NULL AS `目的地地圖名稱`,
    NULL AS `目的地X`,
    NULL AS `目的地Y`,
    CASE npc_row.`InteractionFamily`
        WHEN 'Merchant' THEN '商人互動'
        WHEN 'Dialog' THEN '對話互動'
        WHEN 'Quest' THEN '任務互動'
        WHEN 'Portal' THEN '傳送互動'
        ELSE COALESCE(npc_row.`InteractionFamily`, '互動功能未確認')
    END AS `功能說明`,
    npc_row.`RecoveryStatus` AS `回收狀態Key`,
    CASE npc_row.`RecoveryStatus`
        WHEN 'Recovered' THEN '已由證據回收'
        ELSE npc_row.`RecoveryStatus`
    END AS `回收狀態`,
    npc_row.`LocalizationStatus` AS `在地化狀態Key`,
    CASE npc_row.`LocalizationStatus`
        WHEN 'ConvertedToTraditional' THEN '已轉為繁體中文'
        WHEN 'TraditionalVerified' THEN '繁體中文已驗證'
        WHEN 'MixedLanguageNormalized' THEN '混合語系已正規化'
        WHEN 'NotApplicable' THEN '不適用'
        ELSE npc_row.`LocalizationStatus`
    END AS `在地化狀態`,
    CASE npc_row.`ProductionSpawnEnabled`
        WHEN 1 THEN '正式生成啟用'
        WHEN 0 THEN '正式生成未啟用'
        ELSE '未標示'
    END AS `正式啟用狀態`,
    npc_row.`ContentRecoveryRunId` AS `內容回收批次`
FROM `god2`.`npcs` npc_row
LEFT JOIN `god2`.`maps` map_row
    ON map_row.`Id` = npc_row.`MapId`
UNION ALL
SELECT
    '商人' AS `資料類型`,
    merchant_row.`Id` AS `資料ID`,
    CAST(merchant_row.`NpcId` AS CHAR) AS `資料代碼`,
    COALESCE(merchant_row.`NameZhTw`, merchant_row.`Name`, npc_row.`NameZhTw`, npc_row.`Name`) AS `名稱`,
    npc_row.`MapId` AS `地圖ID`,
    COALESCE(map_row.`NameZhTw`, map_row.`Name`) AS `地圖名稱`,
    npc_row.`PositionX` AS `座標X`,
    npc_row.`PositionY` AS `座標Y`,
    npc_row.`Direction` AS `方向`,
    NULL AS `目的地地圖ID`,
    NULL AS `目的地地圖名稱`,
    NULL AS `目的地X`,
    NULL AS `目的地Y`,
    '商店買賣互動' AS `功能說明`,
    merchant_row.`RecoveryStatus` AS `回收狀態Key`,
    CASE merchant_row.`RecoveryStatus`
        WHEN 'Recovered' THEN '已由證據回收'
        ELSE merchant_row.`RecoveryStatus`
    END AS `回收狀態`,
    merchant_row.`LocalizationStatus` AS `在地化狀態Key`,
    CASE merchant_row.`LocalizationStatus`
        WHEN 'ConvertedToTraditional' THEN '已轉為繁體中文'
        WHEN 'TraditionalVerified' THEN '繁體中文已驗證'
        WHEN 'MixedLanguageNormalized' THEN '混合語系已正規化'
        WHEN 'NotApplicable' THEN '不適用'
        ELSE merchant_row.`LocalizationStatus`
    END AS `在地化狀態`,
    CASE npc_row.`ProductionSpawnEnabled`
        WHEN 1 THEN '正式生成啟用'
        WHEN 0 THEN '正式生成未啟用'
        ELSE 'NPC 未對應'
    END AS `正式啟用狀態`,
    merchant_row.`ContentRecoveryRunId` AS `內容回收批次`
FROM `god2`.`merchants` merchant_row
LEFT JOIN `god2`.`npcs` npc_row
    ON npc_row.`Id` = merchant_row.`NpcId`
LEFT JOIN `god2`.`maps` map_row
    ON map_row.`Id` = npc_row.`MapId`
UNION ALL
SELECT
    '傳送門' AS `資料類型`,
    portal_row.`Id` AS `資料ID`,
    CAST(portal_row.`Id` AS CHAR) AS `資料代碼`,
    portal_row.`Name` AS `名稱`,
    portal_row.`SourceMapId` AS `地圖ID`,
    COALESCE(source_map_row.`NameZhTw`, source_map_row.`Name`) AS `地圖名稱`,
    portal_row.`SourceX` AS `座標X`,
    portal_row.`SourceY` AS `座標Y`,
    NULL AS `方向`,
    portal_row.`TargetMapId` AS `目的地地圖ID`,
    COALESCE(target_map_row.`NameZhTw`, target_map_row.`Name`) AS `目的地地圖名稱`,
    portal_row.`TargetX` AS `目的地X`,
    portal_row.`TargetY` AS `目的地Y`,
    '地圖傳送互動' AS `功能說明`,
    portal_row.`RecoveryStatus` AS `回收狀態Key`,
    CASE portal_row.`RecoveryStatus`
        WHEN 'Recovered' THEN '已由證據回收'
        ELSE portal_row.`RecoveryStatus`
    END AS `回收狀態`,
    NULL AS `在地化狀態Key`,
    '不適用' AS `在地化狀態`,
    '傳送資料已回收，啟用需依服務端傳送規則判定' AS `正式啟用狀態`,
    NULL AS `內容回收批次`
FROM `god2`.`portals` portal_row
LEFT JOIN `god2`.`maps` source_map_row
    ON source_map_row.`Id` = portal_row.`SourceMapId`
LEFT JOIN `god2`.`maps` target_map_row
    ON target_map_row.`Id` = portal_row.`TargetMapId`;
