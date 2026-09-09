CREATE OR REPLACE VIEW `god2_game`.`vw_account_characters_readable` AS
SELECT
    account_row.`Id` AS `帳號ID`,
    account_row.`LoginName` AS `登入帳號`,
    account_row.`Status` AS `帳號狀態Key`,
    CASE account_row.`Status`
        WHEN 'Active' THEN '啟用'
        WHEN 'Locked' THEN '鎖定'
        WHEN 'Disabled' THEN '停用'
        ELSE account_row.`Status`
    END AS `帳號狀態`,
    account_row.`FailedLoginCount` AS `登入失敗次數`,
    CASE
        WHEN account_row.`LockedUntilUtc` IS NULL THEN '未鎖定'
        ELSE CONCAT('鎖定至 ', DATE_FORMAT(account_row.`LockedUntilUtc`, '%Y-%m-%d %H:%i:%s'), ' UTC')
    END AS `鎖定狀態`,
    CASE
        WHEN account_row.`CurrentSessionId` IS NULL OR account_row.`CurrentSessionId` = '' THEN '無目前連線'
        ELSE '有目前連線'
    END AS `連線狀態`,
    account_row.`LastLoginAtUtc` AS `最後登入時間UTC`,
    character_row.`Id` AS `角色ID`,
    character_row.`Name` AS `角色名稱`,
    character_row.`ActiveName` AS `目前有效角色名稱`,
    character_row.`Class` AS `職業Key`,
    CASE character_row.`Class`
        WHEN 'Warrior' THEN '武者'
        WHEN 'Mage' THEN '法師'
        WHEN 'XianDao' THEN '仙道'
        WHEN 'Assassin' THEN '刺客'
        ELSE COALESCE(character_row.`Class`, '未標示')
    END AS `職業`,
    character_row.`Gender` AS `性別Key`,
    CASE character_row.`Gender`
        WHEN 'Male' THEN '男性'
        WHEN 'Female' THEN '女性'
        ELSE COALESCE(character_row.`Gender`, '未標示')
    END AS `性別`,
    character_row.`LifeSkill` AS `生活技能Key`,
    CASE character_row.`LifeSkill`
        WHEN 'ArmorForging' THEN '防具鍛造'
        WHEN 'WeaponForging' THEN '武器鍛造'
        WHEN 'PillAlchemy' THEN '煉丹'
        WHEN 'MagicTreasureForging' THEN '法寶鍛造'
        ELSE COALESCE(character_row.`LifeSkill`, '未標示')
    END AS `生活技能`,
    character_row.`Level` AS `等級`,
    character_row.`MapId` AS `地圖ID`,
    COALESCE(map_row.`NameZhTw`, map_row.`Name`) AS `地圖名稱`,
    character_row.`PositionX` AS `座標X`,
    character_row.`PositionY` AS `座標Y`,
    character_row.`CurrentDirection` AS `方向Key`,
    character_row.`Status` AS `角色狀態Key`,
    CASE character_row.`Status`
        WHEN 'Active' THEN '啟用'
        WHEN 'Deleted' THEN '已刪除'
        WHEN 'Disabled' THEN '停用'
        ELSE character_row.`Status`
    END AS `角色狀態`,
    CASE
        WHEN character_row.`DeletedAtUtc` IS NULL THEN '未刪除'
        ELSE CONCAT('已刪除於 ', DATE_FORMAT(character_row.`DeletedAtUtc`, '%Y-%m-%d %H:%i:%s'), ' UTC')
    END AS `刪除狀態`,
    character_row.`RuntimeVersion` AS `角色資料版本`,
    character_row.`LastPlayedAtUtc` AS `最後遊玩時間UTC`,
    character_row.`CreatedAtUtc` AS `角色建立時間UTC`,
    character_row.`UpdatedAtUtc` AS `角色更新時間UTC`
FROM `god2`.`accounts` account_row
LEFT JOIN `god2`.`characters` character_row
    ON character_row.`AccountId` = account_row.`Id`
LEFT JOIN `god2`.`maps` map_row
    ON map_row.`Id` = character_row.`MapId`;
