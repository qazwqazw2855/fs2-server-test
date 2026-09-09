DROP VIEW IF EXISTS `god2`.`vw_accounts_readable`;
DROP VIEW IF EXISTS `god2`.`vw_characters_readable`;
DROP VIEW IF EXISTS `god2`.`vw_maps_readable`;
DROP VIEW IF EXISTS `god2`.`vw_npcs_readable`;
DROP VIEW IF EXISTS `god2`.`vw_portals_readable`;
DROP VIEW IF EXISTS `god2`.`vw_blackbox_world_identity_observations_readable`;

CREATE VIEW `god2`.`vw_accounts_readable` AS
SELECT
    account.`Id` AS `帳號ID`,
    account.`LoginName` AS `登入帳號`,
    account.`Status` AS `帳號狀態`,
    account.`FailedLoginCount` AS `登入失敗次數`,
    account.`LockedUntilUtc` AS `鎖定到UTC`,
    CASE
        WHEN account.`CurrentSessionId` IS NULL OR account.`CurrentSessionId` = '' THEN '未在線'
        ELSE '有目前連線'
    END AS `連線狀態`,
    CASE
        WHEN account.`Status` = 'Active' THEN '可登入帳號'
        WHEN account.`LockedUntilUtc` IS NOT NULL THEN '帳號鎖定'
        ELSE '帳號不可登入'
    END AS `功能對照`,
    account.`CreatedAtUtc` AS `建立時間UTC`,
    account.`UpdatedAtUtc` AS `更新時間UTC`,
    account.`LastLoginAtUtc` AS `最後登入時間UTC`
FROM `god2`.`accounts` account;

CREATE VIEW `god2`.`vw_characters_readable` AS
SELECT
    character_row.`Id` AS `角色ID`,
    character_row.`AccountId` AS `帳號ID`,
    character_row.`Name` AS `角色名稱`,
    character_row.`ActiveName` AS `目前顯示名稱`,
    character_row.`Class` AS `職業代碼`,
    character_row.`Gender` AS `性別`,
    character_row.`LifeSkill` AS `先天技能`,
    character_row.`Appearance` AS `外觀`,
    character_row.`Level` AS `等級`,
    character_row.`MapId` AS `地圖ID`,
    COALESCE(map_info.`NameZhTw`, map_info.`Name`, '') AS `地圖名稱`,
    character_row.`PositionX` AS `座標X`,
    character_row.`PositionY` AS `座標Y`,
    character_row.`CurrentDirection` AS `方向`,
    character_row.`RuntimeVersion` AS `Runtime版本`,
    character_row.`LastPortalTemplateId` AS `最後傳送門ID`,
    character_row.`Status` AS `角色狀態`,
    CASE
        WHEN character_row.`DeletedAtUtc` IS NOT NULL THEN '已刪除'
        WHEN character_row.`Status` = 'Active' THEN '可進入世界'
        ELSE '角色不可進入世界'
    END AS `功能對照`,
    character_row.`CreatedAtUtc` AS `建立時間UTC`,
    character_row.`UpdatedAtUtc` AS `更新時間UTC`,
    character_row.`LastPlayedAtUtc` AS `最後遊玩時間UTC`,
    character_row.`DeletedAtUtc` AS `刪除時間UTC`
FROM `god2`.`characters` character_row
LEFT JOIN `god2`.`maps` map_info
    ON map_info.`Id` = character_row.`MapId`;

CREATE VIEW `god2`.`vw_maps_readable` AS
SELECT
    map_row.`Id` AS `地圖ID`,
    map_row.`Code` AS `服務端代碼`,
    COALESCE(map_row.`NameZhTw`, map_row.`Name`, '') AS `地圖名稱`,
    map_row.`Width` AS `寬度`,
    map_row.`Height` AS `高度`,
    map_row.`RecoveryStatus` AS `恢復狀態`,
    map_row.`LocalizationStatus` AS `在地化狀態`,
    identity_row.`ClientBuildId` AS `客戶端版本`,
    identity_row.`ClientMapId` AS `客戶端地圖ID`,
    identity_row.`ClientAreaId` AS `客戶端區域ID`,
    identity_row.`ResourceIdentity` AS `資源識別`,
    CASE WHEN identity_row.`ProductionEnabled` = 1 THEN '正式啟用' ELSE '正式未啟用或未對應' END AS `正式狀態`,
    '地圖尺寸、客戶端資源對應與角色進入世界位置' AS `功能對照`,
    map_row.`CreatedAtUtc` AS `建立時間UTC`,
    map_row.`UpdatedAtUtc` AS `更新時間UTC`
FROM `god2`.`maps` map_row
LEFT JOIN `god2`.`client_map_identities` identity_row
    ON identity_row.`MapId` = map_row.`Id`;

CREATE VIEW `god2`.`vw_npcs_readable` AS
SELECT
    npc.`Id` AS `NPC_ID`,
    npc.`Code` AS `服務端代碼`,
    COALESCE(npc.`NameZhTw`, npc.`Name`, '') AS `NPC名稱`,
    npc.`NpcType` AS `NPC類型`,
    npc.`InteractionFamily` AS `互動家族`,
    npc.`MapId` AS `地圖ID`,
    COALESCE(map_info.`NameZhTw`, map_info.`Name`, '') AS `地圖名稱`,
    npc.`PositionX` AS `座標X`,
    npc.`PositionY` AS `座標Y`,
    npc.`Direction` AS `方向`,
    npc.`RecoveryStatus` AS `恢復狀態`,
    npc.`LocalizationStatus` AS `在地化狀態`,
    npc.`OfficialIdentityStatus` AS `官方身分狀態`,
    CASE WHEN npc.`ProductionSpawnEnabled` = 1 THEN '正式出生啟用' ELSE '正式出生未啟用' END AS `出生狀態`,
    identity_row.`ResourceType` AS `客戶端資源類型`,
    identity_row.`ResourceOrdinal` AS `客戶端資源序號`,
    'NPC顯示、出生、點擊互動、任務與商人入口' AS `功能對照`
FROM `god2`.`npcs` npc
LEFT JOIN `god2`.`maps` map_info
    ON map_info.`Id` = npc.`MapId`
LEFT JOIN `god2`.`npc_client_identities` identity_row
    ON identity_row.`NpcId` = npc.`Id`;

CREATE VIEW `god2`.`vw_portals_readable` AS
SELECT
    portal.`Id` AS `傳送門ID`,
    portal.`Name` AS `傳送門名稱`,
    portal.`SourceMapId` AS `來源地圖ID`,
    COALESCE(source_map.`NameZhTw`, source_map.`Name`, '') AS `來源地圖名稱`,
    portal.`SourceX` AS `來源X`,
    portal.`SourceY` AS `來源Y`,
    portal.`TargetMapId` AS `目標地圖ID`,
    COALESCE(target_map.`NameZhTw`, target_map.`Name`, '') AS `目標地圖名稱`,
    portal.`TargetX` AS `目標X`,
    portal.`TargetY` AS `目標Y`,
    portal.`RecoveryStatus` AS `恢復狀態`,
    '傳送門換圖與角色座標轉移' AS `功能對照`
FROM `god2`.`portals` portal
LEFT JOIN `god2`.`maps` source_map
    ON source_map.`Id` = portal.`SourceMapId`
LEFT JOIN `god2`.`maps` target_map
    ON target_map.`Id` = portal.`TargetMapId`;

CREATE VIEW `god2`.`vw_blackbox_world_identity_observations_readable` AS
SELECT
    '角色進入世界' AS `觀察類型`,
    character_view.`角色ID` AS `主要ID`,
    character_view.`角色名稱` AS `名稱`,
    character_view.`地圖ID`,
    character_view.`地圖名稱`,
    character_view.`座標X`,
    character_view.`座標Y`,
    character_view.`功能對照`,
    CASE
        WHEN character_view.`功能對照` = '可進入世界' THEN '第一優先：測登入選角與進入地圖'
        ELSE '擱置：角色不可進入世界'
    END AS `黑箱測試優先級`,
    '測帳號登入、選角、地圖載入、角色座標、NPC與傳送門顯示。' AS `測試重點`
FROM `god2`.`vw_characters_readable` character_view;
